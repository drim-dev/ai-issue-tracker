using Api.Common.Auth;
using Api.Common.Exceptions;
using Api.Common.Http;
using Api.Common.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Auth;

/// <summary>
/// Authenticates an account: <c>POST /auth/login</c>. Anonymous.
/// </summary>
public static class Login
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/auth/login", async Task<IResult> (
                    [FromBody] Body body,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var request = new Request(body.Email, body.Password);
                    var response = await sender.Send(request, ct);
                    return Results.Ok(response);
                })
                .AllowAnonymous()
                .WithTags("Auth");
        }

        private record Body(string Email, string Password);
    }

    public record Request(string Email, string Password) : IRequest<UserResponse>;

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .WithErrorCode("auth:user:email:required");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .WithErrorCode("auth:user:password:required");
        }
    }

    public class RequestHandler(AppDbContext db, IPasswordHasher passwordHasher)
        : IRequestHandler<Request, UserResponse>
    {
        public async Task<UserResponse> Handle(Request request, CancellationToken ct)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            // Same error whether the email is unknown or the password is wrong —
            // never reveal which accounts exist.
            if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            {
                throw new UnauthorizedException(
                    "Invalid email or password.",
                    "auth:login:invalid_credentials");
            }

            return UserResponse.FromEntity(user);
        }
    }
}
