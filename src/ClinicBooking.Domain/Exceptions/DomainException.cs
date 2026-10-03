namespace ClinicBooking.Domain.Exceptions;

/// <summary>
/// Base type for expected failures. <see cref="ErrorKey"/> is a translation key
/// such as <c>error.appointment.slot_taken</c>, never an English sentence.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string errorKey)
        : base(errorKey)
    {
        ErrorKey = errorKey;
    }

    public string ErrorKey { get; }
}
