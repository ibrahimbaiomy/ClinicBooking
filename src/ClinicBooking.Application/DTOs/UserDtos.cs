namespace ClinicBooking.Application.DTOs;

/// <param name="IsProtected">The protected administrator (D64): read-only, never set through the API.</param>
public sealed record UserSummaryResponse(
    long Id,
    string UserName,
    bool IsActive,
    bool MustChangePassword,
    bool IsProtected,
    DateTimeOffset CreatedAt);

/// <summary>The clinic-scoped permissions a user holds in one clinic. Soft-deleted clinics are never listed.</summary>
public sealed record UserClinicPermissionsResponse(
    long ClinicId,
    string ClinicNameAr,
    string ClinicNameEn,
    IReadOnlyList<string> Permissions);

/// <param name="IsProtected">The protected administrator (D64): cannot be disabled, keeps users.manage, no admin reset.</param>
public sealed record UserDetailResponse(
    long Id,
    string UserName,
    bool IsActive,
    bool MustChangePassword,
    bool IsProtected,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> GlobalPermissions,
    IReadOnlyList<UserClinicPermissionsResponse> ClinicPermissions);

/// <param name="TemporaryPassword">Travels only in this request body; it is never returned or logged (D57).</param>
public sealed record CreateUserRequest(string? UserName, string? TemporaryPassword);

/// <param name="TemporaryPassword">Travels only in this request body; it is never returned or logged (D57).</param>
public sealed record ResetPasswordRequest(string? TemporaryPassword);

/// <summary>A full replace: the user ends up with exactly these global permissions.</summary>
public sealed record ReplaceGlobalPermissionsRequest(IReadOnlyList<string>? Permissions);

/// <summary>A full replace for one clinic: an empty list removes every grant in that clinic.</summary>
public sealed record ReplaceClinicPermissionsRequest(IReadOnlyList<string>? Permissions);

public sealed record AssignablePermissionsResponse(IReadOnlyList<string> Global, IReadOnlyList<string> ClinicScoped);

/// <summary>Query string of the users list. Defaults apply when a parameter is omitted.</summary>
public sealed class ListUsersQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    /// <summary>Matches part of the user name, ignoring case.</summary>
    public string? Search { get; set; }

    /// <summary>Only active (<c>true</c>) or only disabled (<c>false</c>) users; omitted lists both.</summary>
    public bool? IsActive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary><c>userName</c> (default), <c>isActive</c>, <c>mustChangePassword</c> (D64) or <c>createdAt</c>.</summary>
    public string SortBy { get; set; } = UserSortFields.UserName;

    /// <summary><c>asc</c> (default) or <c>desc</c>.</summary>
    public string SortDirection { get; set; } = "asc";
}

public static class UserSortFields
{
    public const string UserName = "userName";
    public const string IsActive = "isActive";
    public const string MustChangePassword = "mustChangePassword";
    public const string CreatedAt = "createdAt";

    public static readonly string[] All = [UserName, IsActive, MustChangePassword, CreatedAt];
}
