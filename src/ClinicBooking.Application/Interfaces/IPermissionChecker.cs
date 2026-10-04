namespace ClinicBooking.Application.Interfaces;

/// <summary>One clinic-scoped permission a user holds in one live clinic.</summary>
public sealed record ClinicPermissionGrant(long ClinicId, string ClinicNameAr, string ClinicNameEn, string Permission);

/// <summary>
/// Answers permission questions from the database on every call, so a revoked permission
/// takes effect immediately (D34). The <c>Has*</c> checks are false for a disabled user, for a
/// permission of the other kind (a global grant never satisfies a clinic-scoped permission or
/// the reverse) and, for clinic-scoped ones, for a soft-deleted clinic (D57). The <c>Get*</c>
/// methods list what is stored for the user and are used to show it, so they do not look at
/// whether the user is active.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasGlobalPermissionAsync(long userId, string permission, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetGlobalPermissionsAsync(long userId, CancellationToken cancellationToken);

    Task<bool> HasClinicPermissionAsync(long userId, long clinicId, string permission, CancellationToken cancellationToken);

    /// <summary>True when the user holds <paramref name="permission"/> in at least one of the clinics.</summary>
    Task<bool> HasClinicPermissionInAnyAsync(
        long userId,
        IReadOnlyCollection<long> clinicIds,
        string permission,
        CancellationToken cancellationToken);

    /// <summary>True when the user holds any clinic-scoped permission in at least one of the clinics.</summary>
    Task<bool> HasAnyClinicPermissionInAnyAsync(
        long userId,
        IReadOnlyCollection<long> clinicIds,
        CancellationToken cancellationToken);

    /// <summary>The live clinics where the user holds <paramref name="permission"/> (for lists and creation).</summary>
    Task<IReadOnlyList<long>> GetClinicIdsWithPermissionAsync(
        long userId,
        string permission,
        CancellationToken cancellationToken);

    /// <summary>Every clinic-scoped permission of the user in live clinics, ordered by clinic then name.</summary>
    Task<IReadOnlyList<ClinicPermissionGrant>> GetClinicPermissionsAsync(long userId, CancellationToken cancellationToken);
}
