using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Common;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Maintenance;

/// <summary>Places inspections on the map and plans the order a crew visits them.</summary>
public sealed partial class MaintenanceService(IInspectionQueries queries, TrafficService traffic, IRoadRouteProvider routes, ICrewDayPlanner planner,
    Microsoft.Extensions.Options.IOptions<MapsOptions> mapsOptions)
{
    public const int MaxStops = 23; // Routes API: origin + 25 intermediates + destination; keep a margin.
    public const double MaxSpreadM = 60_000; // A crew day stays inside one city.
    public const double UrbanSpeedKmh = 22; // Used only for the straight-line estimate.

    /// <summary>Plaza de Armas, Puno: default depot, also the centre of the demo network.</summary>
    public static readonly GeoPoint DefaultDepot = new(-15.8402, -70.0219);

    public async Task<IReadOnlyList<MaintenancePointDto>> ListPointsAsync(CancellationToken cancellationToken)
    {
        var rows = await queries.ListLocationsAsync(200, cancellationToken);
        var points = new List<MaintenancePointDto>(rows.Count);
        foreach (var r in rows)
        {
            var located = Locate(r);
            if (located is null) continue;
            points.Add(new MaintenancePointDto(r.Id, r.Title, r.Location, r.Status, r.Pci, r.DistressCount, r.PotholeCount,
                located.Value.Point.Latitude, located.Value.Point.Longitude, located.Value.Source));
        }
        return points;
    }

    public async Task<CrewRouteDto> PlanCrewRouteAsync(CrewRouteRequest request, CancellationToken cancellationToken)
    {
        var ids = request.InspectionIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) throw new DomainException("Elige al menos una inspección para la ruta.");
        if (ids.Count > MaxStops) throw new DomainException($"Una jornada admite como máximo {MaxStops} obras.");
        var depot = request.Depot ?? DefaultDepot;
        if (!Valid(depot)) throw new DomainException("El punto de partida no tiene coordenadas válidas.");

        var all = (await ListPointsAsync(cancellationToken)).ToDictionary(p => p.Id);
        var sites = ids.Select(id => all.TryGetValue(id, out var p) ? p : throw new DomainException("Una de las inspecciones no tiene ubicación o ya no existe.")).ToList();
        var points = sites.Select(s => (Point: new GeoPoint(s.Latitude, s.Longitude), s.Pci)).ToList();
        if (points.Any(p => CrewRoutePlanner.DistanceM(depot, p.Point) > MaxSpreadM))
            throw new DomainException("Todas las obras deben estar a menos de 60 km del punto de partida.");

        var codeOrder = CrewRoutePlanner.Order(depot, points, request.PriorityFirst);
        var warnings = new List<string>();
        var map = request.Map ?? mapsOptions.Value.Provider;
        var (codeRoad, codeLegs) = await RouteAsync(depot, codeOrder, points, map, cancellationToken);

        // Muse Wonder decides the final order and each closure; code proposed the shortest order and checks the answer.
        var shortIds = sites.Select((_, i) => $"S{i + 1}").ToList();
        var facts = codeOrder.Select((i, k) => new CrewSiteFact(shortIds[i], sites[i].Title, sites[i].Location, sites[i].Pci,
            CrewRoutePlanner.BandLabel(sites[i].Pci), sites[i].DistressCount, sites[i].PotholeCount,
            Math.Round(codeLegs[k].DistanceM, 0), Math.Round(codeLegs[k].DurationS / 60, 1))).ToList();
        CrewPlanResult? plan = null;
        try
        {
            plan = await planner.PlanAsync(new CrewPlanRequest(facts, Math.Round(codeLegs.Sum(l => l.DistanceM), 0),
                Math.Round(codeLegs.Sum(l => l.DurationS) / 60, 1), codeRoad?.Source ?? "estimate", request.Language), cancellationToken);
            var problems = CrewPlanValidator.Check(plan, shortIds);
            if (problems.Count > 0) throw new ModelOutputException(string.Join(" ", problems));
        }
        catch (Exception ex) when (ex is ModelOutputException or ModelUnavailableException or HttpRequestException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            plan = null;
            warnings.Add("Meta no devolvió un plan válido; se muestra el orden calculado por código.");
        }

        var order = plan is null ? codeOrder : plan.Order.Select(id => shortIds.IndexOf(id)).ToList();
        var orderChanged = !order.SequenceEqual(codeOrder);
        var (road, legs) = orderChanged ? await RouteAsync(depot, order, points, map, cancellationToken) : (codeRoad, codeLegs);
        if (road is null) warnings.Add("Distancias en línea recta a 22 km/h: el servicio de rutas no respondió.");
        if (request.PriorityFirst) warnings.Add("Orden por urgencia: primero PCI < 40, luego 40–70 y al final > 70; dentro de cada grupo, el recorrido más corto.");

        var stops = order.Select((siteIndex, k) =>
        {
            var s = sites[siteIndex];
            return new CrewStopDto(k + 1, s.Id, s.Title, s.Pci, CrewRoutePlanner.BandLabel(s.Pci), s.Latitude, s.Longitude,
                Math.Round(legs[k].DistanceM, 0), Math.Round(legs[k].DurationS / 60, 1));
        }).ToList();

        // The model works with short ids (S1, S2…); people read the site's visit number and name instead.
        var visit = order.Select((siteIndex, k) => (Short: shortIds[siteIndex], Label: $"{k + 1} ({sites[siteIndex].Title})")).ToDictionary(v => v.Short, v => v.Label);
        string Readable(string text) => ShortIdPattern().Replace(text, m => visit.GetValueOrDefault(m.Value, m.Value));
        var plannerDto = plan is null ? null : new CrewPlannerDto(
            planner.Engine.ToString(), plan.Model, planner.Engine == AnalysisEngine.Meta ? "meta" : "simulation", Readable(plan.Summary.Trim()),
            // Site plans keyed by inspection id, in visiting order, for the UI.
            order.Select(i => plan.Sites.First(p => p.Id == shortIds[i]) is var p
                ? p with { Id = sites[i].Id.ToString(), Rationale = Readable(p.Rationale) } : null!).ToList(),
            plan.Risks.Select(Readable).ToList(), plan.Communication.Select(Readable).ToList(),
            Math.Round(codeLegs.Sum(l => l.DistanceM), 0), orderChanged);

        return new CrewRouteDto(road?.Source ?? "estimate", depot, stops,
            Math.Round(legs.Sum(l => l.DistanceM), 0), Math.Round(legs.Sum(l => l.DurationS) / 60, 1),
            road?.EncodedPolyline, warnings, plannerDto);
    }

    /// <summary>Real streets when a routing service answers, otherwise straight lines at urban speed.</summary>
    private async Task<(RoadRoute? Road, List<(double DistanceM, double DurationS)> Legs)> RouteAsync(
        GeoPoint depot, IReadOnlyList<int> order, List<(GeoPoint Point, double? Pci)> points, MapProvider map, CancellationToken cancellationToken)
    {
        var waypoints = new List<GeoPoint> { depot };
        waypoints.AddRange(order.Select(i => points[i].Point));
        var road = await routes.RouteAsync(waypoints, map, cancellationToken);
        if (road is not null && road.Legs.Count == order.Count) return (road, road.Legs.ToList());
        return (null, waypoints.Zip(waypoints.Skip(1), (a, b) =>
            (DistanceM: CrewRoutePlanner.DistanceM(a, b), DurationS: CrewRoutePlanner.DistanceM(a, b) / (UrbanSpeedKmh / 3.6))).ToList());
    }

    private (GeoPoint Point, string Source)? Locate(InspectionLocationRow r)
    {
        if (r.Latitude is { } lat && r.Longitude is { } lon && Valid(new GeoPoint(lat, lon))) return (new GeoPoint(lat, lon), "gps");
        if (string.IsNullOrWhiteSpace(r.RoadSegmentId)) return null;
        var network = traffic.Network;
        var segment = network.Segments.FirstOrDefault(s => s.Id == r.RoadSegmentId);
        if (segment is null) return null;
        var a = network.Nodes.First(n => n.Id == segment.From);
        var b = network.Nodes.First(n => n.Id == segment.To);
        return (new GeoPoint(Math.Round((a.Lat + b.Lat) / 2, 6), Math.Round((a.Lon + b.Lon) / 2, 6)), "segment");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![A-Za-z0-9])S[0-9]{1,2}(?![0-9])")]
    private static partial System.Text.RegularExpressions.Regex ShortIdPattern();

    private static bool Valid(GeoPoint p) =>
        double.IsFinite(p.Latitude) && double.IsFinite(p.Longitude) && p.Latitude is >= -85 and <= 85 && p.Longitude is >= -180 and <= 180;
}
