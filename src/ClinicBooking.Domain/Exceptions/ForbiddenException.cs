namespace ClinicBooking.Domain.Exceptions;

/// <summary>The caller is known but not allowed to do this (HTTP 403).</summary>
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string errorKey)
        : base(errorKey)
    {
    }
}
