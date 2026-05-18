namespace Api.Common.Auth;

/// <summary>
/// Registers authentication services. Identity itself is supplied by the BFF
/// via the trusted <c>X-User-Id</c> header (see <see cref="UserContextMiddleware"/>),
/// so this only wires up password hashing.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuth(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        return services;
    }
}
