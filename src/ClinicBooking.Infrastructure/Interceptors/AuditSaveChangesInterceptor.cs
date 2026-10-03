using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClinicBooking.Infrastructure.Interceptors;

/// <summary>
/// Stamps audit fields (D36) and turns removal of soft-deletable entities into an
/// update (D35). Bulk operations (ExecuteUpdate/ExecuteDelete) bypass this class
/// and are not allowed on these entities.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;
    private readonly IUser _user;

    public AuditSaveChangesInterceptor(TimeProvider timeProvider, IUser user)
    {
        _timeProvider = timeProvider;
        _user = user;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var userId = _user.Id;

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is ISoftDeletable && entry.State == EntityState.Deleted)
            {
                // Values are set through the entry so only these columns are updated.
                entry.State = EntityState.Unchanged;
                entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
                entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = now;
                entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = userId;
            }

            if (entry.Entity is not IAuditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = userId;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAuditable.UpdatedBy)).CurrentValue = userId;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }
    }
}
