using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>
/// Muse Wonder diagnosis. Coordinates and metres come from SAM and the geometry layer; the model only
/// classifies and reasons about numbered segments, so it cannot invent positions or sizes.
/// </summary>
internal sealed class MetaReasoner(MetaChatClient chat) : IDiagnosticReasoner
{
    private const int MaxAttempts = 2;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // Names only: a number would map to an undefined value or silently to the wrong distress type.
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public AnalysisEngine Engine => AnalysisEngine.Meta;

    public async Task<DiagnosisResult> DiagnoseAsync(DiagnosisRequest request, CancellationToken cancellationToken)
    {
        var messages = new List<JsonObject>
        {
            MetaChatClient.System(Prompts.Load("diagnosis.v1.md")),
            MetaChatClient.UserWithImage(DescribeRequest(request), request.Image)
        };

        for (var attempt = 1; ; attempt++)
        {
            var content = await chat.CompleteAsync(messages, jsonMode: true, cancellationToken);
            try
            {
                return Parse(content, chat.Model) with { Attempts = attempt };
            }
            catch (ModelOutputException ex) when (attempt < MaxAttempts)
            {
                // Self-repair: show the model its own answer and the exact problem once.
                messages.Add(MetaChatClient.Assistant(content));
                messages.Add(MetaChatClient.User(
                    $"Tu respuesta no cumple el contrato: {ex.Message} Responde de nuevo solo con el objeto JSON corregido."));
            }
        }
    }

    internal static DiagnosisResult Parse(string content, string model)
    {
        DiagnosisResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<DiagnosisResponse>(StripFences(content), Json);
        }
        catch (JsonException ex)
        {
            throw new ModelOutputException($"JSON no válido ({ex.Message}).");
        }

        if (response is null || string.IsNullOrWhiteSpace(response.Summary))
        {
            throw new ModelOutputException("Falta 'summary'.");
        }

        if (response.Distresses is not { Count: > 0 })
        {
            throw new ModelOutputException("Falta 'distresses' o está vacío.");
        }

        var findings = response.Distresses.Select((d, i) =>
        {
            if (d.SegmentIndex is null || string.IsNullOrWhiteSpace(d.Code) || d.Type is null || d.Severity is null)
            {
                throw new ModelOutputException($"distresses[{i}] necesita segmentIndex, code, type y severity.");
            }

            return new DistressFinding(
                d.SegmentIndex.Value,
                d.Code.Trim(),
                d.Type.Value,
                d.Severity.Value,
                d.Diagnosis,
                d.RecommendedAction);
        }).ToList();

        var steps = (response.RepairSteps ?? []).Select((s, i) =>
        {
            if (string.IsNullOrWhiteSpace(s.Title) || string.IsNullOrWhiteSpace(s.Instructions))
            {
                throw new ModelOutputException($"repairSteps[{i}] necesita title e instructions.");
            }

            return new StepPlan(s.Order ?? i + 1, s.Title, s.Instructions, s.Tools ?? [], s.Risk ?? RiskLevel.Medium, s.TargetSegmentIndex);
        }).ToList();

        return new DiagnosisResult(model, response.Summary.Trim(), findings, steps);
    }

    private static string DescribeRequest(DiagnosisRequest request)
    {
        var segments = new JsonArray(request.Segments.Select(m =>
        {
            var s = m.Segment;
            var xs = s.Polygon.Select(p => p.X).ToArray();
            var ys = s.Polygon.Select(p => p.Y).ToArray();
            return (JsonNode)new JsonObject
            {
                ["index"] = s.Index,
                ["concept"] = s.Concept,
                ["score"] = Math.Round(s.Score, 3),
                ["box"] = new JsonObject
                {
                    ["x"] = Math.Round(xs.Min(), 4),
                    ["y"] = Math.Round(ys.Min(), 4),
                    ["width"] = Math.Round(xs.Max() - xs.Min(), 4),
                    ["height"] = Math.Round(ys.Max() - ys.Min(), 4)
                },
                ["areaM2"] = m.Measurement.AreaM2,
                ["lengthM"] = m.Measurement.LengthM,
                ["widthM"] = m.Measurement.WidthM,
                ["eccentricity"] = m.Measurement.Eccentricity,
                ["orientationDeg"] = m.Measurement.OrientationDeg
            };
        }).ToArray());

        var input = new JsonObject
        {
            ["title"] = request.Title,
            ["notes"] = request.Notes,
            ["location"] = request.Location,
            ["regionalContext"] = request.RegionalContext,
            ["language"] = request.Language.ToString().ToLowerInvariant(),
            ["image"] = new JsonObject { ["width"] = request.Image.Width, ["height"] = request.Image.Height },
            ["segments"] = segments
        };
        return string.Create(CultureInfo.InvariantCulture, $"Entrada de la inspección:\n{input.ToJsonString()}");
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

    private sealed record DiagnosisResponse(string? Summary, List<DistressResponse>? Distresses, List<StepResponse>? RepairSteps);

    private sealed record DistressResponse(
        int? SegmentIndex, string? Code, DistressType? Type, Severity? Severity, string? Diagnosis, string? RecommendedAction);

    private sealed record StepResponse(
        int? Order, string? Title, string? Instructions, List<string>? Tools, RiskLevel? Risk, int? TargetSegmentIndex);
}
