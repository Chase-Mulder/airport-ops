/*
    Airport Operations Center: reporting views for SQL Server.

    1. Set "Database:Provider" to "SqlServer" in src/Airport.Desktop/appsettings.json
       and run the app once so EF Core creates the AirportOps database.
    2. Open this file in SSMS (or Visual Studio's SQL Server Object Explorer) and execute it.
    3. Query the views, e.g.  SELECT * FROM rpt.OnTimePerformance ORDER BY Hour DESC;
*/
USE AirportOps;
GO

IF SCHEMA_ID('rpt') IS NULL EXEC('CREATE SCHEMA rpt');
GO

/* Departures board with delay in minutes. */
CREATE OR ALTER VIEW rpt.Departures AS
SELECT
    f.Id,
    f.DepartureNumber                                   AS Flight,
    a.Name                                              AS Airline,
    t.Code                                              AS AircraftType,
    f.Destination,
    g.Code                                              AS Gate,
    f.ScheduledDeparture,
    f.OffBlockAt,
    f.DepartedAt,
    f.Status,
    CASE WHEN f.OffBlockAt IS NULL THEN NULL
         ELSE DATEDIFF(MINUTE, f.ScheduledDeparture, f.OffBlockAt) END AS DelayMinutes,
    f.PassengersOut
FROM dbo.Flights f
JOIN dbo.Airlines a       ON a.Id = f.AirlineId
JOIN dbo.AircraftTypes t  ON t.Id = f.AircraftTypeId
LEFT JOIN dbo.Gates g     ON g.Id = f.GateId;
GO

/* On-time performance per hour (A15: off-block within 15 minutes of schedule). */
CREATE OR ALTER VIEW rpt.OnTimePerformance AS
SELECT
    DATEADD(HOUR, DATEDIFF(HOUR, 0, f.OffBlockAt), 0)   AS [Hour],
    COUNT(*)                                            AS Departures,
    SUM(CASE WHEN DATEDIFF(MINUTE, f.ScheduledDeparture, f.OffBlockAt) <= 15 THEN 1 ELSE 0 END) AS OnTime,
    CAST(100.0 * SUM(CASE WHEN DATEDIFF(MINUTE, f.ScheduledDeparture, f.OffBlockAt) <= 15 THEN 1 ELSE 0 END)
         / COUNT(*) AS DECIMAL(5,1))                    AS OnTimePct,
    AVG(CAST(DATEDIFF(MINUTE, f.ScheduledDeparture, f.OffBlockAt) AS FLOAT)) AS AvgDelayMinutes
FROM dbo.Flights f
WHERE f.OffBlockAt IS NOT NULL
GROUP BY DATEADD(HOUR, DATEDIFF(HOUR, 0, f.OffBlockAt), 0);
GO

/* Turnaround, taxi-in and taxi-out times per aircraft type. */
CREATE OR ALTER VIEW rpt.TurnaroundByAircraftType AS
SELECT
    t.Code                                                      AS AircraftType,
    COUNT(*)                                                    AS Turns,
    AVG(CAST(DATEDIFF(SECOND, f.OnBlockAt, f.OffBlockAt) AS FLOAT)) / 60  AS AvgTurnMinutes,
    MIN(t.MinTurnMinutes)                                       AS MinTurnMinutes,
    AVG(CAST(DATEDIFF(SECOND, f.LandedAt, f.OnBlockAt) AS FLOAT)) / 60    AS AvgTaxiInMinutes,
    AVG(CAST(DATEDIFF(SECOND, f.OffBlockAt, f.DepartedAt) AS FLOAT)) / 60 AS AvgTaxiOutMinutes
FROM dbo.Flights f
JOIN dbo.AircraftTypes t ON t.Id = f.AircraftTypeId
WHERE f.Status = 'Departed'
GROUP BY t.Code;
GO

/* How much each gate was used (minutes occupied) per day. */
CREATE OR ALTER VIEW rpt.GateUtilization AS
SELECT
    g.Code                                                      AS Gate,
    CAST(f.OnBlockAt AS DATE)                                   AS [Day],
    COUNT(*)                                                    AS Turns,
    SUM(DATEDIFF(MINUTE, f.OnBlockAt, ISNULL(f.OffBlockAt, SYSDATETIME()))) AS OccupiedMinutes
FROM dbo.Flights f
JOIN dbo.Gates g ON g.Id = f.GateId
WHERE f.OnBlockAt IS NOT NULL
GROUP BY g.Code, CAST(f.OnBlockAt AS DATE);
GO

/* Ground handling: average task duration and jobs per vehicle. */
CREATE OR ALTER VIEW rpt.GroundHandling AS
SELECT
    s.VehicleName,
    s.Type                                                      AS Task,
    COUNT(*)                                                    AS Jobs,
    AVG(CAST(DATEDIFF(SECOND, s.StartedAt, s.CompletedAt) AS FLOAT)) / 60  AS AvgMinutes,
    AVG(CAST(DATEDIFF(SECOND, s.CreatedAt, s.StartedAt) AS FLOAT)) / 60    AS AvgWaitMinutes
FROM dbo.ServiceTasks s
WHERE s.Status = 'Done' AND s.VehicleName IS NOT NULL
GROUP BY s.VehicleName, s.Type;
GO
