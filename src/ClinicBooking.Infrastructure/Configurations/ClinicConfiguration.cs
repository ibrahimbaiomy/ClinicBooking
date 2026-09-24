namespace ClinicBooking.Infrastructure.Configurations;

public class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Address)
            .HasMaxLength(300);

        builder.Property(c => c.PhoneNumber)
            .HasMaxLength(20);
    }
}
