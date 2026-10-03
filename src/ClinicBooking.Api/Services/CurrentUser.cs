using System.Security.Claims;
using ClinicBooking.Application.Interfaces;

namespace ClinicBooking.Api.Services;

/// <summary>
/// Reads the caller from the JWT claims. There is no authentication yet, so until the
/// auth step <see cref="Id"/> is always null (system or anonymous).
/// </summary>
public sealed class CurrentUser : IUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public long? Id =>
        long.TryParse(_httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
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
