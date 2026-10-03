namespace ClinicBooking.Application.DTOs;

public sealed record LoginRequest(string? UserName, string? Password);

/// <summary>What the client receives. The refresh token travels only in its cookie.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record CurrentUserResponse(long Id, string UserName, IReadOnlyList<string> Permissions);

/// <summary>Service result. <see cref="RefreshToken"/> is the raw value, for the cookie only.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
