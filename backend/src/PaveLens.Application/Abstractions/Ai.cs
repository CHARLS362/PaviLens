using PaveLens.Application.Inspections;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Abstractions;

public sealed record ImagePayload(ReadOnlyMemory<byte> Bytes, string ContentType, int Width, int Height, string Sha256);

// ---------- Segmentation (SAM 3) ----------

public sealed record SegmentationRequest(ImagePayload Image, IReadOnlyList<string> Concepts);

/// <summary>One mask returned by the segmentation model, outline in normalized coordinates.</summary>
public sealed record Segment(int Index, string Concept, double Score, IReadOnlyList<NormalizedPoint> Polygon);

public sealed record SegmentationResult(string Model, IReadOnlyList<Segment> Segments);

public interface IDistressSegmenter
{
    AnalysisEngine Engine { get; }
    Task<SegmentationResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken);
}

// ---------- Diagnosis (Muse Wonder) ----------

/// <summary>A segment plus its ground measurement, so the model reasons with metres, not pixels.</summary>
public sealed record MeasuredSegment(Segment Segment, DistressMeasurement Measurement);

public sealed record DiagnosisRequest(
    ImagePayload Image,
    string Title,
    string? Notes,
    string? Location,
    IReadOnlyList<MeasuredSegment> Segments,
    ReportLanguage Language = ReportLanguage.Es,
    string? RegionalContext = null);

public sealed record DistressFinding(
    int SegmentIndex,
    string Code,
    DistressType Type,
    Severity Severity,
    string? Diagnosis,
    string? RecommendedAction);

public sealed record StepPlan(
    int Order,
    string Title,
    string Instructions,
    IReadOnlyList<string> Tools,
    RiskLevel Risk,
    int? TargetSegmentIndex);

/// <param name="Attempts">Model calls needed: 1, or 2 when the first answer failed validation and was self-repaired.</param>
public sealed record DiagnosisResult(
    string Model,
    string Summary,
    IReadOnlyList<DistressFinding> Findings,
    IReadOnlyList<StepPlan> Steps,
    int Attempts = 1);

public interface IDiagnosticReasoner
{
    AnalysisEngine Engine { get; }
    Task<DiagnosisResult> DiagnoseAsync(DiagnosisRequest request, CancellationToken cancellationToken);
}

// ---------- Follow-up assistant ----------

public sealed record AssistantRequest(
    ImagePayload Image,
    InspectionDetailDto Inspection,
    DistressDto? Distress,
    IReadOnlyList<ConversationMessageDto> History,
    string Question);

public interface IMaintenanceAssistant
{
    AnalysisEngine Engine { get; }
    Task<string> AnswerAsync(AssistantRequest request, CancellationToken cancellationToken);
}

// ---------- Work-zone traffic plan (Muse Wonder) ----------

/// <summary>The solved closure plus the code-computed candidate detours the model must choose from.</summary>
public sealed record TrafficPlanRequest(
    TrafficSimulationDto Simulation, RoadNetwork Network, IReadOnlyList<DetourRouteDto> Candidates,
    ReportLanguage Language = ReportLanguage.Es);

/// <param name="Nodes">Ordered intersections; consecutive ones must share an open segment of the network.</param>
public sealed record PlannedRoute(string Id, string Name, string Vehicles, IReadOnlyList<string> Nodes, string Rationale);

public sealed record PlannedSign(string NodeId, string Kind, string Message);

public sealed record PlannedPhase(int Order, string Title, string When, string Description);

public sealed record TrafficPlanResult(
    string Model,
    string Summary,
    IReadOnlyList<PlannedRoute> Routes,
    IReadOnlyList<PlannedSign> Signage,
    IReadOnlyList<PlannedPhase> Phases,
    IReadOnlyList<string> Communication,
    IReadOnlyList<string> Risks);

public interface ITrafficPlanner
{
    AnalysisEngine Engine { get; }
    Task<TrafficPlanResult> PlanAsync(TrafficPlanRequest request, CancellationToken cancellationToken);
}
