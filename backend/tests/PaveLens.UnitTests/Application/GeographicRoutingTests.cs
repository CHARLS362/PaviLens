using System.Text.Json;
using PaveLens.Application.Geography;
using PaveLens.Infrastructure.Geography;
using PaveLens.Application.Common;

namespace PaveLens.UnitTests.Application;

public class GeographicRoutingTests
{
    private static readonly string[] ExpectedDetour = ["a", "c", "d"];
    private static readonly string[] ExpectedContinuousRoute = ["a", "b"];
    [Fact]
    public void RespectsOneWayStreetsClosuresAndForbiddenTurns()
    {
        var network = new GeographicNetwork("test", DateTime.UtcNow, 0, 0, 600,
            [new(1, 0, 0), new(2, 0, 1), new(3, 1, 0), new(4, 1, 1)],
            [new("closed", 10, 1, 2, "closed", 50, 30, false, true, false),
             new("a", 20, 1, 3, "a", 100, 30, false, true, false),
             new("b", 30, 3, 2, "b", 100, 30, false, true, false),
             new("c", 40, 3, 4, "c", 200, 30, false, true, false),
             new("d", 50, 4, 2, "d", 200, 30, false, true, false)],
            [new(20, 3, 30, false)], []);
        var result = GeographicRouting.Close(network, "closed");
        Assert.Equal(ExpectedDetour, result.Routes.Single().EdgeIds);
        Assert.DoesNotContain("closed", result.Routes.Single().EdgeIds);
        var reversed = network with { Edges = network.Edges.Select(e => e.Id == "a" ? e with { Forward = false, Backward = true } : e).ToArray() };
        Assert.Empty(GeographicRouting.Close(reversed, "closed").Routes);
    }

    [Fact]
    public void ImportsActualDirectionAndDistinguishesMissingSpeeds()
    {
        using var json = JsonDocument.Parse("""
        {"elements":[{"type":"way","id":7,"nodes":[1,2],"geometry":[{"lat":-15.84,"lon":-70.02},{"lat":-15.841,"lon":-70.02}],"tags":{"highway":"residential","name":"Calle Real","oneway":"-1"}}]}
        """);
        var network = OverpassNetworkProvider.Parse(json.RootElement, -15.84, -70.02, 600);
        var edge = Assert.Single(network.Edges);
        Assert.False(edge.Forward); Assert.True(edge.Backward); Assert.True(edge.SpeedAssumed);
        Assert.InRange(edge.LengthM, 110, 112);
    }

    [Fact]
    public void ANoUTurnRestrictionStillAllowsContinuingAlongTheSameWay()
    {
        var network = new GeographicNetwork("test", DateTime.UtcNow, 0, 0, 600,
            [new(1, 0, 0), new(2, 0, 1), new(3, 1, 0)],
            [new("closed", 10, 1, 2, "closed", 50, 30, false, true, false),
             new("a", 20, 1, 3, "continuous", 100, 30, false, true, true),
             new("b", 20, 3, 2, "continuous", 100, 30, false, true, true)],
            [new(20, 3, 20, false, "no_u_turn")], []);
        Assert.Equal(ExpectedContinuousRoute, GeographicRouting.Close(network, "closed").Routes.Single().EdgeIds);
    }

    [Fact]
    public async Task AServiceTimeoutProducesAnActionableAvailabilityError()
    {
        using var http = new HttpClient(new TimeoutHandler()) { BaseAddress = new Uri("https://example.test/") };
        var provider = new OverpassNetworkProvider(http, new GeographicNetworkCache());
        var error = await Assert.ThrowsAsync<ModelUnavailableException>(() => provider.LoadAsync(-15.84, -70.02, 600, TestContext.Current.CancellationToken));
        Assert.Contains("tiempo de espera", error.Message, StringComparison.Ordinal);
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("Service timeout");
    }
}
