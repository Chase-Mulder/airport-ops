using Airport.Core.Domain;

namespace Airport.Core.Simulation;

/// <summary>Operator commands (what the UI calls). Each one validates, mutates state and queues persistence.</summary>
public sealed partial class AirportEngine
{
    public CommandResult CreateFlight(FlightRequest request)
    {
        lock (_gate)
        {
            if (!IsReady) return CommandResult.Fail("The airport is still loading.");
            if (!_airlines.TryGetValue(request.AirlineId, out var airline)) return CommandResult.Fail("Select an airline.");
            if (!_types.TryGetValue(request.AircraftTypeId, out var type)) return CommandResult.Fail("Select an aircraft type.");

            var origin = (request.Origin ?? "").Trim().ToUpperInvariant();
            var destination = (request.Destination ?? "").Trim().ToUpperInvariant();
            if (origin.Length != 3 || !origin.All(char.IsLetter)) return CommandResult.Fail("Origin must be a 3-letter airport code.");
            if (destination.Length != 3 || !destination.All(char.IsLetter)) return CommandResult.Fail("Destination must be a 3-letter airport code.");
            if (request.ArrivalInMinutes < 2) return CommandResult.Fail("Arrival must be at least 2 minutes from now.");
            if (request.GroundMinutes < type.MinTurnMinutes)
                return CommandResult.Fail($"{type.Code} needs at least {type.MinTurnMinutes} minutes on the ground.");

            var flight = SeedData.NewFlight(_nextFlightId++, airline, type, origin, destination,
                SimNow.AddMinutes(request.ArrivalInMinutes), request.GroundMinutes, _rng, "Operator");

            if (request.GateId is int gateId)
            {
                if (!_gates.TryGetValue(gateId, out var gate)) return CommandResult.Fail("Gate not found.");
                if (gate.IsClosed) return CommandResult.Fail($"Gate {gate.Code} is closed.");
                if (!gate.Fits(type.Size)) return CommandResult.Fail($"Gate {gate.Code} can't take a {type.Size.ToString().ToLowerInvariant()} aircraft.");
                if (GateHasConflict(gate, flight)) return CommandResult.Fail($"Gate {gate.Code} is already planned for another flight at that time.");
                flight.GateId = gate.Id;
                _flights[flight.Id] = flight;
                _data.Flights.Add(flight);
                _openFlights.Add(flight.Id);
                _store.Enqueue(flight);
            }
            else
            {
                AddFlight(flight);
            }

            var gateText = flight.GateId is int g ? $"gate {_gates[g].Code}" : "no gate available yet";
            var msg = $"{flight.ArrivalNumber}/{flight.DepartureNumber} {origin}→{destination} scheduled {flight.ScheduledArrival:HH:mm}, {gateText}.";
            Log(EventKind.Info, msg);
            return CommandResult.Success(msg, flight.Id);
        }
    }

    public CommandResult CancelFlight(int flightId)
    {
        lock (_gate)
        {
            if (!_flights.TryGetValue(flightId, out var f)) return CommandResult.Fail("Flight not found.");
            if (f.Status is not (FlightStatus.Scheduled or FlightStatus.Approach))
                return CommandResult.Fail($"{f.ArrivalNumber} has already landed and can't be cancelled.");

            if (f.GateId is int g && _gateOccupant.TryGetValue(g, out var occ) && occ == f.Id) _gateOccupant.Remove(g);
            f.Status = FlightStatus.Cancelled;
            _openFlights.Remove(f.Id);
            _store.Enqueue(f);
            Log(EventKind.Warning, $"{f.ArrivalNumber}/{f.DepartureNumber} cancelled.");
            return CommandResult.Success($"{f.ArrivalNumber}/{f.DepartureNumber} cancelled.");
        }
    }

