using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;

namespace PaveLens.Application.Traffic;

public sealed record PlannedRouteDto(
    string Id, string Name, string Vehicles, IReadOnlyList<string> Nodes, IReadOnlyList<string> SegmentIds,
    IReadOnlyList<string> Streets, double TimeMin, double ExtraTimeMin, string Rationale);

public sealed record PlannedSignDto(string NodeId, string NodeName, string Kind, string Message);

public sealed record TrafficPlanDto(
    string Engine,
    string Model,
    string Summary,
    IReadOnlyList<PlannedRouteDto> Routes,
    IReadOnlyList<PlannedSignDto> Signage,
    IReadOnlyList<PlannedPhase> Phases,
    IReadOnlyList<string> Communication,
    IReadOnlyList<string> Risks,
    TrafficSimulationDto Simulation);

/// <summary>
/// Work-zone traffic management plan: the equilibrium and the candidate detours are computed here, the planner
/// (Muse Wonder through the Meta API, or the simulation) decides how to use them, and the result is checked
/// against the network before it is returned.
/// </summary>
public sealed class TrafficPlanService(TrafficService traffic, ITrafficPlanner planner)
{
    public async Task<TrafficPlanDto> PlanAsync(SimulateWorkZoneCommand command, CancellationToken cancellationToken)
    {
        var simulation = traffic.Simulate(command);
        var network = traffic.Network;
        var candidates = traffic.CandidateRoutes(simulation);
        var plan = await planner.PlanAsync(new TrafficPlanRequest(simulation, network, candidates, command.Language), cancellationToken);

        var problems = TrafficPlanValidator.Check(plan, network, simulation.SegmentId);
        if (problems.Count > 0)
        {
            throw new ModelOutputException($"Plan de desvíos inválido: {string.Join(" ", problems)}");
        }

        var nodes = network.Nodes.ToDictionary(n => n.Id);
        var times = simulation.Links.ToDictionary(l => (l.From, l.To));
        var direct = simulation.Links.Where(l => l.SegmentId == simulation.SegmentId).ToDictionary(l => (l.From, l.To), l => l.BaselineTimeMin);

        var routes = plan.Routes.Select(r =>
        {
            var hops = r.Nodes.Zip(r.Nodes.Skip(1)).Select(h => times[(h.First, h.Second)]).ToList();
            var segmentIds = hops.Select(h => h.SegmentId).ToList();
            var time = hops.Sum(h => h.WorkZoneTimeMin);
            var directMin = direct.GetValueOrDefault((r.Nodes[0], r.Nodes[^1]), 0);
            var streets = segmentIds
                .Select(id => network.Segments.First(s => s.Id == id).Name)
                .Aggregate(new List<string>(), (acc, name) => { if (acc.Count == 0 || acc[^1] != name) acc.Add(name); return acc; });
            return new PlannedRouteDto(r.Id, r.Name, r.Vehicles, r.Nodes, segmentIds, streets,
                Math.Round(time, 2), Math.Round(time - directMin, 2), r.Rationale);
        }).ToList();

        var signage = plan.Signage
            .Select(s => new PlannedSignDto(s.NodeId, nodes[s.NodeId].Name, s.Kind, s.Message))
            .ToList();

        return new TrafficPlanDto(
            planner.Engine.ToString(), plan.Model, plan.Summary, routes, signage,
            plan.Phases.OrderBy(p => p.Order).ToList(), plan.Communication, plan.Risks, simulation);
    }
}

/// <summary>Checks a plan against the network, so a model cannot route traffic through streets that do not connect.</summary>
public static class TrafficPlanValidator
{
    public static IReadOnlyList<string> Check(TrafficPlanResult plan, RoadNetwork network, string closedSegmentId)
    {
        var problems = new List<string>();
        var nodeIds = network.Nodes.Select(n => n.Id).ToHashSet();
        var adjacent = network.Segments
            .Where(s => s.Id != closedSegmentId)
            .SelectMany(s => new[] { (s.From, s.To), (s.To, s.From) })
            .ToHashSet();

        if (string.IsNullOrWhiteSpace(plan.Summary))
        {
            problems.Add("Falta 'summary'.");
        }

        if (plan.Routes.Count == 0)
        {
            problems.Add("'routes' está vacío.");
        }

        foreach (var (route, i) in plan.Routes.Select((r, i) => (r, i)))
        {
            if (route.Nodes.Count < 2)
            {
                problems.Add($"routes[{i}] necesita al menos 2 nodos.");
                continue;
            }

            var unknown = route.Nodes.FirstOrDefault(n => !nodeIds.Contains(n));
            if (unknown is not null)
            {
                problems.Add($"routes[{i}] usa el nodo inexistente '{unknown}'.");
                continue;
            }

            var gap = route.Nodes.Zip(route.Nodes.Skip(1)).FirstOrDefault(h => !adjacent.Contains(h));
            if (gap != default)
            {
                problems.Add($"routes[{i}]: {gap.First} y {gap.Second} no están unidos por un tramo abierto (el tramo {closedSegmentId} está en obra).");
            }
        }

        foreach (var (sign, i) in plan.Signage.Select((s, i) => (s, i)))
        {
            if (!nodeIds.Contains(sign.NodeId))
            {
                problems.Add($"signage[{i}] usa el nodo inexistente '{sign.NodeId}'.");
            }
        }

        return problems;
    }
}
