using ClinicBooking.Infrastructure.Health;

namespace ClinicBooking.Infrastructure;

public static class DependencyInjection
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("database", tags: [ReadyTag]);

        return services;
    }
}
