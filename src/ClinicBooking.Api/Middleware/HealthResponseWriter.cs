using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClinicBooking.Api.Middleware;

public static class HealthResponseWriter
{
    public const string NotReadyKey = "error.health.not_ready";

    public static async Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        if (report.Status == HealthStatus.Healthy)
        {
            httpContext.Response.ContentType = MediaTypeNames.Application.Json;
            await httpContext.Response.WriteAsync("{\"status\":\"Healthy\"}");
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = NotReadyKey
        };
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }
}
