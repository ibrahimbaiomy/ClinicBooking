namespace ClinicBooking.Domain.Entities;

public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public long? DeletedBy { get; set; }
}
