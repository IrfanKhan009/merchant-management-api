using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace MerchantManagement.Api.Infrastructure;

/// <summary>
/// Converts exceptions to RFC 7807 problem responses. Unexpected errors are logged in full but the
/// client only ever receives a generic message, so internals (SQL, paths, connection strings) never leak.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
            // Backstop for races: the unique index fired even though the pre-check passed (SQLite constraint = 19).
            DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } } =>
                (StatusCodes.Status409Conflict, "Conflict", "The request conflicts with existing data."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred",
                  "The server could not process the request.")
        };

        if (status >= 500) logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else logger.LogInformation("Request rejected with {Status}: {Title}", status, title);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cid)) problem.Extensions["correlationId"] = cid;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}
