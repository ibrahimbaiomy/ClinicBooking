namespace ClinicBooking.Domain.Entities;

public abstract class AuditableEntity : BaseEntity, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }

    /// <summary>
    /// Optimistic concurrency token (SQL rowversion, D50). Clients send it back on every edit;
    /// a stale value is a 409.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}
