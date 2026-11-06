namespace ClinicBooking.Domain.Exceptions;

/// <summary>The request clashes with existing data (HTTP 409).</summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string errorKey, IReadOnlyDictionary<string, long>? details = null)
        : base(errorKey)
    {
        Details = details ?? new Dictionary<string, long>();
    }

    /// <summary>
    /// Numeric extension members of the ProblemDetails, for example the <c>appointmentCount</c> of a
    /// confirm-and-cancel answer (D66). Numbers only, as <see cref="BusinessRuleException.Details"/>.
    /// </summary>
    public IReadOnlyDictionary<string, long> Details { get; }
}
