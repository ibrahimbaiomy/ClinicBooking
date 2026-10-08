namespace ClinicBooking.Application.DTOs;

/// <param name="Phone">E.164 (for example +201012345678).</param>
/// <param name="RowVersion">Opaque concurrency token (base64). Send it back unchanged on edit.</param>
public sealed record PatientResponse(
    long Id,
    string Name,
    string Phone,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion);

/// <param name="Phone">Egyptian national or international form, stored as E.164 (D55).</param>
/// <param name="ConfirmDuplicatePhone">True to save although other patients have this phone (D44, D63).</param>
public sealed record CreatePatientRequest(string? Name, string? Phone, bool? ConfirmDuplicatePhone);

/// <summary>A full replace (D55). The duplicate-phone warning applies only when the phone changes.</summary>
public sealed record UpdatePatientRequest(string? Name, string? Phone, string? RowVersion, bool? ConfirmDuplicatePhone);

/// <summary>Query string of the list endpoint. Defaults apply when a parameter is omitted.</summary>
public sealed class ListPatientsQuery : IListQuery
{
    /// <summary>
    /// A phone search when, ignoring spaces, hyphens and a leading +, it is 3 or more digits (a leading 0 or 00
    /// or + is a prefix match on the stored E.164 number, anything else a contains match); otherwise a name
    /// search where every word must match (D49, D63).
    /// </summary>
    public string? Search { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = IListQuery.DefaultPageSize;

    /// <summary><c>name</c> (default) or <c>createdAt</c>.</summary>
    public string SortBy { get; set; } = PatientSortFields.Name;

    /// <summary><c>asc</c> (default) or <c>desc</c>.</summary>
    public string SortDirection { get; set; } = "asc";
}

public static class PatientSortFields
{
    public const string Name = "name";
    public const string CreatedAt = "createdAt";

    public static readonly string[] All = [Name, CreatedAt];
}
