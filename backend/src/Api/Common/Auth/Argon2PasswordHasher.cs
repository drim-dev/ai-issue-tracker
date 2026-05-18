using Isopoh.Cryptography.Argon2;

namespace Api.Common.Auth;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by Argon2id. Produces a self-contained
/// encoded string; tuning parameters are fixed constants — they are embedded in
/// the hash, so older hashes still verify if the constants change later.
/// </summary>
public class Argon2PasswordHasher : IPasswordHasher
{
    // OWASP-aligned Argon2id parameters.
    private const int TimeCost = 3;          // iterations
    private const int MemoryCost = 65536;    // 64 MB (KiB)
    private const int Parallelism = 1;       // lanes / threads
    private const int HashLength = 32;       // bytes

    public string Hash(string password) =>
        Argon2.Hash(
            password: password,
            timeCost: TimeCost,
            memoryCost: MemoryCost,
            parallelism: Parallelism,
            type: Argon2Type.HybridAddressing,
            hashLength: HashLength);

    public bool Verify(string password, string hash) =>
        Argon2.Verify(hash, password);
}
