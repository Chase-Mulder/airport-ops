using Airport.Core.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Airport.Data;

public static class DataServiceExtensions
{
    public const string DefaultSqlServer =
        "Server=(localdb)\\MSSQLLocalDB;Database=AirportOps;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>
    /// Registers EF Core and <see cref="IAirportStore"/>. Reads:
    /// <code>
    /// "Database": { "Provider": "Sqlite" | "SqlServer", "ConnectionString": "..." }
    /// </code>
    /// </summary>
    public static IServiceCollection AddAirportDatabase(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var connectionString = config["Database:ConnectionString"];
        var useSqlServer = provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);

        string description;
        if (useSqlServer)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) connectionString = DefaultSqlServer;
            var csb = new SqlConnectionStringBuilder(connectionString);
            description = $"SQL Server · {csb.DataSource} / {csb.InitialCatalog}";
            services.AddDbContextFactory<AirportDbContext>(o => o.UseSqlServer(connectionString));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(connectionString)) connectionString = "Data Source=airport.db";
            var csb = new SqliteConnectionStringBuilder(connectionString);
            // Relative paths are resolved next to the .exe so double-clicking it always finds the same file.
            if (!Path.IsPathRooted(csb.DataSource))
                csb.DataSource = Path.Combine(AppContext.BaseDirectory, csb.DataSource);
            connectionString = csb.ToString();
            description = $"SQLite · {Path.GetFileName(csb.DataSource)}";
            services.AddDbContextFactory<AirportDbContext>(o => o.UseSqlite(connectionString));
        }

        services.AddSingleton<IAirportStore>(sp => new EfAirportStore(
            sp.GetRequiredService<IDbContextFactory<AirportDbContext>>(),
            description,
            sp.GetRequiredService<ILogger<EfAirportStore>>()));
        return services;
    }
}
