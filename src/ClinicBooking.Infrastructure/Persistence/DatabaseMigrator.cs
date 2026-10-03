namespace ClinicBooking.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>Applies pending migrations (D14). Called at startup in Development only.</summary>
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
