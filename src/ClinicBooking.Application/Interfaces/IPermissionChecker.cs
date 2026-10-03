namespace ClinicBooking.Application.Interfaces;

/// <summary>
/// Answers permission questions from the database on every call, so a revoked permission
/// takes effect immediately (D34). Clinic-scoped checks are added with Clinics (Phase 1).
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasGlobalPermissionAsync(long userId, string permission, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetGlobalPermissionsAsync(long userId, CancellationToken cancellationToken);
}
