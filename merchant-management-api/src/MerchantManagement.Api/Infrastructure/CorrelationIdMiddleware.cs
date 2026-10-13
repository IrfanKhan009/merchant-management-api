using System.Text.RegularExpressions;

namespace MerchantManagement.Api.Infrastructure;

/// <summary>Accepts or creates an X-Correlation-Id, echoes it on the response and adds it to every log entry.</summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ValidId();

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        // Reject anything that could be used for log injection or is unreasonably long.
        if (string.IsNullOrEmpty(id) || !ValidId().IsMatch(id)) id = Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = id;
        // OnStarting so the header survives the exception handler clearing the response.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(context);
    }
}
