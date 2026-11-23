namespace Airport.Core.Domain;

/// <summary>
/// Base class for everything that is persisted. Ids are assigned by the engine
/// (not the database) so the same code works with SQL Server, SQLite or in-memory storage.
/// </summary>
public abstract class Entity
{
    public int Id { get; set; }

    /// <summary>Shallow copy handed to the persistence layer so the engine can keep mutating the original.</summary>
    public Entity Clone() => (Entity)MemberwiseClone();
}

/// <summary>A (fictional) airline.</summary>
public class Airline : Entity
{
    /// <summary>Two-letter designator, e.g. "LK".</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Hex color used for the tail and labels on the map.</summary>
    public string Color { get; set; } = "#3987e5";
}

public class AircraftType : Entity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AircraftSize Size { get; set; }
    public int Seats { get; set; }
    /// <summary>Shortest scheduled ground time the airline allows.</summary>
    public int MinTurnMinutes { get; set; }
    public double FuelMinutes { get; set; }
    public double BaggageMinutes { get; set; }
    public double CateringMinutes { get; set; }
    public double CleaningMinutes { get; set; }
}

/// <summary>A gate with its aircraft stand on the apron.</summary>
public class Gate : Entity
{
    public string Code { get; set; } = "";
    public string Concourse { get; set; } = "";
    /// <summary>Grid cell where the aircraft parks.</summary>
    public int StandX { get; set; }
    public int StandY { get; set; }
    /// <summary>Largest aircraft the stand can take.</summary>
    public AircraftSize MaxSize { get; set; }
    public bool IsClosed { get; set; }

    public bool Fits(AircraftSize size) => size <= MaxSize;
}

/// <summary>
/// One aircraft visit ("turn"): an arriving flight number and the departing flight number
/// flown by the same aircraft after the turnaround.
/// </summary>
public class Flight : Entity
{
    public int AirlineId { get; set; }
    public int AircraftTypeId { get; set; }
    public string Registration { get; set; } = "";
    public string ArrivalNumber { get; set; } = "";
    public string DepartureNumber { get; set; } = "";
    /// <summary>IATA code the arrival comes from.</summary>
    public string Origin { get; set; } = "";
    /// <summary>IATA code the departure goes to.</summary>
    public string Destination { get; set; } = "";
    public DateTime ScheduledArrival { get; set; }
    public DateTime ScheduledDeparture { get; set; }
    public int? GateId { get; set; }
    public FlightStatus Status { get; set; }
    public int PassengersIn { get; set; }
    public int PassengersOut { get; set; }
    /// <summary>"Operator" or "Auto".</summary>
    public string Source { get; set; } = "Operator";

    public DateTime? LandedAt { get; set; }
    public DateTime? OnBlockAt { get; set; }
    public DateTime? OffBlockAt { get; set; }
    public DateTime? DepartedAt { get; set; }

    public bool IsOpen => Status is not (FlightStatus.Departed or FlightStatus.Cancelled);
    public bool IsOnGround => Status is FlightStatus.Landing or FlightStatus.TaxiIn or FlightStatus.AtGate or FlightStatus.Boarding or FlightStatus.TaxiOut;
    public bool HasLanded => Status is not (FlightStatus.Scheduled or FlightStatus.Approach or FlightStatus.Cancelled);
}

/// <summary>One turnaround activity for a flight.</summary>
public class ServiceTask : Entity
{
    public int FlightId { get; set; }
    public ServiceType Type { get; set; }
    public ServiceStatus Status { get; set; }
    public double DurationMinutes { get; set; }
    public string? VehicleName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public bool NeedsVehicle => Type is ServiceType.Baggage or ServiceType.Fuel or ServiceType.Catering or ServiceType.Cleaning;
}

/// <summary>Everything that is loaded from / saved to storage.</summary>
public class AirportData
{
    public List<Airline> Airlines { get; set; } = new();
    public List<AircraftType> AircraftTypes { get; set; } = new();
    public List<Gate> Gates { get; set; } = new();
    public List<Flight> Flights { get; set; } = new();
    public List<ServiceTask> Tasks { get; set; } = new();
}
