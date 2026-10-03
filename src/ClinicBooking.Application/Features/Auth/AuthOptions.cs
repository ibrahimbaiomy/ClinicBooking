namespace ClinicBooking.Application.Features.Auth;

/// <summary>Bound from the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Absolute cap on a session, however often it is refreshed.</summary>
    public int SessionMaxDays { get; set; } = 30;

    /// <summary>
    /// A rotated token presented again within this many seconds is rejected without
    /// revoking the session (concurrent refreshes from two tabs).
    /// </summary>
    public int ReuseGraceSeconds { get; set; } = 10;

    /// <summary>Spent tokens older than this are removed when the user logs in.</summary>
    public int CleanupRetentionDays { get; set; } = 7;
}
