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
            var status = problem.Status ?? httpContext.Response.StatusCode;
            problem.Title = status switch
            {
                StatusCodes.Status401Unauthorized => "error.auth.unauthorized",
                StatusCodes.Status403Forbidden => "error.auth.forbidden",
                _ => $"{KeyPrefix}http.{status}"
            };
            problem.Detail = null;
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problem.Extensions["correlationId"] = httpContext.Items[CorrelationIdMiddleware.ItemKey];
    }
}
