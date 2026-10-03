using System.Security.Claims;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ClinicBooking.Infrastructure.Identity;

public static class IdentitySeeder
{
    /// <summary>
    /// Creates the first user from <c>Seed:AdminUserName</c> / <c>Seed:AdminPassword</c>, with every
    /// global permission, but only when no user exists yet. The password is never logged.
    /// </summary>
    public static async Task SeedInitialUserAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Configuration is checked first so nothing touches the database when seeding is off.
        var userName = configuration["Seed:AdminUserName"];
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            logger.LogInformation("No seed credentials are configured; nothing seeded");
            return;
        }

        if (await users.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var user = new ApplicationUser { UserName = userName };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            // Error codes only: never the password.
            throw new InvalidOperationException(
                "The initial user could not be created: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }

        var granted = await users.AddClaimsAsync(
            user,
            Permissions.Global.Select(p => new Claim(PermissionChecker.ClaimType, p)));
        if (!granted.Succeeded)
        {
            throw new InvalidOperationException(
                "The initial user's permissions could not be granted: " + string.Join(", ", granted.Errors.Select(e => e.Code)));
        }

        logger.LogInformation("Initial user created with {PermissionCount} global permissions", Permissions.Global.Count);
    }
}
