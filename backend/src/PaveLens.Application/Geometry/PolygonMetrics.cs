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

    public static Shape Compute(IReadOnlyList<GroundPoint> polygon) => Compute(polygon, []);

    /// <summary>
    /// Same integrals over the region between the outline and its <paramref name="holes"/>. Every ring is
    /// integrated counter-clockwise and the holes are subtracted, so area, centroid and moments are those of
    /// the net surface; the perimeter adds the hole edges. L × W stay the extents of the outline.
    /// </summary>
    public static Shape Compute(IReadOnlyList<GroundPoint> polygon, IReadOnlyList<IReadOnlyList<GroundPoint>> holes)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(holes);
        if (polygon.Count < 3 || holes.Any(h => h.Count < 3))
        {
            throw new ArgumentException("Un polígono necesita al menos 3 vértices.", nameof(polygon));
        }

        var total = Integrate(polygon);
        foreach (var hole in holes)
        {
            total = total.Without(Integrate(hole));
        }

        var (area2, cx, cy, ixx, iyy, ixy, perimeter) = total;
        var area = area2 / 2;
        if (area < 1e-12)
        {
            return new Shape(0, perimeter, polygon[0], 0, 0, 0, 0, 0, 0, 0);
        }

        var centroid = new GroundPoint(cx / (6 * area), cy / (6 * area));
        var mu20 = iyy / 12 / area - centroid.X * centroid.X;
        var mu02 = ixx / 12 / area - centroid.Y * centroid.Y;
        var mu11 = ixy / 24 / area - centroid.X * centroid.Y;

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
            area,
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
    public static DistressMeasurement Measure(
        IReadOnlyList<NormalizedPoint> mask, GroundProjection projection, IReadOnlyList<IReadOnlyList<NormalizedPoint>>? holes = null)
    {
        var shape = Compute(projection.Project(mask), holes?.Select(h => projection.Project(h)).ToArray() ?? []);
        return new DistressMeasurement(
            Round(shape.Area, 4),
            Round(shape.Perimeter, 3),
            Round(shape.Length, 3),
            Round(shape.Width, 3),
            Round(shape.Eccentricity, 4),
            Round(Math.Clamp(shape.OrientationDeg, -90, 90), 2));
    }

    /// <summary>Green's-theorem sums of one ring, flipped to counter-clockwise so outlines and holes combine by sign.</summary>
    private static Sums Integrate(IReadOnlyList<GroundPoint> ring)
    {
        var s = default(Sums);
        for (var i = 0; i < ring.Count; i++)
        {
            var (x0, y0) = (ring[i].X, ring[i].Y);
            var next = ring[(i + 1) % ring.Count];
            var (x1, y1) = (next.X, next.Y);
            var c = x0 * y1 - x1 * y0;
            s = new Sums(
                s.Area2 + c,
                s.Cx + (x0 + x1) * c,
                s.Cy + (y0 + y1) * c,
                s.Ixx + c * (y0 * y0 + y0 * y1 + y1 * y1),
                s.Iyy + c * (x0 * x0 + x0 * x1 + x1 * x1),
                s.Ixy + c * (x0 * y1 + 2 * x0 * y0 + 2 * x1 * y1 + x1 * y0),
                s.Perimeter + Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
        }

        return s.Area2 < 0 ? s with { Area2 = -s.Area2, Cx = -s.Cx, Cy = -s.Cy, Ixx = -s.Ixx, Iyy = -s.Iyy, Ixy = -s.Ixy } : s;
    }

    private readonly record struct Sums(double Area2, double Cx, double Cy, double Ixx, double Iyy, double Ixy, double Perimeter)
    {
        /// <summary>Removes a hole: its integrals are subtracted, its edge adds to the perimeter.</summary>
        public Sums Without(Sums hole) => new(
            Area2 - hole.Area2, Cx - hole.Cx, Cy - hole.Cy, Ixx - hole.Ixx, Iyy - hole.Iyy, Ixy - hole.Ixy, Perimeter + hole.Perimeter);
    }

    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
