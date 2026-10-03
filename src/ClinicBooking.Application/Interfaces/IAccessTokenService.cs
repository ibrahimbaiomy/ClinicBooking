namespace ClinicBooking.Application.Interfaces;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface IAccessTokenService
{
    /// <summary>Creates a short-lived signed token carrying only the user id.</summary>
    AccessToken Create(long userId);
}
