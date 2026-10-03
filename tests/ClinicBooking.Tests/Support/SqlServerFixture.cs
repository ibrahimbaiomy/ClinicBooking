using Testcontainers.MsSql;

namespace ClinicBooking.Tests.Support;

/// <summary>
/// One real SQL Server container for the whole test run (D30). Requires Docker.
/// Each test class gets its own database inside it, see <see cref="ApiDatabaseFixture"/>.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Same pin as docker-compose.yml.
    private const string Image = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
