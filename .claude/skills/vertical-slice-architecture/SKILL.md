---
name: vertical-slice-architecture
description: Используй при реализации backend-фич — предоставляет паттерны вертикальных слайсов с MediatR, FluentValidation и IEndpoint (проект)
---

# Реализация Vertical Slice Architecture

## Когда использовать этот скилл

Используй этот скилл когда:

- Реализуешь ЛЮБУЮ backend-фичу (команду или запрос)
- Создаёшь новые вертикальные слайсы в `backend/src/Api/Features/`
- Нужны примеры валидации, авторизации или обработки ошибок

**Для тестирования вертикальных слайсов:** Используй скилл `component-testing`. Он предоставляет паттерны тестирования на основе harness, которые тестируют фичи как целые единицы через HTTP-эндпоинты.

**Объяви в начале:** "Я использую скилл vertical-slice-architecture для реализации этой фичи."

## Структура проекта

### Фичи (вертикальные слайсы)

Каждая фича = один статический класс-файл с вложенными типами:

```text
Features/
├── [Domain]/
│   ├── [Feature].cs          # Один файл, все вложенные типы
│   └── [Feature]Tests.cs     # Совмещённый тест (или в проекте tests/)
```

**Пример:** `Features/Blog/CreatePost.cs`

### Общая инфраструктура (организация по назначению)

Весь общий инфраструктурный код организован в папки по назначению. **Каждый файл принадлежит конкретному назначению, а не общей категории вроде "Middleware".**

```text
Common/
├── Auth/                     # Аутентификация и авторизация
│   └── UserContextMiddleware.cs  # BFF заголовок → Claims конвертация
├── Exceptions/               # Обработка исключений и доменные исключения
│   ├── Exceptions.cs        # NotFoundException, ForbiddenException и т.д.
│   └── ExceptionHandlerMiddleware.cs  # Глобальная обработка исключений
├── Http/                     # HTTP/endpoint инфраструктура
│   ├── IEndpoint.cs         # Интерфейс для регистрации эндпоинтов
│   └── HttpContextExtensions.cs  # Хелперы для извлечения контекста пользователя
├── Identity/                 # Утилиты генерации ID
│   ├── IdFactory.cs         # IdGen обёртка для DI
│   └── Base32Encoder.cs     # Crockford Base32 кодирование/декодирование
└── Validation/               # Инфраструктура валидации
    └── ValidationBehavior.cs  # MediatR pipeline behavior
```

**Соглашение о пространствах имён:** `DrimAgents.Api.Common.{Concern}`
- Пример: `DrimAgents.Api.Common.Auth`, `DrimAgents.Api.Common.Exceptions`, `DrimAgents.Api.Common.Http`

**При создании новых файлов в Common/:**
1. Определи **конкретное назначение** (Auth, Exceptions, Http, Identity, Validation, Caching, Storage и т.д.)
2. Middleware принадлежит тому назначению, которому служит (например, auth middleware → `Common/Auth/`, caching middleware → `Common/Caching/`)
3. Создай новую папку назначения при необходимости (например, `Common/Caching/`, `Common/Storage/`)
4. Размести файл в соответствующей папке назначения
5. Используй пространство имён `DrimAgents.Api.Common.{Concern}`

**Анти-паттерн:** Не создавай общие папки вроде `Middleware/`, `Utilities/`, `Helpers/` — всегда используй конкретное назначение.

## Основной паттерн

Каждый вертикальный слайс следует одной структуре:

1. **Endpoint** — маппит HTTP-маршрут, извлекает пользователя, вызывает MediatR
2. **Request** — MediatR команда/запрос со всеми входными данными
3. **Response** — явный DTO, возвращаемый обработчиком
4. **RequestValidator** — правила FluentValidation
5. **RequestHandler** — бизнес-логика, операции с базой данных

## Краткий справочник

### Минимальный пример команды

```csharp
namespace Api.Features.Blog;

public static class CreatePost
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(WebApplication app)
        {
            app.MapPost("/api/blog/posts", async (
                [FromBody] Body body,
                ISender sender,
                CancellationToken ct) =>
            {
                var request = new Request(body.Title, body.Slug);
                var response = await sender.Send(request, ct);
                return Results.Created($"/api/blog/posts/{response.PostId}", response);
            });
        }

        private record Body(string Title, string Slug);
    }

    public record Request(string Title, string Slug) : IRequest<Response>;
    public record Response(Guid PostId);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Slug).NotEmpty().Matches("^[a-z0-9-]+$");
        }
    }

    public class RequestHandler : IRequestHandler<Request, Response>
    {
        private readonly AppDbContext _db;

        public RequestHandler(AppDbContext db) => _db = db;

        public async Task<Response> Handle(Request request, CancellationToken ct)
        {
            var post = new Post
            {
                Id = Guid.NewGuid(),
                Title = request.Title,
                Slug = request.Slug.ToLower()
            };

            _db.Posts.Add(post);
            await _db.SaveChangesAsync(ct);

            return new Response(post.Id);
        }
    }
}
```

### Минимальный пример запроса

