using Airport.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Airport.Data;

/// <summary>
/// EF Core model. Table and column names are fixed explicitly so the reporting views in
/// sql/ReportingViews.sql (and anything you write in SSMS) keep working.
/// </summary>
public class AirportDbContext(DbContextOptions<AirportDbContext> options) : DbContext(options)
{
    public DbSet<Airline> Airlines => Set<Airline>();
    public DbSet<AircraftType> AircraftTypes => Set<AircraftType>();
    public DbSet<Gate> Gates => Set<Gate>();
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<ServiceTask> ServiceTasks => Set<ServiceTask>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Airline>(e =>
        {
            e.ToTable("Airlines");
            e.Property(x => x.Code).HasMaxLength(3).IsRequired();
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Color).HasMaxLength(9);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<AircraftType>(e =>
        {
            e.ToTable("AircraftTypes");
            e.Property(x => x.Code).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Size).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<Gate>(e =>
        {
            e.ToTable("Gates");
            e.Property(x => x.Code).HasMaxLength(6).IsRequired();
            e.Property(x => x.Concourse).HasMaxLength(2);
            e.Property(x => x.MaxSize).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<Flight>(e =>
        {
            e.ToTable("Flights");
            e.Property(x => x.Registration).HasMaxLength(10);
            e.Property(x => x.ArrivalNumber).HasMaxLength(8).IsRequired();
            e.Property(x => x.DepartureNumber).HasMaxLength(8).IsRequired();
            e.Property(x => x.Origin).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Destination).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Source).HasMaxLength(10);
            e.HasIndex(x => x.ScheduledArrival);
            e.HasIndex(x => x.Status);

            // Restrict everywhere: SQL Server rejects multiple cascade paths, and history should never vanish by accident.
            e.HasOne<Airline>().WithMany().HasForeignKey(x => x.AirlineId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AircraftType>().WithMany().HasForeignKey(x => x.AircraftTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Gate>().WithMany().HasForeignKey(x => x.GateId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ServiceTask>(e =>
        {
            e.ToTable("ServiceTasks");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.VehicleName).HasMaxLength(12);
            e.HasIndex(x => x.FlightId);
            e.HasOne<Flight>().WithMany().HasForeignKey(x => x.FlightId).OnDelete(DeleteBehavior.Restrict);
        });

        // The engine assigns every Id itself.
        foreach (var entity in b.Model.GetEntityTypes().ToList())
            b.Entity(entity.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
    }
}
