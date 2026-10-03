using ClinicBooking.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicBooking.Tests.Support;

/// <summary>
/// The real API on a fresh database inside the shared container. The Development
/// environment makes startup apply the real migrations (D14). The database is
/// dropped when the fixture is disposed.
/// </summary>
public sealed class ApiDatabaseFixture : IAsyncLifetime
{
    private readonly string _masterConnectionString;
    private readonly string _databaseName = $"cb_test_{Guid.NewGuid():N}";

    public ApiDatabaseFixture(SqlServerFixture sqlServer)
    {
        _masterConnectionString = sqlServer.ConnectionString;

        var connection = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = _databaseName
        };

        Factory = new DatabaseApiFactory(connection.ConnectionString, Clock, User);
    }

    public WebApplicationFactory<Program> Factory { get; }

    public TestClock Clock { get; } = new();

    public TestUser User { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();

        // Pooled connections would keep the database busy.
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{_databaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class DatabaseApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly TestClock _clock;
        private readonly TestUser _user;

        public DatabaseApiFactory(string connectionString, TestClock clock, TestUser user)
        {
            _connectionString = connectionString;
            _clock = clock;
            _user = user;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _connectionString,
                    ["Jwt:SigningKey"] = TestJwt.SigningKey
                }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);

                services.RemoveAll<IUser>();
                services.AddSingleton<IUser>(_user);
            });
        }
    }
}
