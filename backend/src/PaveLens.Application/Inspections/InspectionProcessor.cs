using System.Diagnostics;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Geometry;
using PaveLens.Application.Pavement;
using PaveLens.Domain.Common;
using PaveLens.Domain.Inspections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaveLens.Application.Inspections;

/// <summary>
/// Runs the analysis pipeline for one inspection:
/// image → segmentation (SAM) → RDP → ground projection and metrics → diagnosis (Muse) → quantities and PCI → persisted result.
/// Any failure marks the inspection as Failed with a reason; no substitute data is produced.
/// </summary>
public sealed partial class InspectionProcessor(
    IInspectionRepository inspections,
    IImageRepository images,
    IImageStorage storage,
    IDistressSegmenter segmenter,
    IDiagnosticReasoner reasoner,
    QuantityEstimator quantityEstimator,
    IUnitOfWork unitOfWork,
    IOptions<AnalysisOptions> options,
    TimeProvider clock,
    ILogger<InspectionProcessor> logger)
{
    private readonly AnalysisOptions _options = options.Value;

    public async Task ProcessAsync(Guid inspectionId, CancellationToken cancellationToken)
    {
        var inspection = await inspections.GetAsync(inspectionId, cancellationToken);
        if (inspection is null || inspection.Status != InspectionStatus.Pending)
        {
            LogSkipped(inspectionId, inspection?.Status);
            return;
        }

        inspection.BeginProcessing(clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await RunPipelineAsync(inspection, stopwatch, cancellationToken);
            LogCompleted(inspectionId, inspection.Distresses.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: leave it Processing; the worker resets interrupted inspections on start.
            throw;
        }
#pragma warning disable CA1031 // Any pipeline error must end as a visible Failed state, never a stuck one.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex, inspectionId);
            inspection.Fail(Describe(ex), clock.GetUtcNow().UtcDateTime);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RunPipelineAsync(Inspection inspection, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var image = await images.GetAsync(inspection.ImageId, cancellationToken)
            ?? throw new DomainException("La imagen de la inspección no existe.");
        var bytes = await storage.ReadAllBytesAsync(image.StorageKey, cancellationToken);
        var payload = new ImagePayload(bytes, image.ContentType, image.Width, image.Height, image.Sha256);

        var segmentationStarted = stopwatch.ElapsedMilliseconds;
        var segmentation = await segmenter.SegmentAsync(new SegmentationRequest(payload, _options.SegmentationConcepts), cancellationToken);
        var segmentationMs = stopwatch.ElapsedMilliseconds - segmentationStarted;
        if (segmentation.Segments.Count == 0)
        {
            throw new ModelOutputException("El modelo de segmentación no encontró deterioros en la imagen.");
        }

        inspection.AdvanceTo(AnalysisStage.Measuring);

        // Every vertex of the full-resolution mask is projected to the road plane and measured there.
        // Simplification is for storage and display only: a pixel tolerance costs more metres far from the
        // camera, so measuring the simplified outline would bias distant distresses low.
        var projection = GroundProjection.Create(inspection.ReferenceWidthM, inspection.Calibration, (double)image.Height / image.Width);
        var measured = new List<MeasuredSegment>();
        foreach (var s in segmentation.Segments)
        {
            // An oblique photo shows road beyond the calibrated plane's horizon, where metres are undefined.
            // A mask that reaches it cannot be measured; it is left out instead of failing the whole photo.
            if (!projection.Covers(s.Polygon))
            {
                LogSegmentBeyondHorizon(inspection.Id, s.Index, s.Concept);
                continue;
            }

            measured.Add(new MeasuredSegment(
                s with { Polygon = PolygonSimplifier.Simplify(s.Polygon, _options.MaskSimplificationTolerance) },
                PolygonMetrics.Measure(s.Polygon, projection)));
        }

        if (measured.Count == 0)
        {
            throw new GroundProjectionException("Todos los deterioros quedan sobre el horizonte de la calibración.");
        }

        var skipped = segmentation.Segments.Count - measured.Count;
        var rawVertexCounts = segmentation.Segments.ToDictionary(s => s.Index, s => s.Polygon.Count);

        // Segmentation and reasoning are the slow steps: commit the stage so polling clients see it.
        inspection.AdvanceTo(AnalysisStage.Reasoning);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var reasoningStarted = stopwatch.ElapsedMilliseconds;
        var diagnosis = await reasoner.DiagnoseAsync(
            new DiagnosisRequest(payload, inspection.Title, inspection.Notes, inspection.Location, measured, inspection.Language), cancellationToken);
        var reasoningMs = stopwatch.ElapsedMilliseconds - reasoningStarted;
        ValidateDiagnosis(diagnosis, measured.Select(m => m.Segment).ToList());
        inspection.AdvanceTo(AnalysisStage.Computing);

        var segmentsByIndex = measured.ToDictionary(m => m.Segment.Index);
        var distresses = diagnosis.Findings
            .Select(f =>
            {
                var (segment, measurement) = (segmentsByIndex[f.SegmentIndex].Segment, segmentsByIndex[f.SegmentIndex].Measurement);
                var quantities = Distress.IsPatchableType(f.Type)
                    ? quantityEstimator.Estimate(measurement, quantityEstimator.AssumedDepthM(f.Severity), DepthSource.Assumed)
                    : null;
                return Distress.Create(
                    f.Code,
                    f.Type,
                    f.Severity,
                    segment.Score,
                    segment.Polygon,
                    rawVertexCounts[segment.Index],
                    measurement,
                    quantities,
                    segment.Concept,
                    f.Diagnosis,
                    f.RecommendedAction);
            })
            .ToList();

        var pci = PciCalculator.Compute(InspectionMapper.PciInputs(distresses), projection.SurveyedAreaM2);

        var codeBySegment = diagnosis.Findings.ToDictionary(f => f.SegmentIndex, f => f.Code);
        var steps = diagnosis.Steps
            .OrderBy(s => s.Order)
            .Select((s, i) => RepairStep.Create(
                i + 1,
                s.Title,
                s.Instructions,
                s.Tools,
                s.Risk,
                s.TargetSegmentIndex is { } index ? codeBySegment.GetValueOrDefault(index) : null))
            .ToList();

        var engine = segmenter.Engine == AnalysisEngine.Meta && reasoner.Engine == AnalysisEngine.Meta
            ? AnalysisEngine.Meta
            : AnalysisEngine.Simulation;

        var summary = skipped == 0
            ? diagnosis.Summary
            : inspection.Language == ReportLanguage.En
                ? skipped == 1
                    ? $"{diagnosis.Summary} 1 distress detected beyond the calibration horizon was not measured and does not count in the PCI."
                    : $"{diagnosis.Summary} {skipped} distresses detected beyond the calibration horizon were not measured and do not count in the PCI."
                : skipped == 1
                    ? $"{diagnosis.Summary} 1 deterioro detectado más allá del horizonte de la calibración no se midió ni cuenta en el PCI."
                    : $"{diagnosis.Summary} {skipped} deterioros detectados más allá del horizonte de la calibración no se midieron ni cuentan en el PCI.";

        var trace = new AnalysisTrace(
            ConceptTraces(_options.SegmentationConcepts, segmentation.Segments),
            segmentationMs,
            reasoningMs,
            diagnosis.Attempts,
            skipped);

        inspection.Complete(
            engine,
            segmentation.Model,
            diagnosis.Model,
            summary,
            distresses,
            steps,
            projection.SurveyedAreaM2,
            pci.Pci,
            trace,
            clock.GetUtcNow().UtcDateTime,
            stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Every prompted concept with the masks it produced, including those that found nothing.</summary>
    private static List<ConceptTrace> ConceptTraces(IReadOnlyList<string> concepts, IReadOnlyList<Segment> segments)
    {
        var counts = segments
            .GroupBy(s => s.Concept, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var prompted = concepts.Select(c => new ConceptTrace(c, counts.GetValueOrDefault(c)));
        var unprompted = counts.Keys
            .Where(k => !concepts.Contains(k, StringComparer.OrdinalIgnoreCase))
            .Select(k => new ConceptTrace(k, counts[k]));
        return [.. prompted, .. unprompted];
    }

    private static void ValidateDiagnosis(DiagnosisResult diagnosis, IReadOnlyList<Segment> segments)
    {
        var indexes = segments.Select(s => s.Index).ToHashSet();

        if (string.IsNullOrWhiteSpace(diagnosis.Summary))
        {
            throw new ModelOutputException("El diagnóstico no incluye resumen.");
        }

        if (diagnosis.Findings.Count == 0)
        {
            throw new ModelOutputException("El diagnóstico no describe ningún deterioro.");
        }

        var unknown = diagnosis.Findings.FirstOrDefault(f => !indexes.Contains(f.SegmentIndex));
        if (unknown is not null)
        {
            throw new ModelOutputException($"El diagnóstico se refiere al segmento {unknown.SegmentIndex}, que no existe.");
        }

        if (diagnosis.Findings.Select(f => f.SegmentIndex).Distinct().Count() != diagnosis.Findings.Count)
        {
            throw new ModelOutputException("El diagnóstico describe el mismo segmento más de una vez.");
        }

        var badStep = diagnosis.Steps.FirstOrDefault(s =>
            s.TargetSegmentIndex is { } i && diagnosis.Findings.All(f => f.SegmentIndex != i));
        if (badStep is not null)
        {
            throw new ModelOutputException($"El paso \"{badStep.Title}\" apunta a un segmento sin diagnóstico.");
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        GroundProjectionException => $"Calibración no válida: {ex.Message}",
        ModelOutputException or DomainException => $"Respuesta del modelo no válida: {ex.Message}",
        ModelUnavailableException or HttpRequestException or TimeoutException => $"Servicio de IA no disponible: {ex.Message}",
        IOException => $"Error de almacenamiento: {ex.Message}",
        // Unexpected errors keep their details in the logs only.
        _ => "Error interno durante el análisis."
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection {InspectionId} skipped (status {Status})")]
    private partial void LogSkipped(Guid inspectionId, InspectionStatus? status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection {InspectionId} completed with {DistressCount} distresses in {ElapsedMs} ms")]
    private partial void LogCompleted(Guid inspectionId, int distressCount, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inspection {InspectionId}: segment {SegmentIndex} ({Concept}) is beyond the calibration horizon and was skipped")]
    private partial void LogSegmentBeyondHorizon(Guid inspectionId, int segmentIndex, string concept);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inspection {InspectionId} failed")]
    private partial void LogFailed(Exception exception, Guid inspectionId);
}
