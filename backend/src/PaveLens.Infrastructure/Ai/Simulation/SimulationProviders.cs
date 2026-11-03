using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaveLens.Application.Abstractions;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Simulation;

/// <summary>
/// Fixed fixtures keyed by image SHA-256, embedded from Ai/Simulation/Scenarios/*.json.
/// Lets the full pipeline run end to end without model credentials; results are always
/// tagged <see cref="AnalysisEngine.Simulation"/>.
/// </summary>
internal sealed class SimulationCatalog
{
    public const string SegmentationModel = "simulation/sam-fixture";
    public const string ReasoningModel = "simulation/muse-fixture";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, Scenario> _byHash;
    private readonly Scenario _generic;

    public SimulationCatalog()
    {
        var assembly = typeof(SimulationCatalog).Assembly;
        var scenarios = assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Simulation.Scenarios.", StringComparison.Ordinal))
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                return JsonSerializer.Deserialize<Scenario>(stream, Json)
                    ?? throw new InvalidOperationException($"Escenario vacío: {n}");
            })
            .ToList();

        _generic = scenarios.Single(s => s.Sha256 == "*");
        _byHash = scenarios.Where(s => s.Sha256 != "*").ToDictionary(s => s.Sha256, StringComparer.OrdinalIgnoreCase);
    }

    public Scenario Find(string sha256) => _byHash.GetValueOrDefault(sha256) ?? _generic;

    internal sealed record Scenario(
        string Sha256, string Summary, List<ScenarioDistress> Distresses, List<ScenarioStep> Steps, ScenarioTranslation? En)
    {
        /// <summary>The scenario with its texts in <paramref name="language"/>; geometry and grades never change.</summary>
        public Scenario In(ReportLanguage language)
        {
            if (language != ReportLanguage.En || En is null)
            {
                return this;
            }

            return this with
            {
                Summary = En.Summary,
                Distresses = Distresses
                    .Select(d => En.Distresses.TryGetValue(d.Code, out var t)
                        ? d with { Diagnosis = t.Diagnosis, RecommendedAction = t.RecommendedAction }
                        : d)
                    .ToList(),
                Steps = Steps
                    .Select((s, i) => i < En.Steps.Count
                        ? s with { Title = En.Steps[i].Title, Instructions = En.Steps[i].Instructions, Tools = En.Steps[i].Tools }
                        : s)
                    .ToList()
            };
        }
    }

    /// <summary>English texts: findings by distress code, steps by position.</summary>
    internal sealed record ScenarioTranslation(
        string Summary, Dictionary<string, TranslatedDistress> Distresses, List<TranslatedStep> Steps);

    internal sealed record TranslatedDistress(string? Diagnosis, string? RecommendedAction);

    internal sealed record TranslatedStep(string Title, string Instructions, List<string> Tools);

    internal sealed record ScenarioDistress(
        string Concept, double Score, double[][] Polygon, string Code,
        DistressType Type, Severity Severity, string? Diagnosis, string? RecommendedAction);

    internal sealed record ScenarioStep(string Title, string Instructions, List<string> Tools, RiskLevel Risk, string? Target);
}

internal sealed class SimulatedSegmenter(SimulationCatalog catalog) : IDistressSegmenter
{
    public AnalysisEngine Engine => AnalysisEngine.Simulation;

    public Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
    {
        var scenario = catalog.Find(request.Image.Sha256);
        var segments = scenario.Distresses
            .Select((d, i) => new Segment(i, d.Concept, d.Score, d.Polygon.Select(p => new NormalizedPoint(p[0], p[1])).ToArray()))
            .ToList();
        return Task.FromResult(new SegmentationResult(SimulationCatalog.SegmentationModel, segments));
    }
}

internal sealed class SimulatedReasoner(SimulationCatalog catalog) : IDiagnosticReasoner
{
    public AnalysisEngine Engine => AnalysisEngine.Simulation;

    public Task<DiagnosisResult> DiagnoseAsync(DiagnosisRequest request, CancellationToken cancellationToken)
    {
        var scenario = catalog.Find(request.Image.Sha256).In(request.Language);
        var indexByCode = scenario.Distresses
            .Select((d, i) => (d.Code, Index: i))
            .ToDictionary(x => x.Code, x => x.Index, StringComparer.OrdinalIgnoreCase);

        // Like the real model, describe only the segments that were measured (the rest lie beyond the horizon).
        var measured = request.Segments.Select(m => m.Segment.Index).ToHashSet();
        var findings = scenario.Distresses
            .Select((d, i) => new DistressFinding(i, d.Code, d.Type, d.Severity, d.Diagnosis, d.RecommendedAction))
            .Where(f => measured.Contains(f.SegmentIndex))
            .ToList();
        var steps = scenario.Steps
            .Where(s => s.Target is null || measured.Contains(indexByCode[s.Target]))
            .Select((s, i) => new StepPlan(i + 1, s.Title, s.Instructions, s.Tools, s.Risk,
                s.Target is null ? null : indexByCode[s.Target]))
            .ToList();

        return Task.FromResult(new DiagnosisResult(SimulationCatalog.ReasoningModel, scenario.Summary, findings, steps));
    }
}

