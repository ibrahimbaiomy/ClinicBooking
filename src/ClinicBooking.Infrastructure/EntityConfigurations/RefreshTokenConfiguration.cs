using ClinicBooking.Domain.Entities;
using ClinicBooking.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        // A SHA-256 hash in hex: ASCII only, so varchar is right here.
        builder.Property(t => t.TokenHash).IsRequired().IsUnicode(false).HasMaxLength(64);
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
