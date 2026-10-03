namespace ClinicBooking.Application.Interfaces;

/// <summary>The caller on whose behalf the current operation runs.</summary>
public interface IUser
{
    /// <summary>Null means the system or an anonymous caller.</summary>
    long? Id { get; }
}
