namespace ClinicBooking.Domain.Exceptions;

/// <summary>The resource does not exist or is not accessible to the caller (HTTP 404).</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string errorKey)
        : base(errorKey)
    {
    }
}
