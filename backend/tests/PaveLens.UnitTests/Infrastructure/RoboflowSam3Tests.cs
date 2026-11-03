using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Domain.Inspections;
using PaveLens.Infrastructure.Ai;
using PaveLens.Infrastructure.Ai.Meta;
using Microsoft.Extensions.Options;

namespace PaveLens.UnitTests.Infrastructure;

public class RoboflowSam3Tests
{
    private const int W = 1000;
    private const int H = 500;

    private static string Square(double x, double y, double size) =>
        $"[[{x},{y}],[{x + size},{y}],[{x + size},{y + size}],[{x},{y + size}]]";

    [Fact]
    public void NormalizesPixelPolygonsAndUsesEchoedConcept()
    {
        var body = JsonNode.Parse($$"""
            { "prompt_results": [
              { "echo": { "type": "text", "text": "pothole" },
                "predictions": [ { "confidence": 0.91, "masks": [ {{Square(100, 100, 50)}} ] } ] },
              { "echo": { "type": "text", "text": "rutting" }, "predictions": [] }
            ] }
            """);

        var instance = Assert.Single(Sam3Instances.Parse(body, ["pothole", "rutting"], W, H));

        Assert.Equal("pothole", instance.Concept);
        Assert.Equal(0.91, instance.Score);
        Assert.Equal(0.1, instance.Polygon[0].X, 9);
        Assert.Equal(0.2, instance.Polygon[0].Y, 9);
        Assert.Equal(0.3, instance.Polygon[2].Y, 9);
    }

