namespace ClinicBooking.Infrastructure.Configurations;

public class DoctorWorkingHourConfiguration : IEntityTypeConfiguration<DoctorWorkingHour>
{
    public void Configure(EntityTypeBuilder<DoctorWorkingHour> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.DayOfWeek)
            .IsRequired();

        builder.Property(w => w.StartTime)
            .IsRequired();

        builder.Property(w => w.EndTime)
            .IsRequired();

        builder.HasOne(w => w.Doctor)
            .WithMany(d => d.WorkingHours)
            .HasForeignKey(w => w.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
