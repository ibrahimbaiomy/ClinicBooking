namespace ClinicBooking.Application.DTOs;

/// <param name="RowVersion">Opaque concurrency token (base64). Send it back unchanged on edit.</param>
public sealed record SpecialtyResponse(
    long Id,
    string NameAr,
    string NameEn,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion);

public sealed record CreateSpecialtyRequest(string? NameAr, string? NameEn);

public sealed record UpdateSpecialtyRequest(string? NameAr, string? NameEn, string? RowVersion);

/// <summary>Query string of the list endpoint. Defaults apply when a parameter is omitted.</summary>
public sealed class ListSpecialtiesQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    /// <summary>Matches either name, ignoring case, diacritics and the Arabic letter variants (D49).</summary>
    public string? Search { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary><c>nameEn</c> (default), <c>nameAr</c> or <c>createdAt</c>.</summary>
    public string SortBy { get; set; } = SpecialtySortFields.NameEn;

    /// <summary><c>asc</c> (default) or <c>desc</c>.</summary>
    public string SortDirection { get; set; } = "asc";
}

public static class SpecialtySortFields
{
    public const string NameEn = "nameEn";
    public const string NameAr = "nameAr";
    public const string CreatedAt = "createdAt";

    public static readonly string[] All = [NameEn, NameAr, CreatedAt];
}
