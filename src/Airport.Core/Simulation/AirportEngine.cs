using System.Diagnostics;
using Airport.Core.Domain;
using Airport.Core.Persistence;

namespace Airport.Core.Simulation;

/// <summary>
/// The heart of the Airport Operations Center. Owns the live state, validates operator commands,
/// plans gates, sequences the runway, taxis aircraft, dispatches ground vehicles to turnaround
/// tasks and streams every change to <see cref="IAirportStore"/>.
///
/// Thread safety: all reads and writes go through <c>_gate</c>. UI code reads with
/// <see cref="Read{T}"/>, which hands it an <see cref="AirportView"/> inside the lock.
/// </summary>
public sealed partial class AirportEngine : IDisposable
{
    public const int TickMs = 100;
    private const int MaxClosedFlightsInMemory = 1500;
    private const int MaxEvents = 80;

    private readonly object _gate = new();
    private readonly IAirportStore _store;
    private readonly Random _rng = new();
    private readonly AirportView _view;

    private readonly bool[,] _roads = AirportLayout.BuildVehicleRoads();
    private readonly Dictionary<Cell, List<Cell>> _taxiGraph = AirportLayout.BuildTaxiGraph();
    private readonly Func<Cell, IEnumerable<Cell>> _roadNeighbors = Pathfinder.GridNeighbors(AirportLayout.Width, AirportLayout.Height);
    private readonly Func<Cell, IEnumerable<Cell>> _taxiNeighbors;

    private AirportData _data = new();
    private Dictionary<int, Airline> _airlines = new();
    private Dictionary<int, AircraftType> _types = new();
    private Dictionary<int, Gate> _gates = new();
    private Dictionary<int, Flight> _flights = new();
    private Dictionary<int, ServiceTask> _tasks = new();
    private readonly Dictionary<int, List<ServiceTask>> _tasksByFlight = new();
    private readonly HashSet<int> _openFlights = new();

    /// <summary>gateId → flightId physically using or holding the stand (from approach until pushback).</summary>
    private readonly Dictionary<int, int> _gateOccupant = new();
    /// <summary>flightId → aircraft on the airfield.</summary>
    private readonly Dictionary<int, Aircraft> _aircraft = new();
    private readonly List<GroundVehicle> _vehicles = new();
    private readonly LinkedList<EngineEvent> _events = new();

    private Aircraft? _runwayOwner;
    private RunwayOperation _lastRunwayOp = RunwayOperation.None;
    private bool _arrivalReady, _departureWaiting, _departureWaitingNext;

    private int _nextFlightId = 1, _nextTaskId = 1;
    private DateTime _nextAutoArrival;
    private double _pruneAccumulator;
    private bool _resetting;
    private CancellationTokenSource? _cts;

    public AirportEngine(IAirportStore store)
    {
        _store = store;
        _view = new AirportView(this);
        _taxiNeighbors = Pathfinder.GraphNeighbors(_taxiGraph);
    }

    public SimSettings Settings { get; } = new();
    public DateTime SimNow { get; private set; } = DateTime.Now;
    public bool IsReady { get; private set; }
    public string? LastError { get; private set; }
    public string StorageDescription => _store.Description;
    public string? StorageError => _store.LastError;

    /// <summary>Raised ~5 times per second while the simulation runs.</summary>
    public event Action? Changed;

    // ------------------------------------------------------------------ lifecycle

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var data = await _store.LoadAsync(ct);
        if (data is null || data.Gates.Count == 0)
        {
            data = SeedData.Create(DateTime.Now);
            await _store.SaveAllAsync(data, ct);
        }

