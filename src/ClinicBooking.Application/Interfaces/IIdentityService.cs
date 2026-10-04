namespace ClinicBooking.Application.Interfaces;

public enum CredentialStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut
}

/// <param name="UserId">Set only when the status is <see cref="CredentialStatus.Succeeded"/>.</param>
/// <param name="RetryAfter">Set only when the status is <see cref="CredentialStatus.LockedOut"/>.</param>
public sealed record CredentialCheckResult(CredentialStatus Status, long? UserId = null, TimeSpan? RetryAfter = null);

public sealed record IdentityUserInfo(long Id, string UserName, bool MustChangePassword);

public enum PasswordChangeStatus
{
    Succeeded,
    LockedOut,
    CurrentPasswordIncorrect,
    PasswordUnchanged,
    PolicyViolation
}

/// <param name="PolicyErrorKeys">The password-policy keys, set only for <see cref="PasswordChangeStatus.PolicyViolation"/>.</param>
/// <param name="RetryAfter">Set only for <see cref="PasswordChangeStatus.LockedOut"/>.</param>
public sealed record PasswordChangeResult(
    PasswordChangeStatus Status,
    IReadOnlyList<string>? PolicyErrorKeys = null,
    TimeSpan? RetryAfter = null);

/// <summary>Users and credentials, backed by ASP.NET Core Identity in Infrastructure.</summary>
public interface IIdentityService
{
    /// <summary>
    /// Checks the lockout first, so the result never depends on whether the password was
    /// right. An unknown user, a disabled user and a wrong password give the same result.
    /// </summary>
    Task<CredentialCheckResult> CheckCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken);

    /// <summary>The user, or null when it does not exist, is disabled or is locked out.</summary>
    Task<IdentityUserInfo?> GetActiveUserAsync(long userId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the password of a user who proves the current one. A wrong current password counts as
    /// a failed attempt (it feeds the lockout); a right one resets the counter. On success the
    /// must-change flag is cleared.
    /// </summary>
    Task<PasswordChangeResult> ChangePasswordAsync(
        long userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);
}
