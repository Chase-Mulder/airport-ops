using Airport.Core.Domain;

namespace Airport.Core.Simulation;

/// <summary>Tick loop: auto scheduler, flight lifecycle, runway sequencing, taxiing and ground handling.</summary>
public sealed partial class AirportEngine
{
    /// <summary>Largest simulated time step; keeps movement stable at high speed multipliers.</summary>
    private const double MaxStepSeconds = 1.0;

    private Flight? _readyArrival;

    private void Tick(double realDt)
    {
        if (!IsReady || _resetting || Settings.Paused) return;

        var dt = realDt * Math.Clamp(Settings.SpeedMultiplier, 1, 240);
        var steps = Math.Max(1, (int)Math.Ceiling(dt / MaxStepSeconds));
        var step = dt / steps;
        for (var i = 0; i < steps; i++) Step(step);

        _pruneAccumulator += dt;
        if (_pruneAccumulator > 600)
        {
            _pruneAccumulator = 0;
            PruneHistory();
        }
    }

    private void Step(double dt)
    {
        SimNow = SimNow.AddSeconds(dt);

        if (Settings.AutoFlights) AutoSchedule();
        UpdateFlights();

        _departureWaiting = _departureWaitingNext;
        _departureWaitingNext = false;
        RunwayControl();

        DispatchVehicles();

        foreach (var ac in _aircraft.Values.ToList()) UpdateAircraft(ac, dt);
        foreach (var v in _vehicles) UpdateVehicle(v, dt);
    }

    // ------------------------------------------------------------------ auto schedule (demo mode)

    private void AutoSchedule()
    {
        if (_nextAutoArrival < SimNow) _nextAutoArrival = SimNow.AddMinutes(2);

        var horizon = SimNow.AddMinutes(Settings.ScheduleHorizonMinutes);
        var guard = 0;
        while (_nextAutoArrival <= horizon && guard++ < 20)
        {
            var holding = _openFlights.Count(id => _flights[id].Status == FlightStatus.Approach);
            if (holding < 5)
            {
                var type = SeedData.PickType(_data, _rng);
                var flight = SeedData.NewFlight(_nextFlightId++, _data, type, _nextAutoArrival,
                    type.MinTurnMinutes + _rng.Next(5, 20), _rng, "Auto");
                AddFlight(flight);
            }
            _nextAutoArrival = _nextAutoArrival.AddMinutes(Settings.AutoArrivalMinutes * (0.5 + _rng.NextDouble()));
        }
    }

    private void AddFlight(Flight f)
    {
        f.GateId = PlanGate(f)?.Id;
        _flights[f.Id] = f;
        _data.Flights.Add(f);
        _openFlights.Add(f.Id);
        _store.Enqueue(f);
    }

    // ------------------------------------------------------------------ flight lifecycle

    private void UpdateFlights()
    {
        _readyArrival = null;
        foreach (var id in _openFlights.ToList())
        {
            var f = _flights[id];
            switch (f.Status)
            {
                case FlightStatus.Scheduled:
                    if (SimNow >= f.ScheduledArrival.AddMinutes(-Settings.ApproachLeadMinutes))
                    {
                        f.Status = FlightStatus.Approach;
                        _store.Enqueue(f);
                    }
                    break;

                case FlightStatus.Approach:
                    if (SecureGate(f) && SimNow >= f.ScheduledArrival.AddMinutes(-1) &&
                        (_readyArrival is null || f.ScheduledArrival < _readyArrival.ScheduledArrival))
                        _readyArrival = f;
                    break;

                case FlightStatus.AtGate or FlightStatus.Boarding:
                    UpdateTurnaround(f);
                    break;
            }
        }
        _arrivalReady = _readyArrival is not null;
    }

