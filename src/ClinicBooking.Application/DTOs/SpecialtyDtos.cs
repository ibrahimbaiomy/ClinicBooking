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

/// <summary>Query string of the list endpoint (<see cref="NamedListQuery"/>).</summary>
public sealed class ListSpecialtiesQuery : NamedListQuery;
