using System.Text.RegularExpressions;
using Serilog.Context;

namespace ClinicBooking.Api.Middleware;

/// <summary>
/// Accepts an incoming <c>X-Correlation-Id</c> (when it is a safe token), otherwise
/// generates one; returns it in the response and attaches it to every log event.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = SafeToken().IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;

        // Set when the response starts: the exception handler clears headers already set.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await _next(context);
        }
    }

    // Restricted charset and length so a caller cannot inject log content.
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SafeToken();
}
