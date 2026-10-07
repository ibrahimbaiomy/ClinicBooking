using System.Data;
using System.Linq.Expressions;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ClinicBooking.Infrastructure.Persistence;

public class AppDbContext : IdentityUserContext<ApplicationUser, long>, IAppDbContext
{
    /// <summary>Name of the named query filter that hides soft-deleted rows.</summary>
    public const string SoftDeleteFilter = "SoftDelete";

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Specialty> Specialties => Set<Specialty>();

    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserClinicPermission> UserClinicPermissions => Set<UserClinicPermission>();

    public DbSet<Doctor> Doctors => Set<Doctor>();

    public DbSet<DoctorSpecialty> DoctorSpecialties => Set<DoctorSpecialty>();

    public DbSet<DoctorClinic> DoctorClinics => Set<DoctorClinic>();

    public DbSet<DoctorSlotDuration> DoctorSlotDurations => Set<DoctorSlotDuration>();

    public DbSet<WorkingHourPeriod> WorkingHourPeriods => Set<WorkingHourPeriod>();

    public void MarkModified(AuditableEntity entity) => Entry(entity).State = EntityState.Modified;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own tables first (users and their claims; no roles, permissions are claims).
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                entityType.SetQueryFilter(SoftDeleteFilter, NotDeleted(entityType.ClrType));
            }

            // Every auditable entity carries the optimistic concurrency token (D50).
            if (entityType.BaseType is null && typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(AuditableEntity.RowVersion)).IsRowVersion();
            }
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueViolationTranslation.TryGetConflictKey(Model, exception, out _))
        {
            UniqueViolationTranslation.TryGetConflictKey(Model, exception, out var key);
            throw new ConflictException(key);
        }
    }

    public async Task<T> InSerializableTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (IsDeadlockVictim(exception))
        {
            // The disposal above rolls back. The caller looks again and retries by hand.
            throw new ConflictException(ConcurrencyErrors.Conflict);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException exception) when (UniqueViolationTranslation.TryGetConflictKey(Model, exception, out _))
        {
            UniqueViolationTranslation.TryGetConflictKey(Model, exception, out var key);
            throw new ConflictException(key);
        }
    }

    private const int DeadlockVictim = 1205;

    private static bool IsDeadlockVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictim })
            {
                return true;
            }
        }

        return false;
    }

    // e => !e.IsDeleted
    private static LambdaExpression NotDeleted(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var isDeleted = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
        return Expression.Lambda(Expression.Not(isDeleted), parameter);
    }
}