    [Fact]
    public void SplitsSeparatePartsKeepsHolesAndDropsSpeckle()
    {
        var body = JsonNode.Parse($$"""
            { "prompt_results": [ { "predictions": [ { "confidence": 0.8, "masks": [
              {{Square(0, 0, 200)}},
              {{Square(50, 50, 20)}},
              {{Square(600, 300, 100)}},
              {{Square(900, 10, 3)}}
            ] } ] } ] }
            """);

        var parts = Sam3Instances.Parse(body, ["alligator crack"], W, H).ToList();

        // Outer 200 px square carrying its hole, separate 100 px square; the 3 × 3 speck is dropped.
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal("alligator crack", p.Concept));
        var hole = Assert.Single(parts[0].Holes!);
        Assert.Equal(0.05, hole[0].X, 9);
        Assert.Null(parts[1].Holes);
    }

    [Fact]
    public void AnIslandInsideAHoleIsASeparatePart()
    {
        var parts = Sam3Instances.Nest([
            [(0, 0), (100, 0), (100, 100), (0, 100)],
            [(20, 20), (80, 20), (80, 80), (20, 80)],
            [(40, 40), (60, 40), (60, 60), (40, 60)]
        ]);

        Assert.Equal(2, parts.Count);
        Assert.Single(parts[0].Holes);
        Assert.Empty(parts[1].Holes);
        Assert.Equal(40, parts[1].Outer[0].X);
    }

    [Fact]
    public void DeduplicationComparesNetAreas()
    {
        // A ring of alligator cracking around intact pavement does not duplicate a pothole filling the gap.
        NormalizedPoint[] Sq(double x, double s) => [new(x, x), new(x + s, x), new(x + s, x + s), new(x, x + s)];
        var ring = new Sam3Instances.Instance("alligator crack", 0.9, Sq(0.1, 0.4), [Sq(0.15, 0.3)]);
        var filled = new Sam3Instances.Instance("pothole", 0.8, Sq(0.1, 0.4));

        Assert.Equal(2, Sam3Instances.Deduplicate([ring, filled]).Count);
    }

    [Fact]
    public void AcceptsSingleRingAndObjectVertices()
    {
        var body = JsonNode.Parse("""
            { "prompt_results": [ { "predictions": [ { "confidence": 0.7, "masks":
              [ {"x":10,"y":10}, {"x":60,"y":10}, {"x":60,"y":60}, {"x":10,"y":60} ] } ] } ] }
            """);

        Assert.Single(Sam3Instances.Parse(body, ["pothole"], W, H));
    }

    [Fact]
    public void RejectsCoordinatesOutsideTheImage()
    {
        var body = JsonNode.Parse($$"""
            { "prompt_results": [ { "predictions": [ { "confidence": 0.7, "masks": [ {{Square(980, 100, 80)}} ] } ] } ] }
            """);

        Assert.Throws<ModelOutputException>(() => Sam3Instances.Parse(body, ["pothole"], W, H).ToList());
    }

    [Fact]
    public void RejectsBodiesWithoutPromptResults()
    {
        Assert.Throws<ModelOutputException>(() => Sam3Instances.Parse(JsonNode.Parse("{}"), ["pothole"], W, H).ToList());
    }

    [Fact]
    public void KeepsTheBetterLabelWhenTwoConceptsReturnTheSameMask()
    {
        var pothole = Instance("pothole", 0.9, 0.1, 0.1, 0.3);
        var sameAsAlligator = Instance("alligator crack", 0.6, 0.11, 0.1, 0.3);
        var other = Instance("rutting", 0.5, 0.6, 0.6, 0.2);

        var kept = Sam3Instances.Deduplicate([pothole, sameAsAlligator, other]);

        Assert.Equal(["pothole", "rutting"], kept.Select(k => k.Concept));
    }

    [Fact]
    public void KeepsAPotholeInsideAnAlligatorArea()
    {
        var alligator = Instance("alligator crack", 0.7, 0.1, 0.1, 0.6);
        var pothole = Instance("pothole", 0.9, 0.3, 0.3, 0.1);

        Assert.Equal(2, Sam3Instances.Deduplicate([alligator, pothole]).Count);
    }

    [Fact]
    public async Task SendsOneTextPromptPerConceptWithBearerKey()
    {
        var handler = new CapturingHandler("""
            { "prompt_results": [
              { "echo": { "text": "pothole" }, "predictions": [ { "confidence": 0.9, "masks": [ [[100,100],[200,100],[200,200],[100,200]] ] } ] },
              { "echo": { "text": "rutting" }, "predictions": [] } ] }
            """);
        var options = Options.Create(new AiOptions { Provider = AiProvider.Meta });
        options.Value.Meta.Segmentation.ApiKey = "test-key";
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://serverless.roboflow.com/") };
        var segmenter = new RoboflowSam3Segmenter(http, options);

        var image = new ImagePayload(new byte[] { 1, 2, 3 }, "image/jpeg", W, H, "abc");
        var result = await segmenter.SegmentAsync(new SegmentationRequest(image, ["pothole", "rutting"]), TestContext.Current.CancellationToken);

        Assert.Equal("https://serverless.roboflow.com/sam3/concept_segment", handler.Uri!.ToString());
        Assert.Equal("Bearer test-key", handler.Authorization);
        var sent = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("base64", sent["image"]!["type"]!.GetValue<string>());
        Assert.Equal("AQID", sent["image"]!["value"]!.GetValue<string>());
        Assert.Equal(["pothole", "rutting"], sent["prompts"]!.AsArray().Select(p => p!["text"]!.GetValue<string>()));
        Assert.Equal("polygon", sent["format"]!.GetValue<string>());
        var segment = Assert.Single(result.Segments);
        Assert.Equal(0, segment.Index);
        Assert.Equal("sam3", result.Model);
    }

    [Fact]
    public async Task MapsHttpErrorsToModelUnavailable()
    {
        var handler = new CapturingHandler("{}", HttpStatusCode.Unauthorized);
        var options = Options.Create(new AiOptions { Provider = AiProvider.Meta });
        var segmenter = new RoboflowSam3Segmenter(new HttpClient(handler) { BaseAddress = new Uri("https://x.test/") }, options);
        var image = new ImagePayload(new byte[] { 1 }, "image/jpeg", W, H, "abc");

        await Assert.ThrowsAsync<ModelUnavailableException>(() =>
            segmenter.SegmentAsync(new SegmentationRequest(image, ["pothole"]), TestContext.Current.CancellationToken));
    }

    private static Sam3Instances.Instance Instance(string concept, double score, double x, double y, double size) =>
        new(concept, score,
        [
            new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)
        ]);

    private sealed class CapturingHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
