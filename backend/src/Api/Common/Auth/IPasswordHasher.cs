namespace Api.Common.Auth;

/// <summary>
/// Hashes and verifies passwords. Implementations produce self-contained
/// encoded strings (algorithm, parameters and salt embedded).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password into an encoded string for storage.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored encoded hash.</summary>
    bool Verify(string password, string hash);
}
