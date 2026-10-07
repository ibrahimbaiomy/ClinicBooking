using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Features.Doctors;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("Doctors");

        // nvarchar, never varchar: names are Arabic. No unique index: people share names (D61).
        builder.Property(d => d.NameAr).IsRequired().IsUnicode().HasMaxLength(Doctor.NameMaxLength);
        builder.Property(d => d.NameEn).IsRequired().IsUnicode().HasMaxLength(Doctor.NameMaxLength);
        builder.Property(d => d.NameArNormalized).IsRequired().IsUnicode().HasMaxLength(Doctor.NameMaxLength);
        builder.Property(d => d.NameEnNormalized).IsRequired().IsUnicode().HasMaxLength(Doctor.NameMaxLength);

        // No cascade anywhere from Doctor: a delete is a soft delete and keeps every related row (D35).
        builder.HasMany(d => d.Specialties).WithOne().HasForeignKey(s => s.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(d => d.Clinics).WithOne().HasForeignKey(c => c.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(d => d.SlotDurations).WithOne().HasForeignKey(s => s.DoctorId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DoctorSpecialtyConfiguration : IEntityTypeConfiguration<DoctorSpecialty>
{
    public void Configure(EntityTypeBuilder<DoctorSpecialty> builder)
    {
        builder.ToTable("DoctorSpecialties");

        builder.HasIndex(s => new { s.DoctorId, s.SpecialtyId })
            .IsUnique()
            .HasDatabaseName("UX_DoctorSpecialties_Doctor_Specialty")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, ConcurrencyErrors.Conflict);
        builder.HasIndex(s => s.SpecialtyId); // the in-use check on delete (D50)

        // Specialties are soft-deleted; one in use by a live doctor cannot be deleted (error.specialty.in_use).
        builder.HasOne(s => s.Specialty).WithMany().HasForeignKey(s => s.SpecialtyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DoctorClinicConfiguration : IEntityTypeConfiguration<DoctorClinic>
{
    public void Configure(EntityTypeBuilder<DoctorClinic> builder)
    {
        builder.ToTable("DoctorClinics");

        // One assignment per doctor and clinic, active or not (D61).
        builder.HasIndex(c => new { c.DoctorId, c.ClinicId })
            .IsUnique()
            .HasDatabaseName("UX_DoctorClinics_Doctor_Clinic")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, DoctorErrors.ClinicAlreadyAssigned);
        builder.HasIndex(c => c.ClinicId); // the in-use check on clinic delete and the list filter

        builder.HasOne(c => c.Clinic).WithMany().HasForeignKey(c => c.ClinicId).OnDelete(DeleteBehavior.Restrict);

        // Assignments are never deleted, so the cascade only keeps the table consistent if one ever is.
        builder.HasMany(c => c.WorkingHours).WithOne().HasForeignKey(p => p.DoctorClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DoctorSlotDurationConfiguration : IEntityTypeConfiguration<DoctorSlotDuration>
{
    public void Configure(EntityTypeBuilder<DoctorSlotDuration> builder)
    {
        builder.ToTable("DoctorSlotDurations");

        // One duration per doctor and starting date (D43). Two parallel changes for the same date: 409.
        builder.HasIndex(s => new { s.DoctorId, s.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("UX_DoctorSlotDurations_Doctor_EffectiveFrom")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, ConcurrencyErrors.Conflict);
    }
}

public class WorkingHourPeriodConfiguration : IEntityTypeConfiguration<WorkingHourPeriod>
{
    public void Configure(EntityTypeBuilder<WorkingHourPeriod> builder)
    {
        builder.ToTable("WorkingHourPeriods");

        // Cairo wall-clock times (D12): SQL time(0), whole minutes.
        builder.Property(p => p.Start).HasColumnType("time(0)");
        builder.Property(p => p.End).HasColumnType("time(0)");
        builder.Property(p => p.DayOfWeek).HasConversion<byte>();

        builder.HasIndex(p => p.DoctorClinicId);
    }
}
