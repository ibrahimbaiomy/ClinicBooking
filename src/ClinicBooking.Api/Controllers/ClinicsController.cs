using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Controllers;

/// <summary>
/// Reading needs a signed-in user; changing needs <c>clinics.manage</c> (D55). Same shape as
/// <see cref="SpecialtiesController"/> (D50): thin actions, validation through the global filter, every
/// failure an error-key ProblemDetails.
/// </summary>
[ApiController]
[Route("api/clinics")]
[ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
public sealed class ClinicsController : ControllerBase
{
    private const string GetByIdRoute = "GetClinicById";

    private readonly IClinicService _clinics;

    public ClinicsController(IClinicService clinics)
    {
        _clinics = clinics;
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<ClinicResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ClinicResponse>>> List(
        [FromQuery] ListClinicsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _clinics.ListAsync(query, cancellationToken));

    [HttpGet("{id:long}", Name = GetByIdRoute)]
    [Authorize]
    [ProducesResponseType(typeof(ClinicResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ClinicResponse>> GetById(long id, CancellationToken cancellationToken) =>
        Ok(await _clinics.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.Clinics.Manage)]
    [ProducesResponseType(typeof(ClinicResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ClinicResponse>> Create(
        [FromBody] CreateClinicRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _clinics.CreateAsync(request, cancellationToken);

        return CreatedAtRoute(GetByIdRoute, new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(ClinicResponse), StatusCodes.Status200OK)]
    [Authorize(Policy = Permissions.Clinics.Manage)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ClinicResponse>> Update(
        long id,
        [FromBody] UpdateClinicRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _clinics.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = Permissions.Clinics.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await _clinics.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
