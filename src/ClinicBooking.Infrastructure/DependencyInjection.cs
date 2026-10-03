using ClinicBooking.Application.Interfaces;
using ClinicBooking.Infrastructure.Health;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Infrastructure.Interceptors;
using ClinicBooking.Infrastructure.Persistence;

namespace ClinicBooking.Infrastructure;

public static class DependencyInjection
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditSaveChangesInterceptor>();

        // No retry strategy yet: it needs the transaction wrapper from D31 (see D46).
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseSqlServer(configuration.GetConnectionString("Default"))
            .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        // UserManager only: SignInManager is cookie-oriented and not used (D29, D48).
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();

        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("database", tags: [ReadyTag]);

        return services;
    }
}
