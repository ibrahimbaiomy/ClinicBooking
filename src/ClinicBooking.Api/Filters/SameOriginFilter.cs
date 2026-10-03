using ClinicBooking.Api.Authentication;
using ClinicBooking.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ClinicBooking.Api.Filters;

/// <summary>
/// CSRF defence in depth for the cookie-based endpoints (refresh, logout), on top of
/// <c>SameSite=Strict</c>: a request that carries an <c>Origin</c> header must come from this
/// host or from <c>Auth:AllowedOrigins</c>. Requests without <c>Origin</c> (non-browser clients) pass.
/// The host is compared without the scheme, so it still works behind a TLS-terminating proxy.
/// </summary>
public sealed class SameOriginFilter : IAsyncActionFilter
{
    private readonly OriginCheckOptions _options;

    public SameOriginFilter(IOptions<OriginCheckOptions> options)
    {
        _options = options.Value;
    }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var origin = request.Headers.Origin.ToString();

        if (origin.Length > 0 && !IsAllowed(origin, request.Host.ToString()))
        {
            throw new ForbiddenException("error.auth.forbidden");
        }

        return next();
    }

    private bool IsAllowed(string origin, string requestHost)
    {
        // "null" (sandboxed pages) and anything that is not an absolute URL are rejected.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var originHost = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        if (string.Equals(originHost, requestHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _options.AllowedOrigins.Any(allowed =>
            string.Equals(allowed.TrimEnd('/'), uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
    }
}
