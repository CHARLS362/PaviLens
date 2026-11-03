using System.ComponentModel.DataAnnotations;
using FluentValidation;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Inspections;

/// <summary>Four normalized image corners (near-left, near-right, far-right, far-left) of a known rectangle.</summary>
public sealed record CalibrationInput(IReadOnlyList<double[]> Corners, double WidthM, double LengthM);

public sealed record CreateInspectionCommand(
    string Title,
    string? Notes,
    string? Location,
    string? RoadSegmentId,
    double? ReferenceWidthM,
    CalibrationInput? Calibration,
    Stream Image,
    string? FileName,
    ReportLanguage Language = ReportLanguage.Es,
    string? RegionId = null,
    double? Latitude = null,
    double? Longitude = null);

public sealed record AskQuestionCommand(Guid InspectionId, Guid? DistressId, string Question);

public sealed record SetDepthCommand(Guid InspectionId, Guid DistressId, double DepthM);

public sealed class CreateInspectionValidator : AbstractValidator<CreateInspectionCommand>
{
    public CreateInspectionValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Notes).MaximumLength(2000);
        RuleFor(c => c.Location).MaximumLength(200);
        RuleFor(c => c.RoadSegmentId).MaximumLength(64);
        RuleFor(c => c.RegionId).MaximumLength(80);
        RuleFor(c => c.Latitude).InclusiveBetween(-85, 85).When(c => c.Latitude.HasValue);
        RuleFor(c => c.Longitude).InclusiveBetween(-180, 180).When(c => c.Longitude.HasValue);
        RuleFor(c => c).Must(c => c.Latitude.HasValue == c.Longitude.HasValue).WithMessage("Indica latitud y longitud juntas.");
        RuleFor(c => c.ReferenceWidthM).InclusiveBetween(0.5, 100).When(c => c.ReferenceWidthM is not null);
        RuleFor(c => c.Image).NotNull();
        RuleFor(c => c.Calibration!).ChildRules(cal =>
        {
            cal.RuleFor(x => x.Corners)
                .Must(c => c.Count == 4 && c.All(p => p.Length == 2 && p.All(v => v is >= 0 and <= 1)))
                .WithMessage("La calibración necesita 4 esquinas [x, y] normalizadas entre 0 y 1.");
            cal.RuleFor(x => x.WidthM).InclusiveBetween(0.1, 100);
            cal.RuleFor(x => x.LengthM).InclusiveBetween(0.1, 500);
        }).When(c => c.Calibration is not null);
    }
}

public sealed class AskQuestionValidator : AbstractValidator<AskQuestionCommand>
{
    public AskQuestionValidator()
    {
        RuleFor(c => c.Question).NotEmpty().MaximumLength(1000);
    }
}

public sealed class SetDepthValidator : AbstractValidator<SetDepthCommand>
{
    public SetDepthValidator()
    {
        RuleFor(c => c.DepthM).InclusiveBetween(0.005, 0.5)
            .WithMessage("La profundidad debe estar entre 0.005 y 0.5 m.");
    }
}

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";

    [Range(1024, 50 * 1024 * 1024)] public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    [Range(16, 4096)] public int MinImageDimension { get; set; } = 64;
    [Range(256, 50_000)] public int MaxImageDimension { get; set; } = 12_000;
}

public sealed class AnalysisOptions
{
    public const string SectionName = "Analysis";

    /// <summary>
    /// Maximum perpendicular deviation for mask simplification, as a fraction of image width.
    /// 0.0006 is under one pixel at 1280 px, so the stored outline is visually identical to the model's.
    /// </summary>
    [Range(0, 0.05)] public double MaskSimplificationTolerance { get; set; } = 0.0006;

    /// <summary>Text concepts used to prompt the segmentation model.</summary>
    [MinLength(1)]
    public List<string> SegmentationConcepts { get; set; } =
    [
        "pothole", "alligator crack", "longitudinal crack", "transverse crack", "rutting"
    ];

    /// <summary>Previous turns sent to the assistant as conversation memory.</summary>
    [Range(0, 50)] public int AssistantHistoryTurns { get; set; } = 10;
}
