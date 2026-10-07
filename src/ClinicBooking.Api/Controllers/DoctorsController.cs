using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Controllers;

/// <summary>
/// Doctors (D61). Reading needs a signed-in user (D57, no doctors.read). Endpoints addressed by the
/// doctor's own id are authorized in the service against the doctor's clinics (D6, D57); endpoints with a
/// clinic in the route use the <c>doctors.manage</c> policy for that clinic.
/// </summary>
[ApiController]
[Route("api/doctors")]
[ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
public sealed class DoctorsController : ControllerBase
{
    private const string GetByIdRoute = "GetDoctorById";

    private readonly IDoctorService _doctors;

    public DoctorsController(IDoctorService doctors)
    {
        _doctors = doctors;
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<DoctorResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<DoctorResponse>>> List(
        [FromQuery] ListDoctorsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _doctors.ListAsync(query, cancellationToken));

    [HttpGet("{id:long}", Name = GetByIdRoute)]
    [Authorize]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<DoctorResponse>> GetById(long id, CancellationToken cancellationToken) =>
        Ok(await _doctors.GetAsync(id, cancellationToken));

    /// <summary>Needs doctors.manage in every clinic of the request (checked in the service).</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<DoctorResponse>> Create(
        [FromBody] CreateDoctorRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _doctors.CreateAsync(request, cancellationToken);

        return CreatedAtRoute(GetByIdRoute, new { id = created.Id }, created);
    }

    /// <summary>Names and specialties, a full replace. Needs doctors.manage in any of the doctor's clinics.</summary>
    [HttpPut("{id:long}")]
    [Authorize]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<DoctorResponse>> Update(
        long id,
        [FromBody] UpdateDoctorRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _doctors.UpdateAsync(id, request, cancellationToken));

    /// <summary>Soft delete. Needs doctors.manage in all of the doctor's clinics, active or not.</summary>
    [HttpDelete("{id:long}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await _doctors.DeleteAsync(id, cancellationToken);

        return NoContent();
    }

    /// <summary>Assigns the doctor to a clinic, active. Needs doctors.manage in that clinic.</summary>
    [HttpPost("{id:long}/clinics/{clinicId:long}")]
    [Authorize(Policy = Permissions.Doctors.Manage)]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<DoctorResponse>> AddClinic(long id, long clinicId, CancellationToken cancellationToken) =>
        Ok(await _doctors.AddClinicAsync(id, clinicId, cancellationToken));

    /// <summary>The doctor works at the clinic again (idempotent). Needs doctors.manage in that clinic.</summary>
    [HttpPost("{id:long}/clinics/{clinicId:long}/activate")]
    [Authorize(Policy = Permissions.Doctors.Manage)]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<DoctorResponse>> ActivateClinic(long id, long clinicId, CancellationToken cancellationToken) =>
        Ok(await _doctors.ActivateClinicAsync(id, clinicId, cancellationToken));

    /// <summary>The doctor stopped working at the clinic (idempotent). Needs doctors.manage in that clinic.</summary>
    [HttpPost("{id:long}/clinics/{clinicId:long}/deactivate")]
    [Authorize(Policy = Permissions.Doctors.Manage)]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<DoctorResponse>> DeactivateClinic(long id, long clinicId, CancellationToken cancellationToken) =>
        Ok(await _doctors.DeactivateClinicAsync(id, clinicId, cancellationToken));

    /// <summary>The week of one assignment, active or not. Open to any signed-in user (no policy).</summary>
    [HttpGet("{id:long}/clinics/{clinicId:long}/working-hours")]
    [Authorize]
    [ProducesResponseType(typeof(WorkingHoursResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<WorkingHoursResponse>> GetWorkingHours(
        long id,
        long clinicId,
        CancellationToken cancellationToken) =>
        Ok(await _doctors.GetWorkingHoursAsync(id, clinicId, cancellationToken));

    /// <summary>Replaces the whole week of one assignment. Needs doctors.manage in that clinic.</summary>
    [HttpPut("{id:long}/clinics/{clinicId:long}/working-hours")]
    [Authorize(Policy = Permissions.Doctors.Manage)]
    [ProducesResponseType(typeof(WorkingHoursResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<WorkingHoursResponse>> ReplaceWorkingHours(
        long id,
        long clinicId,
        [FromBody] ReplaceWorkingHoursRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _doctors.ReplaceWorkingHoursAsync(id, clinicId, request, cancellationToken));
}
