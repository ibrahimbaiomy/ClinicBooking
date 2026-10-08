using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Controllers;

/// <summary>
/// Patients (D44, D63). Every action needs its own global permission, reading included: this is personal
/// data. Same shape as <see cref="ClinicsController"/> otherwise.
/// </summary>
[ApiController]
[Route("api/patients")]
[ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class PatientsController : ControllerBase
{
    private const string GetByIdRoute = "GetPatientById";

    private readonly IPatientService _patients;

    public PatientsController(IPatientService patients)
    {
        _patients = patients;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Patients.Read)]
    [ProducesResponseType(typeof(PagedResponse<PatientResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PatientResponse>>> List(
        [FromQuery] ListPatientsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _patients.ListAsync(query, cancellationToken));

    [HttpGet("{id:long}", Name = GetByIdRoute)]
    [Authorize(Policy = Permissions.Patients.Read)]
    [ProducesResponseType(typeof(PatientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PatientResponse>> GetById(long id, CancellationToken cancellationToken) =>
        Ok(await _patients.GetAsync(id, cancellationToken));

    /// <summary>409 error.patient.phone_exists (with the matches) when other patients have this phone, unless confirmed.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Patients.Create)]
    [ProducesResponseType(typeof(PatientResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<PatientResponse>> Create(
        [FromBody] CreatePatientRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _patients.CreateAsync(request, cancellationToken);

        return CreatedAtRoute(GetByIdRoute, new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = Permissions.Patients.Edit)]
    [ProducesResponseType(typeof(PatientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<PatientResponse>> Update(
        long id,
        [FromBody] UpdatePatientRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _patients.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = Permissions.Patients.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await _patients.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
