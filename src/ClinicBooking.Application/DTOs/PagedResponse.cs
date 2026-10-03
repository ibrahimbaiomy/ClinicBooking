namespace ClinicBooking.Application.DTOs;

/// <summary>One page of a list, with the total across all pages.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
