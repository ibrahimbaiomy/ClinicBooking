using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Interfaces;

/// <summary>The data access surface services depend on (D2). Implemented in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Specialty> Specialties { get; }

    DbSet<Clinic> Clinics { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<UserClinicPermission> UserClinicPermissions { get; }

    DbSet<Doctor> Doctors { get; }

    DbSet<DoctorSpecialty> DoctorSpecialties { get; }

    DbSet<DoctorClinic> DoctorClinics { get; }

    DbSet<DoctorSlotDuration> DoctorSlotDurations { get; }

    DbSet<WorkingHourPeriod> WorkingHourPeriods { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a loaded entity as changed although none of its own columns changed (its children did), so
    /// the save stamps <c>Updated*</c>, bumps its row version and checks the version it was loaded with (D50, D61).
    /// </summary>
    void MarkModified(AuditableEntity entity);

    /// <summary>
    /// Runs <paramref name="work"/> in one serializable transaction, so a rule that reads and then
    /// writes (the last administrator, D57) cannot be beaten by a parallel request. When SQL Server
    /// picks this request as a deadlock victim the result is a 409 <c>error.concurrency.conflict</c>.
    /// </summary>
    Task<T> InSerializableTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}
