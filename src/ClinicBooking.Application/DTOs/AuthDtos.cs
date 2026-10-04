namespace ClinicBooking.Application.DTOs;

public sealed record LoginRequest(string? UserName, string? Password);

/// <summary>What the client receives. The refresh token travels only in its cookie.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <param name="Permissions">The global permissions.</param>
/// <param name="MustChangePassword">True while the user still has a temporary password: every other endpoint answers 403 <c>error.auth.password_change_required</c> (D57).</param>
/// <param name="ClinicPermissions">The clinic-scoped permissions, per live clinic.</param>
public sealed record CurrentUserResponse(
    long Id,
    string UserName,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword,
    IReadOnlyList<UserClinicPermissionsResponse> ClinicPermissions);

/// <summary>Both passwords travel only in this request body; they are never returned or logged.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>Service result. <see cref="RefreshToken"/> is the raw value, for the cookie only.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