    /// <summary>
    /// An arrival may only land once it holds a free stand. Keeps the planned gate when possible,
    /// otherwise switches to any free compatible stand (gate change).
    /// </summary>
    private bool SecureGate(Flight f)
    {
        if (f.GateId is int held && _gateOccupant.TryGetValue(held, out var occupant) && occupant == f.Id) return true;

        var size = _types[f.AircraftTypeId].Size;
        bool Usable(Gate g) => !g.IsClosed && g.Fits(size) && !_gateOccupant.ContainsKey(g.Id);

        var gate = f.GateId is int gid && _gates.TryGetValue(gid, out var planned) ? planned : null;
        if (gate is null || !Usable(gate))
        {
            // Not due yet and the planned stand is still valid (just occupied): wait for it to free up.
            var plannedStillValid = gate is not null && !gate.IsClosed && gate.Fits(size);
            if (plannedStillValid && SimNow < f.ScheduledArrival) return false;

            var alternative = _data.Gates
                .Where(Usable)
                .OrderBy(g => GateHasConflict(g, f))
                .ThenBy(g => g.MaxSize)
                .ThenBy(g => g.Id)
                .FirstOrDefault();
            if (alternative is null) return false;

            if (gate is not null && gate.Id != alternative.Id)
                Log(EventKind.Warning, $"Gate change: {f.ArrivalNumber} {gate.Code} → {alternative.Code}.");
            f.GateId = alternative.Id;
            _store.Enqueue(f);
            gate = alternative;
        }

        _gateOccupant[gate.Id] = f.Id;
        return true;
    }

    private void UpdateTurnaround(Flight f)
    {
        if (!_tasksByFlight.TryGetValue(f.Id, out var tasks)) return;

        bool IsDone(ServiceType st) => tasks.Any(t => t.Type == st && t.Status == ServiceStatus.Done);

        foreach (var t in tasks)
        {
            switch (t.Status)
            {
                case ServiceStatus.Waiting:
                    var ready = t.Type switch
                    {
                        ServiceType.Cleaning => IsDone(ServiceType.Deboarding),
                        // Boarding starts once the cabin is clean, catered and fueled, and not too early.
                        ServiceType.Boarding => IsDone(ServiceType.Cleaning) && IsDone(ServiceType.Catering) && IsDone(ServiceType.Fuel)
                                                && SimNow >= f.ScheduledDeparture.AddMinutes(-(t.DurationMinutes + 5)),
                        _ => true
                    };
                    if (ready)
                    {
                        t.Status = ServiceStatus.Ready;
                        _store.Enqueue(t);
                    }
                    break;

                case ServiceStatus.Ready when !t.NeedsVehicle:
                    t.Status = ServiceStatus.InProgress;
                    t.StartedAt = SimNow;
                    _store.Enqueue(t);
                    if (t.Type == ServiceType.Boarding)
                    {
                        f.Status = FlightStatus.Boarding;
                        _store.Enqueue(f);
                    }
                    break;

                case ServiceStatus.InProgress when !t.NeedsVehicle:
                    if (t.StartedAt is DateTime started && SimNow >= started.AddMinutes(t.DurationMinutes))
                    {
                        t.Status = ServiceStatus.Done;
                        t.CompletedAt = SimNow;
                        _store.Enqueue(t);
                    }
                    break;
            }
        }

        if (tasks.All(t => t.Status is ServiceStatus.Done or ServiceStatus.Cancelled) &&
            SimNow >= f.ScheduledDeparture.AddMinutes(-1))
        {
            Pushback(f);
        }
    }

    private void Pushback(Flight f)
    {
        if (!_aircraft.TryGetValue(f.Id, out var ac)) return;
        var path = Pathfinder.FindPath(ac.Cell, AirportLayout.RunwayWestEnd, _taxiNeighbors, _ => true);
        if (path is null) { LastError = $"No taxi route for {f.DepartureNumber}"; return; }

        ac.Path = path;
        ac.PathIndex = 0;
        ac.Goal = AirportLayout.RunwayWestEnd;

        f.Status = FlightStatus.TaxiOut;
        f.OffBlockAt = SimNow;
        if (f.GateId is int g) _gateOccupant.Remove(g);
        _store.Enqueue(f);

        var delay = (SimNow - f.ScheduledDeparture).TotalMinutes;
        var gateCode = f.GateId is int gid ? _gates[gid].Code : "?";
        if (delay > Settings.OnTimeToleranceMinutes)
            Log(EventKind.Warning, $"{f.DepartureNumber} pushed back from {gateCode}, {delay:0} min late.");
        else if (f.Source == "Operator")
            Log(EventKind.Success, $"{f.DepartureNumber} pushed back from {gateCode} on time.");
    }

