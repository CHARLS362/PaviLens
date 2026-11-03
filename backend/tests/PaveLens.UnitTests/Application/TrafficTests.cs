using Microsoft.Extensions.Options;
using FluentValidation;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Traffic;

namespace PaveLens.UnitTests.Application;

public class TrafficAssignmentTests
{
    [Fact]
    public void BprMatchesThePlanningExample()
    {
        // V/C = 0.8 → 1.06·t0; closing one of two lanes doubles V/C to 1.6 → ≈1.98·t0.
        Assert.Equal(1.06144, TrafficAssignment.BprTime(1, 800, 1000), 5);
        Assert.Equal(1.98304, TrafficAssignment.BprTime(1, 800, 500), 5);
    }

    /// <summary>Two parallel routes A→B: a fast narrow one and a slow wide one.</summary>
    private static RoadNetwork TwoRoutes(double demand) => new(
        "test", "test",
        [new RoadNode("A", "A", 0, 0, 0, 0), new RoadNode("M", "M", 1, 1, 0, 0), new RoadNode("B", "B", 2, 0, 0, 0)],
        [
            new RoadSegment("fast", "fast", "A", "B", 1, 60, 1, 800),
            new RoadSegment("slow1", "slow", "A", "M", 1, 60, 2, 1000),
            new RoadSegment("slow2", "slow", "M", "B", 0.5, 60, 2, 1000)
        ],
        [new TripDemand("A", "B", demand)]);

    [Fact]
    public void LightDemandTakesOnlyTheShortestRoute()
    {
        var result = TrafficAssignment.Solve(TwoRoutes(100));

        Assert.Equal(100, Flow(result, "fast", "A", "B"), 3);
        Assert.Equal(0, Flow(result, "slow1", "A", "M"), 3);
    }

    [Fact]
    public void HeavyDemandSplitsUntilUsedRoutesTakeTheSameTime()
    {
        var result = TrafficAssignment.Solve(TwoRoutes(2000));
        var fastTime = Link(result, "fast", "A", "B").TimeH;
        var slowTime = Link(result, "slow1", "A", "M").TimeH + Link(result, "slow2", "M", "B").TimeH;

        // Wardrop: every used route has the same travel time (within the convergence gap).
        Assert.True(Flow(result, "slow1", "A", "M") > 0);
        Assert.Equal(1, slowTime / fastTime, 3);
        Assert.Equal(2000, Flow(result, "fast", "A", "B") + Flow(result, "slow1", "A", "M"), 3);
        Assert.True(result.RelativeGap < 1e-4);
    }

    [Fact]
    public void ClosingALaneRaisesTotalTravelTime()
    {
        var baseline = TrafficAssignment.Solve(TwoRoutes(2000));
        var workZone = TrafficAssignment.Solve(TwoRoutes(2000), new WorkZone("slow1", 1));

        Assert.True(workZone.TotalTravelTimeVehH > baseline.TotalTravelTimeVehH);
    }

    [Fact]
    public void FullClosureRemovesTheSegmentAndDivertsAllFlow()
    {
        var result = TrafficAssignment.Solve(TwoRoutes(500), new WorkZone("fast", 1));

        Assert.DoesNotContain(result.Links, l => l.SegmentId == "fast");
        Assert.Equal(500, Flow(result, "slow1", "A", "M"), 3);
    }

    [Fact]
    public void DemoNetworkWorkZoneProducesADetour()
    {
        var service = new TrafficService();

        var result = service.Simulate(new SimulateWorkZoneCommand("H01", 1));

        Assert.True(result.DeltaVehH > 0);
        Assert.NotEmpty(result.DetourSegmentIds);
        Assert.True(result.WorkZone.RelativeGap < 1e-4);
    }

    [Fact]
    public void LowerDemandWindowsCostLessAndNightIsRecommended()
    {
        var result = new TrafficService().Simulate(new SimulateWorkZoneCommand("V00", 1, WorkPeriod.Peak, 8));

        var cost = result.Windows.ToDictionary(w => w.Period, w => w.UserCostPen);
        Assert.Equal(3, cost.Count);
        Assert.True(cost[WorkPeriod.Peak] > cost[WorkPeriod.OffPeak]);
        Assert.True(cost[WorkPeriod.OffPeak] > cost[WorkPeriod.Night]);
        Assert.Equal(WorkPeriod.Night, result.RecommendedPeriod);
        Assert.Equal(cost[WorkPeriod.Peak], result.UserCostPen);
    }

