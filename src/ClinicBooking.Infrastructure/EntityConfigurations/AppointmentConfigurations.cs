using ClinicBooking.Application.Features.Appointments;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Enums;
using ClinicBooking.Domain.ValueObjects;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicBooking.Infrastructure.EntityConfigurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.Property(a => a.BookingReference).IsRequired().IsUnicode(false).HasMaxLength(BookingReference.Length);
        builder.Property(a => a.Status).HasConversion<byte>();

        // The reference is checked before the insert inside a serializable transaction (D66); the index guards it.
        builder.HasIndex(a => a.BookingReference).IsUnique().HasDatabaseName("UX_Appointments_BookingReference");

        // The double-booking guard (D31, D43): one live appointment per doctor and start, across every clinic.
        builder.HasIndex(a => new { a.DoctorId, a.StartUtc })
            .IsUnique()
            .HasFilter($"[Status] <> {(byte)AppointmentStatus.Cancelled}")
            .HasDatabaseName("UX_Appointments_Doctor_Start")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, AppointmentErrors.SlotTaken);

        builder.HasIndex(a => new { a.ClinicId, a.StartUtc }); // the day list
        builder.HasIndex(a => new { a.PatientId, a.StartUtc }); // the patient-overlap warning, deleting a patient

        // Real foreign keys, no navigations (D66): a soft-deleted doctor or patient must never hide an
        // appointment through a filtered join. Nothing cascades: appointments are never deleted (D35). The
        // database refuses a hard delete (NO ACTION, as restrict); ClientNoAction keeps EF from touching tracked
        // appointments when their doctor or patient is soft-deleted in the same save (the confirm-and-cancel flow).
        builder.HasOne<Doctor>().WithMany().HasForeignKey(a => a.DoctorId).OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne<Clinic>().WithMany().HasForeignKey(a => a.ClinicId).OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.ClientNoAction);
    }
}

public class DoctorLeaveDayConfiguration : IEntityTypeConfiguration<DoctorLeaveDay>
{
    public void Configure(EntityTypeBuilder<DoctorLeaveDay> builder)
    {
        builder.ToTable("DoctorLeaveDays");

        // One row per doctor and date; a parallel add of the same date is a 409.
        builder.HasIndex(l => new { l.DoctorId, l.Date })
            .IsUnique()
            .HasDatabaseName("UX_DoctorLeaveDays_Doctor_Date")
            .HasAnnotation(UniqueViolationTranslation.ConflictKeyAnnotation, ConcurrencyErrors.Conflict);

        builder.HasOne<Doctor>().WithMany().HasForeignKey(l => l.DoctorId).OnDelete(DeleteBehavior.Restrict);
    }
}
