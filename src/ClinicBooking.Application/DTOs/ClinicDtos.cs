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

/// <summary>Query string of the list endpoint (<see cref="NamedListQuery"/>).</summary>
public sealed class ListClinicsQuery : NamedListQuery;
