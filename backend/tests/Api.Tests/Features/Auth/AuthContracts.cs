namespace Api.Tests.Features.Auth;

/// <summary>HTTP request/response contracts for the Auth endpoints under test.</summary>
public record RegisterRequestContract(string Email, string Name, string Password);

public record LoginRequestContract(string Email, string Password);

public record UserResponseContract(string Id, string Email, string Name, string? Avatar);

/// <summary>Minimal RFC 7807 shape needed to assert error responses.</summary>
public record ProblemDetailsContract(
    string Title,
    int Status,
    string? Detail,
    string? ErrorCode,
    Dictionary<string, string[]>? Errors,
    Dictionary<string, string[]>? ErrorCodes);