    // ------------------------------------------------------------------ runway

    /// <summary>
    /// One aircraft on the runway at a time. When both an arrival and a departure are waiting,
    /// operations alternate so neither side starves.
    /// </summary>
    private void RunwayControl()
    {
        if (_runwayOwner is not null || _readyArrival is null) return;
        if (_departureWaiting && _lastRunwayOp == RunwayOperation.Landing) return;
        SpawnLanding(_readyArrival);
    }

    private void SpawnLanding(Flight f)
    {
        var gate = _gates[f.GateId!.Value];
        var stand = AirportLayout.StandCell(gate);
        var path = Pathfinder.FindPath(AirportLayout.ApproachCell, stand, _taxiNeighbors, _ => true);
        if (path is null) { LastError = $"No taxi route to {gate.Code}"; return; }

        var ac = new Aircraft { FlightId = f.Id, HeadingDeg = 180 };
        ac.PlaceAt(AirportLayout.ApproachCell);
        ac.X = AirportLayout.ApproachCell.X + 6; // comes in from beyond the threshold
        ac.Path = path;
        ac.Goal = stand;
        _aircraft[f.Id] = ac;

        _runwayOwner = ac;
        _lastRunwayOp = RunwayOperation.Landing;

        f.Status = FlightStatus.Landing;
        f.LandedAt = SimNow;
        _store.Enqueue(f);
        if (f.Source == "Operator")
            Log(EventKind.Info, $"{f.ArrivalNumber} from {f.Origin} landed, taxiing to {gate.Code}.");
    }

    // ------------------------------------------------------------------ aircraft movement

    private void UpdateAircraft(Aircraft ac, double dt)
    {
        var f = _flights[ac.FlightId];
        switch (f.Status)
        {
            case FlightStatus.Landing or FlightStatus.TaxiIn:
            {
                var arrived = Move(ac, dt, AircraftSpeed, c => AircraftCanEnter(ac, c), replan: null, ghostAfter: Settings.GhostAfterSeconds * 4);
                if (f.Status == FlightStatus.Landing && !AirportLayout.IsRunway(ac.Cell))
                {
                    f.Status = FlightStatus.TaxiIn;
                    if (_runwayOwner == ac) _runwayOwner = null;
                    _store.Enqueue(f);
                }
                if (arrived) OnBlock(f, ac);
                break;
            }
            case FlightStatus.TaxiOut:
            {
                Move(ac, dt, AircraftSpeed, c => AircraftCanEnter(ac, c), replan: null, ghostAfter: Settings.GhostAfterSeconds * 4);
                if (AirportLayout.IsRunway(ac.Cell) && ac.X <= Settings.LiftOffX) Airborne(f, ac);
                break;
            }
        }
    }

    private double AircraftSpeed(Cell next) => AirportLayout.IsRunway(next) ? Settings.RunwaySpeed : Settings.TaxiSpeed;

    /// <summary>
    /// Traffic rules for aircraft: the runway is exclusive; elsewhere a cell must be free and,
    /// to keep a gap behind the aircraft ahead, so must the cell after it.
    /// </summary>
    private bool AircraftCanEnter(Aircraft ac, Cell next)
    {
        if (AirportLayout.IsRunway(next))
        {
            if (_runwayOwner == ac) return true;
            if (_runwayOwner is not null)
            {
                _departureWaitingNext = true;
                return false;
            }
            if (_arrivalReady && _lastRunwayOp == RunwayOperation.Takeoff)
            {
                _departureWaitingNext = true;
                return false;
            }
            _runwayOwner = ac;
            _lastRunwayOp = RunwayOperation.Takeoff;
            return true;
        }

        var after = ac.PathIndex + 1 < ac.Path.Count ? ac.Path[ac.PathIndex + 1] : (Cell?)null;
        foreach (var other in _aircraft.Values)
        {
            if (ReferenceEquals(other, ac)) continue;
            if (other.Cell == next || other.Claim == next) return false;
            if (after is Cell a && !AirportLayout.IsRunway(a) && (other.Cell == a || other.Claim == a)) return false;
        }
        return true;
    }

