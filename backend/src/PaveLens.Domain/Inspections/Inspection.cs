using PaveLens.Domain.Common;

namespace PaveLens.Domain.Inspections;

/// <summary>
/// Aggregate root: one photographed stretch of pavement, its analysis lifecycle and the resulting distresses.
/// </summary>
public sealed class Inspection : Entity
{
    /// <summary>Default visible road width when the user gives none: one 3.5 m lane.</summary>
    public const double DefaultReferenceWidthM = 3.5;

    private readonly List<Distress> _distresses = [];
    private readonly List<RepairStep> _repairSteps = [];

    private Inspection()
    {
        // EF Core
    }

    public string Title { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    /// <summary>Free-text place (street, kilometre post).</summary>
    public string? Location { get; private set; }

    /// <summary>Id of the road network edge this photo belongs to, used by the traffic module.</summary>
    public string? RoadSegmentId { get; private set; }

    /// <summary>Ground width spanned by the full image width, for nadir photos without calibration.</summary>
    public double ReferenceWidthM { get; private set; }

    /// <summary>Optional 4-point calibration for oblique photos; takes precedence over <see cref="ReferenceWidthM"/>.</summary>
    public GroundCalibration? Calibration { get; private set; }

    /// <summary>Language of the summary, findings and repair protocol written by the reasoner.</summary>
    public ReportLanguage Language { get; private set; }

    public Guid ImageId { get; private set; }
    public InspectionStatus Status { get; private set; }

    /// <summary>Pipeline step while <see cref="InspectionStatus.Processing"/>; null otherwise.</summary>
    public AnalysisStage? Stage { get; private set; }

    /// <summary>What each model was asked and how long it took; set when the analysis completes.</summary>
    public AnalysisTrace? Trace { get; private set; }

    public AnalysisEngine? Engine { get; private set; }
    public string? SegmentationModel { get; private set; }
    public string? ReasoningModel { get; private set; }
    public string? Summary { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>Pavement area covered by the evaluation, in m² (denominator of the distress densities).</summary>
    public double? SurveyedAreaM2 { get; private set; }

    /// <summary>ASTM D6433 condition index computed from the distresses (partial when the area is below a sample unit).</summary>
    public double? Pci { get; private set; }

    public int Attempts { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public long? ProcessingTimeMs { get; private set; }

    /// <summary>Optimistic concurrency token, rotated on every mutation.</summary>
    public Guid Version { get; private set; }

    public IReadOnlyList<Distress> Distresses => _distresses;
    public IReadOnlyList<RepairStep> RepairSteps => _repairSteps;

    public static Inspection Create(
        string title,
        string? notes,
        string? location,
        string? roadSegmentId,
        Guid imageId,
        double? referenceWidthM,
        GroundCalibration? calibration,
        ReportLanguage language,
        DateTime nowUtc)
    {
        if (imageId == Guid.Empty)
        {
            throw new DomainException("La inspección necesita una imagen.");
        }

        return new Inspection
        {
            Title = Guard.NotBlank(title, "Title", 200),
            Notes = Guard.Optional(notes, "Notes", 2000),
            Location = Guard.Optional(location, "Location", 200),
            RoadSegmentId = Guard.Optional(roadSegmentId, "RoadSegmentId", 64),
            ReferenceWidthM = Guard.InRange(referenceWidthM ?? DefaultReferenceWidthM, 0.5, 100, "ReferenceWidthM"),
            Calibration = calibration,
            Language = language,
            ImageId = imageId,
            Status = InspectionStatus.Pending,
            CreatedAt = nowUtc,
            Version = Guid.NewGuid()
        };
    }

    public void BeginProcessing(DateTime nowUtc)
    {
        if (Status != InspectionStatus.Pending)
        {
            throw new DomainException($"Solo se puede procesar una inspección pendiente (estado actual: {Status}).");
        }

        Status = InspectionStatus.Processing;
        Stage = AnalysisStage.Segmenting;
        StartedAt = nowUtc;
        FailureReason = null;
        Attempts++;
        Touch();
    }

    public void RecordTrace(AnalysisTrace trace)
    {
        if (Status is not (InspectionStatus.Pending or InspectionStatus.Processing))
            throw new DomainException("Solo se puede registrar progreso durante el análisis.");
        Trace = trace;
        Touch();
    }

    /// <summary>Records the pipeline step now running, so clients can show real progress.</summary>
    public void AdvanceTo(AnalysisStage stage)
    {
        EnsureProcessing();
        if (Stage is { } current && stage <= current)
        {
            throw new DomainException($"El análisis no puede volver de {current} a {stage}.");
        }

        Stage = stage;
        Touch();
    }

    public void Complete(
        AnalysisEngine engine,
        string segmentationModel,
        string reasoningModel,
        string summary,
        IEnumerable<Distress> distresses,
        IEnumerable<RepairStep> repairSteps,
        double surveyedAreaM2,
        double pci,
        AnalysisTrace trace,
        DateTime nowUtc,
        long processingTimeMs)
    {
        EnsureProcessing();

        var distressList = distresses.ToList();
        var duplicated = distressList
            .GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            throw new DomainException($"El código {duplicated.Key} aparece más de una vez.");
        }

        var codes = distressList.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var steps = repairSteps.OrderBy(s => s.Order).ToList();
        var orphan = steps.FirstOrDefault(s => s.TargetCode is not null && !codes.Contains(s.TargetCode));
        if (orphan is not null)
        {
            throw new DomainException($"El paso \"{orphan.Title}\" apunta a {orphan.TargetCode}, que no existe.");
        }

        _distresses.Clear();
        _distresses.AddRange(distressList);
        _repairSteps.Clear();
        _repairSteps.AddRange(steps);

        Engine = engine;
        SegmentationModel = Guard.NotBlank(segmentationModel, "SegmentationModel", 100);
        ReasoningModel = Guard.NotBlank(reasoningModel, "ReasoningModel", 100);
        Summary = Guard.NotBlank(summary, "Summary", 4000);
        SurveyedAreaM2 = Guard.InRange(surveyedAreaM2, 0.01, 1_000_000, "SurveyedAreaM2");
        Pci = Guard.InRange(pci, 0, 100, "Pci");
        Trace = trace;
        Status = InspectionStatus.Completed;
        Stage = null;
        CompletedAt = nowUtc;
        ProcessingTimeMs = processingTimeMs;
        Touch();
    }

    public void Fail(string reason, DateTime nowUtc)
    {
        EnsureProcessing();
        Status = InspectionStatus.Failed;
        Stage = null;
        FailureReason = Guard.NotBlank(reason, "FailureReason", 1000);
        CompletedAt = nowUtc;
        Touch();
    }

    /// <summary>Puts a failed inspection back in the queue.</summary>
    public void Retry()
    {
        if (Status != InspectionStatus.Failed)
        {
            throw new DomainException("Solo se pueden reintentar inspecciones fallidas.");
        }

        Status = InspectionStatus.Pending;
        FailureReason = null;
        CompletedAt = null;
        Touch();
    }

    /// <summary>Recovers an inspection whose processing was interrupted (e.g. by a restart).</summary>
    public void ResetInterrupted()
    {
        if (Status == InspectionStatus.Processing)
        {
            Status = InspectionStatus.Pending;
            Stage = null;
            Touch();
        }
    }

    /// <summary>Replaces the depth of a patchable distress (e.g. measured in the field) and its recomputed quantities.</summary>
    public void UpdateDistressDepth(Guid distressId, Severity severity, DistressQuantities quantities, double pci)
    {
        EnsureCompleted();
        var distress = _distresses.FirstOrDefault(d => d.Id == distressId)
            ?? throw new DomainException("El deterioro no pertenece a esta inspección.");
        distress.UpdateDepth(severity, quantities);
        Pci = Guard.InRange(pci, 0, 100, "Pci");
        if (Trace is { Orchestration: { } run } trace)
            Trace = new AnalysisTrace(trace.Concepts, trace.SegmentationMs, trace.ReasoningMs, trace.ReasoningAttempts, trace.SkippedBeyondHorizon,
                run with { Status = "NeedsReview", Warnings = run.Warnings.Append("Se modificaron medidas de campo después de la auditoría. Las evidencias de ejecución corresponden al cálculo original.").Distinct(StringComparer.Ordinal).ToArray() });
        Touch();
    }

    public void SetStepCompletion(Guid stepId, bool completed, DateTime nowUtc)
    {
        EnsureCompleted();
        var step = _repairSteps.FirstOrDefault(s => s.Id == stepId)
            ?? throw new DomainException("El paso no pertenece a esta inspección.");
        step.SetCompleted(completed, nowUtc);
        Touch();
    }

    private void EnsureCompleted()
    {
        if (Status != InspectionStatus.Completed)
        {
            throw new DomainException("La inspección todavía no tiene resultados.");
        }
    }

    private void EnsureProcessing()
    {
        if (Status != InspectionStatus.Processing)
        {
            throw new DomainException($"La inspección no está en proceso (estado actual: {Status}).");
        }
    }

    private void Touch() => Version = Guid.NewGuid();
}
