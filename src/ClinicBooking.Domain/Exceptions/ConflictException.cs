namespace ClinicBooking.Domain.Exceptions;

/// <summary>The request clashes with existing data (HTTP 409).</summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string errorKey)
        : base(errorKey)
    {
    }
}
