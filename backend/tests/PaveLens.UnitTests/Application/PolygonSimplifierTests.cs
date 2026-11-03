using PaveLens.Application.Geometry;
using PaveLens.Domain.Inspections;

namespace PaveLens.UnitTests.Application;

public class PolygonSimplifierTests
{
    [Fact]
    public void CollinearVerticesAreRemoved()
    {
        NormalizedPoint[] square =
        [
            new(0.1, 0.1), new(0.2, 0.1), new(0.3, 0.1),
            new(0.3, 0.2), new(0.3, 0.3),
            new(0.2, 0.3), new(0.1, 0.3),
            new(0.1, 0.2)
        ];

        var simplified = PolygonSimplifier.Simplify(square, 0.001);

        Assert.Equal(4, simplified.Count);
        Assert.Contains(new NormalizedPoint(0.3, 0.3), simplified);
    }

    [Fact]
    public void DenseCircleIsReducedButKeepsItsShape()
    {
        var circle = Enumerable.Range(0, 360)
            .Select(i => i * Math.PI / 180)
            .Select(a => new NormalizedPoint(0.5 + 0.2 * Math.Cos(a), 0.5 + 0.2 * Math.Sin(a)))
            .ToArray();

        var simplified = PolygonSimplifier.Simplify(circle, 0.002);

        Assert.InRange(simplified.Count, 8, 60);
        Assert.All(simplified, p =>
            Assert.InRange(Math.Sqrt(Math.Pow(p.X - 0.5, 2) + Math.Pow(p.Y - 0.5, 2)), 0.195, 0.205));
    }

    [Fact]
    public void ZeroToleranceKeepsEveryNonCollinearVertex()
    {
        NormalizedPoint[] triangle = [new(0.1, 0.1), new(0.9, 0.1), new(0.5, 0.8)];

        Assert.Equal(3, PolygonSimplifier.Simplify(triangle, 0).Count);
    }

    [Fact]
    public void PerpendicularDistanceToASegment()
    {
        var d = PolygonSimplifier.PerpendicularDistance(new(0.5, 0.5), new(0, 0), new(1, 0));

        Assert.Equal(0.5, d, 9);
    }
}
