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

public sealed record IdentityUserInfo(long Id, string UserName);

/// <summary>Users and credentials, backed by ASP.NET Core Identity in Infrastructure.</summary>
public interface IIdentityService
{
    /// <summary>
    /// Checks the lockout first, so the result never depends on whether the password was
    /// right. An unknown user and a wrong password give the same result.
    /// </summary>
    Task<CredentialCheckResult> CheckCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken);

    /// <summary>The user, or null when it does not exist or is locked out.</summary>
    Task<IdentityUserInfo?> GetActiveUserAsync(long userId, CancellationToken cancellationToken);
}
