namespace ClinicBooking.Domain.Entities;

/// <summary>
/// One clinic-scoped permission granted to one user in one clinic (D34, D57). The user is an
/// Identity user, so there is no navigation to it. Revoking deletes the row (a join table, D35);
/// <c>CreatedBy</c> records who granted it. Valid only while the clinic is not soft-deleted and
/// the user is active; both are checked when the grant is read, never stored here.
/// </summary>
public class UserClinicPermission : AuditableEntity
{
    public const int PermissionMaxLength = 64;

    public long UserId { get; set; }

    public long ClinicId { get; set; }

    /// <summary>One of <c>Permissions.ClinicScoped</c>.</summary>
    public string Permission { get; set; } = string.Empty;
}
