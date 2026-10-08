namespace ClinicBooking.Application.DTOs;

/// <summary>
/// The list query shared by every entity with two names (D50): search, paging and sorting.
/// Defaults apply when a parameter is omitted.
/// </summary>
public abstract class NamedListQuery : IListQuery
{
    public const int DefaultPageSize = IListQuery.DefaultPageSize;
    public const int MaxPageSize = IListQuery.MaxPageSize;
    public const int MaxSearchLength = IListQuery.MaxSearchLength;

    /// <summary>Matches either name, ignoring case, diacritics and the Arabic letter variants (D49).</summary>
    public string? Search { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary><c>nameEn</c> (default), <c>nameAr</c> or <c>createdAt</c>.</summary>
    public string SortBy { get; set; } = NameSortFields.NameEn;

    /// <summary><c>asc</c> (default) or <c>desc</c>.</summary>
    public string SortDirection { get; set; } = "asc";
}

public static class NameSortFields
{
    public const string NameEn = "nameEn";
    public const string NameAr = "nameAr";
    public const string CreatedAt = "createdAt";

    public static readonly string[] All = [NameEn, NameAr, CreatedAt];
}
