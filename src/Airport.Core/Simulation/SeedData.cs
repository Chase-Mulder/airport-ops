using Airport.Core.Domain;

namespace Airport.Core.Simulation;

/// <summary>Master data and a starting schedule for the demo airport. All airlines are fictional.</summary>
public static class SeedData
{
    public static readonly string[] Cities =
    {
        "ORD", "DTW", "ATL", "DEN", "MSP", "BOS", "LGA", "CLT", "DFW", "PHX",
        "SEA", "MCO", "LAS", "YYZ", "LAX", "BNA", "AUS", "STL", "PIT", "MKE"
    };

    public static AirportData Create(DateTime now, int seed = 7)
    {
        var rng = new Random(seed);
        var data = new AirportData
        {
            Airlines = new List<Airline>
            {
                new() { Id = 1, Code = "LK", Name = "Lakeshore Air",     Color = "#d55181" },
                new() { Id = 2, Code = "PS", Name = "Prairie Sky",       Color = "#0ca30c" },
                new() { Id = 3, Code = "CW", Name = "Coastline Airways", Color = "#9085e9" },
                new() { Id = 4, Code = "SJ", Name = "Summit Jet",        Color = "#3987e5" },
            },
            AircraftTypes = new List<AircraftType>
            {
                new() { Id = 1, Code = "RJ70",  Name = "Regional jet 70",   Size = AircraftSize.Regional,   Seats = 76,  MinTurnMinutes = 30, FuelMinutes = 8,  BaggageMinutes = 10, CateringMinutes = 6,  CleaningMinutes = 5 },
                new() { Id = 2, Code = "NB180", Name = "Narrowbody 180",    Size = AircraftSize.Narrowbody, Seats = 180, MinTurnMinutes = 40, FuelMinutes = 12, BaggageMinutes = 14, CateringMinutes = 10, CleaningMinutes = 8 },
                new() { Id = 3, Code = "NB220", Name = "Narrowbody 220",    Size = AircraftSize.Narrowbody, Seats = 220, MinTurnMinutes = 45, FuelMinutes = 14, BaggageMinutes = 16, CateringMinutes = 11, CleaningMinutes = 9 },
                new() { Id = 4, Code = "WB300", Name = "Widebody 300",      Size = AircraftSize.Widebody,   Seats = 300, MinTurnMinutes = 70, FuelMinutes = 22, BaggageMinutes = 24, CateringMinutes = 18, CleaningMinutes = 15 },
            }
        };

        var gateId = 1;
        for (var i = 0; i < AirportLayout.GateXs.Length; i++)
        {
            var concourse = i < 4 ? "A" : "B";
            var number = i < 4 ? i + 1 : i - 3;
            data.Gates.Add(new Gate
            {
                Id = gateId++,
                Code = $"{concourse}{number}",
                Concourse = concourse,
                StandX = AirportLayout.GateXs[i],
                StandY = AirportLayout.StandY,
                // B1 and B2 take widebodies; A1 is a regional-only stand.
                MaxSize = (concourse, number) switch
                {
                    ("B", 1) or ("B", 2) => AircraftSize.Widebody,
                    ("A", 1) => AircraftSize.Regional,
                    _ => AircraftSize.Narrowbody
                }
            });
        }

        // A few aircraft already on stand, and the next hour of arrivals.
        var flightId = 1;
        var occupied = new[] { 2, 4, 5, 7 }; // gate ids
        foreach (var g in occupied)
        {
            var gate = data.Gates.First(x => x.Id == g);
            var type = gate.MaxSize == AircraftSize.Widebody ? data.AircraftTypes[3] : data.AircraftTypes[rng.Next(1, 3)];
            var onBlock = now.AddMinutes(-rng.Next(4, 18));
            var f = NewFlight(flightId++, data, type, onBlock.AddMinutes(-8), type.MinTurnMinutes + rng.Next(5, 15), rng, "Auto");
            f.GateId = gate.Id;
            f.Status = FlightStatus.AtGate;
            f.LandedAt = onBlock.AddMinutes(-7);
            f.OnBlockAt = onBlock;
            data.Flights.Add(f);
        }

        var t = now.AddMinutes(5);
        for (var i = 0; i < 9; i++)
        {
            var type = PickType(data, rng);
            data.Flights.Add(NewFlight(flightId++, data, type, t, type.MinTurnMinutes + rng.Next(5, 20), rng, "Auto"));
            t = t.AddMinutes(rng.Next(4, 9));
        }
        return data;
    }

    public static AircraftType PickType(AirportData data, Random rng)
    {
        var roll = rng.NextDouble();
        var id = roll < 0.25 ? 1 : roll < 0.65 ? 2 : roll < 0.90 ? 3 : 4;
        return data.AircraftTypes.FirstOrDefault(t => t.Id == id) ?? data.AircraftTypes[0];
    }

    public static Flight NewFlight(int id, AirportData data, AircraftType type, DateTime arrival, int groundMinutes, Random rng, string source)
    {
        var airline = data.Airlines[rng.Next(data.Airlines.Count)];
        return NewFlight(id, airline, type, Cities[rng.Next(Cities.Length)], Cities[rng.Next(Cities.Length)], arrival, groundMinutes, rng, source);
    }

    public static Flight NewFlight(int id, Airline airline, AircraftType type, string origin, string destination,
        DateTime arrival, int groundMinutes, Random rng, string source)
    {
        var number = rng.Next(50, 1499) * 2 + 1; // odd inbound, even outbound
        return new Flight
        {
            Id = id,
            AirlineId = airline.Id,
            AircraftTypeId = type.Id,
            Registration = $"N{rng.Next(100, 999)}{airline.Code}",
            ArrivalNumber = $"{airline.Code}{number}",
            DepartureNumber = $"{airline.Code}{number + 1}",
            Origin = origin,
            Destination = destination,
            ScheduledArrival = Trim(arrival),
            ScheduledDeparture = Trim(arrival.AddMinutes(groundMinutes)),
            Status = FlightStatus.Scheduled,
            PassengersIn = (int)(type.Seats * (0.6 + rng.NextDouble() * 0.38)),
            PassengersOut = (int)(type.Seats * (0.6 + rng.NextDouble() * 0.38)),
            Source = source
        };
    }

    /// <summary>Schedules are published to the minute.</summary>
    private static DateTime Trim(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
}
