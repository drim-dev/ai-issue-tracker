using System.Security.Claims;

namespace Api.Common.Auth;

/// <summary>
/// Trusts the <c>X-User-Id</c> header set by the BFF and turns it into an
/// authenticated <see cref="ClaimsPrincipal"/> carrying a
/// <see cref="ClaimTypes.NameIdentifier"/> claim. The API is not exposed
/// publicly, so the header is trusted as-is. No header → anonymous principal.
/// </summary>
public class UserContextMiddleware(RequestDelegate next)
{
    public const string UserIdHeader = "X-User-Id";
    private const string AuthenticationType = "BffHeader";

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.Request.Headers[UserIdHeader].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                AuthenticationType);
            context.User = new ClaimsPrincipal(identity);
        }

        await next(context);
    }
}
