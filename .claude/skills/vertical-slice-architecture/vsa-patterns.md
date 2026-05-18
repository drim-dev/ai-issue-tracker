# Vertical Slice Architecture — Паттерны реализации

Полные паттерны реализации и примеры для vertical slice architecture в DrimAgents.

## Полные шаблоны фич

### Паттерн команды (Create/Update/Delete)

```csharp
using System.Security.Claims;
using Api.Common;
using Api.Data;
using Api.Data.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Blog;

public static class CreatePost
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(WebApplication app)
        {
            app.MapPost("/api/blog/posts", async Task<IResult> (
                [FromBody] Body body,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var request = new Request(
                    Title: body.Title,
                    Slug: body.Slug,
                    Content: body.Content,
                    UserId: Guid.Parse(userId));

                var response = await sender.Send(request, cancellationToken);

                return Results.Created($"/api/blog/posts/{response.PostId}", response);
            })
            .RequireAuthorization("Admin")
            .WithTags("Blog")
            .WithOpenApi();
        }

        private record Body(string Title, string Slug, string Content);
    }

    public record Request(
        string Title,
        string Slug,
        string Content,
        Guid UserId) : IRequest<Response>;

    public record Response(Guid PostId);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required")
                .MaximumLength(200).WithMessage("Title must be 200 characters or less");

            RuleFor(x => x.Slug)
                .NotEmpty()
                .MaximumLength(200)
                .Matches("^[a-z0-9-]+$").WithMessage("Slug must be lowercase letters, numbers, and hyphens only");

            RuleFor(x => x.Content)
                .NotEmpty().WithMessage("Content is required");

            RuleFor(x => x.UserId)
                .NotEmpty();
        }
    }

    public class RequestHandler : IRequestHandler<Request, Response>
    {
        private readonly AppDbContext _db;
        private readonly ILogger<RequestHandler> _logger;

        public RequestHandler(AppDbContext db, ILogger<RequestHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
        {
            var exists = await _db.Posts
                .AnyAsync(p => p.Slug == request.Slug.ToLower(), cancellationToken);

            if (exists)
            {
                throw new ValidationException("A post with this slug already exists");
            }

            var post = new Post
            {
                Id = Guid.NewGuid(),
                AuthorId = request.UserId,
                Title = request.Title,
                Slug = request.Slug.ToLower(),
                Content = request.Content,
                IsPublished = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Posts.Add(post);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created post {PostId} with slug {Slug}", post.Id, post.Slug);

            return new Response(post.Id);
        }
    }
}
```

### Паттерн запроса (чтение данных)

```csharp
using Api.Common;
using Api.Data;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Blog;

public static class GetPost
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(WebApplication app)
        {
            app.MapGet("/api/blog/posts/{slug}", async Task<IResult> (
                string slug,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var request = new Request(slug);
                var response = await sender.Send(request, cancellationToken);

                return response != null
                    ? Results.Ok(response)
                    : Results.NotFound();
            })
            .WithTags("Blog")
            .WithOpenApi();
        }
    }

    public record Request(string Slug) : IRequest<Response?>;

    public record Response(
        Guid Id,
        string Title,
        string Slug,
        string Content,
        string AuthorName,
        DateTime PublishedAt);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Slug)
                .NotEmpty()
                .MaximumLength(200);
        }
    }

    public class RequestHandler : IRequestHandler<Request, Response?>
    {
        private readonly AppDbContext _db;

        public RequestHandler(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Response?> Handle(Request request, CancellationToken cancellationToken)
        {
            var post = await _db.Posts
                .Where(p => p.Slug == request.Slug.ToLower() && p.IsPublished)
                .Select(p => new Response(
                    p.Id,
                    p.Title,
                    p.Slug,
                    p.Content,
                    p.Author.DisplayName ?? p.Author.Email,
                    p.PublishedAt!.Value))
                .FirstOrDefaultAsync(cancellationToken);

            return post;
        }
    }
}
```

## Типовые паттерны

### Пагинация

```csharp
public record Request(int Page = 1, int PageSize = 10) : IRequest<Response>;

public record Response(List<PostDto> Posts, int TotalCount, int Page, int PageSize);

public record PostDto(Guid Id, string Title, string Slug);

public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
{
    var query = _db.Posts.Where(p => p.IsPublished);

    var totalCount = await query.CountAsync(cancellationToken);

    var posts = await query
        .OrderByDescending(p => p.PublishedAt)
        .Skip((request.Page - 1) * request.PageSize)
        .Take(request.PageSize)
        .Select(p => new PostDto(p.Id, p.Title, p.Slug))
        .ToListAsync(cancellationToken);

    return new Response(posts, totalCount, request.Page, request.PageSize);
}
```

