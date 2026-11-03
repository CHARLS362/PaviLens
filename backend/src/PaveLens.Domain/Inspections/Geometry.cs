using PaveLens.Domain.Common;

namespace PaveLens.Domain.Inspections;

/// <summary>A point in image space normalized to [0, 1] on both axes.</summary>
public readonly record struct NormalizedPoint
{
    public NormalizedPoint(double x, double y)
    {
        X = Guard.InRange(x, 0, 1, "X");
        Y = Guard.InRange(y, 0, 1, "Y");
    }

    public double X { get; }
    public double Y { get; }
}

/// <summary>Axis-aligned box in normalized image coordinates.</summary>
public sealed record BoundingBox
{
    public BoundingBox(double x, double y, double width, double height)
    {
        X = Guard.InRange(x, 0, 1, "X");
        Y = Guard.InRange(y, 0, 1, "Y");
        Width = Guard.InRange(width, 0, 1 - X + 1e-9, "Width");
        Height = Guard.InRange(height, 0, 1 - Y + 1e-9, "Height");
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public static BoundingBox FromPolygon(IReadOnlyList<NormalizedPoint> polygon)
    {
        if (polygon.Count < 3)
        {
            throw new DomainException("Un polígono necesita al menos 3 vértices.");
        }

        var minX = polygon.Min(p => p.X);
        var minY = polygon.Min(p => p.Y);
        var maxX = polygon.Max(p => p.X);
        var maxY = polygon.Max(p => p.Y);
        return new BoundingBox(minX, minY, maxX - minX, maxY - minY);
    }
}

/// <summary>
/// Four image points marking a rectangle of known size on the road surface (lane markings, a
/// measured patch). Corners go near-left, near-right, far-right, far-left. They define the
/// image-to-ground homography for oblique photos.
/// </summary>
public sealed record GroundCalibration
{
    public GroundCalibration(IReadOnlyList<NormalizedPoint> corners, double widthM, double lengthM)
    {
        if (corners.Count != 4)
        {
            throw new DomainException("La calibración necesita exactamente 4 esquinas.");
        }

        // Twice the signed area of the quad; near-zero means collinear or repeated corners.
        var doubled = 0.0;
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            doubled += a.X * b.Y - b.X * a.Y;
        }

        if (Math.Abs(doubled) < 1e-4)
        {
            throw new DomainException("Las esquinas de calibración no forman un cuadrilátero válido.");
        }

        Corners = corners.ToArray();
        WidthM = Guard.InRange(widthM, 0.1, 100, "WidthM");
        LengthM = Guard.InRange(lengthM, 0.1, 500, "LengthM");
    }

    public IReadOnlyList<NormalizedPoint> Corners { get; }
    public double WidthM { get; }
    public double LengthM { get; }
}

/// <summary>Ground-plane geometry of a distress mask, in metres (see PLANIFICACION §4.B–C).</summary>
public sealed record DistressMeasurement(
    double AreaM2,
    double PerimeterM,
    double LengthM,
    double WidthM,
    double Eccentricity,
    double OrientationDeg);

/// <summary>Patch quantities (PLANIFICACION §4.D): rectangular cut, volume and hot-mix mass.</summary>
public sealed record DistressQuantities(
    double DepthM,
    DepthSource DepthSource,
    double CutAreaM2,
    double VolumeM3,
    double MassT);
