using Api.Domain;

namespace Api.Features.Auth;

/// <summary>
/// Public representation of a <see cref="User"/>. Returned by the Auth
/// endpoints; never exposes <c>PasswordHash</c>.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the raw <see cref="long"/> rendered as a string: it stays
/// JS-safe (no 53-bit precision loss) and round-trips verbatim through the
/// BFF's <c>X-User-Id</c> header.
/// </remarks>
public record UserResponse(string Id, string Email, string Name, string? Avatar)
{
    public static UserResponse FromEntity(User user) =>
        new(user.Id.ToString(), user.Email, user.Name, user.Avatar);
}
