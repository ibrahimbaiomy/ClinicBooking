using System.Globalization;
using ClinicBooking.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ClinicBooking.Api.Authorization;

/// <summary>
/// Succeeds when the caller holds the permission in the clinic the request addresses, read from the
/// database on every call so a revoked grant applies immediately (D34, D57). The clinic comes from
/// the registered <see cref="IClinicResolver"/>s. No clinic, a missing or deleted clinic, a disabled
/// user, or a permission held only in another clinic all fail the requirement (403).
/// </summary>
public sealed class ClinicPermissionAuthorizationHandler : AuthorizationHandler<ClinicPermissionRequirement>
{
    private readonly IPermissionChecker _permissions;
    private readonly IEnumerable<IClinicResolver> _resolvers;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClinicPermissionAuthorizationHandler(
        IPermissionChecker permissions,
        IEnumerable<IClinicResolver> resolvers,
        IHttpContextAccessor httpContextAccessor)
    {
        _permissions = permissions;
        _resolvers = resolvers;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ClinicPermissionRequirement requirement)
    {
        var sub = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!long.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            return;
        }

        var http = context.Resource as HttpContext ?? _httpContextAccessor.HttpContext;
        if (http is null)
        {
            return;
        }

        var cancellationToken = http.RequestAborted;
        foreach (var resolver in _resolvers)
        {
            if (await resolver.ResolveClinicIdAsync(http, cancellationToken) is { } clinicId)
            {
                if (await _permissions.HasClinicPermissionAsync(userId, clinicId, requirement.Permission, cancellationToken))
                {
                    context.Succeed(requirement);
                }

                return;
            }
        }
    }
}