    private void OnBlock(Flight f, Aircraft ac)
    {
        f.Status = FlightStatus.AtGate;
        f.OnBlockAt = SimNow;
        ac.HeadingDeg = -90;
        _store.Enqueue(f);
        CreateTurnaroundTasks(f);
    }

    private void Airborne(Flight f, Aircraft ac)
    {
        f.Status = FlightStatus.Departed;
        f.DepartedAt = SimNow;
        _store.Enqueue(f);
        _aircraft.Remove(f.Id);
        _openFlights.Remove(f.Id);
        if (_runwayOwner == ac) _runwayOwner = null;
    }

    // ------------------------------------------------------------------ ground vehicles

    private bool IsVehicleAvailable(GroundVehicle v) =>
        v.TaskId is null && v.State is VehicleState.Idle or VehicleState.Parking;

    /// <summary>Most urgent departure first; each task goes to the closest free vehicle of the right kind.</summary>
    private void DispatchVehicles()
    {
        var turning = _openFlights
            .Select(id => _flights[id])
            .Where(f => f.Status is FlightStatus.AtGate or FlightStatus.Boarding)
            .OrderBy(f => f.ScheduledDeparture);

        foreach (var f in turning)
        {
            if (!_tasksByFlight.TryGetValue(f.Id, out var tasks) || f.GateId is not int gid) continue;
            var gate = _gates[gid];
            foreach (var t in tasks)
            {
                if (!t.NeedsVehicle || t.Status != ServiceStatus.Ready) continue;
                var spot = AirportLayout.ServiceSpot(gate, t.Type);

                GroundVehicle? best = null;
                var bestDistance = int.MaxValue;
                foreach (var v in _vehicles)
                {
                    if (v.Serves != t.Type || !IsVehicleAvailable(v)) continue;
                    var d = (v.Claim ?? v.Cell).Manhattan(spot);
                    if (d < bestDistance) { best = v; bestDistance = d; }
                }
                if (best is null) continue;

                best.TaskId = t.Id;
                best.State = VehicleState.EnRoute;
                PlanVehicle(best, spot, avoidOthers: false);

                t.Status = ServiceStatus.Assigned;
                t.VehicleName = best.Name;
                _store.Enqueue(t);
            }
        }
    }

    private void UpdateVehicle(GroundVehicle v, double dt)
    {
        v.TotalSeconds += dt;
        if (v.IsBusy) v.BusySeconds += dt;

        switch (v.State)
        {
            case VehicleState.Idle:
                if (v.Cell != v.Home)
                {
                    v.State = VehicleState.Parking;
                    PlanVehicle(v, v.Home, avoidOthers: false);
                }
                break;

            case VehicleState.Parking:
                if (MoveVehicle(v, dt)) v.State = VehicleState.Idle;
                break;

            case VehicleState.EnRoute:
                if (MoveVehicle(v, dt) && v.TaskId is int tid)
                {
                    var t = _tasks[tid];
                    v.State = VehicleState.Servicing;
                    v.ActionTimer = t.DurationMinutes * 60;
                    v.HeadingDeg = 90; // facing the aircraft
                    t.Status = ServiceStatus.InProgress;
                    t.StartedAt = SimNow;
                    _store.Enqueue(t);
                }
                break;

            case VehicleState.Servicing:
                v.ActionTimer -= dt;
                if (v.ActionTimer <= 0)
                {
                    if (v.TaskId is int done && _tasks.TryGetValue(done, out var task))
                    {
                        task.Status = ServiceStatus.Done;
                        task.CompletedAt = SimNow;
                        _store.Enqueue(task);
                    }
                    v.TaskId = null;
                    v.JobsCompleted++;
                    v.State = VehicleState.Idle;
                }
                break;
        }
    }

    private bool MoveVehicle(GroundVehicle v, double dt) =>
        Move(v, dt, _ => Settings.VehicleSpeed, c => VehicleCanEnter(v, c),
            replan: () => PlanVehicle(v, v.Goal, avoidOthers: true), ghostAfter: Settings.GhostAfterSeconds);

