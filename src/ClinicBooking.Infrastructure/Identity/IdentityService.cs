using System.Globalization;
using ClinicBooking.Application.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace ClinicBooking.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    // Verified against for an unknown or disabled user, so the response time does not reveal
    // whether the user exists. Computed once.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "not-a-real-password"));

    private readonly UserManager<ApplicationUser> _users;
    private readonly IPasswordHasher<ApplicationUser> _hasher;
    private readonly TimeProvider _timeProvider;

    public IdentityService(
        UserManager<ApplicationUser> users,
        IPasswordHasher<ApplicationUser> hasher,
        TimeProvider timeProvider)
    {
        _users = users;
        _hasher = hasher;
        _timeProvider = timeProvider;
    }

    public async Task<CredentialCheckResult> CheckCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByNameAsync(userName);

        // A disabled user looks exactly like a wrong password, before the lockout is looked at, so the
        // response reveals neither that the account exists nor that it is disabled (D57).
        if (user is null || !user.IsActive)
        {
            _hasher.VerifyHashedPassword(new ApplicationUser(), DummyHash.Value, password);
            return new CredentialCheckResult(CredentialStatus.InvalidCredentials);
        }

        // The lockout is checked before the password, so this answer never depends
        // on whether the password was correct.
        if (await _users.IsLockedOutAsync(user))
        {
            return new CredentialCheckResult(CredentialStatus.LockedOut, RetryAfter: await RetryAfterAsync(user));
        }

        if (await _users.CheckPasswordAsync(user, password))
        {
            await _users.ResetAccessFailedCountAsync(user);
            return new CredentialCheckResult(CredentialStatus.Succeeded, user.Id);
        }

        await _users.AccessFailedAsync(user);
        return new CredentialCheckResult(CredentialStatus.InvalidCredentials);
    }

    public async Task<IdentityUserInfo?> GetActiveUserAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
        if (user is null || !user.IsActive || await _users.IsLockedOutAsync(user))
        {
            return null;
        }

        return new IdentityUserInfo(user.Id, user.UserName ?? string.Empty, user.MustChangePassword);
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        long userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
        if (user is null || !user.IsActive)
        {
            // The account gate already refused these; this keeps the method safe on its own.
            return new PasswordChangeResult(PasswordChangeStatus.CurrentPasswordIncorrect);
        }

        if (await _users.IsLockedOutAsync(user))
        {
            return new PasswordChangeResult(PasswordChangeStatus.LockedOut, RetryAfter: await RetryAfterAsync(user));
        }

        // A wrong current password is a failed sign-in attempt: it is counted explicitly, so repeated
        // guesses lock the account exactly as at login (D48, D57). A right one resets the counter.
        if (!await _users.CheckPasswordAsync(user, currentPassword))
        {
            await _users.AccessFailedAsync(user);
            return new PasswordChangeResult(PasswordChangeStatus.CurrentPasswordIncorrect);
        }

        await _users.ResetAccessFailedCountAsync(user);

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return new PasswordChangeResult(PasswordChangeStatus.PasswordUnchanged);
        }

        var changed = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
        {
            var keys = PasswordPolicyKeys.From(changed.Errors);
            if (keys.Count == 0)
            {
                // Codes only, never the passwords.
                throw new InvalidOperationException(
                    "The password could not be changed: " + string.Join(", ", changed.Errors.Select(e => e.Code)));
            }

            return new PasswordChangeResult(PasswordChangeStatus.PolicyViolation, keys);
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            var cleared = await _users.UpdateAsync(user);
            if (!cleared.Succeeded)
            {
                throw new InvalidOperationException(
                    "The must-change flag could not be cleared: " + string.Join(", ", cleared.Errors.Select(e => e.Code)));
            }
        }

        return new PasswordChangeResult(PasswordChangeStatus.Succeeded);
    }

    private async Task<TimeSpan?> RetryAfterAsync(ApplicationUser user)
    {
        var end = await _users.GetLockoutEndDateAsync(user);
        return end is { } lockoutEnd ? lockoutEnd - _timeProvider.GetUtcNow() : null;
    }
}
