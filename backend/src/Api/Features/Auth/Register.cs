using Api.Common.Auth;
using Api.Common.Exceptions;
using Api.Common.Http;
using Api.Common.Identity;
using Api.Common.Persistence;
using Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Features.Auth;

/// <summary>
/// Registers a new account: <c>POST /auth/register</c>. Anonymous.
/// </summary>
public static class Register
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/auth/register", async Task<IResult> (
                    [FromBody] Body body,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var request = new Request(body.Email, body.Name, body.Password);
                    var response = await sender.Send(request, ct);
                    return Results.Created($"/auth/users/{response.Id}", response);
                })
                .AllowAnonymous()
                .WithTags("Auth");
        }

        private record Body(string Email, string Name, string Password);
    }

    public record Request(string Email, string Name, string Password) : IRequest<UserResponse>;

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .WithErrorCode("auth:user:email:required")
                .EmailAddress().WithMessage("Email must be a valid email address")
                .WithErrorCode("auth:user:email:invalid_format");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required")
                .WithErrorCode("auth:user:name:required")
                .MaximumLength(100).WithMessage("Name must be 100 characters or less")
                .WithErrorCode("auth:user:name:too_long");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .WithErrorCode("auth:user:password:required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters")
                .WithErrorCode("auth:user:password:too_short");
        }
    }

    public class RequestHandler(
        AppDbContext db,
        IdFactory idFactory,
        IPasswordHasher passwordHasher,
        ILogger<RequestHandler> logger)
        : IRequestHandler<Request, UserResponse>
    {
        public async Task<UserResponse> Handle(Request request, CancellationToken ct)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                throw new ConflictException(
                    "An account with this email already exists.",
                    "auth:user:email:already_exists");
            }

            var user = new User
            {
                Id = idFactory.Create(),
                Email = email,
                Name = request.Name.Trim(),
                PasswordHash = passwordHasher.Hash(request.Password),
                CreatedAt = DateTime.UtcNow,
            };

            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Lost a race against a concurrent registration with the same email.
                throw new ConflictException(
                    "An account with this email already exists.",
                    "auth:user:email:already_exists");
            }

            logger.LogInformation("Registered user {UserId}", user.Id);

            return UserResponse.FromEntity(user);
        }
    }
}
