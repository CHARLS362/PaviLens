using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaveLens.Application.Common;
using PaveLens.Application.Maintenance;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>
/// Crew day plan by Muse Wonder: final visiting order, closure and time window per site. The facts and the
/// shortest order come from code; the answer is checked (every site once, no invented ids) with one self-repair.
/// </summary>
internal sealed class MetaCrewPlanner(MetaChatClient chat) : ICrewDayPlanner
{
    private const int MaxAttempts = 2;

    public AnalysisEngine Engine => AnalysisEngine.Meta;

    public async Task<CrewPlanResult> PlanAsync(CrewPlanRequest request, CancellationToken cancellationToken)
    {
        var ids = request.Sites.Select(s => s.Id).ToList();
        var messages = new List<JsonObject>
        {
            MetaChatClient.System(Prompts.Load("crew-plan.v1.md")),
            MetaChatClient.User(Describe(request))
        };

        for (var attempt = 1; ; attempt++)
        {
            var content = await chat.CompleteAsync(messages, jsonMode: true, cancellationToken);
            try
            {
                var plan = Parse(content, chat.Model);
                var problems = CrewPlanValidator.Check(plan, ids);
                return problems.Count == 0 ? plan : throw new ModelOutputException(string.Join(" ", problems));
            }
            catch (ModelOutputException ex) when (attempt < MaxAttempts)
            {
                messages.Add(MetaChatClient.Assistant(content));
                messages.Add(MetaChatClient.User($"Tu respuesta no cumple el contrato: {ex.Message} Responde de nuevo solo con el objeto JSON corregido."));
            }
        }
    }

    internal static CrewPlanResult Parse(string content, string model)
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
        if (response is null || string.IsNullOrWhiteSpace(response.Summary)) throw new ModelOutputException("Falta 'summary'.");

        var sites = (response.Sites ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => new CrewSitePlan(s.Id!.Trim(), Clip(s.Closure, "Cierre parcial"), Clip(s.Window, "—"), Clip(s.Rationale, "")))
            .ToList();
        return new CrewPlanResult(model, Clip(response.Summary, ""), (response.Order ?? []).Select(o => o.Trim()).ToList(), sites,
            (response.Risks ?? []).Select(r => Clip(r, "")).Where(r => r.Length > 0).Take(6).ToList(),
            (response.Communication ?? []).Select(c => Clip(c, "")).Where(c => c.Length > 0).Take(6).ToList());
    }

    /// <summary>Model text is shown as plain text and bounded in length.</summary>
    private static string Clip(string? text, string fallback)
    {
        var t = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        return t.Length > 600 ? t[..600] : t;
    }

    private static string Describe(CrewPlanRequest request)
    {
        var input = new JsonObject
        {
            ["language"] = request.Language.ToString().ToLowerInvariant(),
            ["totals"] = new JsonObject
            {
                ["distanceM"] = request.TotalDistanceM, ["durationMin"] = request.TotalDurationMin, ["routing"] = request.RoutingSource
            },
            ["sites"] = new JsonArray(request.Sites.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id, ["title"] = s.Title, ["location"] = s.Location, ["pci"] = s.Pci, ["band"] = s.Band,
                ["distresses"] = s.DistressCount, ["potholes"] = s.PotholeCount,
                ["legDistanceM"] = s.LegDistanceM, ["legDurationMin"] = s.LegDurationMin
            }).ToArray())
        };
        return string.Create(CultureInfo.InvariantCulture, $"Obras de la jornada:\n{input.ToJsonString()}");
    }

    private static string StripFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine > 0 && lastFence > firstNewLine ? trimmed[(firstNewLine + 1)..lastFence].Trim() : trimmed;
    }

    private sealed record PlanResponse(string? Summary, List<string>? Order, List<SiteResponse>? Sites, List<string>? Risks, List<string>? Communication);
    private sealed record SiteResponse(string? Id, string? Closure, string? Window, string? Rationale);
}
