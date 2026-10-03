namespace ClinicBooking.Domain.Exceptions;

/// <summary>The request is malformed or fails input checks (HTTP 400).</summary>
public sealed class InvalidRequestException : DomainException
{
    public InvalidRequestException(
        string errorKey,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(errorKey)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    /// <summary>Field name to error keys.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
