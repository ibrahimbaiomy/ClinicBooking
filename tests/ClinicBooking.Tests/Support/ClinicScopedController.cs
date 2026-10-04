using System.Collections.Concurrent;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Tests.Support;

/// <summary>
/// Clinic-scoped endpoints that exist only in the test assembly (D57): one addressed by clinic (a policy
/// decides) and one addressed by a resource's own id (the service decides through <see cref="IClinicAccess"/>).
/// </summary>
[ApiController]
[Route("test")]
public sealed class ClinicScopedController : ControllerBase
{
    public const string ResourceNotFoundKey = "error.test.resource_not_found";

    /// <summary>Resource id to the clinics it belongs to; filled by the tests.</summary>
    public static ConcurrentDictionary<long, long[]> Resources { get; } = new();

    [HttpGet("clinics/{clinicId:long}/doctors")]
    [Authorize(Policy = Permissions.Doctors.Manage)]
    public IActionResult ByClinic(long clinicId) => Ok(new { clinicId });

    // No clinicId in the route: no resolver can answer, so the policy must fail closed.
    [HttpGet("scoped-without-a-clinic")]
    [Authorize(Policy = Permissions.Doctors.Manage)]
    public IActionResult WithoutAClinic() => Ok();

    [HttpGet("resources/{id:long}")]
    [Authorize]
    public async Task<IActionResult> ById(long id, [FromServices] IClinicAccess access, CancellationToken cancellationToken)
    {
        if (!Resources.TryGetValue(id, out var clinicIds))
        {
            throw new NotFoundException(ResourceNotFoundKey);
        }

        await access.RequireAsync(clinicIds, Permissions.Doctors.Manage, ResourceNotFoundKey, cancellationToken);

        return Ok(new { id });
    }
}
