namespace ClinicBooking.Domain.Entities;

public abstract class AuditableEntity : BaseEntity, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }
}
