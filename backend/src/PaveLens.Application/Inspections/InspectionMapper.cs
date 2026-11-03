using PaveLens.Application.Pavement;
using PaveLens.Domain.Images;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Inspections;

public static class InspectionMapper
{
    public static InspectionDetailDto ToDetail(Inspection inspection, StoredImage image) =>
        new(
            inspection.Id,
            inspection.Title,
            inspection.Notes,
            inspection.Location,
            inspection.RoadSegmentId,
            inspection.Status,
            inspection.Stage,
            inspection.Trace is { } t
                ? new AnalysisTraceDto(
                    t.Concepts.Select(c => new ConceptTraceDto(c.Concept, c.Masks)).ToList(),
                    t.SegmentationMs, t.ReasoningMs, t.ReasoningAttempts, t.SkippedBeyondHorizon, t.Orchestration)
                : null,
            inspection.Language,
            inspection.Engine,
            inspection.SegmentationModel,
            inspection.ReasoningModel,
            inspection.Summary,
            inspection.FailureReason,
            inspection.ReferenceWidthM,
            inspection.Calibration is { } c
                ? new CalibrationDto(c.Corners.Select(p => new[] { p.X, p.Y }).ToList(), c.WidthM, c.LengthM)
                : null,
            new ImageDto(image.Id, ApiRoutes.Image(image.Id), image.Width, image.Height, image.ContentType, image.SizeBytes),
            inspection.Attempts,
            inspection.CreatedAt,
            inspection.StartedAt,
            inspection.CompletedAt,
            inspection.ProcessingTimeMs,
            inspection.SurveyedAreaM2,
            inspection is { Status: InspectionStatus.Completed, SurveyedAreaM2: { } area }
                ? PciCalculator.Compute(PciInputs(inspection.Distresses), area)
                : null,
            inspection.Distresses.OrderBy(d => d.Code, StringComparer.Ordinal).Select(ToDto).ToList(),
            inspection.RepairSteps.OrderBy(s => s.Order).Select(ToDto).ToList());

    public static IEnumerable<PciInput> PciInputs(IEnumerable<Distress> distresses) =>
        distresses.Select(d => new PciInput(d.Type, d.Severity, d.Measurement.AreaM2, d.Measurement.LengthM));

    public static string? RddCode(DistressType type) => type switch
    {
        DistressType.LongitudinalCrack => "D00",
        DistressType.TransverseCrack => "D10",
        DistressType.AlligatorCrack => "D20",
        DistressType.Pothole => "D40",
        _ => null
    };

    public static DistressDto ToDto(Distress d) =>
        new(
            d.Id,
            d.Code,
            d.Type,
            RddCode(d.Type),
            d.Severity,
            d.Confidence,
            new BoxDto(Math.Round(d.Box.X, 5), Math.Round(d.Box.Y, 5), Math.Round(d.Box.Width, 5), Math.Round(d.Box.Height, 5)),
            d.Mask.Select(p => new[] { p.X, p.Y }).ToList(),
            d.MaskVertexCountRaw,
            d.SourceConcept,
            new MeasurementDto(
                d.Measurement.AreaM2, d.Measurement.PerimeterM, d.Measurement.LengthM,
                d.Measurement.WidthM, d.Measurement.Eccentricity, d.Measurement.OrientationDeg),
            d.Quantities is { } q ? new QuantitiesDto(q.DepthM, q.DepthSource, q.CutAreaM2, q.VolumeM3, q.MassT) : null,
            d.Diagnosis,
            d.RecommendedAction,
            d.Holes.Select(h => (IReadOnlyList<double[]>)h.Select(p => new[] { p.X, p.Y }).ToList()).ToList());

    public static RepairStepDto ToDto(RepairStep s) =>
        new(s.Id, s.Order, s.Title, s.Instructions, s.Tools, s.Risk, s.TargetCode, s.CompletedAt);

    public static ConversationMessageDto ToDto(ConversationMessage m) =>
        new(m.Id, m.DistressId, m.Role, m.Content, m.Engine, m.CreatedAt);
}
