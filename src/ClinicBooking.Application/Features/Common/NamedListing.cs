using System.Linq.Expressions;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Features.Common;

/// <summary>Search, ordering and paging of entities with two names (D49, D50), shared at the third copy (D61).</summary>
public static class NamedListing
{
    /// <summary>Every word of the search must match one of the two normalised names (D49).</summary>
    public static IQueryable<T> MatchingEveryWord<T>(this IQueryable<T> source, string? search)
        where T : IBilingualName
    {
        foreach (var token in SearchText.Normalize(search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            source = source.Where(e => e.NameArNormalized.Contains(token) || e.NameEnNormalized.Contains(token));
        }

        return source;
    }

    /// <summary>Counts, orders and returns one page, projected with <paramref name="projection"/>.</summary>
    public static async Task<PagedResponse<TResponse>> ToPageAsync<T, TResponse>(
        this IQueryable<T> source,
        NamedListQuery query,
        Expression<Func<T, TResponse>> projection,
        CancellationToken cancellationToken)
        where T : AuditableEntity, IBilingualName
    {
        var totalCount = await source.CountAsync(cancellationToken);

        var items = await Order(source, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return new PagedResponse<TResponse>(items, query.Page, query.PageSize, totalCount);
    }

    // Sorting by the normalised text gives Arabic alphabetical order, ignoring hamza forms and
    // diacritics. Id breaks ties so pages are stable.
    private static IOrderedQueryable<T> Order<T>(IQueryable<T> source, NamedListQuery query)
        where T : AuditableEntity, IBilingualName
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<T> ordered;
        if (string.Equals(query.SortBy, NameSortFields.NameAr, StringComparison.OrdinalIgnoreCase))
        {
            ordered = descending
                ? source.OrderByDescending(e => e.NameArNormalized)
                : source.OrderBy(e => e.NameArNormalized);
        }
        else if (string.Equals(query.SortBy, NameSortFields.CreatedAt, StringComparison.OrdinalIgnoreCase))
        {
            // Newest first also means the highest id first when rows share a timestamp.
            return descending
                ? source.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                : source.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id);
        }
        else
        {
            ordered = descending
                ? source.OrderByDescending(e => e.NameEnNormalized)
                : source.OrderBy(e => e.NameEnNormalized);
        }

        return ordered.ThenBy(e => e.Id);
    }
}