```csharp
namespace Api.Features.Blog;

public static class GetPost
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(WebApplication app)
        {
            app.MapGet("/api/blog/posts/{slug}", async (
                string slug,
                ISender sender,
                CancellationToken ct) =>
            {
                var response = await sender.Send(new Request(slug), ct);
                return response != null ? Results.Ok(response) : Results.NotFound();
            });
        }
    }

    public record Request(string Slug) : IRequest<Response?>;

    public record Response(Guid Id, string Title, string Slug);

    public class RequestHandler : IRequestHandler<Request, Response?>
    {
        private readonly AppDbContext _db;

        public RequestHandler(AppDbContext db) => _db = db;

        public async Task<Response?> Handle(Request request, CancellationToken ct)
        {
            return await _db.Posts
                .Where(p => p.Slug == request.Slug && p.IsPublished)
                .Select(p => new Response(p.Id, p.Title, p.Slug))
                .FirstOrDefaultAsync(ct);
        }
    }
}
```

## Управление конфигурацией

### Паттерн Options

**Всегда используй паттерн Options для доступа к конфигурации** в вертикальных слайсах. Никогда не инжектируй `IConfiguration` напрямую в обработчики.

**Структура:**
```text
Features/
└── [Domain]/
    ├── Options/
    │   └── [Domain]Options.cs    # Класс конфигурации для домена
    └── [Feature].cs               # Фича, использующая IOptions<DomainOptions>
```

**Пример:** `Features/Users/Options/UsersOptions.cs`

```csharp
namespace Api.Features.Users.Options;

public class UsersOptions
{
    public string[] AdminEmails { get; set; } = [];
}
```

**Использование в обработчике:**

```csharp
using Microsoft.Extensions.Options;

public class RequestHandler : IRequestHandler<Request, Response>
{
    private readonly AppDbContext _db;
    private readonly UsersOptions _usersOptions;

    public RequestHandler(AppDbContext db, IOptions<UsersOptions> usersOptions)
    {
        _db = db;
        _usersOptions = usersOptions.Value;
    }

    public async Task<Response> Handle(Request request, CancellationToken ct)
    {
        var isAdmin = _usersOptions.AdminEmails.Contains(request.Email);
        // ...
    }
}
```

**Регистрация в Program.cs:**

```csharp
builder.Services.Configure<UsersOptions>(builder.Configuration.GetSection("Users"));
```

**Конфигурация в appsettings.json:**

```json
{
  "Users": {
    "AdminEmails": ["admin@example.com"]
  }
}
```

**Преимущества:**
- Строго типизированная конфигурация
- Тестируемость (легко мокать options)
- Поддержка валидации (DataAnnotations или FluentValidation)
- Перезагружаемая конфигурация (при использовании `IOptionsMonitor<T>`)

**Правила:**
- Один класс options на домен (например, `UsersOptions`, `CoursesOptions`)
- Размещай в папке `Features/[Domain]/Options/`
- Используй `IOptions<T>` для статической конфигурации
- Используй `IOptionsMonitor<T>` для перезагружаемой конфигурации
- Никогда не инжектируй `IConfiguration` напрямую в обработчики

## Руководство по реализации

Полные паттерны, примеры и анти-паттерны смотри в:

- **[Паттерны реализации](vsa-patterns.md)** — полные шаблоны, типовые паттерны, авторизация, пагинация, тестирование, анти-паттерны

## Чеклист для каждой фичи

При реализации нового вертикального слайса:

- [ ] Создать файл фичи: `Features/[Domain]/[Feature].cs`
- [ ] Реализовать вложенный класс `Endpoint` с методом `MapEndpoint`
- [ ] Определить запись `Request`, реализующую `IRequest<TResponse>`
- [ ] Определить запись `Response` (или `IRequest` для команд без возврата)
- [ ] Создать `RequestValidator`, наследующий `AbstractValidator<Request>`
- [ ] Реализовать правила валидации в конструкторе валидатора
- [ ] Создать `RequestHandler`, реализующий `IRequestHandler<Request, Response>`
- [ ] Инжектировать зависимости в конструкторе обработчика (`AppDbContext`, `ILogger` и т.д.)
- [ ] Реализовать бизнес-логику в методе `Handle`
- [ ] Добавить политику авторизации к эндпоинту при необходимости (`.RequireAuthorization("PolicyName")`)
- [ ] Извлечь пользователя из `HttpContext.User` в эндпоинте при необходимости
- [ ] Добавить структурированное логирование в обработчике (важные операции, ошибки)
- [ ] Написать компонентные тесты (паттерны смотри в скилле `component-testing`)
- [ ] Протестировать happy path, ошибки валидации, граничные случаи, авторизацию
- [ ] Проверить HTTP-ответ и состояние базы данных в тестах
- [ ] Закоммитить с описательным сообщением, следуя conventional commits

## Ключевые правила

1. **Одна фича = один файл** — все вложенные типы в одном статическом классе
2. **Вложенные типы internal/private** — только `Request` и `Response` должны быть public
3. **Всегда используй MediatR** — валидация запускается автоматически через pipeline
4. **Тонкие эндпоинты** — делегируй всю логику обработчику
5. **Используй DTO** — никогда не возвращай EF-сущности напрямую
6. **Компонентные тесты** — тестируй через HTTP-эндпоинт, а не внутренние классы

## Резюме

Vertical Slice Architecture = Одна фича, один файл

**Преимущества:**

- Легко находить и модифицировать фичи (всё в одном месте)
- Консистентная структура для всех фич
- Автоматическая валидация через MediatR pipeline
- Тестируемость в изоляции с помощью компонентных тестов

**Используй этот скилл для каждой backend-фичи для поддержания консистентности и качества.**
