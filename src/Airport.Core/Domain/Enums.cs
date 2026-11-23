namespace Airport.Core.Domain;

public enum AircraftSize
{
    Regional = 1,
    Narrowbody = 2,
    Widebody = 3
}

public enum FlightStatus
{
    /// <summary>On the schedule, not yet near the airport.</summary>
    Scheduled,
    /// <summary>Inbound; waiting for a free gate and the runway (shown as "Holding" once late).</summary>
    Approach,
    /// <summary>On the runway after touchdown.</summary>
    Landing,
    /// <summary>Taxiing from the runway to its stand.</summary>
    TaxiIn,
    /// <summary>Parked at the gate, turnaround in progress.</summary>
    AtGate,
    /// <summary>Passengers boarding.</summary>
    Boarding,
    /// <summary>Pushed back, taxiing to the runway / taking off.</summary>
    TaxiOut,
    Departed,
    Cancelled
}

/// <summary>Turnaround activities. Fuel, Baggage, Catering and Cleaning need a ground vehicle.</summary>
public enum ServiceType
{
    Deboarding,
    Baggage,
    Fuel,
    Catering,
    Cleaning,
    Boarding
}

public enum ServiceStatus
{
    /// <summary>Waiting for another task to finish first.</summary>
    Waiting,
    /// <summary>Can start; vehicle tasks wait for a vehicle.</summary>
    Ready,
    /// <summary>Vehicle driving to the aircraft.</summary>
    Assigned,
    InProgress,
    Done,
    Cancelled
}

public enum VehicleState
{
    Idle,
    Parking,
    EnRoute,
    Servicing
}

public enum RunwayOperation
{
    None,
    Landing,
    Takeoff
}
