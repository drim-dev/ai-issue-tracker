---
name: id-generation
description: Используй при создании сущностей или реализации фич, которым нужны ID — предоставляет паттерны IdGen для распределённой, упорядоченной по времени генерации ID (проект)
---

# Стратегия генерации ID

**Паттерн:** Slug + Long ID (IdGen) + Crockford Base32 кодирование

Используй этот скилл при создании сущностей или реализации фич, которым нужны ID.

## Когда использовать этот скилл

- Создание новых доменных сущностей (Skill, Course, User и т.д.)
- Реализация фич, генерирующих ID
- Кодирование ID для публичных API или URL
- Понимание стратегии и паттернов ID

## Обзор стратегии ID

**Трёхчастная стратегия:**

1. **Первичные ключи БД**: Long ID, сгенерированные IdGen (распределённые, упорядоченные по времени)
2. **Публичные API**: Crockford Base32 закодированные ID (URL-безопасные, компактные, ~13 символов)
3. **URL контента**: Slug для человекочитаемых URL (где применимо)

**Преимущества:**

- **Без обращения к БД** — ID генерируются в приложении
- **Упорядочены по времени** — естественная хронологическая сортировка
- **Распределённые** — не нужна координация между серверами
- **Компактное кодирование** — 13 символов вместо 19 цифр
- **URL-безопасные** — Crockford Base32 в URL
- **Человекопонятные** — slug для контента (навыки, курсы, блог)

## Руководство по реализации

Подробные паттерны и примеры смотри в:

- **[Руководство по реализации](id-generation-guide.md)** — конфигурация IdGen, паттерны сущностей, кодирование, тестирование, устранение неполадок

## Краткий справочник

### Сущность с ID

```csharp
// Domain/Skills/Skill.cs
public class Skill
{
    public long Id { get; set; }              // Raw long ID (первичный ключ)
    public string Slug { get; set; }          // "test-driven-development"
    public string Title { get; set; }
    public long AuthorId { get; set; }        // FK к users
}
```

### Генерация ID в обработчике

```csharp
public class RequestHandler : IRequestHandler<Request, Response>
{
    private readonly AppDbContext _db;
    private readonly IdFactory _idFactory;

    public RequestHandler(AppDbContext db, IdFactory idFactory)
    {
        _db = db;
        _idFactory = idFactory;
    }

    public async Task<Response> Handle(Request request, CancellationToken ct)
    {
        var skill = new Skill
        {
            Id = _idFactory.Create(),
            Slug = request.Slug,
            Title = request.Title
        };

        await _db.Skills.AddAsync(skill, ct);
        await _db.SaveChangesAsync(ct);

        return new Response(skill.Id);
    }
}
```

### Кодирование ID для ответа

```csharp
public record SkillResponse(string Id, string Slug, string Title)
{
    public static SkillResponse FromEntity(Skill skill)
    {
        return new SkillResponse(
            IdEncoding.Encode(skill.Id),
            skill.Slug,
            skill.Title);
    }
}
```

### Ответ публичного API

```json
{
  "id": "3g5w7x9y1z2a",
  "slug": "test-driven-development",
  "title": "Test-Driven Development"
}
```

### Паттерны URL

```text
GET /api/skills/test-driven-development      // Slug предпочтителен для контента
GET /api/users/3g5w7x9y1z2a                  // Закодированный ID для внутренних сущностей
```

## Ключевые правила

1. **БД**: Всегда используй raw `long` ID для первичных и внешних ключей
2. **Публичные API**: Всегда кодируй ID в JSON-ответах через `IdEncoding.Encode()`
3. **URL**: Предпочитай slug для контентных сущностей, закодированные ID для внутренних
4. **Никогда не декодируй в запросах**: Используй slug или raw ID, не закодированные ID
5. **Инжектируй IdFactory**: Используй DI для инжекции `IdFactory` в обработчики
6. **Генерируй до сохранения**: Вызывай `_idFactory.Create()` перед добавлением в DbContext

## Конфигурация

```json
{
  "IdGen": {
    "GeneratorId": 0,
    "Epoch": "2024-01-01T00:00:00Z"
  }
}
```

**GeneratorId:** Уникален для каждого экземпляра сервера (0-255)
**Epoch:** Начальная дата для расчёта timestamp (рекомендуется 2024-01-01)
