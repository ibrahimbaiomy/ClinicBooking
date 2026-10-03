using System.Security.Cryptography;
using System.Text;

namespace ClinicBooking.Application.Features.Auth;

/// <summary>Creation and hashing of refresh tokens. Only the hash is ever stored.</summary>
internal static class RefreshTokenSecret
{
    private const int TokenBytes = 32;

    public static string Generate() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