    public CommandResult DelayFlight(int flightId, int minutes)
    {
        lock (_gate)
        {
            if (!_flights.TryGetValue(flightId, out var f)) return CommandResult.Fail("Flight not found.");
            if (f.Status != FlightStatus.Scheduled) return CommandResult.Fail($"{f.ArrivalNumber} is already inbound; only scheduled flights can be delayed.");

            f.ScheduledArrival = f.ScheduledArrival.AddMinutes(minutes);
            f.ScheduledDeparture = f.ScheduledDeparture.AddMinutes(minutes);

            var note = "";
            if (f.GateId is int g && GateHasConflict(_gates[g], f))
            {
                var old = _gates[g].Code;
                f.GateId = PlanGate(f)?.Id;
                note = f.GateId is int ng ? $" Gate moved {old} → {_gates[ng].Code}." : $" Gate {old} unassigned (no free gate).";
            }
            _store.Enqueue(f);
            var msg = $"{f.ArrivalNumber} delayed {minutes} min, now {f.ScheduledArrival:HH:mm}.{note}";
            Log(EventKind.Warning, msg);
            return CommandResult.Success(msg);
        }
    }

    public CommandResult AssignGate(int flightId, int gateId)
    {
        lock (_gate)
        {
            if (!_flights.TryGetValue(flightId, out var f)) return CommandResult.Fail("Flight not found.");
            if (f.Status is not (FlightStatus.Scheduled or FlightStatus.Approach))
                return CommandResult.Fail($"{f.ArrivalNumber} has already landed; the gate can't change now.");
            if (!_gates.TryGetValue(gateId, out var gate)) return CommandResult.Fail("Gate not found.");
            if (f.GateId == gate.Id) return CommandResult.Success($"{f.ArrivalNumber} is already at {gate.Code}.");

            var size = _types[f.AircraftTypeId].Size;
            if (gate.IsClosed) return CommandResult.Fail($"Gate {gate.Code} is closed.");
            if (!gate.Fits(size)) return CommandResult.Fail($"Gate {gate.Code} can't take a {size.ToString().ToLowerInvariant()} aircraft.");
            if (GateHasConflict(gate, f)) return CommandResult.Fail($"Gate {gate.Code} is planned for another flight at that time.");
            if (f.Status == FlightStatus.Approach && _gateOccupant.ContainsKey(gate.Id))
                return CommandResult.Fail($"Gate {gate.Code} is occupied right now.");

            if (f.GateId is int old && _gateOccupant.TryGetValue(old, out var occ) && occ == f.Id) _gateOccupant.Remove(old);
            f.GateId = gate.Id;
            _store.Enqueue(f);
            var msg = $"{f.ArrivalNumber} assigned to gate {gate.Code}.";
            Log(EventKind.Info, msg);
            return CommandResult.Success(msg);
        }
    }

    public CommandResult ToggleGateClosed(int gateId)
    {
        lock (_gate)
        {
            if (!_gates.TryGetValue(gateId, out var gate)) return CommandResult.Fail("Gate not found.");
            gate.IsClosed = !gate.IsClosed;
            _store.Enqueue(gate);

            var moved = 0;
            if (gate.IsClosed)
            {
                foreach (var id in _openFlights.ToList())
                {
                    var f = _flights[id];
                    if (f.Status != FlightStatus.Scheduled || f.GateId != gate.Id) continue;
                    f.GateId = PlanGate(f)?.Id;
                    _store.Enqueue(f);
                    moved++;
                }
            }

            var msg = gate.IsClosed
                ? $"Gate {gate.Code} closed{(moved > 0 ? $", {moved} planned flight(s) re-planned" : "")}."
                : $"Gate {gate.Code} reopened.";
            Log(EventKind.Warning, msg);
            return CommandResult.Success(msg);
        }
    }

    /// <summary>Re-runs gate planning for scheduled flights that have no gate.</summary>
    public CommandResult PlanUnassignedGates()
    {
        lock (_gate)
        {
            var planned = 0;
            foreach (var id in _openFlights)
            {
                var f = _flights[id];
                if (f.Status != FlightStatus.Scheduled || f.GateId is not null) continue;
                if (PlanGate(f) is { } gate)
                {
                    f.GateId = gate.Id;
                    _store.Enqueue(f);
                    planned++;
                }
            }
            return CommandResult.Success(planned == 0 ? "No unassigned flights could be planned." : $"{planned} flight(s) got a gate.");
        }
    }
}
