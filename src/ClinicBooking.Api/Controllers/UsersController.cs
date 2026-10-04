using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Controllers;

/// <summary>
/// User administration (D57). Every action needs the global <c>users.manage</c>. A temporary password
/// is accepted only in a request body and is never returned or logged.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Permissions.Users.Manage)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class UsersController : ControllerBase
{
    private const string GetByIdRoute = "GetUserById";

    private readonly IUserService _users;

    public UsersController(IUserService users)
    {
        _users = users;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<UserSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<UserSummaryResponse>>> List(
        [FromQuery] ListUsersQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _users.ListAsync(query, cancellationToken));

    [HttpGet("{id:long}", Name = GetByIdRoute)]
    [ProducesResponseType(typeof(UserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<UserDetailResponse>> GetById(long id, CancellationToken cancellationToken) =>
        Ok(await _users.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(UserDetailResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<UserDetailResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _users.CreateAsync(request, cancellationToken);

        return CreatedAtRoute(GetByIdRoute, new { id = created.Id }, created);
    }

    [HttpPost("{id:long}/disable")]
    [ProducesResponseType(typeof(UserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<UserDetailResponse>> Disable(long id, CancellationToken cancellationToken) =>
        Ok(await _users.DisableAsync(id, cancellationToken));

    [HttpPost("{id:long}/enable")]
    [ProducesResponseType(typeof(UserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<UserDetailResponse>> Enable(long id, CancellationToken cancellationToken) =>
        Ok(await _users.EnableAsync(id, cancellationToken));

    [HttpPut("{id:long}/global-permissions")]
    [ProducesResponseType(typeof(UserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<UserDetailResponse>> ReplaceGlobalPermissions(
        long id,
        [FromBody] ReplaceGlobalPermissionsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _users.ReplaceGlobalPermissionsAsync(id, request, cancellationToken));

    [HttpPut("{id:long}/clinics/{clinicId:long}/permissions")]
    [ProducesResponseType(typeof(UserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<UserDetailResponse>> ReplaceClinicPermissions(
        long id,
        long clinicId,
        [FromBody] ReplaceClinicPermissionsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _users.ReplaceClinicPermissionsAsync(id, clinicId, request, cancellationToken));

    [HttpPost("{id:long}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ResetPassword(
        long id,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _users.ResetPasswordAsync(id, request, cancellationToken);

        return NoContent();
    }
}
