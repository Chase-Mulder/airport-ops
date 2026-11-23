using Airport.Core.Domain;

namespace Airport.Core.Simulation;

public sealed partial class AirportEngine
{
    /// <summary>
    /// Read-only window on the live state. Only valid inside <see cref="AirportEngine.Read{T}"/>;
    /// project what you need into records or lists before returning.
    /// </summary>
    public sealed class AirportView
    {
        private readonly AirportEngine _e;
        internal AirportView(AirportEngine engine) => _e = engine;

        public DateTime Now => _e.SimNow;
        public bool IsReady => _e.IsReady;
        public SimSettings Settings => _e.Settings;

        public IReadOnlyList<Airline> Airlines => _e._data.Airlines;
        public IReadOnlyList<AircraftType> AircraftTypes => _e._data.AircraftTypes;
        public IReadOnlyList<Gate> Gates => _e._data.Gates;
        public IReadOnlyList<Flight> Flights => _e._data.Flights;
        public IReadOnlyList<ServiceTask> Tasks => _e._data.Tasks;
        public IEnumerable<Flight> OpenFlights => _e._openFlights.Select(id => _e._flights[id]);
        public IEnumerable<Aircraft> Aircraft => _e._aircraft.Values;
        public IReadOnlyList<GroundVehicle> Vehicles => _e._vehicles;
        public IEnumerable<EngineEvent> Events => _e._events;

        public Airline? AirlineOf(Flight f) => _e._airlines.GetValueOrDefault(f.AirlineId);
        public AircraftType? TypeOf(Flight f) => _e._types.GetValueOrDefault(f.AircraftTypeId);
        public Gate? GateOf(Flight f) => f.GateId is int g ? _e._gates.GetValueOrDefault(g) : null;
        public Gate? GateById(int id) => _e._gates.GetValueOrDefault(id);
        public Flight? FlightById(int id) => _e._flights.GetValueOrDefault(id);
        public ServiceTask? TaskById(int id) => _e._tasks.GetValueOrDefault(id);
        public Aircraft? AircraftFor(int flightId) => _e._aircraft.GetValueOrDefault(flightId);

        public IReadOnlyList<ServiceTask> TasksFor(int flightId) =>
            _e._tasksByFlight.TryGetValue(flightId, out var list) ? list : Array.Empty<ServiceTask>();

        /// <summary>The flight using or holding a stand right now.</summary>
        public Flight? OccupantOf(int gateId) =>
            _e._gateOccupant.TryGetValue(gateId, out var fid) ? _e._flights.GetValueOrDefault(fid) : null;

        public Flight? RunwayFlight => _e._runwayOwner is { } ac ? _e._flights.GetValueOrDefault(ac.FlightId) : null;
        public RunwayOperation RunwayOperation => _e._runwayOwner is null ? RunwayOperation.None : _e._lastRunwayOp;

        /// <summary>Flight number shown for the current phase: arrival number until on block, then the departure number.</summary>
        public string Callsign(Flight f) => _e.Label(f);

        /// <summary>Inbound and past its scheduled arrival without a stand: circling.</summary>
        public bool IsHolding(Flight f) => f.Status == FlightStatus.Approach && _e.SimNow > f.ScheduledArrival.AddMinutes(1);

        /// <summary>Minutes late leaving the gate (actual or, if still on the ground, projected).</summary>
        public double DepartureDelayMinutes(Flight f)
        {
            var offBlock = f.OffBlockAt ?? (f.IsOpen && _e.SimNow > f.ScheduledDeparture ? _e.SimNow : (DateTime?)null);
            return offBlock is DateTime t ? Math.Max(0, (t - f.ScheduledDeparture).TotalMinutes) : 0;
        }

        public double ArrivalDelayMinutes(Flight f)
        {
            var onBlock = f.OnBlockAt ?? (f.Status is FlightStatus.Approach or FlightStatus.Landing or FlightStatus.TaxiIn && _e.SimNow > f.ScheduledArrival ? _e.SimNow : (DateTime?)null);
            return onBlock is DateTime t ? Math.Max(0, (t - f.ScheduledArrival).TotalMinutes) : 0;
        }

        /// <summary>0..1 share of turnaround tasks done.</summary>
        public double TurnaroundProgress(Flight f)
        {
            var tasks = TasksFor(f.Id);
            return tasks.Count == 0 ? 0 : tasks.Count(t => t.Status == ServiceStatus.Done) / (double)tasks.Count;
        }

        public bool GateHasConflict(Gate gate, Flight flight) => _e.GateHasConflict(gate, flight);
    }
}
