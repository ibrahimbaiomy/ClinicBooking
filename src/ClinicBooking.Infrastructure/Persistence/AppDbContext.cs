using System.Linq.Expressions;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

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

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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
        }
    }

    // e => !e.IsDeleted
    private static LambdaExpression NotDeleted(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var isDeleted = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
        return Expression.Lambda(Expression.Not(isDeleted), parameter);
    }
}
