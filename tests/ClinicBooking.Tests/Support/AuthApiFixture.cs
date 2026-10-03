using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicBooking.Tests.Support;

/// <summary>
/// The real API with the real <c>CurrentUser</c> (unlike <see cref="ApiDatabaseFixture"/>), a
/// movable clock, and a fresh database dropped on disposal. Development, so migrations run.
/// </summary>
public class AuthApiFixture : IAsyncLifetime
{
    private readonly string _masterConnectionString;
    private readonly string _databaseName = $"cb_auth_{Guid.NewGuid():N}";

    public AuthApiFixture(SqlServerFixture sqlServer)
        : this(sqlServer, new Dictionary<string, string?>())
    {
    }

    protected AuthApiFixture(SqlServerFixture sqlServer, Dictionary<string, string?> extraConfiguration)
    {
        _masterConnectionString = sqlServer.ConnectionString;

        var connection = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = _databaseName
        };

        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connection.ConnectionString,
            ["Jwt:SigningKey"] = TestJwt.SigningKey,
            // High by default so ordinary tests never trip the limiter.
            ["RateLimiting:LoginPerMinute"] = "1000",
            ["RateLimiting:RefreshPerMinute"] = "1000"
        };
        foreach (var (key, value) in extraConfiguration)
        {
            configuration[key] = value;
        }

        Factory = new AuthFactory(configuration, Clock);
    }

    public WebApplicationFactory<Program> Factory { get; }

    public TestClock Clock { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
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

    private sealed class AuthFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _configuration;
        private readonly TestClock _clock;

        public AuthFactory(Dictionary<string, string?> configuration, TestClock clock)
        {
            _configuration = configuration;
            _clock = clock;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_configuration));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);

                services.AddControllers().AddApplicationPart(typeof(AuthApiFixture).Assembly);
            });
        }
    }
}

/// <summary>Login limit of 3 per minute, to test the rate limiter.</summary>
public sealed class LowRateLimitAuthFixture : AuthApiFixture
{
    public LowRateLimitAuthFixture(SqlServerFixture sqlServer)
        : base(sqlServer, new Dictionary<string, string?> { ["RateLimiting:LoginPerMinute"] = "3" })
    {
    }
}

/// <summary>Seed credentials configured, so startup creates the first user.</summary>
public sealed class SeededAuthFixture : AuthApiFixture
{
    public const string UserName = "seed.admin";
    public const string Password = "Seeded-Password-123";

    public SeededAuthFixture(SqlServerFixture sqlServer)
        : base(sqlServer, new Dictionary<string, string?>
        {
            ["Seed:AdminUserName"] = UserName,
            ["Seed:AdminPassword"] = Password
        })
    {
    }
}
