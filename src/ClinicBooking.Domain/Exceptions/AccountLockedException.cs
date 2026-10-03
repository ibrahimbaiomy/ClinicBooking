namespace ClinicBooking.Domain.Exceptions;

/// <summary>The account is temporarily locked after repeated failed logins (HTTP 423).</summary>
public sealed class AccountLockedException : DomainException
{
    public AccountLockedException(string errorKey, TimeSpan? retryAfter)
        : base(errorKey)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
