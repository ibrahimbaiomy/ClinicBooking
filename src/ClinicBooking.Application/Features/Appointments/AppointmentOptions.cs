namespace ClinicBooking.Application.Features.Appointments;

/// <summary>Bound from the "Appointments" configuration section (D66).</summary>
public sealed class AppointmentOptions
{
    public const string SectionName = "Appointments";

    /// <summary>How far ahead a booking may be: the start's Cairo date is at most today plus this many days.</summary>
    public int MaxDaysAhead { get; set; } = 90;
}
