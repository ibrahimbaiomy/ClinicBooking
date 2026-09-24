namespace ClinicBooking.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, long>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Doctor> Doctors => Set<Doctor>();

    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<Specialty> Specialties => Set<Specialty>();

    public DbSet<DoctorWorkingHour> DoctorWorkingHours => Set<DoctorWorkingHour>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var isDeletedProperty = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
            var notDeleted = Expression.Lambda(Expression.Not(isDeletedProperty), parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(notDeleted);
        }
    }
}
