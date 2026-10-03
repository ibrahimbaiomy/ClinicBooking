using ClinicBooking.Application.Interfaces;

namespace ClinicBooking.Tests.Support;

/// <summary>A caller tests can switch, standing in for the JWT-based user.</summary>
public sealed class TestUser : IUser
{
    public long? Id { get; set; }
}
