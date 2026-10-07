using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Application.Features.Common;

/// <summary>The optimistic concurrency steps every edit repeats (D50), shared at the third copy (D61).</summary>
public static class ConcurrencyGuard
{
    /// <summary>
    /// The client edited a version it read earlier; if the row has moved on, it must reload (409). The
    /// save is guarded too (<see cref="SaveGuardedAsync"/>: EF compares the version it loaded), so a
    /// change that slips in between this check and the save is caught as well.
    /// </summary>
    public static void EnsureCurrent(AuditableEntity entity, string? sentRowVersion)
    {
        if (!RowVersionCodec.TryDecode(sentRowVersion, out var sent) || !sent.AsSpan().SequenceEqual(entity.RowVersion))
        {
            throw new ConflictException(ConcurrencyErrors.Conflict);
        }
    }

    /// <summary>Saves; a row changed by someone else since it was loaded is a 409.</summary>
    public static async Task SaveGuardedAsync(this IAppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ConcurrencyErrors.Conflict);
        }
    }
}
