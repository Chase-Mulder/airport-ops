namespace Airport.Core.Domain;

/// <summary>A grid cell on the airport map.</summary>
public readonly record struct Cell(int X, int Y)
{
    public int Manhattan(Cell other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    public override string ToString() => $"({X},{Y})";
}

/// <summary>Anything that drives on the map: aircraft and ground vehicles. Not persisted.</summary>
public abstract class Mover
{
    /// <summary>Exact (interpolated) position in grid units.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double HeadingDeg { get; set; }

    /// <summary>Last cell fully reached.</summary>
    public Cell Cell { get; set; }
    /// <summary>Cell reserved and being driven into; null while standing still.</summary>
    public Cell? Claim { get; set; }

    public List<Cell> Path { get; set; } = new();
    public int PathIndex { get; set; }
    public Cell Goal { get; set; }
    /// <summary>Simulated seconds spent waiting for a blocked cell.</summary>
    public double WaitTime { get; set; }
    /// <summary>Simulated seconds without moving at all (reset only by movement).</summary>
    public double StuckTime { get; set; }
    public double DistanceCells { get; set; }

    public bool AtGoal => PathIndex >= Path.Count;

    public void PlaceAt(Cell cell)
    {
        Cell = cell;
        Claim = null;
        X = cell.X;
        Y = cell.Y;
        Path = new List<Cell>();
        PathIndex = 0;
        Goal = cell;
    }
}

/// <summary>An aircraft on the airfield, tied to the flight it operates.</summary>
public sealed class Aircraft : Mover
{
    public int FlightId { get; init; }
}

/// <summary>Ground support equipment: baggage tug, fuel truck, catering truck or cleaning van.</summary>
public sealed class GroundVehicle : Mover
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Which turnaround task this vehicle performs.</summary>
    public ServiceType Serves { get; init; }
    public Cell Home { get; init; }

    public VehicleState State { get; set; } = VehicleState.Idle;
    public int? TaskId { get; set; }
    public double ActionTimer { get; set; }

    public int JobsCompleted { get; set; }
    public double BusySeconds { get; set; }
    public double TotalSeconds { get; set; }

    public bool IsBusy => State is VehicleState.EnRoute or VehicleState.Servicing;
}
