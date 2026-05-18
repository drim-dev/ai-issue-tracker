# Руководство по реализации генерации ID

Полное руководство по реализации IdGen и Crockford Base32 кодирования ID в DrimAgents.

## Генерация ID с IdGen

### Структура IdStructure

```csharp
new IdStructure(52, 8, 3)
// 52 бита: Timestamp (миллисекунды с эпохи) — ~142 года
//  8 бит: Generator ID (идентификатор worker/хоста) — 256 различных серверов
//  3 бита: Sequence (инкремент в пределах одной мс) — 8 ID/мс на генератор
// Итого: 63 бита (помещается в long)
```

### Регистрация в Program.cs

```csharp
using IdGen;
using IdGen.DependencyInjection;
using Common.Web.Identification;

var generatorId = builder.Configuration.GetValue<int>("IdGenerator:GeneratorId");
builder.Services.AddIdFactory(generatorId);
```

### Реализация IdFactory

```csharp
// Common/Web/Identification/IdFactory.cs
using IdGen;
using IdGen.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Web.Identification;

public class IdFactory
{
    private readonly IdGenerator _idGenerator;

    public IdFactory(IdGenerator idGenerator)
    {
        _idGenerator = idGenerator;
    }

    public long Create() => _idGenerator.CreateId();
}

public static class IdGeneratorExtensions
{
    public static IServiceCollection AddIdFactory(this IServiceCollection services, int generatorId)
    {
        services.AddIdGen(generatorId, () => new IdGeneratorOptions
        {
            IdStructure = new IdStructure(52, 8, 3),
        });

        services.AddSingleton<IdFactory>();

        return services;
    }
}
```

## Паттерны сущностей

### Контентная сущность (со Slug)

Для публичного контента (навыки, курсы, посты блога):

```csharp
// Domain/Skills/Skill.cs
namespace DrimAgents.Api.Domain.Skills;

public class Skill
{
    public long Id { get; set; }              // Сгенерирован IdFactory (первичный ключ)
    public string Slug { get; set; }          // "test-driven-development" (уникальный, индексированный)
    public string Title { get; set; }
    public string Description { get; set; }
    public long AuthorId { get; set; }        // FK к users
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Стандартная сущность (без Slug)

Для внутренних сущностей (записи на курсы, комментарии и т.д.):

```csharp
// Domain/Courses/Enrollment.cs
namespace DrimAgents.Api.Domain.Courses;

