using System.Text;

namespace ClinicBooking.Infrastructure.Identity;

/// <summary>Bound from the "Jwt" section. <see cref="SigningKey"/> comes from the environment only.</summary>
public sealed class JwtOptions
{
    /// <summary>HS256 needs a 256-bit key.</summary>
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>Startup validation. The messages never contain the key.</summary>
    public static bool IsValid(JwtOptions options) =>
        !string.IsNullOrWhiteSpace(options.Issuer)
        && !string.IsNullOrWhiteSpace(options.Audience)
        && options.AccessTokenMinutes > 0
        && options.ClockSkewSeconds >= 0
        && Encoding.UTF8.GetByteCount(options.SigningKey) >= MinimumKeyBytes;
}
