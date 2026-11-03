using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Geometry;
using PaveLens.Application.Geography;
using PaveLens.Application.Inspections;
using PaveLens.Application.Pavement;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Common;
using PaveLens.Domain.Images;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Orchestration;

/// <summary>One isolated, dependency-checked toolbox for a durable inspection.</summary>
internal sealed class InspectionToolSession(
    Inspection inspection, StoredImage image, ImagePayload payload,
    IDistressSegmenter segmenter, IDiagnosticReasoner reasoner, IInspectionAuditor auditor,
    TrafficPlanService mobility, CostEstimator costs, RegionalConfiguration profile,
    AnalysisOptions options, IUnitOfWork unitOfWork, IInspectionCoordinator coordinator,
    TimeProvider clock, IGeographicNetworkProvider geography, OrchestrationOptions limits)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly List<AgentActionTrace> _actions = [];
    // Each warning remembers the tool that raised it, so repeating a step drops the warnings it invalidates.
    private static readonly string[] Pipeline = ["segment_image", "measure_segments", "diagnose_distresses", "compute_report", "assess_mobility", "verify_report"];
    private readonly List<(string Tool, string Text)> _warnings = [];
    private string _tool = "";
    private readonly Guid _runId = Guid.NewGuid();
    private readonly QuantityEstimator _quantities = new(Options.Create(profile.Pavement));
    private readonly AgentRunTrace? _selection = inspection.Trace?.Orchestration;
    private SegmentationResult? _segmentation;
    private DiagnosisResult? _diagnosis;
    private readonly List<MeasuredSegment> _measured = [];
    private List<Distress> _distresses = [];
    private List<RepairStep> _steps = [];
    private string[] _concepts = options.SegmentationConcepts.ToArray();
    private double _area;
    private double _pci;
    private long _segmentationMs, _reasoningMs;
    private bool _regionLoaded, _computed, _mobilityDone, _verified, _finished;
    private string _reviewStatus = "NeedsReview";
    private string? _mobilityJson;
    private GeographicNetwork? _cityNetwork;
    private bool _cityAssessed;

    public async Task<AgentToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var agent = name switch
        {
            "load_region" => "RegionalContext", "load_city_network" => "GeographicContext", "segment_image" => "VisualInspector",
            "measure_segments" or "compute_report" => "EngineeringTools", "diagnose_distresses" => "DiagnosticEngineer",
            "assess_mobility" => "MobilityPlanner", "verify_report" => "IndependentReviewer", _ => "Coordinator"
        };
        var model = "deterministic-code";
        _tool = name;
        AgentToolResult result;
        try
        {
            Require(!_finished, "La ejecución ya terminó.");
            Require(arguments.ValueKind == JsonValueKind.Object, "Los argumentos deben ser un objeto JSON.");
            Require(arguments.EnumerateObject().All(p => name == "segment_image" && p.Name == "concepts"), "La herramienta recibió argumentos no permitidos.");
            switch (name)
            {
                case "load_region":
                    ResetWarningsFrom(name);
                    _regionLoaded = true;
                    if (!profile.JurisdictionValidated) Warn("Perfil regional de referencia; no acredita conformidad normativa local.");
                    if (!profile.PricesAvailable) Warn("Falta un libro de precios local. No se emitirá presupuesto para esta región.");
                    result = Ok(new { profile.Id, profile.Name, profile.Version, profile.CountryCode, profile.ConditionMethod, profile.JurisdictionValidated, profile.PricesAvailable, profile.Sources });
                    break;
                case "load_city_network":
                    Require(_regionLoaded, "Carga primero el perfil regional.");
                    ResetWarningsFrom(name);
                    if (_selection?.Latitude is { } latitude && _selection.Longitude is { } longitude)
                    {
                        try
                        {
                            _cityNetwork = await geography.LoadAsync(latitude, longitude, 600, cancellationToken);
                            result = Ok(new { status = "Loaded", _cityNetwork.Id, _cityNetwork.RetrievedAtUtc, streets = _cityNetwork.Edges.Count, _cityNetwork.Warnings });
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ModelUnavailableException or DomainException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Warn("La consulta geográfica no estuvo disponible; carga el mapa para revisar la red antes de planificar un cierre.");
                            result = Ok(new { status = "Unavailable", reason = ex.Message });
                        }
                    }
                    else result = Ok(new { status = "NeedsInput", required = "Coordenadas de la inspección para obtener una red real." });
                    _cityAssessed = true;
                    break;
                case "segment_image":
                    Require(_regionLoaded, "Ejecuta load_region antes de segmentar.");
                    var requestedConcepts = arguments.TryGetProperty("concepts", out var concepts)
                        ? concepts.EnumerateArray().Select(c => c.GetString() ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : options.SegmentationConcepts.ToArray();
                    Require(requestedConcepts.Length is > 0 and <= 8 && requestedConcepts.All(c => options.SegmentationConcepts.Contains(c, StringComparer.OrdinalIgnoreCase)), "Usa únicamente conceptos de deterioro habilitados en Analysis:SegmentationConcepts.");
                    _concepts = requestedConcepts;
                    ResetWarningsFrom(name);
                    _measured.Clear(); _diagnosis = null; _computed = false; _verified = false; _mobilityDone = false;
                    _segmentation = null;
                    _segmentation = await segmenter.SegmentAsync(new SegmentationRequest(payload, _concepts), cancellationToken);
                    _segmentationMs += timer.ElapsedMilliseconds;
                    model = _segmentation.Model;
                    Require(_segmentation.Segments.Count > 0, "El modelo de segmentación no encontró deterioros en la imagen.");
                    Require(_segmentation.Segments.Count <= limits.MaxSegments, "Hay demasiados segmentos para una evaluación verificable. Reduce el área de la fotografía.");
                    Require(_segmentation.Segments.Select(s => s.Index).Distinct().Count() == _segmentation.Segments.Count, "La segmentación repitió identificadores.");
                    _measured.Clear(); _diagnosis = null; _computed = false; _verified = false; _mobilityDone = false;
                    result = Ok(new { model, concepts = _concepts, masks = _segmentation.Segments.Count });
                    break;
                case "measure_segments":
                    Require(_segmentation is not null, "Ejecuta segment_image primero.");
                    ResetWarningsFrom(name);
                    _diagnosis = null; _computed = false; _verified = false; _mobilityDone = false;
                    Advance(AnalysisStage.Measuring);
                    var projection = GroundProjection.Create(inspection.ReferenceWidthM, inspection.Calibration, (double)image.Height / image.Width);
                    _measured.Clear();
                    foreach (var s in _segmentation!.Segments.Where(s => projection.Covers(s.Polygon)))
                        _measured.Add(new MeasuredSegment(s with { Polygon = PolygonSimplifier.Simplify(s.Polygon, options.MaskSimplificationTolerance) }, PolygonMetrics.Measure(s.Polygon, projection)));
                    Require(_measured.Count > 0, "Todos los deterioros quedan sobre el horizonte de la calibración.");
                    _area = projection.SurveyedAreaM2;
                    _diagnosis = null; _computed = false; _verified = false; _mobilityDone = false;
                    result = Ok(new { surveyedAreaM2 = _area, skippedBeyondHorizon = _segmentation.Segments.Count - _measured.Count, segments = _measured.Select(m => new { m.Segment.Index, m.Measurement }) });
                    break;
                case "diagnose_distresses":
                    Require(_regionLoaded && _measured.Count > 0, "Se requieren perfil regional y medidas.");
                    ResetWarningsFrom(name);
                    _diagnosis = null; _computed = false; _verified = false; _mobilityDone = false;
                    Advance(AnalysisStage.Reasoning);
                    await SaveProgressAsync(cancellationToken);
                    _diagnosis = await reasoner.DiagnoseAsync(new DiagnosisRequest(payload, inspection.Title, inspection.Notes, inspection.Location, _measured, inspection.Language,
                        Serialize(new { profile.Name, profile.CountryCode, profile.ConditionMethod, profile.JurisdictionValidated, profile.Sources, profile.Pavement.Region })), cancellationToken);
                    _reasoningMs += timer.ElapsedMilliseconds;
                    model = _diagnosis.Model;
                    ValidateDiagnosis(_diagnosis);
                    _computed = false; _verified = false; _mobilityDone = false;
                    result = Ok(new { model, _diagnosis.Summary, findings = _diagnosis.Findings, _diagnosis.Attempts });
                    break;
                case "compute_report":
                    Require(_diagnosis is not null, "Ejecuta diagnose_distresses primero.");
                    ResetWarningsFrom(name);
                    _computed = false; _verified = false; _mobilityDone = false;
                    ValidateDiagnosis(_diagnosis!);
                    Advance(AnalysisStage.Computing);
                    var byIndex = _measured.ToDictionary(m => m.Segment.Index);
                    _distresses = _diagnosis!.Findings.Select(f =>
                    {
                        var m = byIndex[f.SegmentIndex];
                        return Distress.Create(f.Code, f.Type, f.Severity, m.Segment.Score, m.Segment.Polygon,
                            _segmentation!.Segments.Single(s => s.Index == f.SegmentIndex).Polygon.Count, m.Measurement,
                            Distress.IsPatchableType(f.Type) ? _quantities.Estimate(m.Measurement, _quantities.AssumedDepthM(f.Severity), DepthSource.Assumed) : null,
                            m.Segment.Concept, f.Diagnosis, f.RecommendedAction);
                    }).ToList();
                    var pci = PciCalculator.Compute(InspectionMapper.PciInputs(_distresses), _area);
                    _pci = pci.Pci;
                    var codes = _diagnosis.Findings.ToDictionary(f => f.SegmentIndex, f => f.Code);
                    _steps = _diagnosis.Steps.OrderBy(s => s.Order).Select((s, i) => RepairStep.Create(i + 1, s.Title, s.Instructions, s.Tools, s.Risk,
                        s.TargetSegmentIndex is { } index ? codes[index] : null)).ToList();
                    if (pci.IsPartial) Warn("Muestra parcial: el índice no representa una unidad de muestreo completa.");
                    if (_distresses.Any(d => d.Quantities?.DepthSource == DepthSource.Assumed)) Warn("Profundidad supuesta: mide en campo para confirmar severidad, cantidades y presupuesto.");
                    if (inspection.Calibration is null) Warn("La medición supone una fotografía cenital y el ancho visible indicado.");
                    if (_measured.Count != _segmentation!.Segments.Count) Warn("Se excluyeron máscaras fuera del horizonte de calibración.");
                    if (_concepts.Length < options.SegmentationConcepts.Count) Warn("La búsqueda visual no incluyó todos los tipos de deterioro habilitados.");
                    _computed = true; _verified = false; _mobilityDone = false;
                    await SaveProgressAsync(cancellationToken);
                    result = Ok(new { pci, quantities = _distresses.Select(d => new { d.Code, d.Quantities }), budget = profile.PricesAvailable ? costs.Estimate(Draft()) : null });
                    break;
                case "assess_mobility":
                    Require(_computed && _cityAssessed, "Calcula el informe y ejecuta load_city_network antes de evaluar movilidad.");
                    ResetWarningsFrom(name);
                    if (profile.Id == "pe-puno" && !string.IsNullOrWhiteSpace(inspection.RoadSegmentId))
                    {
                        var plan = await mobility.PlanAsync(new SimulateWorkZoneCommand(inspection.RoadSegmentId, 1, Language: inspection.Language), cancellationToken);
                        _mobilityJson = Serialize(plan); model = plan.Model;
                        Warn("Movilidad calculada sobre la red y demanda sintéticas de Puno; no representa aforos reales.");
                        result = Ok(new { plan.Engine, plan.Model, plan.Summary, plan.Simulation.UserCostPen, plan.Simulation.RecommendedPeriod, routes = plan.Routes.Count, dataSource = "PunoDemoNetwork: synthetic geometry and demand" });
                    }
                    else
                    {
                        Warn("No hay red y demanda validadas asignadas a esta inspección. El mapa geográfico permite explorar cierres sin afirmar congestión real.");
                        result = Ok(new { status = "NeedsInput", networkId = _cityNetwork?.Id, required = "Seleccionar una calle y aportar demanda de tráfico local para cuantificar congestión." });
                    }
                    _mobilityDone = true; _verified = false;
                    break;
                case "verify_report":
                    Require(_computed && _mobilityDone, "Se requieren informe calculado y evaluación de movilidad.");
                    Require(double.IsFinite(_area) && _area > 0 && double.IsFinite(_pci) && _pci is >= 0 and <= 100, "Las medidas o el índice no son válidos.");
                    ResetWarningsFrom(name);
                    var audit = await auditor.ReviewAsync(Serialize(new
                    {
                        language = inspection.Language.ToString(), profile = new { profile.Name, profile.Version, profile.JurisdictionValidated, profile.Sources },
                        findings = _diagnosis!.Findings, measurements = _measured.Select(m => new { m.Segment.Index, m.Measurement }),
                        pci = _pci, surveyedAreaM2 = _area, budget = profile.PricesAvailable ? costs.Estimate(Draft()) : null, warnings = Warnings,
                        mobility = _mobilityJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(_mobilityJson)
                    }), cancellationToken);
                    foreach (var issue in audit.Issues) Warn(issue);
                    model = audit.Model;
                    _reviewStatus = _warnings.Count > 0 || audit.Status != "Ready" ? "NeedsReview" : "Ready";
                    _verified = true;
                    result = Ok(new { status = _reviewStatus, issues = Warnings, numericChecks = "Passed: geometry, coverage, quantities and PCI are computed and range checked by code." });
                    break;
                case "finish":
                    Require(_verified, "Ejecuta verify_report antes de terminar.");
                    _finished = true;
                    result = Ok(new { status = _reviewStatus });
                    break;
                default:
                    result = new(false, "Herramienta no permitida.");
                    break;
            }
        }
        catch (Exception ex) when (ex is DomainException or ModelOutputException or InvalidOperationException or JsonException)
        {
            result = new(false, ex.Message.Length > 1500 ? ex.Message[..1500] : ex.Message);
        }
        _actions.Add(new AgentActionTrace(agent, name, model, result.Success ? "Succeeded" : "Rejected", timer.ElapsedMilliseconds, result.Evidence));
        await SaveProgressAsync(cancellationToken);
        return result;
    }

    public async Task CompleteAsync(AgentUsage usage, CancellationToken cancellationToken)
    {
        Require(_finished && _verified && _computed && _diagnosis is not null && _segmentation is not null, "La orquestación no terminó todas las verificaciones.");
        var engine = segmenter.Engine == AnalysisEngine.Meta && reasoner.Engine == AnalysisEngine.Meta && coordinator.Engine == "Meta" ? AnalysisEngine.Meta : AnalysisEngine.Simulation;
        var skipped = _segmentation!.Segments.Count - _measured.Count;
        var summary = _diagnosis!.Summary;
        if (skipped > 0) summary += inspection.Language == ReportLanguage.En
            ? $" {skipped} distresses beyond the calibration horizon were excluded from measurement and PCI."
            : $" {skipped} deterioros más allá del horizonte de calibración se excluyeron de las medidas y del PCI.";
        inspection.Complete(engine, _segmentation.Model, _diagnosis.Model, summary,
            _distresses, _steps, _area, _pci, Trace(_reviewStatus, usage), clock.GetUtcNow().UtcDateTime, _watch.ElapsedMilliseconds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private InspectionDetailDto Draft() => InspectionMapper.ToDetail(inspection, image) with
    {
        Distresses = _distresses.Select(InspectionMapper.ToDto).ToArray(), SurveyedAreaM2 = _area
    };

    private AnalysisTrace Trace(string status, AgentUsage? usage = null) => new(
        _concepts.Select(c => new ConceptTrace(c, _segmentation?.Segments.Count(s => s.Concept.Equals(c, StringComparison.OrdinalIgnoreCase)) ?? 0)).ToArray(),
        _segmentationMs, _reasoningMs, _diagnosis?.Attempts ?? 1, (_segmentation?.Segments.Count ?? 0) - _measured.Count,
        new AgentRunTrace(_runId, coordinator.Engine, coordinator.Model, status, RegionalCatalog.Snapshot(profile), _selection?.Latitude, _selection?.Longitude,
            _actions.ToArray(), Warnings, usage?.Calls ?? 0, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0, _mobilityJson));

    private async Task SaveProgressAsync(CancellationToken cancellationToken)
    {
        inspection.RecordTrace(Trace("Running"));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    private void ValidateDiagnosis(DiagnosisResult diagnosis)
    {
        Require(!string.IsNullOrWhiteSpace(diagnosis.Summary), "El diagnóstico no incluye resumen.");
        var expected = _measured.Select(m => m.Segment.Index).ToHashSet();
        Require(diagnosis.Findings.Count == expected.Count && diagnosis.Findings.Select(f => f.SegmentIndex).ToHashSet().SetEquals(expected), "El diagnóstico debe describir exactamente una vez cada segmento medido.");
        Require(diagnosis.Findings.Select(f => f.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() == diagnosis.Findings.Count, "El diagnóstico contiene códigos repetidos.");
        Require(diagnosis.Steps.All(s => s.TargetSegmentIndex is null || expected.Contains(s.TargetSegmentIndex.Value)), "Un paso apunta a un segmento desconocido.");
    }
    private void Advance(AnalysisStage stage) { if (inspection.Stage is null || inspection.Stage < stage) inspection.AdvanceTo(stage); }
    private string[] Warnings => _warnings.Select(w => w.Text).ToArray();
    private void Warn(string warning) { if (!_warnings.Exists(w => w.Text == warning)) _warnings.Add((_tool, warning)); }

    /// <summary>Drops the warnings of <paramref name="tool"/> and, for a pipeline step, of every step that depends on it.</summary>
    private void ResetWarningsFrom(string tool)
    {
        var from = Array.IndexOf(Pipeline, tool);
        _warnings.RemoveAll(w => w.Tool == tool || (from >= 0 && Array.IndexOf(Pipeline, w.Tool) >= from));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ModelOutputException(message); }
    private static AgentToolResult Ok(object evidence) => new(true, Serialize(evidence));
    private static string Serialize(object evidence) => JsonSerializer.Serialize(evidence, Json);
}
