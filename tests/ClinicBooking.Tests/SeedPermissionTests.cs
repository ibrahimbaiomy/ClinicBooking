using System.Security.Claims;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicBooking.Tests;

/// <summary>The seeder grants each global permission to the seeded admin at most once (D57, option 2).</summary>
[Collection(SqlServerCollection.Name)]
public class SeedRemovalTests : IClassFixture<SeededAuthFixture>
{
    private readonly SeededAuthFixture _fixture;

    public SeedRemovalTests(SeededAuthFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_permission_an_administrator_removed_stays_removed_after_a_restart()
    {
        _ = _fixture.Factory.CreateClient(); // startup seeds the user

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var seeded = (await users.FindByNameAsync(SeededAuthFixture.UserName))!;
            // What the API does when an administrator removes the permission: only the permission claim goes.
            await users.RemoveClaimAsync(seeded, new Claim(PermissionChecker.ClaimType, Permissions.Specialties.Manage));
        }

        await _fixture.Factory.Services.SeedInitialUserAsync(); // the startup top-up

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var seeded = (await users.FindByNameAsync(SeededAuthFixture.UserName))!;
            var held = (await users.GetClaimsAsync(seeded))
                .Where(c => c.Type == PermissionChecker.ClaimType)
                .Select(c => c.Value)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.DoesNotContain(Permissions.Specialties.Manage, held);
            Assert.Equal(
                Permissions.Global.Where(p => p != Permissions.Specialties.Manage).Order(StringComparer.Ordinal).ToArray(),
                held);
        }
    }

    [Fact]
    public async Task The_seeded_admin_has_no_clinic_scoped_permission_it_must_grant_it_to_itself()
    {
        _ = _fixture.Factory.CreateClient();
        await _fixture.Factory.Services.SeedInitialUserAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seeded = (await users.FindByNameAsync(SeededAuthFixture.UserName))!;
        var db = scope.ServiceProvider.GetRequiredService<ClinicBooking.Infrastructure.Persistence.AppDbContext>();

        Assert.Empty(await db.UserClinicPermissions.Where(p => p.UserId == seeded.Id).ToListAsync());
        Assert.DoesNotContain(
            await users.GetClaimsAsync(seeded),
            c => c.Type == PermissionChecker.ClaimType && Permissions.IsClinicScoped(c.Value));
    }
}

[Collection(SqlServerCollection.Name)]
public class SeedFirstRunAfterUpgradeTests : IClassFixture<SeededAuthFixture>
{
    private readonly SeededAuthFixture _fixture;

    public SeedFirstRunAfterUpgradeTests(SeededAuthFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_first_run_after_the_upgrade_marks_what_the_admin_holds_without_duplicating_it()
    {
        _ = _fixture.Factory.CreateClient();

        // A database from before the markers existed: the permissions are held, nothing is marked.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var seeded = (await users.FindByNameAsync(SeededAuthFixture.UserName))!;
            var markers = (await users.GetClaimsAsync(seeded)).Where(c => c.Type == PermissionChecker.SeededClaimType).ToList();
            Assert.Equal(Permissions.Global.Count, markers.Count);
            await users.RemoveClaimsAsync(seeded, markers);
        }

        await _fixture.Factory.Services.SeedInitialUserAsync();
        await _fixture.Factory.Services.SeedInitialUserAsync(); // and again: nothing more happens

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var seeded = (await users.FindByNameAsync(SeededAuthFixture.UserName))!;
            var claims = await users.GetClaimsAsync(seeded);

            Assert.Equal(
                Permissions.Global.Order(StringComparer.Ordinal).ToArray(),
                claims.Where(c => c.Type == PermissionChecker.ClaimType).Select(c => c.Value).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(
                Permissions.Global.Order(StringComparer.Ordinal).ToArray(),
                claims.Where(c => c.Type == PermissionChecker.SeededClaimType).Select(c => c.Value).Order(StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public async Task The_seeded_admin_is_created_active_without_a_forced_password_change()
    {
        _ = _fixture.Factory.CreateClient();

        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seeded = (await users.FindByNameAsync(SeededAuthFixture.UserName))!;

        Assert.True(seeded.IsActive);
        Assert.False(seeded.MustChangePassword);
    }
}
