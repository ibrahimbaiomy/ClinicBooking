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

    /// <summary>
    /// Changes the caller's own password (D57). Every session of the user is revoked except the one
    /// that owns <paramref name="refreshToken"/>; when that token is missing or is not the caller's, all of
    /// them are revoked and the user signs in again.
    /// </summary>
    Task ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        string? refreshToken,
        CancellationToken cancellationToken);
}
