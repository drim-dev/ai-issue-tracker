namespace Api.Common.Exceptions;

/// <summary>
/// Base for domain errors. Carries an HTTP status code and a stable
/// <c>domain:entity:operation:error_type</c> error code surfaced in ProblemDetails.
/// </summary>
public class DomainException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public DomainException(string message, string errorCode, int statusCode = StatusCodes.Status422UnprocessableEntity)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

public class NotFoundException(string message, string errorCode)
    : DomainException(message, errorCode, StatusCodes.Status404NotFound);

public class ForbiddenException(string message, string errorCode)
    : DomainException(message, errorCode, StatusCodes.Status403Forbidden);

public class ConflictException(string message, string errorCode)
    : DomainException(message, errorCode, StatusCodes.Status409Conflict);

public class UnprocessableEntityException(string message, string errorCode)
    : DomainException(message, errorCode, StatusCodes.Status422UnprocessableEntity);

public class UnauthorizedException(string message, string errorCode)
    : DomainException(message, errorCode, StatusCodes.Status401Unauthorized);