### Проверка авторизации в обработчике

```csharp
public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
{
    var post = await _db.Posts.FindAsync(request.PostId);
    if (post == null) throw new NotFoundException("Post not found");

    if (post.AuthorId != request.UserId)
    {
        throw new UnauthorizedException("You can only edit your own posts");
    }

    // Продолжение обновления...
}
```

### Кастомные исключения

Создай в `backend/src/Api/Common/Exceptions.cs`:

```csharp
namespace Api.Common;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Unauthorized") : base(message) { }
}
```

### Сложная валидация (кросс-поля, асинхронная)

```csharp
public class RequestValidator : AbstractValidator<Request>
{
    private readonly AppDbContext _db;

    public RequestValidator(AppDbContext db)
    {
        _db = db;

        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x)
            .Must(x => x.StartDate < x.EndDate)
            .WithMessage("Start date must be before end date")
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue);

        RuleFor(x => x.Slug)
            .MustAsync(BeUniqueSlug)
            .WithMessage("Slug must be unique");
    }

    private async Task<bool> BeUniqueSlug(string slug, CancellationToken cancellationToken)
    {
        return !await _db.Posts.AnyAsync(p => p.Slug == slug.ToLower(), cancellationToken);
    }
}
```

### Возврат различных статус-кодов

```csharp
public class Endpoint : IEndpoint
{
    public void MapEndpoint(WebApplication app)
    {
        app.MapPut("/api/blog/posts/{id}", async Task<IResult> (
            Guid id,
            [FromBody] Body body,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var request = new Request(id, body.Title, body.Content, Guid.Parse(userId));
                await sender.Send(request, cancellationToken);
                return Results.NoContent();
            }
            catch (NotFoundException)
            {
                return Results.NotFound();
            }
            catch (UnauthorizedException ex)
            {
                return Results.Problem(ex.Message, statusCode: 403);
            }
        })
        .RequireAuthorization()
        .WithTags("Blog");
    }
}
```

## Анти-паттерны

### НЕ НАДО: Создавать отдельные файлы для каждого вложенного типа

```csharp
// ПЛОХО: Разделение на несколько файлов
// CreatePostEndpoint.cs
// CreatePostRequest.cs
// CreatePostValidator.cs
// CreatePostHandler.cs
```

### НАДО: Держать всё в одном файле

```csharp
// ХОРОШО: CreatePost.cs со всеми вложенными типами
public static class CreatePost
{
    public class Endpoint : IEndpoint { }
    public record Request(...) : IRequest<Response>;
    public record Response(...);
    public class RequestValidator : AbstractValidator<Request> { }
    public class RequestHandler : IRequestHandler<Request, Response> { }
}
```

### НЕ НАДО: Делать вложенные типы публичными

```csharp
// ПЛОХО: Публичные вложенные типы
public static class CreatePost
{
    public class Endpoint : IEndpoint { }  // public
    public class RequestValidator { }       // public
}
```

### НАДО: Оставлять вложенные типы с уровнем доступа по умолчанию

```csharp
// ХОРОШО: Internal/private вложенные типы
public static class CreatePost
{
    class Endpoint : IEndpoint { }              // internal по умолчанию
    private record Body(...);                   // private для использования только в endpoint
    public record Request(...) : IRequest<...>; // public (нужно для MediatR)
}
```

### НЕ НАДО: Обходить валидацию MediatR

```csharp
// ПЛОХО: Прямой вызов обработчика
var handler = new CreatePost.RequestHandler(db, logger);
var response = await handler.Handle(request, cancellationToken);
```

### НАДО: Всегда использовать MediatR (валидация запускается автоматически)

```csharp
// ХОРОШО: MediatR pipeline включает валидацию
var response = await sender.Send(request, cancellationToken);
```

### НЕ НАДО: Размещать бизнес-логику в эндпоинте

```csharp
// ПЛОХО: Вызовы БД и логика в эндпоинте
app.MapPost("/api/posts", async (Body body, AppDbContext db) =>
{
    var post = new Post { Title = body.Title };
    db.Posts.Add(post);
    await db.SaveChangesAsync();
    return Results.Created();
});
```

### НАДО: Держать эндпоинты тонкими — делегировать обработчику

```csharp
// ХОРОШО: Эндпоинт только маппит HTTP, обработчик содержит логику
app.MapPost("/api/posts", async (Body body, ISender sender) =>
{
    var request = new Request(body.Title, body.Content);
    var response = await sender.Send(request, cancellationToken);
    return Results.Created($"/api/posts/{response.Id}", response);
});
```

### НЕ НАДО: Возвращать сущности напрямую

```csharp
// ПЛОХО: Раскрытие EF Core сущностей
public record Response(Post Post);  // Post — это EF-сущность

// Или ещё хуже:
return Results.Ok(post);  // Сериализует весь граф сущности
```