/// <summary>Answers from the stored diagnosis only, and says so.</summary>
internal sealed class SimulatedMaintenanceAssistant : IMaintenanceAssistant
{
    public AnalysisEngine Engine => AnalysisEngine.Simulation;

    public Task<string> AnswerAsync(AssistantRequest request, CancellationToken cancellationToken)
    {
        var inv = CultureInfo.InvariantCulture;
        var en = request.Inspection.Language == ReportLanguage.En;
        var text = new StringBuilder();
        text.AppendLine(en
            ? "[Simulation] Answer built from the stored diagnosis, without asking Muse Wonder."
            : "[Simulación] Respuesta generada a partir del diagnóstico guardado, sin consultar a Muse Wonder.");

        if (request.Distress is { } d)
        {
            var m = d.Measurement;
            text.AppendLine(en
                ? string.Create(inv, $"{d.Code} · {TypeLabel(d.Type, en)}, {SeverityLabel(d.Severity, en)} severity.")
                : string.Create(inv, $"{d.Code} · {TypeLabel(d.Type, en)}, severidad {SeverityLabel(d.Severity, en)}."));
            text.AppendLine(en
                ? string.Create(inv, $"Projected area {m.AreaM2:0.###} m², {m.LengthM:0.##} × {m.WidthM:0.##} m.")
                : string.Create(inv, $"Área proyectada {m.AreaM2:0.###} m², {m.LengthM:0.##} × {m.WidthM:0.##} m."));
            if (d.Diagnosis is not null) text.AppendLine(inv, $"{(en ? "Finding" : "Hallazgo")}: {d.Diagnosis}");
            if (d.RecommendedAction is not null) text.AppendLine(inv, $"{(en ? "Recommended action" : "Acción recomendada")}: {d.RecommendedAction}");
            if (d.Quantities is { } q)
            {
                var measured = q.DepthSource == DepthSource.Measured;
                text.AppendLine(en
                    ? string.Create(inv, $"Quantities: cut {q.CutAreaM2:0.###} m² × {q.DepthM * 100:0.#} cm ({(measured ? "measured" : "assumed from severity")}) = {q.VolumeM3:0.####} m³, {q.MassT:0.###} t of mix.")
                    : string.Create(inv, $"Cubicaje: corte {q.CutAreaM2:0.###} m² × {q.DepthM * 100:0.#} cm ({(measured ? "medida" : "supuesta por severidad")}) = {q.VolumeM3:0.####} m³, {q.MassT:0.###} t de mezcla."));
            }

            var steps = request.Inspection.RepairSteps.Where(s => s.TargetCode == d.Code).ToList();
            if (steps.Count > 0)
            {
                text.AppendLine(inv, $"{(en ? "Related steps" : "Pasos relacionados")}: {string.Join("; ", steps.Select(s => $"{s.Order}. {s.Title}"))}.");
            }
        }
        else
        {
            text.AppendLine(request.Inspection.Summary);
            if (request.Inspection.Pci is { } pci)
            {
                text.AppendLine(en
                    ? string.Create(inv, $"Approximate PCI {pci.Pci:0.#}.")
                    : string.Create(inv, $"PCI aproximado {pci.Pci:0.#} ({pci.Rating}): {pci.Treatment}."));
            }
        }

        text.Append(en
            ? "Add META_API_KEY to .env to get an answer to your specific question."
            : "Para responder a tu pregunta concreta añade META_API_KEY al .env.");
        return Task.FromResult(text.ToString());
    }

    internal static string TypeLabel(DistressType type, bool en = false) => en ? EnglishTypeLabel(type) : type switch
    {
        DistressType.Pothole => "bache",
        DistressType.AlligatorCrack => "piel de cocodrilo",
        DistressType.LongitudinalCrack => "fisura longitudinal",
        DistressType.TransverseCrack => "fisura transversal",
        DistressType.Rutting => "ahuellamiento",
        _ => "otro deterioro"
    };

    private static string EnglishTypeLabel(DistressType type) => type switch
    {
        DistressType.Pothole => "pothole",
        DistressType.AlligatorCrack => "alligator cracking",
        DistressType.LongitudinalCrack => "longitudinal crack",
        DistressType.TransverseCrack => "transverse crack",
        DistressType.Rutting => "rutting",
        _ => "other distress"
    };

    private static string SeverityLabel(Severity severity, bool en) => severity switch
    {
        Severity.High => en ? "high" : "alta",
        Severity.Medium => en ? "medium" : "media",
        _ => en ? "low" : "baja"
    };
}
