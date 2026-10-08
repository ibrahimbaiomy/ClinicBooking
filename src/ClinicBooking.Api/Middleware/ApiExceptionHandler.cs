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

        if (exception is AccountLockedException { RetryAfter: { } retryAfter } && retryAfter > TimeSpan.Zero)
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

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
            UnauthorizedException e => Create(StatusCodes.Status401Unauthorized, e.ErrorKey),
            ForbiddenException e => Create(StatusCodes.Status403Forbidden, e.ErrorKey),
            AccountLockedException e => Create(StatusCodes.Status423Locked, e.ErrorKey),
            NotFoundException e => Create(StatusCodes.Status404NotFound, e.ErrorKey),
            ConflictException e => Create(StatusCodes.Status409Conflict, e.ErrorKey),
            DuplicatePhoneException e => WithMatches(Create(StatusCodes.Status409Conflict, e.ErrorKey), e),
            BusinessRuleException e => WithDetails(Create(StatusCodes.Status422UnprocessableEntity, e.ErrorKey), e.Details),
            _ => Create(StatusCodes.Status500InternalServerError, UnexpectedKey)
        };
    }

    // The matches are personal data: they go to the caller only, never to a log (the log line above has the key only).
    private static ProblemDetails WithMatches(ProblemDetails problem, DuplicatePhoneException exception)
    {
        problem.Extensions["matchCount"] = exception.MatchCount;
        if (exception.Matches.Count > 0)
        {
            problem.Extensions["matches"] = exception.Matches
                .Select(m => new Dictionary<string, object> { ["id"] = m.Id, ["name"] = m.Name, ["phone"] = m.Phone })
                .ToList();
        }

        return problem;
    }

    private static ProblemDetails WithDetails(ProblemDetails problem, IReadOnlyDictionary<string, long> details)
    {
        foreach (var (name, value) in details)
        {
            problem.Extensions[name] = value;
        }

        return problem;
    }

    private static ProblemDetails Create(int status, string errorKey)
    {
        return new ProblemDetails { Status = status, Title = errorKey };
    }
}
