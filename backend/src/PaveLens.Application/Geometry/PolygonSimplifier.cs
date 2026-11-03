using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Geometry;

/// <summary>
/// Ramer-Douglas-Peucker simplification for closed mask outlines.
/// Iterative (explicit stack) so dense SAM masks cannot overflow the call stack.
/// </summary>
public static class PolygonSimplifier
{
    /// <param name="polygon">Closed outline; the first point is not repeated at the end.</param>
    /// <param name="tolerance">Maximum perpendicular deviation allowed, in normalized units.</param>
    public static IReadOnlyList<NormalizedPoint> Simplify(IReadOnlyList<NormalizedPoint> polygon, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        if (polygon.Count <= 3 || tolerance == 0)
        {
            return polygon.ToArray();
        }

        // Split the ring at the vertex farthest from the first one; each half is an open polyline.
        var farthest = FarthestFrom(polygon, 0);
        var first = SimplifyOpen(polygon, 0, farthest, tolerance);
        var second = SimplifyOpen(polygon, farthest, polygon.Count, tolerance);

        var result = new List<NormalizedPoint>(first.Count + second.Count);
        result.AddRange(first);
        result.AddRange(second.Skip(1)); // shared split vertex
        if (result.Count > 1 && result[^1] == result[0])
        {
            result.RemoveAt(result.Count - 1);
        }

        return result.Count >= 3 ? result : polygon.ToArray();
    }

    /// <summary>Simplifies polygon[start..end] where index <c>Count</c> wraps to 0.</summary>
    private static List<NormalizedPoint> SimplifyOpen(IReadOnlyList<NormalizedPoint> polygon, int start, int end, double tolerance)
    {
        NormalizedPoint At(int i) => polygon[i % polygon.Count];

        var keep = new bool[end - start + 1];
        keep[0] = true;
        keep[^1] = true;

        var stack = new Stack<(int From, int To)>();
        stack.Push((start, end));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            var maxDistance = 0d;
            var index = -1;
            for (var i = from + 1; i < to; i++)
            {
                var distance = PerpendicularDistance(At(i), At(from), At(to));
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    index = i;
                }
            }

            if (index >= 0 && maxDistance > tolerance)
            {
                keep[index - start] = true;
                stack.Push((from, index));
                stack.Push((index, to));
            }
        }

        var points = new List<NormalizedPoint>();
        for (var i = 0; i < keep.Length; i++)
        {
            if (keep[i])
            {
                points.Add(At(start + i));
            }
        }

        return points;
    }

    private static int FarthestFrom(IReadOnlyList<NormalizedPoint> polygon, int origin)
    {
        var best = origin;
        var bestDistance = -1d;
        for (var i = 0; i < polygon.Count; i++)
        {
            var dx = polygon[i].X - polygon[origin].X;
            var dy = polygon[i].Y - polygon[origin].Y;
            var distance = dx * dx + dy * dy;
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    internal static double PerpendicularDistance(NormalizedPoint p, NormalizedPoint a, NormalizedPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < double.Epsilon)
        {
            return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        }

        return Math.Abs(dy * p.X - dx * p.Y + b.X * a.Y - b.Y * a.X) / length;
    }
}