    [Fact]
    public void UserCostScalesWithDurationAndValueOfTime()
    {
        var service = new TrafficService(Options.Create(new TrafficOptions { ValueOfTimePenPerVehH = 10 }));

        var four = service.Simulate(new SimulateWorkZoneCommand("V00", 1, WorkPeriod.OffPeak, 4));
        var eight = service.Simulate(new SimulateWorkZoneCommand("V00", 1, WorkPeriod.OffPeak, 8));

        Assert.Equal(four.DeltaVehH * 4 * 10, four.UserCostPen, 1);
        Assert.Equal(2 * four.UserCostPen, eight.UserCostPen, 1);
        Assert.Equal(0.65, four.DemandFactor);
    }

    [Fact]
    public void RejectsNonPositiveDurations()
    {
        Assert.Throws<ValidationException>(() => new TrafficService().Simulate(new SimulateWorkZoneCommand("V00", 1, WorkPeriod.Peak, 0)));
    }

    [Fact]
    public void ClosingMoreLanesThanTheSegmentHasIsRejected()
    {
        Assert.Throws<ValidationException>(() => new TrafficService().Simulate(new SimulateWorkZoneCommand("H11", 2)));
        Assert.Throws<ValidationException>(() => new TrafficService().Simulate(new SimulateWorkZoneCommand("nope", 1)));
    }

    [Fact]
    public void DetourRoutesGoAroundTheClosureInBothDirections()
    {
        var service = new TrafficService();
        var result = service.Simulate(new SimulateWorkZoneCommand("H11", 1));
        var adjacent = service.Network.Segments.Where(s => s.Id != "H11")
            .SelectMany(s => new[] { (s.From, s.To), (s.To, s.From) }).ToHashSet();

        Assert.Equal(2, result.DetourRoutes.Count);
        Assert.Contains(result.DetourRoutes, r => r.Nodes[0] == "N11" && r.Nodes[^1] == "N12");
        Assert.Contains(result.DetourRoutes, r => r.Nodes[0] == "N12" && r.Nodes[^1] == "N11");
        foreach (var route in result.DetourRoutes)
        {
            Assert.DoesNotContain("H11", route.SegmentIds);
            Assert.All(route.Nodes.Zip(route.Nodes.Skip(1)), hop => Assert.Contains(hop, adjacent));
            Assert.True(route.ExtraTimeMin > 0);
        }
    }

    [Fact]
    public void AlternativesShareNoSegmentWithTheFastestRoute()
    {
        var service = new TrafficService();
        var candidates = service.CandidateRoutes(service.Simulate(new SimulateWorkZoneCommand("H11", 1)));

        var fastest = candidates.Single(c => c.Id == "R1");
        var alternate = candidates.Single(c => c.Id == "R1b");
        Assert.Empty(fastest.SegmentIds.Intersect(alternate.SegmentIds));
        Assert.True(alternate.TimeMin >= fastest.TimeMin);
    }

    [Fact]
    public void PlanValidatorRejectsRoutesThatJumpOrCrossTheWorkZone()
    {
        var network = new TrafficService().Network;
        PlannedRoute Route(params string[] nodes) => new("R1", "x", "Todos", nodes, "");
        TrafficPlanResult Plan(PlannedRoute route) => new("m", "ok", [route], [new("N11", "Reglamentaria", "Cerrado")], [], [], []);

        Assert.Empty(TrafficPlanValidator.Check(Plan(Route("N11", "N01", "N02", "N12")), network, "H11"));
        Assert.NotEmpty(TrafficPlanValidator.Check(Plan(Route("N11", "N12")), network, "H11"));
        Assert.NotEmpty(TrafficPlanValidator.Check(Plan(Route("N11", "N22")), network, "H11"));
        Assert.NotEmpty(TrafficPlanValidator.Check(Plan(Route("N11", "N99")), network, "H11"));
    }

    private static LinkFlow Link(AssignmentResult result, string segment, string from, string to) =>
        result.Links.Single(l => l.SegmentId == segment && l.From == from && l.To == to);

    private static double Flow(AssignmentResult result, string segment, string from, string to) =>
        Link(result, segment, from, to).FlowVph;
}
