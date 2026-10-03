using System.Globalization;
using ClinicBooking.Application.Interfaces;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ClinicBooking.Api.Services;

/// <summary>
/// Reads the caller from the access token's <c>sub</c> claim (inbound claim mapping is off, so
/// the name is the raw JWT one). Null means anonymous: the audit fields then record the system.
/// </summary>
public sealed class CurrentUser : IUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public long? Id =>
        long.TryParse(
            _httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var id)
            ? id
            : null;
}

public static class CurrentUserExtensions
{
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUser, CurrentUser>();
        return services;
    }
}