    private bool VehicleCanEnter(GroundVehicle v, Cell next)
    {
        foreach (var other in _vehicles)
        {
            if (ReferenceEquals(other, v)) continue;
            if (other.Cell == next || other.Claim == next) return false;
        }
        return true;
    }

    private bool PlanVehicle(GroundVehicle v, Cell goal, bool avoidOthers)
    {
        HashSet<Cell>? blocked = null;
        if (avoidOthers)
        {
            blocked = new HashSet<Cell>();
            foreach (var o in _vehicles)
            {
                if (ReferenceEquals(o, v)) continue;
                blocked.Add(o.Cell);
                if (o.Claim is Cell c) blocked.Add(c);
            }
        }

        var from = v.Claim ?? v.Cell;
        var path = Pathfinder.FindPath(from, goal, _roadNeighbors, c => _roads[c.X, c.Y], blocked);
        if (path is null) return false;

        var full = new List<Cell>(path.Count + 1);
        if (v.Claim is Cell claim) full.Add(claim);
        full.AddRange(path);
        v.Path = full;
        v.PathIndex = 0;
        v.Goal = goal;
        return true;
    }

    // ------------------------------------------------------------------ shared movement

    /// <summary>
    /// Moves a vehicle or aircraft along its path. A cell is claimed before it is entered;
    /// if the rules refuse it the mover waits, re-plans after a while, and as a last resort
    /// ignores the conflict (deadlock breaker). Returns true when the goal is reached.
    /// </summary>
    private bool Move(Mover m, double dt, Func<Cell, double> speedAt, Func<Cell, bool> canEnter, Func<bool>? replan, double ghostAfter)
    {
        var timeLeft = dt;
        var guard = 0;
        while (timeLeft > 1e-9 && m.PathIndex < m.Path.Count && guard++ < 64)
        {
            var next = m.Path[m.PathIndex];
            var hasClaim = m.Claim is Cell current && current == next;
            if (!hasClaim)
            {
                var ghost = m.StuckTime > ghostAfter;
                if (!ghost && !canEnter(next))
                {
                    m.WaitTime += timeLeft;
                    m.StuckTime += timeLeft;
                    if (replan is not null && m.WaitTime > Settings.ReplanAfterSeconds)
                    {
                        replan();
                        m.WaitTime = 0;
                    }
                    return false;
                }
                m.Claim = next;
            }
            m.WaitTime = 0;
            m.StuckTime = 0;

            var speed = Math.Max(0.01, speedAt(next));
            var dx = next.X - m.X;
            var dy = next.Y - m.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > 1e-9) m.HeadingDeg = Math.Atan2(dy, dx) * 180 / Math.PI;

            var reach = speed * timeLeft;
            if (dist <= reach)
            {
                m.X = next.X;
                m.Y = next.Y;
                m.Cell = next;
                m.Claim = null;
                m.PathIndex++;
                m.DistanceCells += 1;
                timeLeft -= dist / speed;
            }
            else
            {
                m.X += dx / dist * reach;
                m.Y += dy / dist * reach;
                timeLeft = 0;
            }
        }
        return m.AtGoal;
    }

    // ------------------------------------------------------------------ memory hygiene

    /// <summary>Old history stays in the database; memory keeps what the screens need.</summary>
    private void PruneHistory()
    {
        var closed = _data.Flights.Where(f => !f.IsOpen).OrderBy(f => f.Id).ToList();
        if (closed.Count <= MaxClosedFlightsInMemory) return;

        var drop = closed.Take(closed.Count - MaxClosedFlightsInMemory).Select(f => f.Id).ToHashSet();
        _data.Flights.RemoveAll(f => drop.Contains(f.Id));
        _data.Tasks.RemoveAll(t => drop.Contains(t.FlightId));
        foreach (var id in drop)
        {
            _flights.Remove(id);
            if (_tasksByFlight.Remove(id, out var tasks))
                foreach (var t in tasks) _tasks.Remove(t.Id);
        }
    }
}
