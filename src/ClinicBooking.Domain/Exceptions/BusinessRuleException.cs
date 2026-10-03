namespace ClinicBooking.Domain.Exceptions;

/// <summary>A business rule was violated (HTTP 422).</summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string errorKey)
        : base(errorKey)
    {
    }
}
