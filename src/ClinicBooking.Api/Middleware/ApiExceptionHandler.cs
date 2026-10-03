using ClinicBooking.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ClinicBooking.Api.Middleware;

/// <summary>Converts exceptions into RFC 7807 problems whose title is an error key (D10).</summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private const string UnexpectedKey = "error.unexpected";

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ApiExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = Map(exception);

        if (exception is DomainException domainException)
        {
            // Only the key is logged: domain exceptions are expected outcomes.
            _logger.LogInformation(
                "Request failed with {ErrorKey} ({StatusCode})",
                domainException.ErrorKey,
                problem.Status);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Map(Exception exception)
    {
        return exception switch
        {
            InvalidRequestException e => new HttpValidationProblemDetails(
                e.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = e.ErrorKey
            },
            NotFoundException e => Create(StatusCodes.Status404NotFound, e.ErrorKey),
            ConflictException e => Create(StatusCodes.Status409Conflict, e.ErrorKey),
            BusinessRuleException e => Create(StatusCodes.Status422UnprocessableEntity, e.ErrorKey),
            _ => Create(StatusCodes.Status500InternalServerError, UnexpectedKey)
        };
    }

    private static ProblemDetails Create(int status, string errorKey)
    {
        return new ProblemDetails { Status = status, Title = errorKey };
    }
}
