namespace ClinicBooking.Application.Features.Common;

/// <summary>Error keys shared by every entity that has a row version (D50).</summary>
public static class ConcurrencyErrors
{
    public const string Conflict = "error.concurrency.conflict";
    public const string RowVersionRequired = "error.concurrency.row_version_required";
    public const string RowVersionInvalid = "error.concurrency.row_version_invalid";
}
