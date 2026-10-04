using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class UserClinicPermissionConfiguration : IEntityTypeConfiguration<UserClinicPermission>
{
    public void Configure(EntityTypeBuilder<UserClinicPermission> builder)
    {
        builder.ToTable("UserClinicPermissions");

        // A permission name is an ASCII identifier, so varchar is right here.
        builder.Property(p => p.Permission).IsRequired().IsUnicode(false).HasMaxLength(UserClinicPermission.PermissionMaxLength);

        // One grant per user, clinic and permission. Two administrators granting the same thing at
        // once is a conflict, not a server error.
        builder.HasIndex(p => new { p.UserId, p.ClinicId, p.Permission })
            .IsUnique()
            .HasDatabaseName("UX_UserClinicPermissions_User_Clinic_Permission")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, ConcurrencyErrors.Conflict);
        builder.HasIndex(p => p.ClinicId);

        // Users are never deleted; the cascade only keeps the table consistent if one ever is.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Clinics are soft-deleted, so the rows stay and are ignored while the clinic is deleted (D57).
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
