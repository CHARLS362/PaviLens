using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>
/// Work-zone traffic plan by Muse Wonder. The equilibrium, costs and candidate paths come from the traffic model;
/// the model chooses routes, signage and phases, and its routes are checked against the network (one self-repair).
/// </summary>
internal sealed class MetaTrafficPlanner(MetaChatClient chat) : ITrafficPlanner
{
    private const int MaxAttempts = 2;

    public AnalysisEngine Engine => AnalysisEngine.Meta;

    public async Task<TrafficPlanResult> PlanAsync(TrafficPlanRequest request, CancellationToken cancellationToken)
    {
        var messages = new List<JsonObject>
        {
            MetaChatClient.System(Prompts.Load("traffic-plan.v1.md")),
            MetaChatClient.User(DescribeRequest(request))
        };

        for (var attempt = 1; ; attempt++)
        {
            var content = await chat.CompleteAsync(messages, jsonMode: true, cancellationToken);
            try
            {
                var plan = Parse(content, chat.Model);
                var problems = TrafficPlanValidator.Check(plan, request.Network, request.Simulation.SegmentId);
                return problems.Count == 0 ? plan : throw new ModelOutputException(string.Join(" ", problems));
            }
            catch (ModelOutputException ex) when (attempt < MaxAttempts)
            {
                messages.Add(MetaChatClient.Assistant(content));
                messages.Add(MetaChatClient.User(
                    $"Tu respuesta no cumple el contrato: {ex.Message} Responde de nuevo solo con el objeto JSON corregido."));
            }
        }
    }

    internal static TrafficPlanResult Parse(string content, string model)
    {
        PlanResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<PlanResponse>(StripFences(content), MetaReasoner.Json);
        }
        catch (JsonException ex)
        {
            throw new ModelOutputException($"JSON no válido ({ex.Message}).");
        }

        if (response is null || string.IsNullOrWhiteSpace(response.Summary))
        {
            throw new ModelOutputException("Falta 'summary'.");
        }

        var routes = (response.Routes ?? []).Select((r, i) =>
            string.IsNullOrWhiteSpace(r.Id) || r.Nodes is not { Count: >= 2 }
                ? throw new ModelOutputException($"routes[{i}] necesita id y al menos 2 nodos.")
                : new PlannedRoute(r.Id.Trim(), r.Name ?? r.Id, r.Vehicles ?? "Todos", r.Nodes, r.Rationale ?? "")).ToList();

        var signage = (response.Signage ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.NodeId) && !string.IsNullOrWhiteSpace(s.Message))
            .Select(s => new PlannedSign(s.NodeId!.Trim(), s.Kind ?? "Informativa", s.Message!.Trim()))
            .ToList();

        var phases = (response.Phases ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Title))
            .Select((p, i) => new PlannedPhase(p.Order ?? i + 1, p.Title!, p.When ?? "", p.Description ?? ""))
            .ToList();

        return new TrafficPlanResult(model, response.Summary.Trim(), routes, signage, phases,
            response.Communication ?? [], response.Risks ?? []);
    }

    private static string DescribeRequest(TrafficPlanRequest request)
    {
        var sim = request.Simulation;
        var network = request.Network;
        var closed = network.Segments.Single(s => s.Id == sim.SegmentId);
        var names = network.Segments.ToDictionary(s => s.Id, s => s.Name);

        var input = new JsonObject
        {
            ["language"] = request.Language.ToString().ToLowerInvariant(),
            ["network"] = new JsonObject
            {
                ["nodes"] = new JsonArray(network.Nodes.Select(n => (JsonNode)new JsonObject { ["id"] = n.Id, ["name"] = n.Name }).ToArray()),
                ["segments"] = new JsonArray(network.Segments.Select(s => (JsonNode)new JsonObject
                {
                    ["id"] = s.Id, ["name"] = s.Name, ["from"] = s.From, ["to"] = s.To,
                    ["lanesPerDirection"] = s.LanesPerDirection, ["capacityPerLaneVph"] = s.CapacityPerLaneVph
                }).ToArray())
            },
            ["closure"] = new JsonObject
            {
                ["segmentId"] = sim.SegmentId, ["name"] = sim.SegmentName, ["from"] = closed.From, ["to"] = closed.To,
                ["lanesClosed"] = sim.LanesClosed, ["lanesPerDirection"] = sim.LanesPerDirection,
                ["period"] = sim.Period.ToString(), ["durationH"] = sim.DurationH
            },
            ["results"] = new JsonObject
            {
                ["deltaVehHPerHour"] = sim.DeltaVehH, ["deltaPct"] = sim.DeltaPct, ["userCostPen"] = sim.UserCostPen,
                ["overCapacityLinks"] = sim.OverCapacityLinks, ["recommendedPeriod"] = sim.RecommendedPeriod.ToString(),
                ["windows"] = new JsonArray(sim.Windows.Select(w => (JsonNode)new JsonObject
                {
                    ["period"] = w.Period.ToString(), ["userCostPen"] = w.UserCostPen, ["deltaPct"] = w.DeltaPct
                }).ToArray()),
                ["detourLinks"] = new JsonArray(sim.Links
                    .Where(l => sim.DetourSegmentIds.Contains(l.SegmentId) && l.WorkZoneFlowVph > l.BaselineFlowVph)
                    .Select(l => (JsonNode)new JsonObject
                    {
                        ["segmentId"] = l.SegmentId, ["name"] = names[l.SegmentId], ["from"] = l.From, ["to"] = l.To,
                        ["baselineVph"] = l.BaselineFlowVph, ["workZoneVph"] = l.WorkZoneFlowVph,
                        ["vc"] = Math.Round(l.WorkZoneFlowVph / Math.Max(1, l.WorkZoneCapacityVph), 2)
                    }).ToArray())
            },
            ["candidates"] = new JsonArray(request.Candidates.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id, ["label"] = c.Label,
                ["nodes"] = new JsonArray(c.Nodes.Select(n => (JsonNode)n).ToArray()),
                ["streets"] = new JsonArray(c.SegmentIds.Select(id => (JsonNode)names[id]).Distinct().ToArray()),
                ["extraTimeMin"] = c.ExtraTimeMin
            }).ToArray())
        };

        return string.Create(CultureInfo.InvariantCulture, $"Datos del cierre:\n{input.ToJsonString()}");
    }

    private static string StripFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine > 0 && lastFence > firstNewLine ? trimmed[(firstNewLine + 1)..lastFence] : trimmed;
    }

    private sealed record PlanResponse(
        string? Summary, List<RouteResponse>? Routes, List<SignResponse>? Signage, List<PhaseResponse>? Phases,
        List<string>? Communication, List<string>? Risks);

    private sealed record RouteResponse(string? Id, string? Name, string? Vehicles, List<string>? Nodes, string? Rationale);

    private sealed record SignResponse(string? NodeId, string? Kind, string? Message);

    private sealed record PhaseResponse(int? Order, string? Title, string? When, string? Description);
}
