using System.Security.Claims;
using Api.Common.Exceptions;

namespace Api.Common.Http;

/// <summary>
/// Helpers for reading the BFF-supplied user identity off the current request.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Returns the authenticated user's id, or <c>null</c> when the request is
    /// anonymous (no <c>X-User-Id</c> header).
    /// </summary>
    public static long? GetUserId(this HttpContext context)
    {
        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Returns the authenticated user's id, throwing <see cref="UnauthorizedException"/>
    /// (mapped to 401) when the request is anonymous.
    /// </summary>
    public static long GetRequiredUserId(this HttpContext context) =>
        context.GetUserId()
        ?? throw new UnauthorizedException(
            "Authentication is required to access this resource.",
            "auth:unauthorized");
}
