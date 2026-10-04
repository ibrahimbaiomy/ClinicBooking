using ClinicBooking.Application.Interfaces;

namespace ClinicBooking.Application.Features.Auth;

/// <summary>Revokes refresh-token sessions of a user (disable, reset, change-password; D57).</summary>
internal static class SessionRevocation
{
    /// <summary>
    /// Marks every live refresh token of the user as revoked, except those of <paramref name="exceptFamilyId"/>.
    /// Rotated (consumed) tokens are already unusable. The caller saves.
    /// </summary>
    public static async Task<int> RevokeUserSessionsAsync(
        IAppDbContext db,
        long userId,
        Guid? exceptFamilyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId
                && t.RevokedAt == null
                && t.ConsumedAt == null
                && (exceptFamilyId == null || t.FamilyId != exceptFamilyId))
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }

        return tokens.Count;
    }
}