public class Enrollment
{
    public long Id { get; set; }              // Сгенерирован IdFactory
    public long CourseId { get; set; }        // FK к courses
    public long UserId { get; set; }          // FK к users
    public DateTime EnrolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### Конфигурация EF Core

```csharp
// Database/Configurations/SkillConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DrimAgents.Api.Domain.Skills;

namespace DrimAgents.Api.Database.Configurations;

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("skills", "skills");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever(); // ID генерируется IdFactory, а не базой данных

        builder.Property(x => x.Slug)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(x => x.Slug).IsUnique();
    }
}
```

## Использование в вертикальных слайсах

### Создание сущностей

```csharp
// Features/Skills/CreateSkill.cs
public class Handler : IRequestHandler<Request, long>
{
    private readonly AppDbContext _db;
    private readonly IdFactory _idFactory;

    public Handler(AppDbContext db, IdFactory idFactory)
    {
        _db = db;
        _idFactory = idFactory;
    }

    public async Task<long> Handle(Request request, CancellationToken ct)
    {
        var slugExists = await _db.Skills
            .AnyAsync(x => x.Slug == request.Slug, ct);

        if (slugExists)
            throw new ConflictException($"Skill with slug '{request.Slug}' already exists");

        var skill = new Skill
        {
            Id = _idFactory.Create(),
            Slug = request.Slug,
            Title = request.Title,
            Description = request.Description,
            Content = request.Content,
            AuthorId = request.CurrentUserId,
            IsPublished = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.Skills.AddAsync(skill, ct);
        await _db.SaveChangesAsync(ct);

        return skill.Id;
    }
}
```

### Запрос по Slug (предпочтителен для публичного API)

```csharp
// Features/Skills/GetSkill.cs
public class Endpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/skills/{slug}", async (
            [FromRoute] string slug,
            ISender sender,
            CancellationToken ct) =>
        {
            var request = new Request(slug);
            var result = await sender.Send(request, ct);
            return Results.Ok(result);
        });
    }
}
```

## Кодирование ID для публичных API

### Обзор Crockford Base32

**Создан Дугласом Крокфордом** — схема кодирования, удобная для человека, предназначенная для точной передачи чисел между людьми и компьютерами.

**Набор символов:** `0123456789abcdefghjkmnpqrstvwxyz` (32 символа)

**Исключённые буквы:** I, L, O, U

- I и L путаются с 1
- O путается с 0
- U исключена для избежания случайной нецензурности

### Реализация IdEncoding

```csharp
// Common/Web/Identification/IdEncoding.cs
using Common.Web.Utils;
using SimpleBase;

namespace Common.Web.Identification;

public static class IdEncoding
{
    public static string Encode(long id)
    {
        Span<byte> bytes = BitConverter.GetBytes(id);
        Span<byte> bytesWithChecksum = new byte[bytes.Length + 1];
        bytes.CopyTo(bytesWithChecksum);
        bytesWithChecksum[^1] = bytes.CalculateChecksum();
        return Base32.Crockford.Encode(bytesWithChecksum).ToLower();
    }

    public static bool TryDecode(string? encodedId, out long id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(encodedId))
        {
            return false;
        }

        byte[] bytesWithChecksum;
        try
        {
            bytesWithChecksum = Base32.Crockford.Decode(encodedId);
        }
        catch
        {
            return false;
        }

        var bytes = bytesWithChecksum.AsSpan()[..^1];
        var checksum = bytesWithChecksum[^1];
        var calculatedChecksum = bytes.CalculateChecksum();
        if (checksum != calculatedChecksum)
        {
            return false;
        }

        id = BitConverter.ToInt64(bytes);
        return true;
    }
}
```

### Расчёт контрольной суммы

```csharp
// Common/Web/Utils/ChecksumExtensions.cs
namespace Common.Web.Utils;

public static class ChecksumExtensions
{
    public static byte CalculateChecksum(this Span<byte> bytes)
    {
        byte checksum = 0;
        foreach (var b in bytes)
        {
            checksum ^= b;
        }
        return checksum;
    }
}
```

### Зависимости

**NuGet-пакет:** `SimpleBase`

```bash
dotnet add package SimpleBase
```

### Когда использовать закодированные vs raw ID

**Закодированные ID (Crockford Base32):**

- Ответы публичных API (JSON)
- URL, где ID виден (`/api/users/{encodedId}`)
- Клиентские интерфейсы
- Везде, где JavaScript потребляет ID

**Raw Long ID:**

- Операции с БД (первичные ключи, внешние ключи)
- Внутренняя логика обработчиков
- Backend-to-backend коммуникация
- Запросы к БД и связи

**Предпочитай Slug вместо ID:**

- URL контентных сущностей (`/api/skills/{slug}`, а не `/api/skills/{id}`)
- SEO-дружественные маршруты
- Человекочитаемые URL

## Конфигурация окружения

### Назначение Generator ID

Каждому окружению/серверу нужен уникальный generator ID (0-255):

**appsettings.Development.json:**

```json
{
  "IdGenerator": {
    "GeneratorId": 0
  }
}
```

**appsettings.Production.json:**

```json
{
  "IdGenerator": {
    "GeneratorId": 1
  }
}
```

## Тестирование с IdFactory

### Компонентные тесты

```csharp
[Collection(SkillsTestsCollection.Name)]
public class CreateSkillTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CreateSkillTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_create_skill_with_generated_id()
    {
        // Arrange
        var (client, account) = await _fixture.CreateAuthedHttpClient();
        var body = new { title = "TDD", slug = "tdd", description = "Test-Driven Development", content = "..." };

        // Act
        var response = await client.PostAsJsonAsync("/api/skills", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var skill = await _fixture.Database.SingleOrDefault<Skill>(x => x.Slug == "tdd");
        skill.ShouldNotBeNull();
        skill.Id.Should().BeGreaterThan(0);
        skill.Slug.Should().Be("tdd");
    }
}
```

## Устранение неполадок

### Проблема: Дубликаты ID

**Причина:** Два сервера используют один и тот же generator ID

**Решение:** Убедись, что каждый сервер имеет уникальный generator ID в конфигурации

### Проблема: ID не последовательны

**Ожидаемо:** ID примерно упорядочены по времени, не строго последовательны

**Объяснение:** Несколько серверов генерируют ID параллельно, sequence сбрасывается каждую миллисекунду

### Проблема: Потеря точности в JavaScript

**Причина:** Лимит безопасных целых чисел JavaScript в 53 бита

**Решение:** Всегда возвращай ID как строки в JSON-ответах
