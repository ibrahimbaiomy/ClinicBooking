namespace ClinicBooking.Application.DTOs;

/// <summary>
/// The query-string shape every paged list shares (D50): search, page, page size, sort field and direction.
/// Each list names its own sort fields; the paging and search rules are the same everywhere (D63).
/// </summary>
public interface IListQuery
{
    const int DefaultPageSize = 20;
    const int MaxPageSize = 100;
    const int MaxSearchLength = 100;

    string? Search { get; }

    int Page { get; }

    int PageSize { get; }

    string SortBy { get; }

    string SortDirection { get; }
}
