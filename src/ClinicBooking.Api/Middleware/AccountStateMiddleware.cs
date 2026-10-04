using System.Globalization;
using ClinicBooking.Api.Authorization;
using ClinicBooking.Application.Features.Auth;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ClinicBooking.Api.Middleware;

/// <summary>
/// The account gate (D57). On every authenticated request to a protected endpoint it reads the user's
/// state from the database (one primary-key lookup, never a token claim, never cached), so:
/// a disabled or missing user is cut off at once, even with an access token that has not expired, and a
/// user with a temporary password can only reach endpoints marked
/// <see cref="AllowWhilePasswordChangeRequiredAttribute"/>. It sits after authentication and before
/// authorization, so it covers every endpoint, including future ones that forget a policy.
/// Failures are domain exceptions, so the error handler writes the usual problem.
/// </summary>
public sealed class AccountStateMiddleware
{
    private readonly RequestDelegate _next;

    public AccountStateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserAccounts accounts)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null
            || context.User.Identity?.IsAuthenticated != true
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        var sub = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!long.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            throw new UnauthorizedException(AuthService.UnauthorizedKey);
        }

        var state = await accounts.GetStateAsync(userId, context.RequestAborted);
        if (state is null || !state.IsActive)
        {
            throw new UnauthorizedException(AuthService.UnauthorizedKey);
        }

        if (state.MustChangePassword
            && endpoint.Metadata.GetMetadata<AllowWhilePasswordChangeRequiredAttribute>() is null)
        {
            throw new ForbiddenException(AuthService.PasswordChangeRequiredKey);
        }

        await _next(context);
    }
}
