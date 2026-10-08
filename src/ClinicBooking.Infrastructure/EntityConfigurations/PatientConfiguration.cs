using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        // nvarchar, never varchar: names are often Arabic.
        builder.Property(p => p.Name).IsRequired().IsUnicode().HasMaxLength(Patient.NameMaxLength);
        builder.Property(p => p.NameNormalized).IsRequired().IsUnicode().HasMaxLength(Patient.NameMaxLength);
        builder.Property(p => p.Phone).IsRequired().IsUnicode().HasMaxLength(PhoneNumber.MaxLength);

        // Not unique on purpose (D44): family members share numbers. The phone index serves the duplicate
        // warning and the phone prefix search; the name index the default sort. Name search is a scan (D63).
        builder.HasIndex(p => p.Phone).HasFilter("[IsDeleted] = 0").HasDatabaseName("IX_Patients_Phone");
        builder.HasIndex(p => p.NameNormalized).HasFilter("[IsDeleted] = 0").HasDatabaseName("IX_Patients_NameNormalized");
    }
}
