using System.Collections.Generic;
using ClinicBooking.Api.Authentication;
using ClinicBooking.Api.Filters;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Auth;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicBooking.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    public const string RefreshCookieName = "refresh_token";

    // The cookie is only sent to these endpoints.
    private const string CookiePath = "/api/auth";

    private readonly IAuthService _auth;
    private readonly IUser _user;

    public AuthController(IAuthService auth, IUser user)
    {
        _auth = auth;
        _user = user;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthExtensions.LoginRateLimitPolicy)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.LoginAsync(request, cancellationToken);
        SetRefreshCookie(result);

        return Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthExtensions.RefreshRateLimitPolicy)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ServiceFilter(typeof(SameOriginFilter))]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _auth.RefreshAsync(Request.Cookies[RefreshCookieName], cancellationToken);
            SetRefreshCookie(result);

            return Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt));
        }
        catch (UnauthorizedException)
        {
            // The error handler clears response headers, so the cookie is removed as the response starts.
            Response.OnStarting(state =>
            {
                var response = (HttpResponse)state;
                response.Cookies.Delete(RefreshCookieName, CookieOptions(expires: null));
                return Task.CompletedTask;
            }, Response);
            throw;
        }
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [AllowAnonymous]
    [ServiceFilter(typeof(SameOriginFilter))]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(Request.Cookies[RefreshCookieName], cancellationToken);
        Response.Cookies.Delete(RefreshCookieName, CookieOptions(expires: null));

        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [Authorize]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = _user.Id ?? throw new UnauthorizedException(AuthService.UnauthorizedKey);

        return Ok(await _auth.GetCurrentUserAsync(userId, cancellationToken));
    }

    private void SetRefreshCookie(AuthResult result) =>
        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, CookieOptions(result.RefreshTokenExpiresAt));

    // Secure is always set, even over http://localhost: Chrome and Firefox accept it there.
    private static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        IsEssential = true,
        Expires = expires
    };
}
