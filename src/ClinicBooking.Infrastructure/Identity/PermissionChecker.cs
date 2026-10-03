using ClinicBooking.Application.Interfaces;
using ClinicBooking.Infrastructure.Persistence;

namespace ClinicBooking.Infrastructure.Identity;

/// <summary>Global permissions are stored as Identity user claims of type <see cref="ClaimType"/>.</summary>
public sealed class PermissionChecker : IPermissionChecker
{
    public const string ClaimType = "permission";

    private readonly AppDbContext _db;

    public PermissionChecker(AppDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasGlobalPermissionAsync(long userId, string permission, CancellationToken cancellationToken) =>
        _db.UserClaims.AsNoTracking().AnyAsync(
            c => c.UserId == userId && c.ClaimType == ClaimType && c.ClaimValue == permission,
            cancellationToken);

    public async Task<IReadOnlyList<string>> GetGlobalPermissionsAsync(long userId, CancellationToken cancellationToken)
    {
        var permissions = await _db.UserClaims.AsNoTracking()
            .Where(c => c.UserId == userId && c.ClaimType == ClaimType && c.ClaimValue != null)
            .Select(c => c.ClaimValue!)
            .ToListAsync(cancellationToken);

        return permissions.Order(StringComparer.Ordinal).ToList();
    }
}
