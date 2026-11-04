using PaveLens.Application.Abstractions;
using PaveLens.Application.Inspections;
using PaveLens.Application.Maintenance;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Common;
using PaveLens.Domain.Inspections;
using PaveLens.Infrastructure.Maps;

namespace PaveLens.UnitTests.Application;

public class MaintenanceTests
{
    private static readonly GeoPoint Depot = new(-15.8402, -70.0219);

    [Fact]
    public void OrdersSitesByShortestPathWhenUrgencyIsIgnored()
    {
        // Three sites on a line east of the depot, given out of order.
        var sites = new List<(GeoPoint, double?)>
        {
            (new GeoPoint(-15.8402, -70.0119), 80), (new GeoPoint(-15.8402, -70.0199), 80), (new GeoPoint(-15.8402, -70.0159), 80)
        };
        Assert.Equal([1, 2, 0], CrewRoutePlanner.Order(Depot, sites, priorityFirst: false));
    }

    [Fact]
    public void VisitsUrgentSitesFirstWhenAsked()
    {
        var sites = new List<(GeoPoint, double?)>
        {
            (new GeoPoint(-15.8402, -70.0209), 85), // closest, but preventive
            (new GeoPoint(-15.8402, -70.0119), 25), // far, urgent
            (new GeoPoint(-15.8402, -70.0159), 55)
        };
        Assert.Equal([1, 2, 0], CrewRoutePlanner.Order(Depot, sites, priorityFirst: true));
        Assert.Equal("Urgente", CrewRoutePlanner.BandLabel(25));
        Assert.Equal("Sin PCI", CrewRoutePlanner.BandLabel(null));
    }

    [Fact]
    public void TwoOptRemovesCrossings()
    {
        var square = new List<(GeoPoint, double?)>
        {
            (new GeoPoint(-15.8402, -70.0200), null), (new GeoPoint(-15.8302, -70.0100), null),
            (new GeoPoint(-15.8302, -70.0200), null), (new GeoPoint(-15.8402, -70.0100), null)
        };
        var order = CrewRoutePlanner.Order(Depot, square, priorityFirst: false);
        double Length(IReadOnlyList<int> o)
        {
            var at = Depot; var total = 0.0;
            foreach (var i in o) { total += CrewRoutePlanner.DistanceM(at, square[i].Item1); at = square[i].Item1; }
            return total;
        }
        var best = Permutations([0, 1, 2, 3]).Min(Length);
        Assert.Equal(best, Length(order), 3);
    }

    [Fact]
    public void ParsesGoogleRoutesResponse()
    {
        const string json = """
            {"routes":[{"distanceMeters":2500,"duration":"600s","polyline":{"encodedPolyline":"abc"},
              "legs":[{"distanceMeters":1000,"duration":"240s"},{"distanceMeters":1500,"duration":"360s"}]}]}
            """;
        var route = GoogleRoutesProvider.Parse(json, expectedLegs: 2)!;
        Assert.Equal(2500, route.DistanceM);
        Assert.Equal(600, route.DurationS);
        Assert.Equal("abc", route.EncodedPolyline);
        Assert.Equal(360, route.Legs[1].DurationS);
        Assert.Null(GoogleRoutesProvider.Parse(json, expectedLegs: 3));
    }

    [Fact]
    public async Task FallsBackToAnEstimateWithoutGoogleAndPlacesBySegment()
    {
        var traffic = new TrafficService();
        var segment = traffic.Network.Segments[0];
        var urgent = Row(25, segmentId: segment.Id);
        var gps = Row(60, lat: -15.835, lon: -70.025);
        var service = new MaintenanceService(new FakeQueries([urgent, gps, Row(90)]), traffic, new NoRoutes(), new ReversePlanner(), Microsoft.Extensions.Options.Options.Create(new MapsOptions()));

        var points = await service.ListPointsAsync(CancellationToken.None);
        Assert.Equal(2, points.Count); // the row with neither GPS nor segment is not placed
        Assert.Equal("segment", points.Single(p => p.Id == urgent.Id).CoordinateSource);

        var plan = await service.PlanCrewRouteAsync(new CrewRouteRequest(null, [gps.Id, urgent.Id]), CancellationToken.None);
        Assert.Equal("estimate", plan.Source);
        // Code puts the urgent site first; the planner (Meta in production) reverses it and code accepts a valid order.
        Assert.Equal(gps.Id, plan.Stops[0].InspectionId);
        Assert.True(plan.Planner!.OrderChanged);
        Assert.Equal(gps.Id.ToString(), plan.Planner.Sites[0].Id);
        Assert.Equal("Primero 1 (Obra), luego 2 (Obra).", plan.Planner.Summary); // no internal ids reach people
        Assert.True(plan.TotalDistanceM > 0);
        Assert.Null(plan.EncodedPolyline);
    }

