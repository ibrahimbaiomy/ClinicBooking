namespace ClinicBooking.Domain.Entities;

/// <summary>Root of every persisted entity (D6: <c>long</c> keys).</summary>
public abstract class BaseEntity
{
    public long Id { get; set; }
}
