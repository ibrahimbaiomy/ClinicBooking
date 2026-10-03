using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for a new access token and a rotated refresh token.</summary>
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the session of the token. Idempotent: an unknown token is ignored.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(long userId, CancellationToken cancellationToken);
}
