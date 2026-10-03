namespace ClinicBooking.Domain.Entities;

/// <summary>
/// A refresh token of one login session (D29). Only the SHA-256 hash of the token is
/// stored. Tokens of one session share a <see cref="FamilyId"/>. Hard-deleted (D35).
/// </summary>
public class RefreshToken : BaseEntity
{
    public long UserId { get; set; }

    /// <summary>SHA-256 of the raw token, lower-case hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public Guid FamilyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the session (the first token of the family) started.</summary>
    public DateTimeOffset FamilyCreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the token was exchanged for a new one (rotation).</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Set when the family was revoked (logout, reuse, disabled user).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Concurrency token: two simultaneous rotations cannot both succeed.</summary>
    public byte[] RowVersion { get; set; } = [];
}
