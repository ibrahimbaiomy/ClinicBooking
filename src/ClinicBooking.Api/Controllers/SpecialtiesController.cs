using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Controllers;

/// <summary>
/// Reading needs a signed-in user; changing needs <c>specialties.manage</c> (D50). This controller is the
/// reference pattern for Clinics, Doctors and Patients: thin actions, validation through the global
/// filter, every failure an error-key ProblemDetails.
/// </summary>
[ApiController]
[Route("api/specialties")]
[ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
public sealed class SpecialtiesController : ControllerBase
{
    private const string GetByIdRoute = "GetSpecialtyById";

    private readonly ISpecialtyService _specialties;

    public SpecialtiesController(ISpecialtyService specialties)
    {
        _specialties = specialties;
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<SpecialtyResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<SpecialtyResponse>>> List(
        [FromQuery] ListSpecialtiesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _specialties.ListAsync(query, cancellationToken));

    [HttpGet("{id:long}", Name = GetByIdRoute)]
    [Authorize]
    [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<SpecialtyResponse>> GetById(long id, CancellationToken cancellationToken) =>
        Ok(await _specialties.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.Specialties.Manage)]
    [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SpecialtyResponse>> Create(
        [FromBody] CreateSpecialtyRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _specialties.CreateAsync(request, cancellationToken);

        return CreatedAtRoute(GetByIdRoute, new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status200OK)]
    [Authorize(Policy = Permissions.Specialties.Manage)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SpecialtyResponse>> Update(
        long id,
        [FromBody] UpdateSpecialtyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _specialties.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = Permissions.Specialties.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await _specialties.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
