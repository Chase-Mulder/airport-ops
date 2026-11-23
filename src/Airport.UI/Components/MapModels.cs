using Airport.Core.Domain;
using Airport.Core.Simulation;

namespace Airport.UI.Components;

public sealed record AircraftDot(
    int FlightId, double X, double Y, double Heading, double Scale,
    string TailColor, string Label, FlightStatus Status, IReadOnlyList<Cell> RemainingPath);

public sealed record VehicleDot(string Name, double X, double Y, double Heading, string Color, bool Busy);

public sealed record GateDot(int Id, string Code, int StandX, int StandY, bool Closed, AircraftSize MaxSize, string? Occupant, double Progress);

public sealed record MapSnapshot(
    IReadOnlyList<AircraftDot> Aircraft,
    IReadOnlyList<VehicleDot> Vehicles,
    IReadOnlyList<GateDot> Gates,
    RunwayOperation RunwayOp,
    int Holding)
{
    public static readonly MapSnapshot Empty = new(Array.Empty<AircraftDot>(), Array.Empty<VehicleDot>(), Array.Empty<GateDot>(), RunwayOperation.None, 0);

    /// <summary>Builds the map snapshot. Call inside <see cref="AirportEngine.Read{T}"/>.</summary>
    public static MapSnapshot From(AirportEngine.AirportView v)
    {
        var aircraft = new List<AircraftDot>();
        foreach (var ac in v.Aircraft)
        {
            var f = v.FlightById(ac.FlightId);
            if (f is null) continue;
            var type = v.TypeOf(f);
            var scale = type?.Size switch
            {
                AircraftSize.Regional => 11.0,
                AircraftSize.Widebody => 18.0,
                _ => 14.0
            };
            var remaining = ac.PathIndex < ac.Path.Count ? ac.Path.Skip(ac.PathIndex).ToList() : new List<Cell>();
            aircraft.Add(new AircraftDot(f.Id, ac.X, ac.Y, ac.HeadingDeg, scale,
                v.AirlineOf(f)?.Color ?? "#9aa5b1", v.Callsign(f), f.Status, remaining));
        }

        var vehicles = v.Vehicles
            .Select(x => new VehicleDot(x.Name, x.X, x.Y, x.HeadingDeg, Fmt.VehicleColor(x.Serves), x.IsBusy))
            .ToList();

        var gates = v.Gates.Select(g =>
        {
            var occ = v.OccupantOf(g.Id);
            return new GateDot(g.Id, g.Code, g.StandX, g.StandY, g.IsClosed, g.MaxSize,
                occ is null ? null : v.Callsign(occ),
                occ is null ? 0 : v.TurnaroundProgress(occ));
        }).ToList();

        var holding = v.OpenFlights.Count(f => v.IsHolding(f));
        return new MapSnapshot(aircraft, vehicles, gates, v.RunwayOperation, holding);
    }
}
