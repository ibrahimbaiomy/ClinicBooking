namespace ClinicBooking.Domain.Enums;

/// <summary>
/// The status machine of an appointment (D41): <see cref="Booked"/> → <see cref="Completed"/> |
/// <see cref="Cancelled"/> | <see cref="NoShow"/>. The last three are final (D66). Stored as a number.
/// </summary>
public enum AppointmentStatus : byte
{
    Booked = 0,
    Completed = 1,
    Cancelled = 2,
    NoShow = 3,
}
