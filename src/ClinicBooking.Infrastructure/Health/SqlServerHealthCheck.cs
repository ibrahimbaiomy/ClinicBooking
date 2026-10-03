using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClinicBooking.Infrastructure.Health;

/// <summary>Readiness check: opens a connection and runs <c>SELECT 1</c>.</summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    private const int TimeoutSeconds = 3;

    private readonly string? _connectionString;

    public SqlServerHealthCheck(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default");
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return HealthCheckResult.Unhealthy("Connection string 'Default' is not configured.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString)
            {
                ConnectTimeout = TimeoutSeconds
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = TimeoutSeconds;
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception is deliberately not attached: probes run every few seconds and
            // would flood the log with stack traces. Its type is enough to diagnose.
            return HealthCheckResult.Unhealthy($"Database is not reachable ({ex.GetType().Name}).");
        }
    }
}
