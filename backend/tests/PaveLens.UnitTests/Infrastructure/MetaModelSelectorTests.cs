using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaveLens.Infrastructure.Ai;
using PaveLens.Infrastructure.Ai.Meta;

namespace PaveLens.UnitTests.Infrastructure;

public class MetaModelSelectorTests
{
    private static readonly string[] Available =
    [
        "muse-wonder-1.0", "sam-3.1", "muse-spark-1.3-contributor", "muse-voice-transcribe-1.0",
        "muse-spark-1.3", "muse-image-1.0", "muse-spark-1.2", "muse-spark-1.10"
    ];

    [Theory]
    [InlineData(new[] { "muse-wonder", "muse-spark" }, "muse-wonder-1.0")]
    [InlineData(new[] { "muse-spark" }, "muse-spark-1.10")]
    [InlineData(new[] { "sam" }, "sam-3.1")]
    [InlineData(new[] { "llama" }, null)]
    public void PicksTheNewestVersionOfTheFirstAvailableFamily(string[] families, string? expected) =>
        Assert.Equal(expected, MetaModelSelector.Pick(Available, families));

    [Fact]
    public void NeverPicksContributorModels() =>
        Assert.Null(MetaModelSelector.Pick(["muse-spark-1.3-contributor"], ["muse-spark"]));

    [Theory]
    [InlineData("k", null, AiProvider.Meta, SegmentationService.Meta)]
    [InlineData("k", "rf", AiProvider.Meta, SegmentationService.Roboflow)]
    [InlineData(null, null, AiProvider.Simulation, SegmentationService.Meta)]
    public void AutoUsesWhateverKeysAreConfigured(string? metaKey, string? roboflowKey, AiProvider provider, SegmentationService segmentation)
    {
        var options = new AiOptions();
        options.Meta.Reasoning.ApiKey = metaKey;
        options.Meta.Segmentation.ApiKey = roboflowKey;
        options.Resolve();
        Assert.Equal(provider, options.Provider);
        Assert.Equal(segmentation, options.Meta.SegmentationService);
    }

    [Fact]
    public void ExplicitChoicesAreKept()
    {
        var options = new AiOptions { Provider = AiProvider.Simulation };
        options.Meta.Reasoning.ApiKey = "k";
        options.Meta.SegmentationService = SegmentationService.Sidecar;
        options.Resolve();
        Assert.Equal(AiProvider.Simulation, options.Provider);
        Assert.Equal(SegmentationService.Sidecar, options.Meta.SegmentationService);
    }

    [Fact]
    public async Task StartupReplacesAutoModelsByTheBestOnesTheKeyCanUse()
    {
        var options = MetaOptions(fixedReasoning: null);
        using var handler = new ModelsHandler(HttpStatusCode.OK, """{"data":[{"id":"muse-spark-1.3"},{"id":"sam-3.1"},{"id":"muse-wonder-1.0"}]}""");
        await Selector(handler, options).StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal("muse-wonder-1.0", options.Meta.Reasoning.Model);
        Assert.Equal("sam-3.1", options.Meta.Sam31.Model);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
    }

    [Fact]
    public async Task AnExplicitModelIsKeptAndAFailedListingFallsBackToDefaults()
    {
        var options = MetaOptions(fixedReasoning: "muse-spark-1.2");
        using var handler = new ModelsHandler(HttpStatusCode.Unauthorized, "{}");
        await Selector(handler, options).StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal("muse-spark-1.2", options.Meta.Reasoning.Model);
        Assert.Equal(MetaModelSelector.FallbackSegmentation, options.Meta.Sam31.Model);
    }

    private static AiOptions MetaOptions(string? fixedReasoning)
    {
        var options = new AiOptions();
        options.Meta.Reasoning.ApiKey = "test-key";
        if (fixedReasoning is not null) options.Meta.Reasoning.Model = fixedReasoning;
        return options.Resolve();
    }

    private static MetaModelSelector Selector(ModelsHandler handler, AiOptions options) =>
        new(new SingleClientFactory(handler), Options.Create(options), NullLogger<MetaModelSelector>.Instance);

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ModelsHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.meta.ai/v1/models", request.RequestUri!.ToString());
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
