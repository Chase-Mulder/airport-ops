namespace Airport.Core.Simulation;

/// <summary>Runtime knobs exposed in the toolbar. Speeds are in grid cells per simulated second.</summary>
public sealed class SimSettings
{
    public bool Paused { get; set; }

    /// <summary>Simulated seconds per real second. Airports are slow; 30x makes a 45-minute turnaround take 90 seconds.</summary>
    public double SpeedMultiplier { get; set; } = 30;

    /// <summary>When on, the engine keeps the schedule filled with arrivals.</summary>
    public bool AutoFlights { get; set; } = true;
    /// <summary>Average minutes between auto-generated arrivals.</summary>
    public double AutoArrivalMinutes { get; set; } = 6;
    /// <summary>How far ahead (minutes) the auto scheduler plans.</summary>
    public double ScheduleHorizonMinutes { get; set; } = 90;

    /// <summary>Minutes before scheduled arrival that a flight starts its approach.</summary>
    public double ApproachLeadMinutes { get; set; } = 4;

    public double TaxiSpeed { get; set; } = 0.15;
    public double RunwaySpeed { get; set; } = 0.6;
    public double VehicleSpeed { get; set; } = 0.2;

    /// <summary>Re-plan around other vehicles after waiting this long (sim seconds).</summary>
    public double ReplanAfterSeconds { get; set; } = 10;
    /// <summary>Last-resort deadlock breaker for ground vehicles (sim seconds).</summary>
    public double GhostAfterSeconds { get; set; } = 60;

    /// <summary>Departures that leave the gate within this many minutes count as on time (industry standard: 15).</summary>
    public double OnTimeToleranceMinutes { get; set; } = 15;

    /// <summary>A departing aircraft is airborne once it rolls past this x on the runway.</summary>
    public double LiftOffX { get; set; } = 20;
}

public readonly record struct CommandResult(bool Ok, string Message, int? Id = null)
{
    public static CommandResult Fail(string message) => new(false, message);
    public static CommandResult Success(string message, int? id = null) => new(true, message, id);
}

public enum EventKind { Info, Success, Warning }

public sealed record EngineEvent(DateTime Time, EventKind Kind, string Text);

/// <summary>Operator request for a new flight.</summary>
public sealed record FlightRequest(
    int AirlineId,
    int AircraftTypeId,
    string Origin,
    string Destination,
    double ArrivalInMinutes,
    int GroundMinutes,
    int? GateId);
