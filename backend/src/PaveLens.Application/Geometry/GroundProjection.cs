using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Geometry;

/// <summary>A point on the road plane, in metres. X runs across the road, Y along it.</summary>
public readonly record struct GroundPoint(double X, double Y);

/// <summary>The image cannot be mapped to the ground with the given calibration.</summary>
public sealed class GroundProjectionException(string message) : Exception(message);

/// <summary>
/// Maps normalized image coordinates to metres on the road plane (backend/docs/ARCHITECTURE.md, Ground projection).
/// Nadir photos use a constant scale; oblique photos use a homography from a 4-point calibration.
/// </summary>
public abstract class GroundProjection
{
    /// <summary>Pavement area the evaluation covers, in m².</summary>
    public abstract double SurveyedAreaM2 { get; }

    public abstract GroundPoint Project(NormalizedPoint point);

    public IReadOnlyList<GroundPoint> Project(IReadOnlyList<NormalizedPoint> polygon) =>
        polygon.Select(Project).ToArray();

    /// <summary>Whether the point lies on the ground side of the horizon, so <see cref="Project(NormalizedPoint)"/> can map it.</summary>
    public virtual bool Covers(NormalizedPoint point) => true;

    public bool Covers(IReadOnlyList<NormalizedPoint> polygon) => polygon.All(Covers);

    /// <param name="referenceWidthM">Ground width spanned by the full image width (nadir case).</param>
    /// <param name="aspect">Image height / width.</param>
    public static GroundProjection Create(double referenceWidthM, GroundCalibration? calibration, double aspect) =>
        calibration is null
            ? new ScaleProjection(referenceWidthM, referenceWidthM * aspect)
            : HomographyProjection.FromCalibration(calibration);
}

/// <summary>Constant ground sampling distance: x·W, y·H.</summary>
public sealed class ScaleProjection(double widthM, double heightM) : GroundProjection
{
    public override double SurveyedAreaM2 => widthM * heightM;

    public override GroundPoint Project(NormalizedPoint point) => new(point.X * widthM, point.Y * heightM);
}

/// <summary>Planar homography P ~ H·p, estimated by DLT from four correspondences.</summary>
public sealed class HomographyProjection : GroundProjection
{
    private readonly double[] _h; // row-major 3×3 with h[8] = 1
    private readonly double _surveyedArea;

    // Sign of the projective denominator on the ground side of the horizon. With h33 = 1 it is not
    // necessarily positive (the image origin is usually sky), so it is taken from the calibration itself.
    private readonly double _groundSign;

    private HomographyProjection(double[] h, double surveyedArea, NormalizedPoint groundSample)
    {
        _h = h;
        _surveyedArea = surveyedArea;
        _groundSign = Math.Sign(Denominator(groundSample));
    }

    public override double SurveyedAreaM2 => _surveyedArea;

    /// <summary>The calibrated rectangle is the evaluated area: near edge at Y = 0, far edge at Y = length.</summary>
    public static HomographyProjection FromCalibration(GroundCalibration calibration)
    {
        GroundPoint[] world =
        [
            new(0, 0),
            new(calibration.WidthM, 0),
            new(calibration.WidthM, calibration.LengthM),
            new(0, calibration.LengthM)
        ];
        var image = calibration.Corners.Select(c => (c.X, c.Y)).ToArray();
        return new HomographyProjection(Solve(image, world), calibration.WidthM * calibration.LengthM, calibration.Corners[0]);
    }

    public override bool Covers(NormalizedPoint point) => Denominator(point) * _groundSign > 1e-9;

    public override GroundPoint Project(NormalizedPoint point)
    {
        if (!Covers(point))
        {
            throw new GroundProjectionException("Un punto de la máscara queda sobre el horizonte de la calibración.");
        }

        var (u, v) = (point.X, point.Y);
        var w = Denominator(point);

        return new GroundPoint(
            (_h[0] * u + _h[1] * v + _h[2]) / w,
            (_h[3] * u + _h[4] * v + _h[5]) / w);
    }

    private double Denominator(NormalizedPoint p) => _h[6] * p.X + _h[7] * p.Y + _h[8];

    /// <summary>Direct linear transform with h33 = 1: an 8×8 linear system, two rows per correspondence.</summary>
    internal static double[] Solve(IReadOnlyList<(double U, double V)> image, IReadOnlyList<GroundPoint> world)
    {
        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            var (u, v) = image[i];
            var (x, y) = (world[i].X, world[i].Y);
            double[] rowX = [u, v, 1, 0, 0, 0, -u * x, -v * x, x];
            double[] rowY = [0, 0, 0, u, v, 1, -u * y, -v * y, y];
            for (var j = 0; j < 9; j++)
            {
                a[2 * i, j] = rowX[j];
                a[2 * i + 1, j] = rowY[j];
            }
        }

        // Gaussian elimination with partial pivoting on the augmented matrix.
        for (var col = 0; col < 8; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < 8; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
            }

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                throw new GroundProjectionException("La calibración es degenerada: tres esquinas están alineadas.");
            }

            for (var j = 0; j < 9; j++) (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);

            for (var r = 0; r < 8; r++)
            {
                if (r == col) continue;
                var factor = a[r, col] / a[col, col];
                for (var j = col; j < 9; j++) a[r, j] -= factor * a[col, j];
            }
        }

        var h = new double[9];
        for (var i = 0; i < 8; i++) h[i] = a[i, 8] / a[i, i];
        h[8] = 1;
        return h;
    }
}
