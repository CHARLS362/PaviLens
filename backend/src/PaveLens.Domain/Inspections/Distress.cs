using PaveLens.Domain.Common;

namespace PaveLens.Domain.Inspections;

/// <summary>One segmented pavement distress: mask, class, severity, ground geometry and patch quantities.</summary>
public sealed class Distress : Entity
{
    private Distress()
    {
        // EF Core
    }

    public Guid InspectionId { get; private set; }

    /// <summary>Short label unique within the inspection (B1, PC2…), used by steps and the UI.</summary>
    public string Code { get; private set; } = string.Empty;

    public DistressType Type { get; private set; }
    public Severity Severity { get; private set; }

    /// <summary>Segmentation score reported by the mask model, in [0, 1].</summary>
    public double Confidence { get; private set; }

    /// <summary>Text concept used to prompt the segmentation model.</summary>
    public string? SourceConcept { get; private set; }

    public BoundingBox Box { get; private set; } = null!;

    /// <summary>Simplified mask outline.</summary>
    public IReadOnlyList<NormalizedPoint> Mask { get; private set; } = [];

    /// <summary>Vertex count of the mask before polygon simplification.</summary>
    public int MaskVertexCountRaw { get; private set; }

    public DistressMeasurement Measurement { get; private set; } = null!;

    /// <summary>Patch quantities; null for distresses treated by sealing (cracks), not by patching.</summary>
    public DistressQuantities? Quantities { get; private set; }

    public string? Diagnosis { get; private set; }
    public string? RecommendedAction { get; private set; }

    public bool IsPatchable => IsPatchableType(Type);

    /// <summary>Potholes, alligator cracking and rutting are repaired by patching; single cracks by sealing.</summary>
    public static bool IsPatchableType(DistressType type) =>
        type is DistressType.Pothole or DistressType.AlligatorCrack or DistressType.Rutting;

    public static Distress Create(
        string code,
        DistressType type,
        Severity severity,
        double confidence,
        IReadOnlyList<NormalizedPoint> mask,
        int maskVertexCountRaw,
        DistressMeasurement measurement,
        DistressQuantities? quantities,
        string? sourceConcept,
        string? diagnosis,
        string? recommendedAction)
    {
        if (mask.Count < 3)
        {
            throw new DomainException("La máscara necesita al menos 3 vértices.");
        }

        var distress = new Distress
        {
            Code = Guard.NotBlank(code, "Code", 16),
            Type = Guard.Defined(type, "Type"),
            Severity = Guard.Defined(severity, "Severity"),
            Confidence = Guard.InRange(confidence, 0, 1, "Confidence"),
            Box = BoundingBox.FromPolygon(mask),
            Mask = mask.ToArray(),
            MaskVertexCountRaw = Math.Max(maskVertexCountRaw, mask.Count),
            Measurement = Validate(measurement),
            SourceConcept = Guard.Optional(sourceConcept, "SourceConcept", 100),
            Diagnosis = Guard.Optional(diagnosis, "Diagnosis", 2000),
            RecommendedAction = Guard.Optional(recommendedAction, "RecommendedAction", 2000)
        };
        distress.ApplyQuantities(quantities);
        return distress;
    }

    internal void UpdateDepth(Severity severity, DistressQuantities quantities)
    {
        if (!IsPatchable)
        {
            throw new DomainException($"{Code} se trata con sellado; no tiene profundidad de bacheo.");
        }

        Severity = severity;
        ApplyQuantities(quantities);
    }

    private void ApplyQuantities(DistressQuantities? quantities)
    {
        if (quantities is null)
        {
            Quantities = null;
            return;
        }

        if (!IsPatchable)
        {
            throw new DomainException($"{Code} se trata con sellado; no lleva cubicaje de bacheo.");
        }

        Guard.InRange(quantities.DepthM, 0.005, 0.5, "DepthM");
        Guard.InRange(quantities.CutAreaM2, 0, 10_000, "CutAreaM2");
        Guard.InRange(quantities.VolumeM3, 0, 5_000, "VolumeM3");
        Guard.InRange(quantities.MassT, 0, 15_000, "MassT");
        Quantities = quantities;
    }

    private static DistressMeasurement Validate(DistressMeasurement m)
    {
        Guard.InRange(m.AreaM2, 0, 100_000, "AreaM2");
        Guard.InRange(m.PerimeterM, 0, 10_000, "PerimeterM");
        Guard.InRange(m.LengthM, 0, 10_000, "LengthM");
        Guard.InRange(m.WidthM, 0, 10_000, "WidthM");
        Guard.InRange(m.Eccentricity, 0, 1, "Eccentricity");
        Guard.InRange(m.OrientationDeg, -90, 90, "OrientationDeg");
        return m;
    }
}
