using Airport.Core.Domain;

namespace Airport.Core.Persistence;

/// <summary>
/// Storage abstraction. The engine keeps the live state in memory and streams every change here.
/// Desktop build: SQL Server or SQLite through EF Core. Browser build: in-memory (nothing saved).
/// </summary>
public interface IAirportStore
{
    /// <summary>Human readable description shown in the UI, e.g. "SQLite · airport.db".</summary>
    string Description { get; }

    /// <summary>Loads the airport, or returns null when storage is empty.</summary>
    Task<AirportData?> LoadAsync(CancellationToken ct = default);

    /// <summary>Replaces everything in storage with <paramref name="data"/>.</summary>
    Task SaveAllAsync(AirportData data, CancellationToken ct = default);

    /// <summary>Queues an insert-or-update. Must not block: the caller holds the engine lock.</summary>
    void Enqueue(Entity entity);

    /// <summary>Waits until queued changes are written.</summary>
    Task FlushAsync();

    /// <summary>Last write error, if any (shown in the UI).</summary>
    string? LastError { get; }
}

/// <summary>Used by the GitHub Pages demo: state lives only in the browser tab.</summary>
public sealed class InMemoryAirportStore : IAirportStore
{
    public string Description => "In-memory (browser demo)";
    public string? LastError => null;
    public Task<AirportData?> LoadAsync(CancellationToken ct = default) => Task.FromResult<AirportData?>(null);
    public Task SaveAllAsync(AirportData data, CancellationToken ct = default) => Task.CompletedTask;
    public void Enqueue(Entity entity) { }
    public Task FlushAsync() => Task.CompletedTask;
}