### НАДО: Использовать DTO/Response записи

```csharp
// ХОРОШО: Явная форма ответа
public record Response(Guid Id, string Title, string Slug, DateTime PublishedAt);

var response = new Response(post.Id, post.Title, post.Slug, post.PublishedAt!.Value);
return response;
```

### НЕ НАДО: Использовать магические строки для сообщений валидации

```csharp
// ПЛОХО: Неконсистентные сообщения
RuleFor(x => x.Title).NotEmpty().WithMessage("title required");
RuleFor(x => x.Slug).NotEmpty().WithMessage("The slug field is mandatory");
```

### НАДО: Использовать консистентные, понятные сообщения валидации

```csharp
// ХОРОШО: Понятные, консистентные сообщения
RuleFor(x => x.Title)
    .NotEmpty().WithMessage("Title is required")
    .MaximumLength(200).WithMessage("Title must be 200 characters or less");
```

## Паттерны тестирования

**Для полного руководства по тестированию используй скилл `component-testing`.**

Скилл `component-testing` предоставляет:

- **Тестирование на основе harness** — абстрагирование внешних зависимостей (БД, Redis, Kafka и т.д.)
- **Компонентные тесты** — тестирование всей фичи через HTTP-эндпоинт, а не внутренних классов
- **Гибкие зависимости** — выбор между реальными зависимостями (TestContainers) или моками
- **xUnit fixtures** — правильное управление жизненным циклом тестов и параллелизмом
- **Arrange/Act/Assert** — подготовка данных, вызов эндпоинта, проверка ответа и побочных эффектов

**Быстрый пример** (полные детали в скилле component-testing):

```csharp
[Collection(AuthTestsCollection.Name)]
public class CreateAccountTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CreateAccountTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_create_account()
    {
        // Arrange
        var request = new CreateAccountRequestContract("Sam", "Qwer1234!");

        // Act
        var client = new RestClient(_fixture.HttpClient.CreateClient());
        var response = await client.ExecutePostAsync<AccountContract>(
            "/auth/accounts", request, CreateCancellationToken());

        // Assert HTTP-ответ
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Data.Login.Should().Be("sam");

        // Assert состояние БД через harness
        var dbAccount = await _fixture.Database.SingleOrDefault<Account>(
            x => x.Login == "sam", CreateCancellationToken());

        dbAccount.Should().NotBeNull();
        dbAccount.PasswordHash.Should().NotBeEmpty();
    }
}
```

**Почему компонентные тесты?**

- **Тестируют поведение, а не реализацию** — рефакторинг внутренних классов не ломает тесты
- **Реалистичные** — тесты проходят полный цикл запрос/ответ
- **Поддерживаемые** — изменения внутренней структуры не требуют переписывания тестов
- **Гибкие** — начни с моков, переключись на реальные зависимости позже (через harness)

**Когда использовать unit-тесты:**

- Сложная логика валидации (правила FluentValidation)
- Сложные алгоритмы или вычисления
- Граничные случаи, которые трудно вызвать через HTTP

**Пример структуры тестов:**

```csharp
[Collection(BlogTestsCollection.Name)]
public class CreatePostTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CreatePostTests(TestFixture fixture) => _fixture = fixture;
    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] public async Task Should_create_post() { }
    [Fact] public async Task Should_return_validation_error_for_invalid_slug() { }
    [Fact] public async Task Should_return_conflict_for_duplicate_slug() { }
    [Fact] public async Task Should_require_authentication() { }
    [Fact] public async Task Should_require_admin_role() { }
}
```

## Чеклист тестирования

Для каждой фичи пиши компонентные тесты, проверяющие:

### Компонентные тесты (через HTTP-эндпоинт)

- [ ] Happy path: валидный запрос возвращает ожидаемый ответ
- [ ] Ответ имеет правильный статус-код (200, 201, 204 и т.д.)
- [ ] Тело ответа соответствует ожидаемой форме
- [ ] Заголовки ответа правильные (Location и т.д.)
- [ ] Состояние БД корректно после операции (через harness)
- [ ] Ошибки валидации возвращают 400 с problem details
- [ ] Сценарии «не найдено» возвращают 404
- [ ] Неавторизованные запросы возвращают 401
- [ ] Запрещённые запросы возвращают 403
- [ ] Ограничения уникальности обработаны корректно
- [ ] Граничные случаи: граничные значения, пустые коллекции и т.д.

### Unit-тесты (для сложной логики)

- [ ] Сложные правила валидации в FluentValidation
- [ ] Сложные алгоритмы или вычисления
- [ ] Граничные случаи, трудно вызываемые через HTTP
