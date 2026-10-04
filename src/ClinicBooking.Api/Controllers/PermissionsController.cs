using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Controllers;

/// <summary>The permission names an administrator can assign, from the same constants the policies use (D57).</summary>
[ApiController]
[Route("api/permissions")]
public sealed class PermissionsController : ControllerBase
{
    private readonly IUserService _users;

    public PermissionsController(IUserService users)
    {
        _users = users;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Users.Manage)]
    [ProducesResponseType(typeof(AssignablePermissionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public ActionResult<AssignablePermissionsResponse> List() => Ok(_users.GetAssignablePermissions());
}
