using System.Net;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Infrastructure.Persistence;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// The protected administrator (D64): never disabled, never loses users.manage, no admin password reset;
/// the flag cannot be set through the API.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ProtectedUserTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public ProtectedUserTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    /// <summary>A protected administrator and another administrator who acts on them.</summary>
    private async Task<(string Admin, long ProtectedId, string ProtectedToken)> SetupAsync()
    {
        var (protectedToken, protectedId) = await UserApi.AdminAsync(_fixture, _client, Permissions.Clinics.Manage);
        await UserApi.SetProtectedAsync(_fixture.Factory.Services, protectedId);
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        return (admin, protectedId, protectedToken);
    }

    [Fact]
    public async Task The_flag_is_returned_in_the_detail_and_the_list()
    {
        var (admin, protectedId, _) = await SetupAsync();
        var other = await UserApi.CreateUserAsync(_client, admin);

        Assert.True((await UserApi.DetailAsync(_client, admin, protectedId)).GetProperty("isProtected").GetBoolean());
        Assert.False((await UserApi.DetailAsync(_client, admin, other.Id)).GetProperty("isProtected").GetBoolean());

        var list = await ProblemAsync(await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users?pageSize=100&sortBy=createdAt&sortDirection=desc", admin));
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt64() == protectedId);
        Assert.True(item.GetProperty("isProtected").GetBoolean());
    }

    [Fact]
    public async Task A_protected_user_cannot_be_disabled()
    {
        var (admin, protectedId, _) = await SetupAsync();

        var response = await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{protectedId}/disable", admin);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "error.user.protected_cannot_disable");
        Assert.True((await UserApi.DetailAsync(_client, admin, protectedId)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task A_protected_user_cannot_lose_users_manage_but_other_permissions_change_freely()
    {
        var (admin, protectedId, _) = await SetupAsync();

        var refused = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{protectedId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Clinics.Manage } });
        await AssertProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "error.user.protected_keeps_users_manage");

        var allowed = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{protectedId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Users.Manage, Permissions.Specialties.Manage } });
        await AssertStatusAsync(allowed, HttpStatusCode.OK);

        var detail = await UserApi.DetailAsync(_client, admin, protectedId);
        Assert.Equal(
            [Permissions.Specialties.Manage, Permissions.Users.Manage],
            UserApi.Strings(detail.GetProperty("globalPermissions")).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_protected_user_without_users_manage_may_be_granted_it()
    {
        var (admin, protectedId, _) = await SetupAsync();
        await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{protectedId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Users.Manage } });
        // Simulate a migrated first user who never held users.manage.
        await UserApi.RemoveGlobalPermissionDirectlyAsync(_fixture.Factory.Services, protectedId, Permissions.Users.Manage);

        var withoutIt = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{protectedId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Clinics.Manage } });
        await AssertStatusAsync(withoutIt, HttpStatusCode.OK);

        var granted = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{protectedId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Users.Manage } });
        await AssertStatusAsync(granted, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_resets_a_protected_users_password_and_it_still_works()
    {
        var (admin, protectedId, _) = await SetupAsync();
        var stampBefore = await SecurityStampAsync(protectedId);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, $"/api/users/{protectedId}/reset-password", admin,
            new { temporaryPassword = UserApi.NewTemporaryPassword() });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "error.user.protected_reset_refused");
        Assert.Equal(stampBefore, await SecurityStampAsync(protectedId));
        Assert.False((await UserApi.DetailAsync(_client, admin, protectedId)).GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task The_flag_cannot_be_set_through_the_api()
    {
        var (admin, _, _) = await SetupAsync();

        // Create ignores an unknown member; the new user is not protected.
        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin,
            new { userName = UserApi.NewUserName(), temporaryPassword = UserApi.NewTemporaryPassword(), isProtected = true });
        await AssertStatusAsync(response, HttpStatusCode.Created);

        Assert.False((await ProblemAsync(response)).GetProperty("isProtected").GetBoolean());
    }

    [Fact]
    public async Task Other_users_keep_the_admin_reset_and_can_be_disabled()
    {
        var (admin, _, _) = await SetupAsync();
        var other = await UserApi.CreateUserAsync(_client, admin);

        await AssertStatusAsync(
            await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{other.Id}/reset-password", admin,
                new { temporaryPassword = UserApi.NewTemporaryPassword() }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(
            await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{other.Id}/disable", admin),
            HttpStatusCode.OK);
    }

    private async Task<string?> SecurityStampAsync(long userId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture)))!.SecurityStamp;
    }
}

/// <summary>The seeder marks the first user it creates as protected (D64).</summary>
[Collection(SqlServerCollection.Name)]
public class ProtectedSeedTests : IClassFixture<SeededAuthFixture>
{
    private readonly SeededAuthFixture _fixture;

    public ProtectedSeedTests(SeededAuthFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_seeded_first_user_is_protected_and_a_top_up_changes_nothing_else()
    {
        _ = _fixture.Factory.CreateClient(); // startup seeds the user
        await _fixture.Factory.Services.SeedInitialUserAsync(); // a later start: top-up only

        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var all = users.Users.ToList();

        var seeded = Assert.Single(all, u => u.UserName == SeededAuthFixture.UserName);
        Assert.True(seeded.IsProtected);
        Assert.Single(all, u => u.IsProtected);
    }
}

/// <summary>The AddProtectedUser migration flags the existing user with the lowest id (D64).</summary>
[Collection(SqlServerCollection.Name)]
public class ProtectedUserMigrationTests
{
    private const string MigrationBefore = "20261008070008_AddPatients";

    private readonly SqlServerFixture _sqlServer;

    public ProtectedUserMigrationTests(SqlServerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    [Fact]
    public async Task The_existing_user_with_the_lowest_id_becomes_protected_whatever_its_state()
    {
        var database = $"cb_migration_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(_sqlServer.ConnectionString) { InitialCatalog = database }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

        try
        {
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(MigrationBefore);

                // The first user is disabled: it is flagged all the same.
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO [AspNetUsers] ([UserName], [NormalizedUserName], [EmailConfirmed], [PhoneNumberConfirmed], " +
                    "[TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [IsActive], [MustChangePassword], [CreatedAt]) VALUES " +
                    "(N'first', N'FIRST', 0, 0, 0, 1, 0, 0, 0, SYSDATETIMEOFFSET()), " +
                    "(N'second', N'SECOND', 0, 0, 0, 1, 0, 1, 0, SYSDATETIMEOFFSET());");

                await migrator.MigrateAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var flags = await db.Users
                    .OrderBy(u => u.Id)
                    .Select(u => new { u.UserName, u.IsProtected })
                    .ToListAsync();

                Assert.Equal([("first", true), ("second", false)], flags.Select(f => (f.UserName!, f.IsProtected)).ToArray());
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(_sqlServer.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
            await command.ExecuteNonQueryAsync();
        }
    }
}
