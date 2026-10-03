namespace ClinicBooking.Domain.Entities;

/// <summary>Creation and modification stamps, set by the save interceptor (D36).</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null means the system or an anonymous caller.</summary>
    long? CreatedBy { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }

    long? UpdatedBy { get; set; }
}
