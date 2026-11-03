using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Geometry;

/// <summary>
/// Area, centroid and second moments of a simple polygon by Green's theorem (PLANIFICACION §4.B–C).
/// Moments are integrated over the surface, so they do not depend on how many vertices the mask has.
/// </summary>
public static class PolygonMetrics
{
    public sealed record Shape(
        double Area,
        double Perimeter,
        GroundPoint Centroid,
        double Mu20,
        double Mu02,
        double Mu11,
        double Eccentricity,
        double OrientationDeg,
        double Length,
        double Width);

    public static Shape Compute(IReadOnlyList<GroundPoint> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3)
        {
            throw new ArgumentException("Un polígono necesita al menos 3 vértices.", nameof(polygon));
        }

        double signedArea2 = 0, cx = 0, cy = 0, ixx = 0, iyy = 0, ixy = 0, perimeter = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (x0, y0) = (polygon[i].X, polygon[i].Y);
            var next = polygon[(i + 1) % polygon.Count];
            var (x1, y1) = (next.X, next.Y);
            var c = x0 * y1 - x1 * y0;

            signedArea2 += c;
            cx += (x0 + x1) * c;
            cy += (y0 + y1) * c;
            ixx += c * (y0 * y0 + y0 * y1 + y1 * y1);
            iyy += c * (x0 * x0 + x0 * x1 + x1 * x1);
            ixy += c * (x0 * y1 + 2 * x0 * y0 + 2 * x1 * y1 + x1 * y0);
            perimeter += Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        }

        var signedArea = signedArea2 / 2;
        if (Math.Abs(signedArea) < 1e-12)
        {
            return new Shape(0, perimeter, polygon[0], 0, 0, 0, 0, 0, 0, 0);
        }

        var centroid = new GroundPoint(cx / (6 * signedArea), cy / (6 * signedArea));

        // Signed sums share the orientation sign of A_s, so the ratios are orientation-independent.
        var mu20 = iyy / 12 / signedArea - centroid.X * centroid.X;
        var mu02 = ixx / 12 / signedArea - centroid.Y * centroid.Y;
        var mu11 = ixy / 24 / signedArea - centroid.X * centroid.Y;

        var mean = (mu20 + mu02) / 2;
        var spread = Math.Sqrt((mu20 - mu02) * (mu20 - mu02) / 4 + mu11 * mu11);
        var lambda1 = mean + spread;
        var lambda2 = Math.Max(mean - spread, 0);
        var eccentricity = lambda1 > 0 ? Math.Sqrt(Math.Clamp(1 - lambda2 / lambda1, 0, 1)) : 0;

        var theta = 0.5 * Math.Atan2(2 * mu11, mu20 - mu02);

        // Extent of the outline along the principal axes: the oriented rectangle L × W.
        var (cos, sin) = (Math.Cos(theta), Math.Sin(theta));
        double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
        foreach (var p in polygon)
        {
            var a = p.X * cos + p.Y * sin;
            var b = -p.X * sin + p.Y * cos;
            (minA, maxA) = (Math.Min(minA, a), Math.Max(maxA, a));
            (minB, maxB) = (Math.Min(minB, b), Math.Max(maxB, b));
        }

        return new Shape(
            Math.Abs(signedArea),
            perimeter,
            centroid,
            mu20,
            mu02,
            mu11,
            eccentricity,
            theta * 180 / Math.PI,
            maxA - minA,
            maxB - minB);
    }

    /// <summary>Ground measurement of a mask. Orientation is measured from the cross-road axis: 0° transverse, ±90° longitudinal.</summary>
    public static DistressMeasurement Measure(IReadOnlyList<NormalizedPoint> mask, GroundProjection projection)
    {
        var shape = Compute(projection.Project(mask));
        return new DistressMeasurement(
            Round(shape.Area, 4),
            Round(shape.Perimeter, 3),
            Round(shape.Length, 3),
            Round(shape.Width, 3),
            Round(shape.Eccentricity, 4),
            Round(Math.Clamp(shape.OrientationDeg, -90, 90), 2));
    }

    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
