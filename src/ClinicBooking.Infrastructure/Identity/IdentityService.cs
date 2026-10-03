using ClinicBooking.Application.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace ClinicBooking.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    // Verified against for an unknown user, so the response time does not reveal
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
        if (user is null)
        {
            _hasher.VerifyHashedPassword(new ApplicationUser(), DummyHash.Value, password);
            return new CredentialCheckResult(CredentialStatus.InvalidCredentials);
        }

        // The lockout is checked before the password, so this answer never depends
        // on whether the password was correct.
        if (await _users.IsLockedOutAsync(user))
        {
            var end = await _users.GetLockoutEndDateAsync(user);
            var retryAfter = end is { } lockoutEnd ? lockoutEnd - _timeProvider.GetUtcNow() : (TimeSpan?)null;
            return new CredentialCheckResult(CredentialStatus.LockedOut, RetryAfter: retryAfter);
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
        var user = await _users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (user is null || await _users.IsLockedOutAsync(user))
        {
            return null;
        }

        return new IdentityUserInfo(user.Id, user.UserName ?? string.Empty);
    }
}
