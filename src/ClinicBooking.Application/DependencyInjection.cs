using ClinicBooking.Application.Features.Auth;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Application.Validators;
using FluentValidation;

namespace ClinicBooking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();

        // Every validator in this assembly, so a new entity needs no registration code.
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

        return services;
    }
}
