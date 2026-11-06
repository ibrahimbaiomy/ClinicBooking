using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Api.Controllers;

/// <summary>
/// Appointments of one clinic (D66): booking, the day list and the free slots. The clinic is in the route, so
/// the clinic-scoped policy decides (403 when the permission is missing there).
/// </summary>
[ApiController]
[Route("api/clinics/{clinicId:long}")]
[ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ClinicAppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointments;

    public ClinicAppointmentsController(IAppointmentService appointments)
    {
        _appointments = appointments;
    }

    /// <summary>
    /// Books a slot. 409 error.appointment.slot_taken when it is taken; 409 error.appointment.patient_overlap (with
    /// the matches) when the patient has an overlapping appointment, unless confirmed.
    /// </summary>
    [HttpPost("appointments")]
    [Authorize(Policy = Permissions.Appointments.Book)]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<AppointmentResponse>> Book(
        long clinicId,
        [FromBody] BookAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _appointments.BookAsync(clinicId, request, cancellationToken);

        return CreatedAtRoute(AppointmentsController.GetByReferenceRoute, new { reference = created.BookingReference }, created);
    }

    /// <summary>One Cairo date in this clinic, every status, by start. The patient's name and phone need patients.read.</summary>
    [HttpGet("appointments")]
    [Authorize(Policy = Permissions.Appointments.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AppointmentResponse>>> ListDay(
        long clinicId,
        [FromQuery] DayAppointmentsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _appointments.ListDayAsync(clinicId, query, cancellationToken));

    /// <summary>The free slots of one doctor here on one Cairo date. Needs appointments.book or appointments.reschedule here.</summary>
    [HttpGet("doctors/{doctorId:long}/available-slots")]
    [Authorize]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<AvailableSlotsResponse>> AvailableSlots(
        long clinicId,
        long doctorId,
        [FromQuery] AvailableSlotsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _appointments.AvailableSlotsAsync(clinicId, doctorId, query, cancellationToken));
}

/// <summary>
/// One appointment, addressed by its booking reference (D6, D66). Authorized in the service against the
/// appointment's clinic: 404 when the caller holds nothing there, 403 when it holds another permission there.
/// </summary>
[ApiController]
[Route("api/appointments/{reference}")]
[ProducesResponseType(typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class AppointmentsController : ControllerBase
{
    public const string GetByReferenceRoute = "GetAppointmentByReference";

    private readonly IAppointmentService _appointments;

    public AppointmentsController(IAppointmentService appointments)
    {
        _appointments = appointments;
    }

    /// <summary>Needs appointments.read in the appointment's clinic.</summary>
    [HttpGet("", Name = GetByReferenceRoute)]
    [Authorize]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AppointmentResponse>> Get(string reference, CancellationToken cancellationToken) =>
        Ok(await _appointments.GetAsync(reference, cancellationToken));

    /// <summary>A new start with the same doctor and clinic. Needs appointments.reschedule there.</summary>
    [HttpPost("reschedule")]
    [Authorize]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<AppointmentResponse>> Reschedule(
        string reference,
        [FromBody] RescheduleAppointmentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _appointments.RescheduleAsync(reference, request, cancellationToken));

    /// <summary>Only before the start. Needs appointments.cancel there.</summary>
    [HttpPost("cancel")]
    [Authorize]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<AppointmentResponse>> Cancel(
        string reference,
        [FromBody] ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _appointments.CancelAsync(reference, request, cancellationToken));

    /// <summary>Only after the start. Needs appointments.record there.</summary>
    [HttpPost("complete")]
    [Authorize]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<AppointmentResponse>> Complete(
        string reference,
        [FromBody] ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _appointments.CompleteAsync(reference, request, cancellationToken));

    /// <summary>Only after the start. Needs appointments.record there.</summary>
    [HttpPost("no-show")]
    [Authorize]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<AppointmentResponse>> NoShow(
        string reference,
        [FromBody] ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _appointments.MarkNoShowAsync(reference, request, cancellationToken));
}
