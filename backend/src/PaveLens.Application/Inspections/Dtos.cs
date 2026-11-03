using PaveLens.Application.Pavement;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Inspections;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record InspectionSummaryDto(
    Guid Id,
    string Title,
    string? Location,
    InspectionStatus Status,
    AnalysisEngine? Engine,
    ReportLanguage Language,
    string ImageUrl,
    int DistressCount,
    int PotholeCount,
    double? Pci,
    DateTime CreatedAt);

public sealed record ImageDto(Guid Id, string Url, int Width, int Height, string ContentType, long SizeBytes);

public sealed record BoxDto(double X, double Y, double Width, double Height);

public sealed record CalibrationDto(IReadOnlyList<double[]> Corners, double WidthM, double LengthM);

public sealed record MeasurementDto(
    double AreaM2,
    double PerimeterM,
    double LengthM,
    double WidthM,
    double Eccentricity,
    double OrientationDeg);

public sealed record QuantitiesDto(
    double DepthM,
    DepthSource DepthSource,
    double CutAreaM2,
    double VolumeM3,
    double MassT);

public sealed record DistressDto(
    Guid Id,
    string Code,
    DistressType Type,
    string? RddCode,
    Severity Severity,
    double Confidence,
    BoxDto Box,
    IReadOnlyList<double[]> Mask,
    int MaskVertexCountRaw,
    string? SourceConcept,
    MeasurementDto Measurement,
    QuantitiesDto? Quantities,
    string? Diagnosis,
    string? RecommendedAction,
    IReadOnlyList<IReadOnlyList<double[]>>? Holes = null);

public sealed record RepairStepDto(
    Guid Id,
    int Order,
    string Title,
    string Instructions,
    IReadOnlyList<string> Tools,
    RiskLevel Risk,
    string? TargetCode,
    DateTime? CompletedAt);

public sealed record ConceptTraceDto(string Concept, int Masks);

public sealed record AnalysisTraceDto(
    IReadOnlyList<ConceptTraceDto> Concepts,
    long SegmentationMs,
    long ReasoningMs,
    int ReasoningAttempts,
    int SkippedBeyondHorizon,
    AgentRunTrace? Orchestration = null);

public sealed record InspectionDetailDto(
    Guid Id,
    string Title,
    string? Notes,
    string? Location,
    string? RoadSegmentId,
    InspectionStatus Status,
    AnalysisStage? Stage,
    AnalysisTraceDto? Trace,
    ReportLanguage Language,
    AnalysisEngine? Engine,
    string? SegmentationModel,
    string? ReasoningModel,
    string? Summary,
    string? FailureReason,
    double ReferenceWidthM,
    CalibrationDto? Calibration,
    ImageDto Image,
    int Attempts,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    long? ProcessingTimeMs,
    double? SurveyedAreaM2,
    PciResult? Pci,
    IReadOnlyList<DistressDto> Distresses,
    IReadOnlyList<RepairStepDto> RepairSteps);

public sealed record ConversationMessageDto(
    Guid Id,
    Guid? DistressId,
    MessageRole Role,
    string Content,
    AnalysisEngine? Engine,
    DateTime CreatedAt);

public sealed record AnswerDto(ConversationMessageDto Question, ConversationMessageDto Answer);

public sealed record BudgetLineDto(string Item, string Description, string Unit, double Quantity, decimal UnitPrice, decimal Amount);

public sealed record RegionDto(string Name, double AltitudeM, string RecommendedBinder, IReadOnlyList<string> Notes);

/// <summary>Unit-price analysis (APU) of the maintenance work, computed on read from the stored quantities.</summary>
public sealed record BudgetDto(
    Guid InspectionId,
    string Currency,
    IReadOnlyList<BudgetLineDto> Lines,
    decimal DirectCost,
    decimal OverheadPct,
    decimal Overhead,
    decimal ProfitPct,
    decimal Profit,
    decimal Subtotal,
    decimal IgvPct,
    decimal Igv,
    decimal Total,
    int AssumedDepthCount,
    RegionDto Region,
    IReadOnlyList<string> Notes);

public static class ApiRoutes
{
    public const string Prefix = "/api/v1";

    public static string Image(Guid imageId) => $"{Prefix}/images/{imageId}";
}
