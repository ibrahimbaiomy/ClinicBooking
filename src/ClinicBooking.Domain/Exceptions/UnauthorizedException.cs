namespace ClinicBooking.Domain.Exceptions;

/// <summary>The caller could not be authenticated (HTTP 401).</summary>
public sealed class UnauthorizedException : DomainException
{
    public UnauthorizedException(string errorKey)
        : base(errorKey)
    {
    }
}
