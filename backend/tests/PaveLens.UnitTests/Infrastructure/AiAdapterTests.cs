using System.Security.Cryptography;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Application.Geometry;
using PaveLens.Domain.Inspections;
using PaveLens.Infrastructure.Ai.Meta;
using PaveLens.Infrastructure.Ai.Simulation;

namespace PaveLens.UnitTests.Infrastructure;

public class MetaReasonerParseTests
{
    private const string ValidJson = """
        {
          "summary": "Bache profundo en la huella derecha.",
          "distresses": [
            { "segmentIndex": 0, "code": "B1", "type": "Pothole", "severity": "High",
              "diagnosis": "Bordes verticales", "recommendedAction": "Bacheo profundo" }
          ],
          "repairSteps": [
            { "order": 1, "title": "Bachear B1", "instructions": "Corte rectangular", "tools": ["Cortadora"], "risk": "Medium", "targetSegmentIndex": 0 }
          ]
        }
        """;

    [Fact]
    public void ParsesPlainJson()
    {
        var result = MetaReasoner.Parse(ValidJson, "muse-wonder");

        Assert.Equal("muse-wonder", result.Model);
        Assert.Equal(DistressType.Pothole, result.Findings.Single().Type);
        Assert.Equal(Severity.High, result.Findings.Single().Severity);
        Assert.Equal(0, result.Steps.Single().TargetSegmentIndex);
    }

    [Fact]
    public void StripsMarkdownCodeFences()
    {
        var result = MetaReasoner.Parse($"```json\n{ValidJson}\n```", "muse-wonder");

        Assert.Equal("B1", result.Findings.Single().Code);
    }

    [Fact]
    public void RejectsUnknownDistressTypes()
    {
        var json = ValidJson.Replace("\"Pothole\"", "\"Crater\"", StringComparison.Ordinal);

        Assert.Throws<ModelOutputException>(() => MetaReasoner.Parse(json, "muse-wonder"));
    }

    [Theory]
    [InlineData("\"Pothole\"", "42")]
    [InlineData("\"Pothole\"", "0")]
    [InlineData("\"High\"", "2")]
    public void RejectsNumericEnumValues(string name, string number)
    {
        var json = ValidJson.Replace(name, number, StringComparison.Ordinal);

        Assert.Throws<ModelOutputException>(() => MetaReasoner.Parse(json, "muse-wonder"));
    }

    [Fact]
    public void RejectsNonJsonOutput()
    {
        Assert.Throws<ModelOutputException>(() => MetaReasoner.Parse("Lo siento, no puedo ayudar.", "muse-wonder"));
    }
}

public class SimulationCatalogTests
{
    private static ImagePayload Image(string sha256) => new(ReadOnlyMemory<byte>.Empty, "image/jpeg", 1280, 960, sha256);

    private static string SampleHash(string file) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", file))));

    [Theory]
    [InlineData("jr-lima-tramo.jpg", "AH1")]
    [InlineData("av-el-sol-bache.jpg", "B1")]
    [InlineData("jr-arequipa-cocodrilo.jpg", "PC1")]
    public async Task EverySampleHasItsOwnScenario(string file, string code)
    {
        var catalog = new SimulationCatalog();
        var ct = TestContext.Current.CancellationToken;
        var image = Image(SampleHash(file));

        var segments = await new SimulatedSegmenter(catalog).SegmentAsync(new SegmentationRequest(image, []), ct);
        var measured = segments.Segments
            .Select(s => new MeasuredSegment(s, new DistressMeasurement(1, 4, 1, 1, 0, 0)))
            .ToList();
        var diagnosis = await new SimulatedReasoner(catalog).DiagnoseAsync(new DiagnosisRequest(image, "t", null, null, measured), ct);

        Assert.Contains(diagnosis.Findings, f => f.Code == code);
        Assert.Equal(segments.Segments.Count, diagnosis.Findings.Count);
        Assert.DoesNotContain("no pertenece al catálogo", diagnosis.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObliqueSceneMasksProjectBackToTheirGroundAreas()
    {
        // The generator draws B1 with a ground area of ≈0.38 m² and renders it through this calibration.
        NormalizedPoint[] corners = [new(0.08, 0.95), new(0.92, 0.95), new(0.60, 0.42), new(0.40, 0.42)];
        var projection = GroundProjection.Create(3.5, new GroundCalibration(corners, 7, 25), 0.75);
        var segments = await new SimulatedSegmenter(new SimulationCatalog()).SegmentAsync(
            new SegmentationRequest(Image(SampleHash("jr-lima-tramo.jpg")), []), TestContext.Current.CancellationToken);

        var b1 = PolygonMetrics.Measure(segments.Segments[0].Polygon, projection);

        Assert.InRange(b1.AreaM2, 0.36, 0.40);
    }

    [Fact]
    public async Task UnknownImageGetsTheLabelledGenericScenario()
    {
        var catalog = new SimulationCatalog();

        var diagnosis = await new SimulatedReasoner(catalog).DiagnoseAsync(
            new DiagnosisRequest(Image(new string('0', 64)), "Otra vía", null, null, []), TestContext.Current.CancellationToken);

        Assert.StartsWith("[Simulación]", diagnosis.Summary, StringComparison.Ordinal);
    }
}
