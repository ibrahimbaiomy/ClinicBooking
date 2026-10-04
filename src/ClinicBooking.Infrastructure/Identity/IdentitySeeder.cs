using System.Security.Claims;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ClinicBooking.Infrastructure.Identity;

public static class IdentitySeeder
{
    /// <summary>
    /// Creates the first user from <c>Seed:AdminUserName</c> / <c>Seed:AdminPassword</c>, with every
    /// global permission, when no user exists yet. When users already exist it only tops up the
    /// seeded user: it adds the permissions of <see cref="Permissions.Global"/> that user lacks, so a
    /// permission added to the code later reaches the seeded admin (D48, D55). It never creates a user,
    /// never touches a password or another user, never removes a permission, and logs only a count.
    /// It stops working when the <c>Seed:*</c> settings are removed after the first deploy.
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
            await TopUpSeededUserAsync(users, userName, logger);
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

    private static async Task TopUpSeededUserAsync(UserManager<ApplicationUser> users, string userName, ILogger logger)
    {
        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            return;
        }

        var held = (await users.GetClaimsAsync(user))
            .Where(c => c.Type == PermissionChecker.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);
        var missing = Permissions.Global.Where(p => !held.Contains(p)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var granted = await users.AddClaimsAsync(user, missing.Select(p => new Claim(PermissionChecker.ClaimType, p)));
        if (!granted.Succeeded)
        {
            throw new InvalidOperationException(
                "The seeded user's permissions could not be topped up: " + string.Join(", ", granted.Errors.Select(e => e.Code)));
        }

        logger.LogInformation("Seeded user topped up with {PermissionCount} missing global permissions", missing.Count);
    }
}
