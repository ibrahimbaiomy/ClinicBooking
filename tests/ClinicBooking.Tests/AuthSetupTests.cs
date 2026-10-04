using System.Net;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class RateLimitTests : IClassFixture<LowRateLimitAuthFixture>
{
    private readonly LowRateLimitAuthFixture _fixture;

    public RateLimitTests(LowRateLimitAuthFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_is_rate_limited_with_a_key_and_retry_after()
    {
        var client = _fixture.Factory.CreateClient();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var allowed = await LoginAsync(client, "nobody", "Wrong-Password-1234");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var limited = await LoginAsync(client, "nobody", "Wrong-Password-1234");

        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "error.auth.rate_limited");
        Assert.True(limited.Headers.Contains("Retry-After"));
    }
}

[Collection(SqlServerCollection.Name)]
public class SeedTests : IClassFixture<SeededAuthFixture>
{
    private readonly SeededAuthFixture _fixture;

    public SeedTests(SeededAuthFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Seeded_user_can_log_in_and_holds_every_global_permission()
    {
        var client = _fixture.Factory.CreateClient();

        var login = await LoginAsync(client, SeededAuthFixture.UserName, SeededAuthFixture.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.SendAsync(WithBearer(HttpMethod.Get, "/api/auth/me", await AccessTokenAsync(login)));
        var body = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            Permissions.Global.Order(StringComparer.Ordinal).ToArray(),
            body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray());
    }

    [Fact]
    public async Task Seeding_again_does_nothing_when_users_exist()
    {
        _ = _fixture.Factory.CreateClient();
        int before;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            before = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users.CountAsync();
        }

        await _fixture.Factory.Services.SeedInitialUserAsync();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Equal(before, await users.Users.CountAsync()); // no user is created
        }
    }

    [Fact]
    public async Task A_global_permission_never_granted_by_the_seeder_is_topped_up_for_the_seeded_user_only()
    {
        _ = _fixture.Factory.CreateClient();
        var missing = Permissions.Global[0];
        var other = await AuthHelpers.CreateUserAsync(_fixture.Factory.Services); // no permissions at all

        long seededId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var seeded = (await users.FindByNameAsync(SeededAuthFixture.UserName))!;
            seededId = seeded.Id;
            // As if the permission had been added to the code after the seeder last ran: neither held nor marked.
            await users.RemoveClaimAsync(seeded, new Claim(PermissionChecker.ClaimType, missing));
            await users.RemoveClaimAsync(seeded, new Claim(PermissionChecker.SeededClaimType, missing));
            await users.AddClaimAsync(seeded, new Claim("custom", "kept")); // not a global permission
        }

        await _fixture.Factory.Services.SeedInitialUserAsync();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var seeded = (await users.FindByIdAsync(seededId.ToString()))!;
            var seededClaims = await users.GetClaimsAsync(seeded);
            Assert.Equal(
                Permissions.Global.Order(StringComparer.Ordinal),
                seededClaims.Where(c => c.Type == PermissionChecker.ClaimType).Select(c => c.Value).Order(StringComparer.Ordinal));
            Assert.Contains(seededClaims, c => c.Type == "custom" && c.Value == "kept"); // nothing is removed

            var untouched = (await users.FindByIdAsync(other.Id.ToString()))!;
            Assert.Empty(await users.GetClaimsAsync(untouched)); // other users are never touched
            Assert.Equal(2, await users.Users.CountAsync()); // the seeded user and the other one: none is created
        }
    }

    [Fact]
    public async Task The_top_up_is_idempotent_and_leaves_the_password_alone()
    {
        var client = _fixture.Factory.CreateClient();

        await _fixture.Factory.Services.SeedInitialUserAsync();
        await _fixture.Factory.Services.SeedInitialUserAsync();

        var login = await LoginAsync(client, SeededAuthFixture.UserName, SeededAuthFixture.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await client.SendAsync(WithBearer(HttpMethod.Get, "/api/auth/me", await AccessTokenAsync(login)));
        var body = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Permissions.Global.Count, body.GetProperty("permissions").GetArrayLength());
    }

    [Fact]
    public async Task A_seed_name_that_matches_nobody_creates_no_user_and_changes_nothing()
    {
        _ = _fixture.Factory.CreateClient();

        // The host seeds at startup: a different seed name finds no such user.
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:AdminUserName"] = "someone.else" })));
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByNameAsync("someone.else"));
    }
}

[Collection(SqlServerCollection.Name)]
public class SeedOffTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;

    public SeedOffTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Without_seed_settings_nothing_is_created_and_no_permission_is_granted()
    {
        _ = _fixture.Factory.CreateClient();
        var user = await AuthHelpers.CreateUserAsync(_fixture.Factory.Services);

        await _fixture.Factory.Services.SeedInitialUserAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(await users.GetClaimsAsync((await users.FindByIdAsync(user.Id.ToString()))!));
    }
}

public class JwtKeyStartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void Startup_fails_when_the_signing_key_is_missing_or_too_short(string? key)
    {
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = key })));

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Jwt settings are invalid", failure.ToString());
        if (!string.IsNullOrEmpty(key))
        {
            Assert.DoesNotContain(key, failure.ToString());
        }
    }
}
