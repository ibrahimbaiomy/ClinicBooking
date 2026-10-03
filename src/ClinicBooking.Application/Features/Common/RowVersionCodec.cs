namespace ClinicBooking.Application.Features.Common;

/// <summary>The row version travels as base64. SQL Server's rowversion is 8 bytes.</summary>
public static class RowVersionCodec
{
    public const int Length = 8;

    public static string Encode(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    public static bool TryDecode(string? value, out byte[] rowVersion)
    {
        rowVersion = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var buffer = new byte[Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written) || written != Length)
        {
            return false;
        }

        rowVersion = buffer;
        return true;
    }
}
