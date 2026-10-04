using ClinicBooking.Application.Features.Users;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

/// <summary>Adds to what Identity configures for its user table (D57).</summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // The unique index Identity already creates over the normalised (upper-case) user name:
        // naming the 409 key makes a race between two creates a conflict, not a server error.
        builder.HasIndex(u => u.NormalizedUserName)
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, UserErrors.UserNameTaken);
    }
}
