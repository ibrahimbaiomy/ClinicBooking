using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Persistence;

namespace ClinicBooking.Infrastructure.Identity;

/// <summary>
/// Global permissions are stored as Identity user claims of type <see cref="ClaimType"/>; clinic-scoped
/// ones in <c>UserClinicPermissions</c> (D34, D57). Every <c>Has*</c> check reads the database and is
/// false for a disabled user. The soft-delete filter on <c>Clinics</c> makes a deleted clinic grant nothing.
/// </summary>
public sealed class PermissionChecker : IPermissionChecker
{
    public const string ClaimType = "permission";

    /// <summary>Marks a permission the seeder has already granted once (D57); not a permission itself.</summary>
    public const string SeededClaimType = "seeded_permission";

    private readonly AppDbContext _db;

    public PermissionChecker(AppDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasGlobalPermissionAsync(long userId, string permission, CancellationToken cancellationToken) =>
        _db.UserClaims.AsNoTracking().AnyAsync(
            c => c.UserId == userId
                && c.ClaimType == ClaimType
                && c.ClaimValue == permission
                && _db.Users.Any(u => u.Id == userId && u.IsActive),
            cancellationToken);

    public async Task<IReadOnlyList<string>> GetGlobalPermissionsAsync(long userId, CancellationToken cancellationToken)
    {
        var permissions = await _db.UserClaims.AsNoTracking()
            .Where(c => c.UserId == userId && c.ClaimType == ClaimType && c.ClaimValue != null)
            .Select(c => c.ClaimValue!)
            .ToListAsync(cancellationToken);

        return permissions.Where(Permissions.IsGlobal).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    public Task<bool> HasClinicPermissionAsync(
        long userId,
        long clinicId,
        string permission,
        CancellationToken cancellationToken) =>
        HasClinicPermissionInAnyAsync(userId, [clinicId], permission, cancellationToken);

    public Task<bool> HasClinicPermissionInAnyAsync(
        long userId,
        IReadOnlyCollection<long> clinicIds,
        string permission,
        CancellationToken cancellationToken)
    {
        var ids = clinicIds.ToArray();

        // A global permission never satisfies a clinic-scoped one: only this table is read.
        return _db.UserClinicPermissions.AsNoTracking().AnyAsync(
            p => p.UserId == userId
                && p.Permission == permission
                && ids.Contains(p.ClinicId)
                && _db.Clinics.Any(c => c.Id == p.ClinicId)
                && _db.Users.Any(u => u.Id == userId && u.IsActive),
            cancellationToken);
    }

    public Task<bool> HasAnyClinicPermissionInAnyAsync(
        long userId,
        IReadOnlyCollection<long> clinicIds,
        CancellationToken cancellationToken)
    {
        var ids = clinicIds.ToArray();

        return _db.UserClinicPermissions.AsNoTracking().AnyAsync(
            p => p.UserId == userId
                && ids.Contains(p.ClinicId)
                && _db.Clinics.Any(c => c.Id == p.ClinicId)
                && _db.Users.Any(u => u.Id == userId && u.IsActive),
            cancellationToken);
    }

    public async Task<IReadOnlyList<long>> GetClinicIdsWithPermissionAsync(
        long userId,
        string permission,
        CancellationToken cancellationToken) =>
        await _db.UserClinicPermissions.AsNoTracking()
            .Where(p => p.UserId == userId
                && p.Permission == permission
                && _db.Clinics.Any(c => c.Id == p.ClinicId)
                && _db.Users.Any(u => u.Id == userId && u.IsActive))
            .Select(p => p.ClinicId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClinicPermissionGrant>> GetClinicPermissionsAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        // Joining the filtered Clinics set drops soft-deleted clinics.
        var rows = await _db.UserClinicPermissions.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Join(_db.Clinics, p => p.ClinicId, c => c.Id, (p, c) => new { c.Id, c.NameAr, c.NameEn, p.Permission })
            .ToListAsync(cancellationToken);

        // Names that are no longer in the code (a permission removed later) are never shown.
        return rows
            .Where(r => Permissions.IsClinicScoped(r.Permission))
            .OrderBy(r => r.Id)
            .ThenBy(r => r.Permission, StringComparer.Ordinal)
            .Select(r => new ClinicPermissionGrant(r.Id, r.NameAr, r.NameEn, r.Permission))
            .ToList();
    }
}
