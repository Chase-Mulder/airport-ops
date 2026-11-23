using System.Threading.Channels;
using Airport.Core.Domain;
using Airport.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Airport.Data;

/// <summary>
/// Persists the engine's changes with EF Core. Writes are queued on a channel and applied in
/// batches by one background writer, so the simulation never waits on the database.
/// </summary>
public sealed class EfAirportStore : IAirportStore, IAsyncDisposable
{
    private const int HistoryToLoad = 1000;
    private const int MaxBatch = 500;

    private readonly IDbContextFactory<AirportDbContext> _factory;
    private readonly ILogger<EfAirportStore> _log;
    private readonly Channel<(int Generation, Entity Entity)> _queue =
        Channel.CreateUnbounded<(int, Entity)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly Task _writer;

    /// <summary>Bumped by <see cref="SaveAllAsync"/>; queued changes from an older generation are discarded.</summary>
    private int _generation;
    private int _pending;

    public EfAirportStore(IDbContextFactory<AirportDbContext> factory, string description, ILogger<EfAirportStore> log)
    {
        _factory = factory;
        _log = log;
        Description = description;
        _writer = Task.Run(WriteLoopAsync);
    }

    public string Description { get; }
    public string? LastError { get; private set; }

    public async Task<AirportData?> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await db.Database.EnsureCreatedAsync(ct);
            if (!await db.Gates.AnyAsync(ct)) return null;

            var data = new AirportData
            {
                Airlines = await db.Airlines.AsNoTracking().ToListAsync(ct),
                AircraftTypes = await db.AircraftTypes.AsNoTracking().ToListAsync(ct),
                Gates = await db.Gates.AsNoTracking().OrderBy(g => g.Id).ToListAsync(ct)
            };

            // Memory stays bounded: every open flight plus the most recent history.
            var open = await db.Flights.AsNoTracking()
                .Where(f => f.Status != FlightStatus.Departed && f.Status != FlightStatus.Cancelled)
                .ToListAsync(ct);
            var recent = await db.Flights.AsNoTracking()
                .Where(f => f.Status == FlightStatus.Departed || f.Status == FlightStatus.Cancelled)
                .OrderByDescending(f => f.Id)
                .Take(HistoryToLoad)
                .ToListAsync(ct);
            data.Flights = open.Concat(recent).OrderBy(f => f.Id).ToList();

            if (data.Flights.Count > 0)
            {
                var ids = data.Flights.Select(f => f.Id).ToHashSet();
                var minId = ids.Min();
                var tasks = await db.ServiceTasks.AsNoTracking().Where(t => t.FlightId >= minId).ToListAsync(ct);
                data.Tasks = tasks.Where(t => ids.Contains(t.FlightId)).ToList();
            }
            return data;
        }
        catch (Exception ex)
        {
            // The app still runs (in memory) if the database is unreachable; the UI shows the error.
            LastError = ex.GetBaseException().Message;
            _log.LogError(ex, "Loading the airport from the database failed");
            return null;
        }
    }

    public async Task SaveAllAsync(AirportData data, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _generation);
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await db.Database.EnsureCreatedAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Children first (foreign keys are RESTRICT).
            await db.ServiceTasks.ExecuteDeleteAsync(ct);
            await db.Flights.ExecuteDeleteAsync(ct);
            await db.Gates.ExecuteDeleteAsync(ct);
            await db.AircraftTypes.ExecuteDeleteAsync(ct);
            await db.Airlines.ExecuteDeleteAsync(ct);

            db.AddRange(data.Airlines.Select(Copy));
            db.AddRange(data.AircraftTypes.Select(Copy));
            db.AddRange(data.Gates.Select(Copy));
            await db.SaveChangesAsync(ct);
            db.AddRange(data.Flights.Select(Copy));
            await db.SaveChangesAsync(ct);
            db.AddRange(data.Tasks.Select(Copy));
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.GetBaseException().Message;
            _log.LogError(ex, "Saving the airport to the database failed");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public void Enqueue(Entity entity)
    {
        Interlocked.Increment(ref _pending);
        _queue.Writer.TryWrite((Volatile.Read(ref _generation), entity.Clone()));
    }

    public async Task FlushAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref _pending) > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await Task.WhenAny(_writer, Task.Delay(TimeSpan.FromSeconds(10)));
    }

    private static object Copy(Entity e) => e.Clone();

    /// <summary>Parents before children so foreign keys are satisfied inside a batch.</summary>
    private static int InsertOrder(Entity e) => e switch
    {
        Flight => 1,
        ServiceTask => 2,
        _ => 0
    };

    private async Task WriteLoopAsync()
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync())
        {
            // Collapse repeated updates of the same row: only the latest snapshot is written.
            var batch = new Dictionary<(Type, int), (int Generation, Entity Entity)>();
            var count = 0;
            while (count < MaxBatch && reader.TryRead(out var item))
            {
                batch[(item.Entity.GetType(), item.Entity.Id)] = item;
                count++;
            }

            await _dbLock.WaitAsync();
            try
            {
                var generation = Volatile.Read(ref _generation);
                var items = batch.Values
                    .Where(i => i.Generation == generation)
                    .Select(i => i.Entity)
                    .OrderBy(InsertOrder)
                    .ToList();

                if (items.Count > 0)
                {
                    await using var db = await _factory.CreateDbContextAsync();
                    foreach (var entity in items)
                    {
                        var existing = await db.FindAsync(entity.GetType(), entity.Id);
                        if (existing is null) db.Add(entity);
                        else db.Entry(existing).CurrentValues.SetValues(entity);
                    }
                    await db.SaveChangesAsync();
                }
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.GetBaseException().Message;
                _log.LogError(ex, "Writing {Count} change(s) to the database failed", count);
            }
            finally
            {
                _dbLock.Release();
                Interlocked.Add(ref _pending, -count);
            }
        }
    }
}
