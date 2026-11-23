using System.Globalization;
using Airport.Core.Domain;

namespace Airport.UI.Components;

/// <summary>Formatting helpers. SVG numbers always use invariant culture ("1.5", never "1,5").</summary>
public static class Fmt
{
    public const double Cell = 20;

    public static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Grid coordinate → SVG pixel at the cell center.</summary>
    public static string P(double gridValue) => N(gridValue * Cell + Cell / 2);

    public static string Time(DateTime? t) => t?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
    public static string Clock(DateTime t) => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Minutes(double? minutes) => minutes is double m ? $"{m:0} min" : "—";
    public static string Pct(double ratio) => $"{ratio * 100:0}%";

    public static string StatusText(FlightStatus s, bool holding) => s switch
    {
        FlightStatus.Approach when holding => "Holding",
        FlightStatus.Approach => "Approach",
        FlightStatus.TaxiIn => "Taxi in",
        FlightStatus.AtGate => "At gate",
        FlightStatus.TaxiOut => "Taxi out",
        _ => s.ToString()
    };

    public static string StatusClass(FlightStatus s, bool holding) => s switch
    {
        FlightStatus.Scheduled => "badge",
        FlightStatus.Approach when holding => "badge warn",
        FlightStatus.Approach or FlightStatus.Landing or FlightStatus.TaxiIn => "badge info",
        FlightStatus.AtGate or FlightStatus.Boarding => "badge active",
        FlightStatus.TaxiOut => "badge info",
        FlightStatus.Departed => "badge done",
        FlightStatus.Cancelled => "badge bad",
        _ => "badge"
    };

    public static string TaskClass(ServiceStatus s) => s switch
    {
        ServiceStatus.Done => "seg done",
        ServiceStatus.InProgress => "seg active",
        ServiceStatus.Assigned => "seg info",
        ServiceStatus.Ready => "seg ready",
        _ => "seg"
    };

    /// <summary>Ground vehicle colors (categorical slots, dark mode).</summary>
    public static string VehicleColor(ServiceType t) => t switch
    {
        ServiceType.Fuel => "#d95926",
        ServiceType.Catering => "#199e70",
        ServiceType.Cleaning => "#c98500",
        _ => "#c3c2b7"
    };

    public static string VehicleLabel(ServiceType t) => t switch
    {
        ServiceType.Baggage => "Baggage tug",
        ServiceType.Fuel => "Fuel truck",
        ServiceType.Catering => "Catering truck",
        ServiceType.Cleaning => "Cleaning van",
        _ => t.ToString()
    };

    public static string SizeLabel(AircraftSize s) => s switch
    {
        AircraftSize.Regional => "Regional",
        AircraftSize.Narrowbody => "Narrowbody",
        AircraftSize.Widebody => "Widebody",
        _ => s.ToString()
    };
}
