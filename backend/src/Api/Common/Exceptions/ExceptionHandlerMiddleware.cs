using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Api.Common.Exceptions;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 <see cref="ProblemDetails"/> responses.
/// Validation failures become 400 with an <c>errors</c> dictionary; domain errors use
/// their declared status; everything else is a 500.
/// </summary>
public class ExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlerMiddleware> logger,
    IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case ValidationException validationEx:
                problem = CreateProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "One or more validation errors occurred.",
                    "validation:failed");
                problem.Extensions["errors"] = validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => JsonNamingPolicy.CamelCase.ConvertName(g.Key),
                        g => g.Select(e => e.ErrorMessage).ToArray());
                problem.Extensions["errorCodes"] = validationEx.Errors
                    .Where(e => !string.IsNullOrEmpty(e.ErrorCode))
                    .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorCode).ToArray());
                logger.LogWarning(exception, "Validation failed");
                break;

            case DomainException domainEx:
                problem = CreateProblemDetails(
                    context,
                    domainEx.StatusCode,
                    GetTitle(domainEx.StatusCode),
                    domainEx.Message,
                    domainEx.ErrorCode);
                logger.LogWarning(exception, "Domain exception: {ErrorCode}", domainEx.ErrorCode);
                break;

            default:
                problem = CreateProblemDetails(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Internal Server Error",
                    env.IsDevelopment()
                        ? exception.Message
                        : "An unexpected error occurred. Please try again later.",
                    "server:internal_error");
                logger.LogError(exception, "Unhandled exception");
                break;
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string errorCode) => new()
    {
        Type = "https://tools.ietf.org/html/rfc7807",
        Title = title,
        Status = statusCode,
        Detail = detail,
        Instance = context.Request.Path,
        Extensions =
        {
            ["errorCode"] = errorCode,
            ["traceId"] = context.TraceIdentifier,
        },
    };

    private static string GetTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        _ => "Error",
    };
}
