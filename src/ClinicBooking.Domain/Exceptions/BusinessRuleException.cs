namespace ClinicBooking.Domain.Exceptions;

/// <summary>A business rule was violated (HTTP 422).</summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string errorKey, IReadOnlyDictionary<string, long>? details = null)
        : base(errorKey)
    {
        Details = details ?? new Dictionary<string, long>();
    }

    /// <summary>
    /// Extension members of the ProblemDetails, camelCase name to number (for example the index of the
    /// offending period, D61). Numbers only, so no text and no personal data can leak through them.
    /// </summary>
    public IReadOnlyDictionary<string, long> Details { get; }
}