    [Fact]
    public async Task RejectsEmptyOrTooFarRoutes()
    {
        var far = Row(30, lat: -12.0464, lon: -77.0428); // Lima
        var service = new MaintenanceService(new FakeQueries([far]), new TrafficService(), new NoRoutes(), new ReversePlanner(), Microsoft.Extensions.Options.Options.Create(new MapsOptions()));
        await Assert.ThrowsAsync<DomainException>(() => service.PlanCrewRouteAsync(new CrewRouteRequest(null, []), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => service.PlanCrewRouteAsync(new CrewRouteRequest(null, [far.Id]), CancellationToken.None));
    }

    [Fact]
    public void ParsesAnOsrmRouteWithItsLegsAndPolyline()
    {
        const string json = """
            {"code":"Ok","routes":[{"geometry":"_p~iF~ps|U_ulLnnqC","distance":1234.5,"duration":321.4,
              "legs":[{"distance":500,"duration":120},{"distance":734.5,"duration":201.4}]}]}
            """;
        var route = OsrmRouteProvider.Parse(json, expectedLegs: 2)!;
        Assert.Equal("osm", route.Source);
        Assert.Equal(1234.5, route.DistanceM);
        Assert.Equal(201.4, route.Legs[1].DurationS);
        Assert.Equal("_p~iF~ps|U_ulLnnqC", route.EncodedPolyline);
        Assert.Null(OsrmRouteProvider.Parse("""{"code":"NoRoute","routes":[]}""", 2));
        Assert.Null(OsrmRouteProvider.Parse(json, expectedLegs: 1));
    }

    [Fact]
    public async Task UsesOsmRoutesByDefaultAndNeverCallsGoogleForAnOsmMap()
    {
        var osrm = new CountingHandler("""{"code":"Ok","routes":[{"geometry":"abc","distance":900,"duration":200,"legs":[{"distance":900,"duration":200}]}]}""");
        var google = new CountingHandler("{}");
        var router = Router(osrm, google, googleKey: "server-key");
        var route = await router.RouteAsync([Depot, new GeoPoint(-15.835, -70.02)], MapProvider.Osm, CancellationToken.None);
        Assert.Equal("osm", route!.Source);
        Assert.Equal(1, osrm.Calls);
        Assert.Equal(0, google.Calls);
        Assert.Contains("/route/v1/driving/-70.021900,-15.840200;-70.020000,-15.835000", osrm.LastUri);
    }

    [Fact]
    public async Task FallsBackFromGoogleToOsmWhenGoogleFails()
    {
        var osrm = new CountingHandler("""{"code":"Ok","routes":[{"geometry":"abc","distance":900,"duration":200,"legs":[{"distance":900,"duration":200}]}]}""");
        var google = new CountingHandler("{}", System.Net.HttpStatusCode.TooManyRequests);
        var route = await Router(osrm, google, googleKey: "server-key").RouteAsync([Depot, new GeoPoint(-15.83, -70.02)], MapProvider.Google, CancellationToken.None);
        Assert.Equal("osm", route!.Source);
        Assert.Equal(1, google.Calls);
    }

    private static RoadRouteRouter Router(CountingHandler osrm, CountingHandler google, string? googleKey)
    {
        var maps = Microsoft.Extensions.Options.Options.Create(new MapsOptions());
        var osrmProvider = new OsrmRouteProvider(new HttpClient(osrm) { BaseAddress = new Uri("https://osrm.test/") }, maps,
            new OsrmCache(TimeProvider.System), Microsoft.Extensions.Logging.Abstractions.NullLogger<OsrmRouteProvider>.Instance);
        var googleProvider = new GoogleRoutesProvider(new HttpClient(google) { BaseAddress = new Uri("https://routes.test/") },
            Microsoft.Extensions.Options.Options.Create(new GoogleMapsOptions { ApiKey = googleKey }), new GoogleMapsUsage(TimeProvider.System),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GoogleRoutesProvider>.Instance);
        return new RoadRouteRouter(osrmProvider, googleProvider);
    }

    private sealed class CountingHandler(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastUri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static InspectionLocationRow Row(double? pci, string? segmentId = null, double? lat = null, double? lon = null) =>
        new(Guid.NewGuid(), "Obra", null, InspectionStatus.Completed, pci, 2, 1, segmentId, lat, lon);

    private static IEnumerable<IReadOnlyList<int>> Permutations(int[] items)
    {
        if (items.Length <= 1) { yield return items; yield break; }
        for (var i = 0; i < items.Length; i++)
            foreach (var rest in Permutations(items.Where((_, k) => k != i).ToArray()))
                yield return [items[i], .. rest];
    }

    [Fact]
    public async Task FallsBackToTheCodeOrderWhenThePlannerAnswersBadly()
    {
        var a = Row(20, lat: -15.84, lon: -70.02);
        var b = Row(80, lat: -15.83, lon: -70.02);
        var service = new MaintenanceService(new FakeQueries([a, b]), new TrafficService(), new NoRoutes(), new BrokenPlanner(), Microsoft.Extensions.Options.Options.Create(new MapsOptions()));
        var plan = await service.PlanCrewRouteAsync(new CrewRouteRequest(null, [b.Id, a.Id]), CancellationToken.None);
        Assert.Null(plan.Planner);
        Assert.Equal(a.Id, plan.Stops[0].InspectionId);
        Assert.Contains(plan.Warnings, w => w.Contains("Meta"));
    }

    [Fact]
    public void ValidatorRejectsDroppedRepeatedOrInventedSites()
    {
        CrewPlanResult Plan(params string[] order) => new("m", "ok", order, order.Distinct().Select(o => new CrewSitePlan(o, "c", "w", "r")).ToList(), [], []);
        Assert.Empty(CrewPlanValidator.Check(Plan("S2", "S1"), ["S1", "S2"]));
        Assert.NotEmpty(CrewPlanValidator.Check(Plan("S1"), ["S1", "S2"]));
        Assert.NotEmpty(CrewPlanValidator.Check(Plan("S1", "S1"), ["S1", "S2"]));
        Assert.NotEmpty(CrewPlanValidator.Check(Plan("S1", "S9"), ["S1", "S2"]));
    }

    private sealed class ReversePlanner : ICrewDayPlanner
    {
        public AnalysisEngine Engine => AnalysisEngine.Meta;
        public Task<CrewPlanResult> PlanAsync(CrewPlanRequest request, CancellationToken cancellationToken)
        {
            var order = request.Sites.Select(s => s.Id).Reverse().ToList();
            return Task.FromResult(new CrewPlanResult("muse-test", "Primero S1, luego S2.", order,
                request.Sites.Select(s => new CrewSitePlan(s.Id, "Un carril", "09:00–10:00", "r")).ToList(), [], []));
        }
    }

    private sealed class BrokenPlanner : ICrewDayPlanner
    {
        public AnalysisEngine Engine => AnalysisEngine.Meta;
        public Task<CrewPlanResult> PlanAsync(CrewPlanRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CrewPlanResult("muse-test", "Plan", ["S1", "S1"], [], [], []));
    }

    private sealed class NoRoutes : IRoadRouteProvider
    {
        public Task<RoadRoute?> RouteAsync(IReadOnlyList<GeoPoint> waypoints, MapProvider preferred, CancellationToken cancellationToken) => Task.FromResult<RoadRoute?>(null);
    }

    private sealed class FakeQueries(IReadOnlyList<InspectionLocationRow> rows) : IInspectionQueries
    {
        public Task<IReadOnlyList<InspectionLocationRow>> ListLocationsAsync(int max, CancellationToken cancellationToken) => Task.FromResult(rows);
        public Task<PagedResult<InspectionSummaryDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InspectionDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversationMessageDto>> GetConversationAsync(Guid inspectionId, Guid? distressId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
