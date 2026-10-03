using System.Net;
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

        await _fixture.Factory.Services.SeedInitialUserAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(1, await users.Users.CountAsync());
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
