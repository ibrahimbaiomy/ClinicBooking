using ClinicBooking.Application.Features.Clinics;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> builder)
    {
        builder.ToTable("Clinics");

        // nvarchar, never varchar: names and addresses are Arabic.
        builder.Property(c => c.NameAr).IsRequired().IsUnicode().HasMaxLength(Clinic.NameMaxLength);
        builder.Property(c => c.NameEn).IsRequired().IsUnicode().HasMaxLength(Clinic.NameMaxLength);
        builder.Property(c => c.NameArNormalized).IsRequired().IsUnicode().HasMaxLength(Clinic.NameMaxLength);
        builder.Property(c => c.NameEnNormalized).IsRequired().IsUnicode().HasMaxLength(Clinic.NameMaxLength);
        builder.Property(c => c.Address).IsUnicode().HasMaxLength(Clinic.AddressMaxLength);
        builder.Property(c => c.Phone).IsUnicode().HasMaxLength(PhoneNumber.MaxLength);

        // Unique over the normalised text, among live rows only (D35, D49). No index on the phone:
        // clinics may share a switchboard.
        builder.HasIndex(c => c.NameArNormalized)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Clinics_NameArNormalized")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, ClinicErrors.NameArTaken);
        builder.HasIndex(c => c.NameEnNormalized)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Clinics_NameEnNormalized")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, ClinicErrors.NameEnTaken);
    }
}
