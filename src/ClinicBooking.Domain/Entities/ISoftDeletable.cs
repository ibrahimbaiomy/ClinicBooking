namespace ClinicBooking.Domain.Entities;

/// <summary>
/// Removed rows are kept and hidden by a global query filter (D35). Removal is
/// turned into an update by the save interceptor.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }

    long? DeletedBy { get; set; }
}
