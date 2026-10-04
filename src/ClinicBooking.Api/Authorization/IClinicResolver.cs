using Microsoft.AspNetCore.Routing;

namespace ClinicBooking.Api.Authorization;

/// <summary>
/// Finds the clinic a request is about, for a clinic-scoped policy (D57). Resolvers are asked in
/// registration order and the first non-null answer wins; when none answers, the policy fails (closed).
/// A feature that addresses clinic-owned resources by their own id registers its own resolver.
/// </summary>
public interface IClinicResolver
{
    ValueTask<long?> ResolveClinicIdAsync(HttpContext httpContext, CancellationToken cancellationToken);
}

/// <summary>Reads the <c>clinicId</c> route value, as in <c>/api/clinics/{clinicId}/doctors</c>.</summary>
public sealed class RouteClinicResolver : IClinicResolver
{
    public const string RouteKey = "clinicId";

    public ValueTask<long?> ResolveClinicIdAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var value = httpContext.GetRouteValue(RouteKey)?.ToString();

        return ValueTask.FromResult<long?>(
            long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
                ? id
                : null);
    }
}
