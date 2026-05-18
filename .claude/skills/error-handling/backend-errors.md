# Обработка ошибок на бэкенде

Полное руководство по обработке ошибок с доменными исключениями и ProblemDetails в DrimAgents.

## Доменные исключения

Создай кастомные типы исключений для различных сценариев ошибок.

```csharp
// Common/Exceptions/DomainException.cs
namespace DrimAgents.Api.Common.Exceptions;

public class DomainException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public DomainException(string message, string errorCode, int statusCode = 422)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string message, string errorCode)
        : base(message, errorCode, StatusCodes.Status404NotFound)
    {
    }
}

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message, string errorCode)
        : base(message, errorCode, StatusCodes.Status403Forbidden)
    {
    }
}

public class ConflictException : DomainException
{
    public ConflictException(string message, string errorCode)
        : base(message, errorCode, StatusCodes.Status409Conflict)
    {
    }
}

public class UnprocessableEntityException : DomainException
{
    public UnprocessableEntityException(string message, string errorCode)
        : base(message, errorCode, StatusCodes.Status422UnprocessableEntity)
    {
    }
}
```

## Использование доменных исключений

```csharp
// Features/Blog/DeletePost.cs
public class RequestHandler : IRequestHandler<Request>
{
    private readonly AppDbContext _db;
    private readonly ILogger<RequestHandler> _logger;

    public RequestHandler(AppDbContext db, ILogger<RequestHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(Request request, CancellationToken ct)
    {
        var post = await _db.Posts.FindAsync(request.PostId, ct);

        if (post == null)
        {
            throw new NotFoundException(
                $"Post with ID {request.PostId} not found",
                "blog:post:delete:not_found");
        }

        if (post.AuthorId != request.UserId)
        {
            throw new ForbiddenException(
                "You do not have permission to delete this post",
                "blog:post:delete:forbidden");
        }

        if (post.IsPublished)
        {
            throw new UnprocessableEntityException(
                "Cannot delete a published post. Unpublish it first.",
                "blog:post:delete:is_published");
        }

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted post {PostId} by user {UserId}", request.PostId, request.UserId);
    }
}
```

## Глобальный обработчик исключений

Конвертирует все исключения в ProblemDetails.

```csharp
// Common/Exceptions/GlobalExceptionHandler.cs
using DrimAgents.Api.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DrimAgents.Api.Common.Exceptions;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            DomainException domainEx => CreateProblemDetails(
                httpContext,
                domainEx.StatusCode,
                GetTitle(domainEx.StatusCode),
                domainEx.Message,
                domainEx.ErrorCode),

            UnauthorizedAccessException => CreateProblemDetails(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Authentication is required to access this resource",
                "auth:unauthorized"),

            _ => CreateProblemDetails(
                httpContext,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                _env.IsDevelopment()
                    ? exception.Message
                    : "An unexpected error occurred. Please try again later.",
                "server:internal_error")
        };

        if (exception is DomainException)
        {
            _logger.LogWarning(exception, "Domain exception occurred: {ErrorCode}",
                (exception as DomainException)!.ErrorCode);
        }
        else
        {
            _logger.LogError(exception, "Unhandled exception occurred");
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? 500;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string errorCode)
    {
        return new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["traceId"] = context.TraceIdentifier
            }
        };
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        500 => "Internal Server Error",
        _ => "Error"
    };
}
```

**Регистрация в Program.cs:**

```csharp
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
```

## Ошибки базы данных

Обработка специфичных ошибок БД (ограничения, дедлоки и т.д.).

```csharp
public async Task Handle(Request request, CancellationToken ct)
{
    try
    {
        var skill = new Skill
        {
            Id = _idFactory.Create(),
            Slug = request.Slug,
            Name = request.Name
        };

        await _db.Skills.AddAsync(skill, ct);
        await _db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
    {
        // Нарушение уникального ограничения (дубликат slug)
        if (pgEx.SqlState == "23505")
        {
            throw new ConflictException(
                "A skill with this slug already exists",
                "skills:skill:create:slug_exists");
        }

        // Нарушение внешнего ключа
        if (pgEx.SqlState == "23503")
        {
            throw new UnprocessableEntityException(
                "Referenced entity does not exist",
                "skills:skill:create:invalid_reference");
        }

        throw;
    }
}
```

## Ошибки авторизации

```csharp
// Features/Users/UpdateProfile.cs
public class RequestHandler : IRequestHandler<Request, Response>
{
    public async Task<Response> Handle(Request request, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync(request.UserId, ct);

        if (user == null)
        {
            throw new NotFoundException(
                $"User with ID {request.UserId} not found",
                "users:profile:update:not_found");
        }

        if (user.Id != request.CurrentUserId && !request.IsAdmin)
        {
            throw new ForbiddenException(
                "You do not have permission to update this profile",
                "users:profile:update:forbidden");
        }

        user.Name = request.Name;
        user.Bio = request.Bio;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new Response(user.Id);
    }
}
```
