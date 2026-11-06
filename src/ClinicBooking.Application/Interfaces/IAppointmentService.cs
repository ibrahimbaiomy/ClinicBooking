using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

/// <summary>Appointments (D41, D43, D66).</summary>
public interface IAppointmentService
{
    Task<AppointmentResponse> BookAsync(long clinicId, BookAppointmentRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AppointmentResponse>> ListDayAsync(long clinicId, DayAppointmentsQuery query, CancellationToken cancellationToken);

    Task<AvailableSlotsResponse> AvailableSlotsAsync(
        long clinicId,
        long doctorId,
        AvailableSlotsQuery query,
        CancellationToken cancellationToken);

    Task<AppointmentResponse> GetAsync(string reference, CancellationToken cancellationToken);

    Task<AppointmentResponse> RescheduleAsync(string reference, RescheduleAppointmentRequest request, CancellationToken cancellationToken);

    Task<AppointmentResponse> CancelAsync(string reference, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken);

    Task<AppointmentResponse> CompleteAsync(string reference, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken);

    Task<AppointmentResponse> MarkNoShowAsync(string reference, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken);
}
