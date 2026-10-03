using ClinicBooking.Application.Features.Specialties;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        builder.ToTable("Specialties");

        // nvarchar, never varchar: names are Arabic.
        builder.Property(s => s.NameAr).IsRequired().IsUnicode().HasMaxLength(Specialty.NameMaxLength);
        builder.Property(s => s.NameEn).IsRequired().IsUnicode().HasMaxLength(Specialty.NameMaxLength);
        builder.Property(s => s.NameArNormalized).IsRequired().IsUnicode().HasMaxLength(Specialty.NameMaxLength);
        builder.Property(s => s.NameEnNormalized).IsRequired().IsUnicode().HasMaxLength(Specialty.NameMaxLength);

        // Unique over the normalised text, among live rows only: أحمد and احمد are the same name,
        // and a deleted name can be re-created (D35, D49). The annotation names the error key
        // returned when the index is violated.
        builder.HasIndex(s => s.NameArNormalized)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Specialties_NameArNormalized")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, SpecialtyErrors.NameArTaken);
        builder.HasIndex(s => s.NameEnNormalized)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Specialties_NameEnNormalized")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, SpecialtyErrors.NameEnTaken);
    }
}
