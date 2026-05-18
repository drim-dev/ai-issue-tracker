# Общие паттерны валидации

Переиспользуемые паттерны валидации для типовых сценариев на бэкенде и фронтенде.

## Валидация Slug

**Бэкенд:**

```csharp
RuleFor(x => x.Slug)
    .NotEmpty()
    .WithErrorCode("blog:post:slug:required")
    .MaximumLength(100)
    .WithErrorCode("blog:post:slug:too_long")
    .Matches("^[a-z0-9-]+$")
    .WithMessage("Slug must be lowercase letters, numbers, and hyphens only")
    .WithErrorCode("blog:post:slug:invalid_format");
```

**Фронтенд:**

```typescript
slug: z
  .string()
  .min(1, 'Slug is required')
  .max(100, 'Slug must be 100 characters or less')
  .regex(/^[a-z0-9-]+$/, 'Slug must be lowercase letters, numbers, and hyphens only')
```

## Валидация Email

**Бэкенд:**

```csharp
RuleFor(x => x.Email)
    .NotEmpty()
    .WithErrorCode("users:email:required")
    .EmailAddress()
    .WithMessage("Invalid email format")
    .WithErrorCode("users:email:invalid_format");
```

**Фронтенд:**

```typescript
email: z
  .string()
  .min(1, 'Email is required')
  .email('Invalid email format')
```

## Валидация URL

**Бэкенд:**

```csharp
RuleFor(x => x.Website)
    .Must(BeValidUrl)
    .When(x => !string.IsNullOrEmpty(x.Website))
    .WithErrorCode("blog:post:website:invalid_format");

private bool BeValidUrl(string? url)
{
    return Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
```

**Фронтенд:**

```typescript
website: z
  .string()
  .url('Invalid URL format')
  .optional()
  .or(z.literal(''))
```

## Валидация даты

**Бэкенд:**

```csharp
RuleFor(x => x.PublishedAt)
    .LessThanOrEqualTo(DateTime.UtcNow)
    .WithMessage("Published date cannot be in the future")
    .WithErrorCode("blog:post:published_at:future_date");
```

**Фронтенд:**

```typescript
publishedAt: z
  .string()
  .datetime()
  .refine((date) => new Date(date) <= new Date(), {
    message: 'Published date cannot be in the future'
  })
```
