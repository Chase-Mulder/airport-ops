using Airport.Core.Persistence;
using Airport.Core.Simulation;

namespace Airport.Desktop;

/// <summary>Loads the airport from the database at startup, runs the simulation, and flushes writes on shutdown.</summary>
public sealed class EngineHostedService(AirportEngine engine, IAirportStore store, ILogger<EngineHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await engine.InitializeAsync(cancellationToken);
        engine.Start();
        log.LogInformation("Simulation started using {Storage}", engine.StorageDescription);
        if (store.LastError is not null)
            log.LogWarning("Database problem, running in memory only: {Error}", store.LastError);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        engine.Stop();
        await store.FlushAsync();
    }
}
