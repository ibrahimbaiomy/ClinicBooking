using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Middleware;

internal static class ProblemDetailsEnricher
{
    private const string KeyPrefix = "error.";

    /// <summary>
    /// Guarantees the title is an error key and adds <c>traceId</c> and
    /// <c>correlationId</c>. Framework-generated problems carry English titles,
    /// which are replaced by <c>error.http.{status}</c>.
    /// </summary>
    public static void Enrich(ProblemDetails problem, HttpContext httpContext)
    {
        if (problem.Title is null || !problem.Title.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            problem.Title = $"{KeyPrefix}http.{problem.Status ?? httpContext.Response.StatusCode}";
            problem.Detail = null;
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problem.Extensions["correlationId"] = httpContext.Items[CorrelationIdMiddleware.ItemKey];
    }
}
