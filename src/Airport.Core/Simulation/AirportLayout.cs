using Airport.Core.Domain;

namespace Airport.Core.Simulation;

/// <summary>
/// Static map of the demo airport (grid units). Rows from top to bottom:
///
///  y=1..4   terminal building (concourse A left, concourse B right)
///  y=5..6   airside service road (ground vehicles), y=6 also holds the service spots next to each aircraft
///  y=7..9   aircraft stands (aircraft parks on y=8)
///  y=11     apron taxilane, one-way eastbound
///  y=16     parallel taxiway, one-way westbound
///  y=17..19 rapid exits from the runway (northbound)
///  y=20     runway, operated westbound (land from the east, take off to the west)
///
/// Aircraft follow a one-way loop so they can never meet head-on:
/// runway → exit → taxiway west → connector x=3 north → taxilane east → stand,
/// then stand → taxilane east → connector x=44 south → hold short → runway.
/// </summary>
public static class AirportLayout
{
    public const int Width = 48;
    public const int Height = 23;

    public const int TerminalTop = 1;
    public const int TerminalBottom = 4;
    public const int TerminalLeft = 3;
    public const int TerminalRight = 44;

    public const int RoadY = 5;
    public const int ServiceY = 6;
    public const int StandY = 8;
    public const int TaxilaneY = 11;
    public const int TaxiwayY = 16;
    public const int RunwayY = 20;
    public const int RunwayStartX = 1;
    public const int RunwayEndX = 46;

    public const int WestConnectorX = 3;
    public const int EastConnectorX = 44;

    public static readonly int[] ExitXs = { 9, 19, 30, 38 };
    public static readonly int[] GateXs = { 6, 11, 16, 21, 27, 32, 37, 42 };

    /// <summary>Where an arriving aircraft appears (east end of the runway).</summary>
    public static readonly Cell ApproachCell = new(RunwayEndX, RunwayY);
    /// <summary>Hold-short point for departures, just before the runway.</summary>
    public static readonly Cell HoldShortCell = new(EastConnectorX, RunwayY - 1);
    /// <summary>Departing aircraft roll to the west end of the runway.</summary>
    public static readonly Cell RunwayWestEnd = new(RunwayStartX, RunwayY);

    public const int MaxVehicles = 10;

    public static bool IsRunway(Cell c) => c.Y == RunwayY;

    /// <summary>Home (depot) cell of vehicle number <paramref name="index"/>: left depot first, then right.</summary>
    public static Cell DepotCell(int index) => index < 5
        ? new Cell(0, 6 + index)
        : new Cell(Width - 1, 6 + (index - 5));

    /// <summary>Where a vehicle of the given type parks next to the aircraft on a stand.</summary>
    public static Cell ServiceSpot(Gate gate, ServiceType type)
    {
        var offset = type switch
        {
            ServiceType.Fuel => -2,
            ServiceType.Baggage => -1,
            ServiceType.Catering => 1,
            ServiceType.Cleaning => 2,
            _ => 0
        };
        return new Cell(gate.StandX + offset, ServiceY);
    }

    public static Cell StandCell(Gate gate) => new(gate.StandX, gate.StandY);

    // ------------------------------------------------------------------ ground vehicle road network

    /// <summary>Two-lane service road (y=5..6) plus two-lane spurs down to the depots on each side.</summary>
    public static bool[,] BuildVehicleRoads()
    {
        var roads = new bool[Width, Height];
        for (var x = 1; x <= Width - 2; x++)
        {
            roads[x, RoadY] = true;
            roads[x, ServiceY] = true;
        }
        for (var y = RoadY; y <= 10; y++)
        {
            roads[1, y] = roads[2, y] = true;
            roads[Width - 3, y] = roads[Width - 2, y] = true;
        }
        return roads;
    }

    // ------------------------------------------------------------------ aircraft taxi network (directed)

    public static Dictionary<Cell, List<Cell>> BuildTaxiGraph()
    {
        var g = new Dictionary<Cell, List<Cell>>();
        void Edge(int x1, int y1, int x2, int y2)
        {
            var from = new Cell(x1, y1);
            if (!g.TryGetValue(from, out var list)) g[from] = list = new List<Cell>();
            var to = new Cell(x2, y2);
            if (!list.Contains(to)) list.Add(to);
        }

        // Runway, westbound.
        for (var x = RunwayEndX; x > RunwayStartX; x--) Edge(x, RunwayY, x - 1, RunwayY);

        // Rapid exits, northbound from the runway to the parallel taxiway.
        foreach (var ex in ExitXs)
            for (var y = RunwayY; y > TaxiwayY; y--) Edge(ex, y, ex, y - 1);

        // Parallel taxiway, westbound.
        for (var x = ExitXs.Max(); x > WestConnectorX; x--) Edge(x, TaxiwayY, x - 1, TaxiwayY);

        // West connector, northbound to the apron.
        for (var y = TaxiwayY; y > TaxilaneY; y--) Edge(WestConnectorX, y, WestConnectorX, y - 1);

        // Apron taxilane, eastbound.
        for (var x = WestConnectorX; x < EastConnectorX; x++) Edge(x, TaxilaneY, x + 1, TaxilaneY);

        // East connector, southbound to the hold-short point and onto the runway.
        for (var y = TaxilaneY; y < RunwayY; y++) Edge(EastConnectorX, y, EastConnectorX, y + 1);

        // Stands: in from the taxilane, out (push back) to the taxilane.
        foreach (var gx in GateXs)
        {
            for (var y = TaxilaneY; y > StandY; y--) Edge(gx, y, gx, y - 1);
            for (var y = StandY; y < TaxilaneY; y++) Edge(gx, y, gx, y + 1);
        }
        return g;
    }

    /// <summary>All taxiway segments as line pieces, for drawing.</summary>
    public static IEnumerable<(Cell From, Cell To)> TaxiSegments()
    {
        yield return (new Cell(WestConnectorX, TaxilaneY), new Cell(EastConnectorX, TaxilaneY));
        yield return (new Cell(WestConnectorX, TaxiwayY), new Cell(ExitXs.Max(), TaxiwayY));
        yield return (new Cell(WestConnectorX, TaxilaneY), new Cell(WestConnectorX, TaxiwayY));
        yield return (new Cell(EastConnectorX, TaxilaneY), new Cell(EastConnectorX, RunwayY));
        foreach (var ex in ExitXs) yield return (new Cell(ex, TaxiwayY), new Cell(ex, RunwayY));
        foreach (var gx in GateXs) yield return (new Cell(gx, StandY), new Cell(gx, TaxilaneY));
    }
}
