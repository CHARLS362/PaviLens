using PaveLens.Application.Geometry;
using PaveLens.Domain.Inspections;

namespace PaveLens.UnitTests.Application;

public class PolygonMetricsTests
{
    private static GroundPoint[] Rectangle(double w, double h) => [new(0, 0), new(w, 0), new(w, h), new(0, h)];

    [Fact]
    public void RectangleAreaCentroidAndPerimeter()
    {
        var shape = PolygonMetrics.Compute(Rectangle(4, 2));

        Assert.Equal(8, shape.Area, 9);
        Assert.Equal(12, shape.Perimeter, 9);
        Assert.Equal(2, shape.Centroid.X, 9);
        Assert.Equal(1, shape.Centroid.Y, 9);
    }

    [Fact]
    public void CentralMomentsMatchTheClosedFormForARectangle()
    {
        // For a w × h rectangle: μ20 = w²/12, μ02 = h²/12, μ11 = 0.
        var shape = PolygonMetrics.Compute(Rectangle(4, 2));

        Assert.Equal(16.0 / 12, shape.Mu20, 9);
        Assert.Equal(4.0 / 12, shape.Mu02, 9);
        Assert.Equal(0, shape.Mu11, 9);
        Assert.Equal(Math.Sqrt(1 - 4.0 / 16), shape.Eccentricity, 9);
    }

    [Fact]
    public void SquareHasZeroEccentricity()
    {
        Assert.Equal(0, PolygonMetrics.Compute(Rectangle(3, 3)).Eccentricity, 9);
    }

    [Fact]
    public void OrientationFollowsTheMajorAxis()
    {
        Assert.Equal(0, PolygonMetrics.Compute(Rectangle(5, 0.5)).OrientationDeg, 6);
        Assert.Equal(90, Math.Abs(PolygonMetrics.Compute(Rectangle(0.5, 5)).OrientationDeg), 6);

        var tilted = PolygonMetrics.Compute([new(0, 0), new(4, 4), new(3.5, 4.5), new(-0.5, 0.5)]);
        Assert.Equal(45, tilted.OrientationDeg, 6);
        Assert.Equal(Math.Sqrt(32), tilted.Length, 6);
    }

    [Fact]
    public void ResultDoesNotDependOnWindingOrVertexDensity()
    {
        var ccw = PolygonMetrics.Compute(Rectangle(4, 2));
        var cw = PolygonMetrics.Compute(Rectangle(4, 2).Reverse().ToArray());

        // Same rectangle with extra collinear vertices on every edge.
        GroundPoint[] dense = [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0), new(4, 1), new(4, 2), new(2, 2), new(0, 2), new(0, 1)];
        var densified = PolygonMetrics.Compute(dense);

        foreach (var other in new[] { cw, densified })
        {
            Assert.Equal(ccw.Area, other.Area, 9);
            Assert.Equal(ccw.Mu20, other.Mu20, 9);
            Assert.Equal(ccw.Mu02, other.Mu02, 9);
            Assert.Equal(ccw.Eccentricity, other.Eccentricity, 9);
        }
    }

    [Fact]
    public void NonConvexPolygonUsesTheSignedShoelaceArea()
    {
        // An L shape: 2×2 square minus its 1×1 top-right quarter.
        GroundPoint[] l = [new(0, 0), new(2, 0), new(2, 1), new(1, 1), new(1, 2), new(0, 2)];

        Assert.Equal(3, PolygonMetrics.Compute(l).Area, 9);
    }
}

public class GroundProjectionTests
{
    private static readonly NormalizedPoint[] Corners = [new(0.08, 0.95), new(0.92, 0.95), new(0.60, 0.42), new(0.40, 0.42)];

    [Fact]
    public void ScaleProjectionUsesTheReferenceWidthAndAspect()
    {
        var projection = GroundProjection.Create(4, null, 0.75);

        Assert.Equal(new GroundPoint(2, 1.5), projection.Project(new NormalizedPoint(0.5, 0.5)));
        Assert.Equal(12, projection.SurveyedAreaM2, 9);
    }

    [Fact]
    public void HomographyMapsTheCalibrationCornersExactly()
    {
        var projection = GroundProjection.Create(3.5, new GroundCalibration(Corners, 7, 25), 0.75);
        GroundPoint[] expected = [new(0, 0), new(7, 0), new(7, 25), new(0, 25)];

        for (var i = 0; i < 4; i++)
        {
            var p = projection.Project(Corners[i]);
            Assert.Equal(expected[i].X, p.X, 6);
            Assert.Equal(expected[i].Y, p.Y, 6);
        }

        Assert.Equal(175, projection.SurveyedAreaM2, 9);
    }

    [Fact]
    public void HomographyPreservesTheVanishingGeometry()
    {
        var projection = GroundProjection.Create(3.5, new GroundCalibration(Corners, 7, 25), 0.75);

        // The image centre line is the road centre line, and farther rows are farther away.
        var near = projection.Project(new NormalizedPoint(0.5, 0.9));
        var far = projection.Project(new NormalizedPoint(0.5, 0.5));

        Assert.Equal(3.5, near.X, 6);
        Assert.Equal(3.5, far.X, 6);
        Assert.True(far.Y > near.Y);
    }

    [Fact]
    public void PointsAboveTheHorizonAreRejected()
    {
        var projection = GroundProjection.Create(3.5, new GroundCalibration(Corners, 7, 25), 0.75);

        Assert.Throws<GroundProjectionException>(() => projection.Project(new NormalizedPoint(0.5, 0.1)));
    }

    [Fact]
    public void CoversTellsWhichPolygonsCanBeProjected()
    {
        var oblique = GroundProjection.Create(3.5, new GroundCalibration(Corners, 7, 25), 0.75);
        NormalizedPoint[] onRoad = [new(0.4, 0.8), new(0.6, 0.8), new(0.5, 0.9)];
        NormalizedPoint[] reachesSky = [new(0.4, 0.8), new(0.6, 0.8), new(0.5, 0.1)];

        Assert.True(oblique.Covers(onRoad));
        Assert.False(oblique.Covers(reachesSky));
        Assert.True(GroundProjection.Create(3.5, null, 0.75).Covers(reachesSky));
    }
}
