using ClinicBooking.Application.Interfaces;
using ClinicBooking.Infrastructure.Health;
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

        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("database", tags: [ReadyTag]);

        return services;
    }
}
