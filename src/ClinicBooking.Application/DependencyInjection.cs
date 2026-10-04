using ClinicBooking.Application.Features.Auth;
using ClinicBooking.Application.Features.Clinics;
using ClinicBooking.Application.Features.Specialties;
using ClinicBooking.Application.Features.Users;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Application.Validators;
using FluentValidation;

namespace ClinicBooking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISpecialtyService, SpecialtyService>();
        services.AddScoped<IClinicService, ClinicService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IClinicAccess, ClinicAccess>();

        // Every validator in this assembly, so a new entity needs no registration code.
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

        return services;
    }
}
