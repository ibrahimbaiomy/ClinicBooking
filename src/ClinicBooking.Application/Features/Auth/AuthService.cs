using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Users;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicBooking.Application.Features.Auth;

public sealed class AuthService : IAuthService
{
    public const string InvalidCredentialsKey = "error.auth.invalid_credentials";
    public const string LockedOutKey = "error.auth.locked_out";
    public const string InvalidRefreshTokenKey = "error.auth.invalid_refresh_token";
    public const string UnauthorizedKey = "error.auth.unauthorized";
    public const string PasswordChangeRequiredKey = "error.auth.password_change_required";
    public const string CurrentPasswordIncorrectKey = "error.auth.current_password_incorrect";
    public const string PasswordUnchangedKey = "error.auth.password_unchanged";

    private readonly IAppDbContext _db;
    private readonly IIdentityService _identity;
    private readonly IAccessTokenService _accessTokens;
    private readonly IPermissionChecker _permissions;
    private readonly TimeProvider _timeProvider;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAppDbContext db,
        IIdentityService identity,
        IAccessTokenService accessTokens,
        IPermissionChecker permissions,
        TimeProvider timeProvider,
        IOptions<AuthOptions> options,
        ILogger<AuthService> logger)
    {
        _db = db;
        _identity = identity;
        _accessTokens = accessTokens;
        _permissions = permissions;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var check = await _identity.CheckCredentialsAsync(
            request.UserName ?? string.Empty,
            request.Password ?? string.Empty,
            cancellationToken);

        if (check.Status == CredentialStatus.LockedOut)
        {
            throw new AccountLockedException(LockedOutKey, check.RetryAfter);
        }

        if (check.Status != CredentialStatus.Succeeded || check.UserId is not { } userId)
        {
            throw new UnauthorizedException(InvalidCredentialsKey);
        }

        var now = _timeProvider.GetUtcNow();
        await RemoveOldTokensAsync(userId, now, cancellationToken);

        var refresh = AddRefreshToken(userId, Guid.NewGuid(), familyCreatedAt: now, now);
        await _db.SaveChangesAsync(cancellationToken);

        return CreateResult(userId, refresh);
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new UnauthorizedException(InvalidRefreshTokenKey);
        }

        var hash = RefreshTokenSecret.Hash(refreshToken);
        var token = await _db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw new UnauthorizedException(InvalidRefreshTokenKey);

        var now = _timeProvider.GetUtcNow();

        if (token.ConsumedAt is { } consumedAt)
        {
            // Inside the grace window this is a concurrent refresh, not an attack.
            if (now - consumedAt > TimeSpan.FromSeconds(_options.ReuseGraceSeconds))
            {
                _logger.LogWarning(
                    "Rotated refresh token reused; revoking session {FamilyId} of user {UserId}",
                    token.FamilyId,
                    token.UserId);
                await RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            }

            throw new UnauthorizedException(InvalidRefreshTokenKey);
        }

        if (token.RevokedAt is not null || token.ExpiresAt <= now)
        {
            throw new UnauthorizedException(InvalidRefreshTokenKey);
        }

        var user = await _identity.GetActiveUserAsync(token.UserId, cancellationToken);
        if (user is null)
        {
            await RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            throw new UnauthorizedException(InvalidRefreshTokenKey);
        }

        var sessionEnd = token.FamilyCreatedAt.AddDays(_options.SessionMaxDays);
        if (sessionEnd <= now)
        {
            throw new UnauthorizedException(InvalidRefreshTokenKey);
        }

        token.ConsumedAt = now;
        var next = AddRefreshToken(token.UserId, token.FamilyId, token.FamilyCreatedAt, now);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated this token at the same moment.
            throw new UnauthorizedException(InvalidRefreshTokenKey);
        }

        return CreateResult(user.Id, next);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var hash = RefreshTokenSecret.Hash(refreshToken);
        var token = await _db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, _timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await _identity.GetActiveUserAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException(UnauthorizedKey);
        var permissions = await _permissions.GetGlobalPermissionsAsync(userId, cancellationToken);
        var clinicPermissions = await _permissions.GetClinicPermissionsAsync(userId, cancellationToken);

        return new CurrentUserResponse(
            user.Id,
            user.UserName,
            permissions,
            user.MustChangePassword,
            UserMapping.GroupByClinic(clinicPermissions));
    }

    public async Task ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        var result = await _identity.ChangePasswordAsync(
            userId,
            request.CurrentPassword!,
            request.NewPassword!,
            cancellationToken);

        switch (result.Status)
        {
            case PasswordChangeStatus.Succeeded:
                break;
            case PasswordChangeStatus.LockedOut:
                throw new AccountLockedException(LockedOutKey, result.RetryAfter);
            case PasswordChangeStatus.CurrentPasswordIncorrect:
                throw FieldError("currentPassword", CurrentPasswordIncorrectKey, CurrentPasswordIncorrectKey);
            case PasswordChangeStatus.PasswordUnchanged:
                throw FieldError("newPassword", PasswordUnchangedKey, PasswordUnchangedKey);
            default:
                throw FieldError("newPassword", UserErrors.ValidationFailed, result.PolicyErrorKeys!.ToArray());
        }

        // The session that made the request survives; every other one ends (D57). A cookie that is
        // missing, unknown, revoked, expired or owned by another user cannot name a session, so all end.
        var now = _timeProvider.GetUtcNow();
        Guid? currentFamily = null;
        if (!string.IsNullOrEmpty(refreshToken))
        {
            var hash = RefreshTokenSecret.Hash(refreshToken);
            currentFamily = await _db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
                .Select(t => (Guid?)t.FamilyId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var revoked = await SessionRevocation.RevokeUserSessionsAsync(_db, userId, currentFamily, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Password changed by user {UserId}; {RevokedSessions} other sessions revoked (current session kept: {CurrentKept})",
            userId,
            revoked,
            currentFamily is not null);
    }

    private static InvalidRequestException FieldError(string field, string title, params string[] keys) =>
        new(title, new Dictionary<string, string[]> { [field] = keys });

    private RefreshTokenIssue AddRefreshToken(
        long userId,
        Guid familyId,
        DateTimeOffset familyCreatedAt,
        DateTimeOffset now)
    {
        var raw = RefreshTokenSecret.Generate();

        // Sliding expiry, but never beyond the absolute session cap.
        var sliding = now.AddDays(_options.RefreshTokenDays);
        var cap = familyCreatedAt.AddDays(_options.SessionMaxDays);
        var expiresAt = sliding < cap ? sliding : cap;

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = RefreshTokenSecret.Hash(raw),
            FamilyId = familyId,
            CreatedAt = now,
            FamilyCreatedAt = familyCreatedAt,
            ExpiresAt = expiresAt
        });

        return new RefreshTokenIssue(raw, expiresAt);
    }

    private AuthResult CreateResult(long userId, RefreshTokenIssue refresh)
    {
        var access = _accessTokens.Create(userId);
        return new AuthResult(access.Value, access.ExpiresAt, refresh.Raw, refresh.ExpiresAt);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null && t.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    // ExecuteDelete is banned (D46), so the few old rows of this user are loaded and removed.
    private async Task RemoveOldTokensAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-_options.CleanupRetentionDays);
        var old = await _db.RefreshTokens
            .Where(t => t.UserId == userId
                && (t.ExpiresAt < cutoff
                    || (t.ConsumedAt != null && t.ConsumedAt < cutoff)
                    || (t.RevokedAt != null && t.RevokedAt < cutoff)))
            .ToListAsync(cancellationToken);

        _db.RefreshTokens.RemoveRange(old);
    }

    private sealed record RefreshTokenIssue(string Raw, DateTimeOffset ExpiresAt);
}
