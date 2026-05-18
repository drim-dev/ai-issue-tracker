using Api.Common.Exceptions;
using Api.Common.Http;
using Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Auth;

/// <summary>
/// Returns the authenticated user: <c>GET /auth/me</c>. Requires the
/// BFF-supplied <c>X-User-Id</c> header.
/// </summary>
public static class GetCurrentUser
{
    public class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/auth/me", async Task<IResult> (
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    // Throws UnauthorizedException (401) when the request is anonymous.
                    var userId = httpContext.GetRequiredUserId();
                    var response = await sender.Send(new Request(userId), ct);
                    return Results.Ok(response);
                })
                .WithTags("Auth");
        }
    }

    public record Request(long UserId) : IRequest<UserResponse>;

    public class RequestHandler(AppDbContext db) : IRequestHandler<Request, UserResponse>
    {
        public async Task<UserResponse> Handle(Request request, CancellationToken ct)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct);

            if (user is null)
            {
                // Session is valid but the account was deleted.
                throw new NotFoundException(
                    "The current user no longer exists.",
                    "auth:user:get:not_found");
            }

            return UserResponse.FromEntity(user);
        }
    }
}
