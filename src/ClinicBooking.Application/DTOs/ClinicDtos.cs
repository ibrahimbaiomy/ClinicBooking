namespace ClinicBooking.Application.DTOs;

/// <param name="Phone">E.164 (for example +201012345678), or null.</param>
/// <param name="RowVersion">Opaque concurrency token (base64). Send it back unchanged on edit.</param>
public sealed record ClinicResponse(
    long Id,
    string NameAr,
    string NameEn,
    string? Address,
    string? Phone,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion);

/// <param name="Address">Optional free text, at most 300 characters.</param>
/// <param name="Phone">Optional; Egyptian national or international form, stored as E.164 (D55).</param>
public sealed record CreateClinicRequest(string? NameAr, string? NameEn, string? Address, string? Phone);

/// <summary>A full replace: an omitted or blank address or phone is cleared (D55).</summary>
public sealed record UpdateClinicRequest(string? NameAr, string? NameEn, string? Address, string? Phone, string? RowVersion);

/// <summary>Query string of the list endpoint. Defaults apply when a parameter is omitted.</summary>
public sealed class ListClinicsQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    /// <summary>Matches either name, ignoring case, diacritics and the Arabic letter variants (D49).</summary>
    public string? Search { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary><c>nameEn</c> (default), <c>nameAr</c> or <c>createdAt</c>.</summary>
    public string SortBy { get; set; } = ClinicSortFields.NameEn;

    /// <summary><c>asc</c> (default) or <c>desc</c>.</summary>
    public string SortDirection { get; set; } = "asc";
}

public static class ClinicSortFields
{
    public const string NameEn = "nameEn";
    public const string NameAr = "nameAr";
    public const string CreatedAt = "createdAt";

    public static readonly string[] All = [NameEn, NameAr, CreatedAt];
}
