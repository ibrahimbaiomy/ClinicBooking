using ClinicBooking.Domain.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Middleware;

public static class ErrorHandlingExtensions
{
    private const string InvalidFieldKey = "error.validation.invalid";

    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
                ProblemDetailsEnricher.Enrich(context.ProblemDetails, context.HttpContext);
        });

        services.AddExceptionHandler<ApiExceptionHandler>();
        services.Configure<ExceptionHandlerOptions>(options =>
        {
            // Domain exceptions are expected outcomes; do not log them as unhandled errors.
            options.SuppressDiagnosticsCallback = context => context.Exception is DomainException;
        });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            // Model-binding failures (shape only): framework messages are English,
            // so only the field names are kept and each gets a generic key.
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .ToDictionary(entry => entry.Key, _ => new[] { InvalidFieldKey });

                var problem = new HttpValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "error.validation.failed"
                };
                ProblemDetailsEnricher.Enrich(problem, context.HttpContext);

                return new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

        return services;
    }
}
