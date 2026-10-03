using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Interfaces;

/// <summary>The data access surface services depend on (D2). Implemented in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Specialty> Specialties { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
