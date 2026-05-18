# Валидация на бэкенде с FluentValidation

Полное руководство по реализации серверной валидации с FluentValidation в DrimAgents.

## Базовые правила валидации

```csharp
using FluentValidation;

namespace DrimAgents.Api.Features.Blog;

public static class CreatePost
{
    public record Request(string Title, string Slug, string Content) : IRequest<Response>;

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty()
                .WithMessage("Title is required")
                .WithErrorCode("blog:post:title:required");

            RuleFor(x => x.Title)
                .MaximumLength(200)
                .WithMessage("Title must be 200 characters or less")
                .WithErrorCode("blog:post:title:too_long");

            RuleFor(x => x.Slug)
                .NotEmpty()
                .WithErrorCode("blog:post:slug:required")
                .Matches("^[a-z0-9-]+$")
                .WithMessage("Slug must be lowercase letters, numbers, and hyphens only")
                .WithErrorCode("blog:post:slug:invalid_format");

            RuleFor(x => x.AuthorEmail)
                .NotEmpty()
                .WithErrorCode("blog:post:author_email:required")
                .EmailAddress()
                .WithMessage("Invalid email format")
                .WithErrorCode("blog:post:author_email:invalid_format");

            RuleFor(x => x.Website)
                .Must(BeValidUrl)
                .When(x => !string.IsNullOrEmpty(x.Website))
                .WithMessage("Invalid URL format")
                .WithErrorCode("blog:post:website:invalid_format");
        }

        private bool BeValidUrl(string? url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
```

## Асинхронная валидация (проверки уникальности)

```csharp
public class RequestValidator : AbstractValidator<Request>
{
    private readonly AppDbContext _db;

    public RequestValidator(AppDbContext db)
    {
        _db = db;

        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithErrorCode("blog:post:slug:required")
            .MustAsync(BeUniqueSlug)
            .WithMessage("A post with this slug already exists")
            .WithErrorCode("blog:post:slug:already_exists");
    }

    private async Task<bool> BeUniqueSlug(string slug, CancellationToken ct)
    {
        var exists = await _db.Posts
            .AnyAsync(p => p.Slug == slug.ToLower(), ct);
        return !exists;
    }
}
```

**ВАЖНО:** Асинхронные валидаторы требуют инжекции DbContext. Зарегистрируй валидатор в DI:

```csharp
// Program.cs
services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Scoped);
```

## Валидация загрузки файлов

```csharp
public record Request(IFormFile Avatar) : IRequest<Response>;

public class RequestValidator : AbstractValidator<Request>
{
    private static readonly string[] AllowedMimeTypes = { "image/jpeg", "image/png", "image/webp" };
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 МБ

    public RequestValidator()
    {
        RuleFor(x => x.Avatar)
            .NotNull()
            .WithMessage("Avatar is required")
            .WithErrorCode("users:avatar:required");

        RuleFor(x => x.Avatar)
            .Must(HaveValidSize)
            .WithMessage($"File size must not exceed {MaxFileSize / 1024 / 1024} MB")
            .WithErrorCode("users:avatar:file_too_large");

        RuleFor(x => x.Avatar)
            .Must(HaveValidMimeType)
            .WithMessage($"File must be one of: {string.Join(", ", AllowedMimeTypes)}")
            .WithErrorCode("users:avatar:invalid_mime_type");

        RuleFor(x => x.Avatar)
            .Must(HaveValidExtension)
            .WithMessage($"File extension must be one of: {string.Join(", ", AllowedExtensions)}")
            .WithErrorCode("users:avatar:invalid_extension");
    }

    private bool HaveValidSize(IFormFile? file)
    {
        return file != null && file.Length > 0 && file.Length <= MaxFileSize;
    }

    private bool HaveValidMimeType(IFormFile? file)
    {
        return file != null && AllowedMimeTypes.Contains(file.ContentType.ToLower());
    }

    private bool HaveValidExtension(IFormFile? file)
    {
        if (file == null) return false;
        var extension = Path.GetExtension(file.FileName).ToLower();
        return AllowedExtensions.Contains(extension);
    }
}
```

## Кросс-поля валидация

```csharp
public record Request(string Password, string PasswordConfirmation) : IRequest<Response>;

public class RequestValidator : AbstractValidator<Request>
{
    public RequestValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty()
            .WithErrorCode("users:password:required")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters")
            .WithErrorCode("users:password:too_short")
            .Matches(@"[A-Z]")
            .WithMessage("Password must contain at least one uppercase letter")
            .WithErrorCode("users:password:missing_uppercase")
            .Matches(@"[a-z]")
            .WithMessage("Password must contain at least one lowercase letter")
            .WithErrorCode("users:password:missing_lowercase")
            .Matches(@"[0-9]")
            .WithMessage("Password must contain at least one number")
            .WithErrorCode("users:password:missing_number");

        RuleFor(x => x.PasswordConfirmation)
            .Equal(x => x.Password)
            .WithMessage("Passwords do not match")
            .WithErrorCode("users:password_confirmation:mismatch");
    }
}
```

## Условная валидация

```csharp
public record Request(bool IsPublished, DateTime? PublishedAt) : IRequest<Response>;

public class RequestValidator : AbstractValidator<Request>
{
    public RequestValidator()
    {
        RuleFor(x => x.PublishedAt)
            .NotNull()
            .WithMessage("Published date is required when publishing")
            .WithErrorCode("blog:post:published_at:required")
            .When(x => x.IsPublished);

        RuleFor(x => x.PublishedAt)
            .LessThanOrEqualTo(DateTime.UtcNow)
            .WithMessage("Published date cannot be in the future")
            .WithErrorCode("blog:post:published_at:future_date")
            .When(x => x.PublishedAt.HasValue);
    }
}
```

## Когда валидировать в валидаторе vs обработчике

**Валидатор (FluentValidation):**

- Валидация формата (regex, длина, email)
- Обязательные поля
- Простые бизнес-правила (сложность пароля)
- Проверки уникальности (асинхронные)
- Кросс-поля валидация
- Валидация файлов (размер, тип, расширение)

**Обработчик (бизнес-логика):**

- Сложные бизнес-правила, требующие нескольких сущностей
- Проверки авторизации (пользователь владеет ресурсом)
- Доменные ограничения
- Переходы состояний (можно публиковать только если черновик)

## Интеграция с ProblemDetails

FluentValidation ошибки автоматически конвертируются в ProblemDetails через MediatR pipeline behavior.

**Настройка (Program.cs):**

```csharp
services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Scoped);
services.AddFluentValidationAutoValidation();

services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

services.AddProblemDetails();
```

**ValidationBehavior (MediatR Pipeline):**

```csharp
using FluentValidation;
using MediatR;

namespace DrimAgents.Api.Common;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .Where(r => !r.IsValid)
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Any())
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
```

**Ответ ProblemDetails:**

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "One or more validation errors occurred",
  "status": 400,
  "errors": {
    "title": ["Title is required"],
    "slug": ["Slug must be lowercase letters, numbers, and hyphens only"]
  },
  "errorCodes": {
    "title": ["blog:post:title:required"],
    "slug": ["blog:post:slug:invalid_format"]
  }
}
```