        lock (_gate)
        {
            Load(data);
            IsReady = true;
            Log(EventKind.Info, $"Airport loaded from {_store.Description}: {_openFlights.Count} open flights, {_vehicles.Count} ground vehicles.");
        }
        Changed?.Invoke();
    }

    /// <summary>Wipes storage and builds a fresh demo schedule.</summary>
    public async Task ResetAsync()
    {
        lock (_gate)
        {
            _resetting = true;
            IsReady = false;
        }
        try
        {
            var data = SeedData.Create(DateTime.Now, seed: Environment.TickCount);
            await _store.SaveAllAsync(data);
            lock (_gate)
            {
                Load(data);
                _events.Clear();
                Log(EventKind.Info, "Demo airport reset.");
            }
        }
        finally
        {
            lock (_gate)
            {
                IsReady = true;
                _resetting = false;
            }
            Changed?.Invoke();
        }
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var last = 0.0;
        var tick = 0;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TickMs, ct); }
            catch (OperationCanceledException) { break; }

            var now = clock.Elapsed.TotalSeconds;
            var dt = Math.Min(now - last, 0.5); // never jump more than half a second (background tab, debugger)
            last = now;

            try
            {
                lock (_gate) Tick(dt);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            if (++tick % 2 == 0) Changed?.Invoke();
        }
    }

    /// <summary>Runs a query against the live state under the engine lock. Return copies, not live lists.</summary>
    public T Read<T>(Func<AirportView, T> query)
    {
        lock (_gate) return query(_view);
    }

    // ------------------------------------------------------------------ loading & recovery

    private void Load(AirportData data)
    {
        _data = data;
        _airlines = data.Airlines.ToDictionary(a => a.Id);
        _types = data.AircraftTypes.ToDictionary(t => t.Id);
        _gates = data.Gates.ToDictionary(g => g.Id);
        _flights = data.Flights.ToDictionary(f => f.Id);
        _tasks = data.Tasks.ToDictionary(t => t.Id);

        _tasksByFlight.Clear();
        foreach (var t in data.Tasks)
        {
            if (!_tasksByFlight.TryGetValue(t.FlightId, out var list)) _tasksByFlight[t.FlightId] = list = new List<ServiceTask>();
            list.Add(t);
        }

        _openFlights.Clear();
        _gateOccupant.Clear();
        _aircraft.Clear();
        _runwayOwner = null;
        _lastRunwayOp = RunwayOperation.None;

        _nextFlightId = data.Flights.Count == 0 ? 1 : data.Flights.Max(f => f.Id) + 1;
        _nextTaskId = data.Tasks.Count == 0 ? 1 : data.Tasks.Max(t => t.Id) + 1;

        var latest = DateTime.Now;
        foreach (var f in data.Flights)
        {
            var stamp = f.DepartedAt ?? f.OffBlockAt ?? f.OnBlockAt ?? f.LandedAt;
            if (stamp is DateTime s && s > latest) latest = s;
        }
        SimNow = latest;

        foreach (var f in data.Flights.OrderBy(f => f.ScheduledArrival))
        {
            if (!f.IsOpen) continue;
            _openFlights.Add(f.Id);

            switch (f.Status)
            {
                // Aircraft positions are not saved. Anything that was moving is snapped to a stable state.
                case FlightStatus.Landing or FlightStatus.TaxiIn:
                    if (f.GateId is int g && !_gateOccupant.ContainsKey(g))
                    {
                        f.Status = FlightStatus.AtGate;
                        f.OnBlockAt = SimNow;
                    }
                    else
                    {
                        f.Status = FlightStatus.Approach;
                        f.LandedAt = null;
                    }
                    _store.Enqueue(f);
                    break;

                case FlightStatus.TaxiOut:
                    f.Status = FlightStatus.Departed;
                    f.DepartedAt = SimNow;
                    _openFlights.Remove(f.Id);
                    _store.Enqueue(f);
                    continue;
            }

            if (f.Status is FlightStatus.AtGate or FlightStatus.Boarding && f.GateId is int gateId && _gates.TryGetValue(gateId, out var gate))
            {
                _gateOccupant[gate.Id] = f.Id;
                var ac = new Aircraft { FlightId = f.Id, HeadingDeg = -90 };
                ac.PlaceAt(AirportLayout.StandCell(gate));
                _aircraft[f.Id] = ac;

                if (!_tasksByFlight.ContainsKey(f.Id))
                {
                    CreateTurnaroundTasks(f);
                }
                else
                {
                    foreach (var t in _tasksByFlight[f.Id].Where(t => t.NeedsVehicle && t.Status is ServiceStatus.Assigned or ServiceStatus.InProgress))
                    {
                        t.Status = ServiceStatus.Ready; // the vehicle that was doing it no longer exists
                        t.VehicleName = null;
                        t.StartedAt = null;
                        _store.Enqueue(t);
                    }
                }
            }
        }

        // Plan stands for anything on the schedule that doesn't have one yet (e.g. a freshly seeded day).
        foreach (var f in _openFlights.Select(id => _flights[id]).Where(f => f.Status == FlightStatus.Scheduled && f.GateId is null).OrderBy(f => f.ScheduledArrival).ToList())
        {
            if (PlanGate(f) is { } planned)
            {
                f.GateId = planned.Id;
                _store.Enqueue(f);
            }
        }

        _vehicles.Clear();
        var fleet = new[]
        {
            (ServiceType.Baggage, "TUG"), (ServiceType.Fuel, "FUEL"), (ServiceType.Catering, "CAT"), (ServiceType.Cleaning, "CLN"),
            (ServiceType.Baggage, "TUG"), (ServiceType.Fuel, "FUEL"), (ServiceType.Catering, "CAT"), (ServiceType.Cleaning, "CLN"),
            (ServiceType.Baggage, "TUG"), (ServiceType.Fuel, "FUEL")
        };
        var counters = new Dictionary<string, int>();
        for (var i = 0; i < fleet.Length; i++)
        {
            var (serves, prefix) = fleet[i];
            counters[prefix] = counters.GetValueOrDefault(prefix) + 1;
            var v = new GroundVehicle
            {
                Id = i + 1,
                Name = $"{prefix}-{counters[prefix]}",
                Serves = serves,
                Home = AirportLayout.DepotCell(i),
                HeadingDeg = i < 5 ? 0 : 180
            };
            v.PlaceAt(v.Home);
            _vehicles.Add(v);
        }

        var lastScheduled = data.Flights.Count == 0 ? SimNow : data.Flights.Max(f => f.ScheduledArrival);
        _nextAutoArrival = lastScheduled > SimNow ? lastScheduled.AddMinutes(Settings.AutoArrivalMinutes) : SimNow.AddMinutes(3);
    }

    // ------------------------------------------------------------------ helpers

    private void Log(EventKind kind, string text)
    {
        _events.AddFirst(new EngineEvent(SimNow, kind, text));
        while (_events.Count > MaxEvents) _events.RemoveLast();
    }

    private string Label(Flight f) => f.HasLanded && f.Status != FlightStatus.Landing && f.Status != FlightStatus.TaxiIn
        ? f.DepartureNumber
        : f.ArrivalNumber;

    /// <summary>Time window a flight is expected to use its stand (with buffers).</summary>
    private (DateTime Start, DateTime End) GateWindow(Flight f)
    {
        var start = (f.OnBlockAt ?? f.ScheduledArrival).AddMinutes(-5);
        var end = f.OffBlockAt ?? (f.ScheduledDeparture > SimNow ? f.ScheduledDeparture : SimNow.AddMinutes(10));
        return (start, end.AddMinutes(10));
    }

    private bool GateHasConflict(Gate gate, Flight flight)
    {
        var (start, end) = GateWindow(flight);
        foreach (var id in _openFlights)
        {
            var other = _flights[id];
            if (other.Id == flight.Id || other.GateId != gate.Id) continue;
            var (os, oe) = GateWindow(other);
            if (start < oe && os < end) return true;
        }
        return false;
    }

    /// <summary>Picks a compatible, conflict-free gate, preferring the smallest stand that fits.</summary>
    private Gate? PlanGate(Flight flight)
    {
        var size = _types[flight.AircraftTypeId].Size;
        return _data.Gates
            .Where(g => !g.IsClosed && g.Fits(size) && !GateHasConflict(g, flight))
            .OrderBy(g => g.MaxSize)
            .ThenBy(g => g.Id)
            .FirstOrDefault();
    }

    private void CreateTurnaroundTasks(Flight f)
    {
        var type = _types[f.AircraftTypeId];
        var list = new List<ServiceTask>();
        void Add(ServiceType st, double minutes, ServiceStatus status) => list.Add(new ServiceTask
        {
            Id = _nextTaskId++,
            FlightId = f.Id,
            Type = st,
            Status = status,
            DurationMinutes = Math.Round(minutes, 1),
            CreatedAt = SimNow
        });

        Add(ServiceType.Deboarding, Math.Max(3, f.PassengersIn / 25.0), ServiceStatus.Ready);
        Add(ServiceType.Baggage, type.BaggageMinutes, ServiceStatus.Ready);
        Add(ServiceType.Fuel, type.FuelMinutes, ServiceStatus.Ready);
        Add(ServiceType.Catering, type.CateringMinutes, ServiceStatus.Ready);
        Add(ServiceType.Cleaning, type.CleaningMinutes, ServiceStatus.Waiting);
        Add(ServiceType.Boarding, Math.Max(5, f.PassengersOut / 15.0), ServiceStatus.Waiting);

        _tasksByFlight[f.Id] = list;
        foreach (var t in list)
        {
            _tasks[t.Id] = t;
            _data.Tasks.Add(t);
            _store.Enqueue(t);
        }
    }
}
