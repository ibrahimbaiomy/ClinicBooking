using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

/// <summary>What the account gate reads on every authenticated request (D57).</summary>
public sealed record AccountState(bool IsActive, bool MustChangePassword);

/// <param name="PasswordErrorKeys">Password-policy keys when the password was refused.</param>
/// <param name="UserNameTaken">True when the user name already exists (case-insensitively).</param>
public sealed record CreateUserOutcome(long? UserId, IReadOnlyList<string> PasswordErrorKeys, bool UserNameTaken);

/// <summary>User administration on top of ASP.NET Core Identity; implemented in Infrastructure (D57).</summary>
public interface IUserAccounts
{
    /// <summary>The state of the user, or null when it does not exist. One primary-key lookup.</summary>
    Task<AccountState?> GetStateAsync(long userId, CancellationToken cancellationToken);

    Task<PagedResponse<UserSummaryResponse>> ListAsync(ListUsersQuery query, CancellationToken cancellationToken);

    Task<UserSummaryResponse?> FindAsync(long userId, CancellationToken cancellationToken);

    /// <summary>Creates an active user that must change the temporary password at the first sign-in.</summary>
    Task<CreateUserOutcome> CreateAsync(string userName, string temporaryPassword, CancellationToken cancellationToken);

    /// <summary>Idempotent. Does nothing to the user's sessions: the caller revokes them.</summary>
    Task SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a new temporary password, requires a change at the next sign-in and clears the lockout.
    /// Returns the policy keys when the password is refused (nothing changes then).
    /// </summary>
    Task<IReadOnlyList<string>> ResetPasswordAsync(long userId, string temporaryPassword, CancellationToken cancellationToken);

    /// <summary>True when the user is active and holds the global <c>users.manage</c>.</summary>
    Task<bool> IsActiveAdministratorAsync(long userId, CancellationToken cancellationToken);

    /// <summary>The active users other than <paramref name="userId"/> that hold the global <c>users.manage</c>.</summary>
    Task<int> CountOtherActiveAdministratorsAsync(long userId, CancellationToken cancellationToken);

    /// <summary>Makes the stored global permissions of the user exactly <paramref name="permissions"/>.</summary>
    Task ReplaceGlobalPermissionsAsync(
        long userId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);
}
