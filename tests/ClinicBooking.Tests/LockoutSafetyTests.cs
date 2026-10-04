using System.Net;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

/// <summary>The rules that keep the system from losing its last administrator (D57).</summary>
[Collection(SqlServerCollection.Name)]
public class LockoutSafetyTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public LockoutSafetyTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task An_administrator_cannot_disable_their_own_account()
    {
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        var (_, _) = await UserApi.AdminAsync(_fixture, _client); // another administrator exists: still refused

        var response = await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{adminId}/disable", admin);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "error.user.cannot_disable_self");
        Assert.True((await UserApi.DetailAsync(_client, admin, adminId)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_remove_their_own_users_manage()
    {
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        await UserApi.DeactivateOtherAdministratorsAsync(_fixture.Factory.Services, adminId);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{adminId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Clinics.Manage } });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "error.user.last_administrator");
        var detail = await UserApi.DetailAsync(_client, admin, adminId);
        Assert.Contains(Permissions.Users.Manage, UserApi.Strings(detail.GetProperty("globalPermissions")));
        // Nothing else was applied either: the replace is all or nothing.
        Assert.DoesNotContain(Permissions.Clinics.Manage, UserApi.Strings(detail.GetProperty("globalPermissions")));
    }

    [Fact]
    public async Task A_disabled_administrator_does_not_count_as_another_administrator()
    {
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        var (_, otherId) = await UserApi.AdminAsync(_fixture, _client);
        await UserApi.SetActiveAsync(_fixture.Factory.Services, otherId, false);
        await UserApi.DeactivateOtherAdministratorsAsync(_fixture.Factory.Services, adminId);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{adminId}/global-permissions", admin, new { permissions = Array.Empty<string>() });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "error.user.last_administrator");
    }

    [Fact]
    public async Task With_another_active_administrator_an_administrator_may_give_up_users_manage()
    {
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        var (_, _) = await UserApi.AdminAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{adminId}/global-permissions", admin, new { permissions = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The next request already sees it.
        await AssertProblemAsync(
            await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users", admin), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task One_administrator_may_remove_users_manage_from_another()
    {
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        var (other, otherId) = await UserApi.AdminAsync(_fixture, _client);
        await UserApi.DeactivateOtherAdministratorsAsync(_fixture.Factory.Services, adminId, otherId);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{otherId}/global-permissions", admin, new { permissions = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertProblemAsync(
            await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users", other), HttpStatusCode.Forbidden, "error.auth.forbidden");
        Assert.Equal(1, await UserApi.CountActiveAdministratorsAsync(_fixture.Factory.Services));
    }

    [Fact]
    public async Task The_service_refuses_to_disable_the_last_active_administrator_whoever_asks()
    {
        // Through the API the actor is always another active administrator, so this rule only bites in a
        // race. The service is called directly with an actor who is not an administrator to prove it.
        var (_, adminId) = await UserApi.AdminAsync(_fixture, _client);
        await UserApi.DeactivateOtherAdministratorsAsync(_fixture.Factory.Services, adminId);
        var actor = await CreateUserAsync(_fixture.Factory.Services);

        using var factory = _fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUser>();
            services.AddSingleton<IUser>(new TestUser { Id = actor.Id });
        }));
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(() => users.DisableAsync(adminId, CancellationToken.None));

        Assert.Equal("error.user.last_administrator", failure.ErrorKey);
        Assert.Equal(1, await UserApi.CountActiveAdministratorsAsync(_fixture.Factory.Services));
    }

    [Fact]
    public async Task Two_administrators_disabling_each_other_at_the_same_moment_never_leave_nobody()
    {
        for (var round = 0; round < 6; round++)
        {
            var (tokenA, idA) = await UserApi.AdminAsync(_fixture, _client);
            var (tokenB, idB) = await UserApi.AdminAsync(_fixture, _client);
            await UserApi.DeactivateOtherAdministratorsAsync(_fixture.Factory.Services, idA, idB);

            var results = await Task.WhenAll(
                UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{idB}/disable", tokenA),
                UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{idA}/disable", tokenB));

            // At least one active administrator remains, whatever the interleaving.
            Assert.True(
                await UserApi.CountActiveAdministratorsAsync(_fixture.Factory.Services) >= 1,
                $"round {round}: both administrators were disabled");

            // Each answer is a success, a refusal by the rule, an unauthorised caller (the other request got
            // there first) or a deadlock conflict: never a server error.
            Assert.All(results, response => Assert.Contains(
                response.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Unauthorized, HttpStatusCode.Conflict }));
        }
    }
}
