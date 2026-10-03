namespace ClinicBooking.Tests.Support;

/// <summary>A clock tests can move, so audit timestamps can be asserted exactly.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
