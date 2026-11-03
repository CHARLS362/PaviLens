using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using PaveLens.Infrastructure.Ai;
using PaveLens.Infrastructure.Ai.Meta;

namespace PaveLens.UnitTests.Infrastructure;

public class MetaSam31Tests
{
    [Fact]
    public async Task CallsMetaResponsesWithOneConceptAndNormalizesDecodedMasks()
    {
        using var handler = new SamHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.meta.ai/v1/") };
        var configuration = new AiOptions(); configuration.Meta.Reasoning.ApiKey = "test-key";
        var segmenter = new MetaSam31Segmenter(http, Options.Create(configuration), new Decoder());
        var result = await segmenter.SegmentAsync(new SegmentationRequest(new ImagePayload(new byte[] { 1 }, "image/jpeg", 100, 100, "test"), ["pothole"]), TestContext.Current.CancellationToken);
        var segment = Assert.Single(result.Segments);
        Assert.Equal("sam-3.1", result.Model); Assert.Equal("pothole", segment.Concept);
        Assert.Equal(.1, segment.Polygon[0].X); Assert.Equal(.2, segment.Polygon[0].Y);
        Assert.Equal("https://api.meta.ai/v1/responses", handler.Url);
        Assert.Equal("test-key", handler.Token);
        Assert.Equal("true", handler.Body!["metadata"]!["include_confidence"]!.GetValue<string>());
        Assert.Equal("pothole", handler.Body["input"]![0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task KeepsHolesSoTheirAreaIsSubtracted()
    {
        using var handler = new SamHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var segmenter = new MetaSam31Segmenter(http, Options.Create(new AiOptions()), new Decoder { Holes = true });
        var result = await segmenter.SegmentAsync(new SegmentationRequest(new ImagePayload(ReadOnlyMemory<byte>.Empty, "image/jpeg", 100, 100, "test"), ["alligator crack"]), TestContext.Current.CancellationToken);
        var segment = Assert.Single(result.Segments);
        var hole = Assert.Single(segment.Holes!);
        Assert.Equal(0.15, hole[0].X, 9);
    }

    [Fact]
    public async Task DropsSpecksOfAFewPixels()
    {
        using var handler = new SamHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var segmenter = new MetaSam31Segmenter(http, Options.Create(new AiOptions()), new Decoder { Speck = true });
        var result = await segmenter.SegmentAsync(new SegmentationRequest(new ImagePayload(ReadOnlyMemory<byte>.Empty, "image/jpeg", 100, 100, "test"), ["crack"]), TestContext.Current.CancellationToken);
        Assert.Single(result.Segments);
    }

    private sealed class Decoder : ISam31Decoder
    {
        public bool Holes { get; init; }
        public bool Speck { get; init; }
        public Task<Sam31Geometry> DecodeAsync(string response, CancellationToken cancellationToken) => Task.FromResult(new Sam31Geometry([new(.9,
            Holes ? [[[10,20],[30,20],[30,40],[10,40]], [[15,25],[25,25],[25,35],[15,35]]]
            : Speck ? [[[10,20],[30,20],[30,40],[10,40]], [[50,50],[52,50],[52,52],[50,52]]]
            : [[[10,20],[30,20],[30,40],[10,40]]], Holes)]));
    }
    private sealed class SamHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Token { get; private set; }
        public JsonObject? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString(); Token = request.Headers.Authorization?.Parameter;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
