using ClinicBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        builder.ToTable("Specialties");

        // nvarchar, never varchar: names are Arabic.
        builder.Property(s => s.NameAr).IsRequired().IsUnicode().HasMaxLength(100);
        builder.Property(s => s.NameEn).IsRequired().IsUnicode().HasMaxLength(100);

        // Unique among live rows only, so a deleted name can be re-created (D35).
        builder.HasIndex(s => s.NameAr)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Specialties_NameAr");
        builder.HasIndex(s => s.NameEn)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Specialties_NameEn");
    }
}
