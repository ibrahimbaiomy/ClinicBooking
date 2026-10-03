using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Tests.Support;

/// <summary>Protected endpoints that exist only in the test assembly.</summary>
[ApiController]
[Route("test/protected")]
public sealed class ProtectedController : ControllerBase
{
    [HttpGet("authenticated")]
    [Authorize]
    public IActionResult Authenticated() => Ok();

    [HttpGet("users-manage")]
    [Authorize(Policy = Permissions.Users.Manage)]
    public IActionResult UsersManage() => Ok();

    [HttpPost("specialty")]
    [Authorize]
    public async Task<IActionResult> CreateSpecialty([FromServices] IAppDbContext db, CancellationToken cancellationToken)
    {
        var specialty = new Specialty { NameAr = Guid.NewGuid().ToString("N"), NameEn = Guid.NewGuid().ToString("N") };
        db.Specialties.Add(specialty);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = specialty.Id });
    }
}
