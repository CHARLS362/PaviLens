using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Maintenance;

/// <summary>
/// Google Maps Platform settings. The keys live only in the git-ignored <c>.env</c> (GOOGLE_MAPS_API_KEY,
/// GOOGLE_MAPS_BROWSER_KEY) or real environment variables; they are never logged or returned in errors.
/// </summary>
public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    /// <summary>Server key: Routes API calls made by the backend, sent in a header, never to the browser.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Key the browser needs to draw the map (Maps JavaScript API). Defaults to <see cref="ApiKey"/>.</summary>
    public string? BrowserKey { get; set; }

    /// <summary>Google calls the backend makes per day at most, so a leaked key or a loop cannot drain the quota.</summary>
    public int DailyRequestLimit { get; set; } = 300;

    /// <summary>Minutes a computed route is reused before asking Google again.</summary>
    public int CacheMinutes { get; set; } = 15;

    public bool ServerEnabled => !string.IsNullOrWhiteSpace(ApiKey);
    public string? EffectiveBrowserKey => string.IsNullOrWhiteSpace(BrowserKey) ? ApiKey : BrowserKey;
}

public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>An inspection placed on the map. <c>CoordinateSource</c>: "gps" (given at capture) or "segment" (street midpoint).</summary>
public sealed record MaintenancePointDto(
    Guid Id, string Title, string? Location, InspectionStatus Status, double? Pci,
    int DistressCount, int PotholeCount, double Latitude, double Longitude, string CoordinateSource);

/// <summary>Read-model row used to place inspections; coordinates come from the agent trace when present.</summary>
public sealed record InspectionLocationRow(
    Guid Id, string Title, string? Location, InspectionStatus Status, double? Pci,
    int DistressCount, int PotholeCount, string? RoadSegmentId, double? Latitude, double? Longitude);

/// <param name="Map">The map the route is drawn on; null uses the configured provider.</param>
public sealed record CrewRouteRequest(GeoPoint? Depot, IReadOnlyList<Guid> InspectionIds, bool PriorityFirst = true,
    ReportLanguage Language = ReportLanguage.Es, MapProvider? Map = null);

public sealed record CrewStopDto(int Order, Guid InspectionId, string Title, double? Pci, string Priority,
    double Latitude, double Longitude, double LegDistanceM, double LegDurationMin);

/// <summary>
/// <c>Source</c>: "osm" (real streets, OSRM over OpenStreetMap), "google" (real streets, Routes API) or "estimate"
/// (straight lines at urban speed, used when no routing service answers).
/// </summary>
public sealed record CrewRouteDto(
    string Source, GeoPoint Depot, IReadOnlyList<CrewStopDto> Stops, double TotalDistanceM, double TotalDurationMin,
    string? EncodedPolyline, IReadOnlyList<string> Warnings, CrewPlannerDto? Planner = null);

/// <summary>
/// A driving route over real streets. <c>EncodedPolyline</c> uses Google's polyline format (precision 5), which OSRM
/// also returns. <c>Source</c>: "osm" (OSRM over OpenStreetMap) or "google" (Routes API).
/// </summary>
public sealed record RoadRoute(double DistanceM, double DurationS, string? EncodedPolyline,
    IReadOnlyList<(double DistanceM, double DurationS)> Legs, string Source = "google");

/// <summary>Map and routing provider. OpenStreetMap needs no key and is the default.</summary>
public enum MapProvider { Osm, Google }

/// <summary>
/// Map settings (section "Maps"). OpenStreetMap is the default so PaveLens never depends on Google: tiles from
/// <see cref="TileUrl"/>, routes from an OSRM server (the public demo by default, or your own).
/// </summary>
public sealed class MapsOptions
{
    public const string SectionName = "Maps";

    public MapProvider Provider { get; set; } = MapProvider.Osm;
    public string OsrmBaseUrl { get; set; } = "https://router.project-osrm.org/";
    public string TileUrl { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string TileAttribution { get; set; } = "© OpenStreetMap contributors · ODbL";
    public int CacheMinutes { get; set; } = 15;
}

/// <summary>
/// Driving routes over real streets. <paramref name="preferred"/> is the map the route will be drawn on; Google
/// routes are only returned for a Google map (its terms), OSRM routes can go on either. Null when no provider answers.
/// </summary>
public interface IRoadRouteProvider
{
    Task<RoadRoute?> RouteAsync(IReadOnlyList<GeoPoint> waypoints, MapProvider preferred, CancellationToken cancellationToken);
}

/// <summary>A work site as the day planner sees it: facts measured by code, never by the model.</summary>
public sealed record CrewSiteFact(
    string Id, string Title, string? Location, double? Pci, string Band, int DistressCount, int PotholeCount,
    double LegDistanceM, double LegDurationMin);

public sealed record CrewPlanRequest(
    IReadOnlyList<CrewSiteFact> Sites, double TotalDistanceM, double TotalDurationMin, string RoutingSource,
    PaveLens.Domain.Inspections.ReportLanguage Language);

/// <summary>What the planner decides for one site: how much road to close, when, and why.</summary>
public sealed record CrewSitePlan(string Id, string Closure, string Window, string Rationale);

public sealed record CrewPlanResult(
    string Model, string Summary, IReadOnlyList<string> Order, IReadOnlyList<CrewSitePlan> Sites,
    IReadOnlyList<string> Risks, IReadOnlyList<string> Communication);

/// <summary>
/// Plans the crew's day: Muse Wonder through the Meta API decides the final visiting order, the closure and the
/// time window of each site; code proposes the shortest order first and checks the answer.
/// </summary>
public interface ICrewDayPlanner
{
    PaveLens.Domain.Inspections.AnalysisEngine Engine { get; }
    Task<CrewPlanResult> PlanAsync(CrewPlanRequest request, CancellationToken cancellationToken);
}

/// <summary>The planner's decisions as shown to people. <c>OrderBy</c>: "meta", "simulation" or "code" (fallback).</summary>
public sealed record CrewPlannerDto(
    string Engine, string Model, string OrderBy, string Summary, IReadOnlyList<CrewSitePlan> Sites,
    IReadOnlyList<string> Risks, IReadOnlyList<string> Communication, double CodeOrderDistanceM, bool OrderChanged);

/// <summary>Checks a day plan against the request, so a model cannot drop, invent or repeat a work site.</summary>
public static class CrewPlanValidator
{
    public static IReadOnlyList<string> Check(CrewPlanResult plan, IReadOnlyCollection<string> ids)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(plan.Summary)) problems.Add("Falta 'summary'.");
        if (plan.Order.Count != ids.Count || plan.Order.Distinct().Count() != plan.Order.Count || !plan.Order.All(ids.Contains))
            problems.Add("'order' debe contener cada id de 'sites' exactamente una vez.");
        var unknown = plan.Sites.Where(s => !ids.Contains(s.Id)).Select(s => s.Id).ToList();
        if (unknown.Count > 0) problems.Add($"'sites' usa ids que no existen: {string.Join(", ", unknown)}.");
        var missing = ids.Where(id => plan.Sites.All(s => s.Id != id)).ToList();
        if (missing.Count > 0) problems.Add($"Falta el plan de los sitios: {string.Join(", ", missing)}.");
        return problems;
    }
}
