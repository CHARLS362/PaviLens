using PaveLens.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace PaveLens.Api.Endpoints;

internal static class AiStatusEndpoints
{
    public static RouteGroupBuilder MapAiStatusEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/ai/status", GetStatus)
            .WithTags("AI")
            .WithSummary("Modelos activos y si sus claves están configuradas. Nunca devuelve las claves.");

        return api;
    }

    private static Ok<AiStatus> GetStatus(IOptions<AiOptions> options)
    {
        var o = options.Value;
        if (o.Provider == AiProvider.Simulation)
        {
            return TypedResults.Ok(new AiStatus("Simulation", null, null));
        }

        var meta = o.Meta;
        var segmentation = meta.SegmentationService == SegmentationService.Meta ? meta.Sam31 : meta.Segmentation;
        return TypedResults.Ok(new AiStatus(
            "Meta",
            new ModelStatus(
                meta.SegmentationService == SegmentationService.Meta ? DisplayName(meta.Sam31.Model, "SAM 3.1") : "SAM 3",
                segmentation.Model,
                meta.SegmentationService.ToString(),
                segmentation.BaseUrl?.Host,
                !string.IsNullOrWhiteSpace(segmentation.ApiKey) || meta.SegmentationService == SegmentationService.Meta && !string.IsNullOrWhiteSpace(meta.Reasoning.ApiKey)),
            new ModelStatus(
                DisplayName(meta.Reasoning.Model, "Muse Wonder"),
                meta.Reasoning.Model,
                "OpenAI-compatible",
                meta.Reasoning.BaseUrl?.Host,
                !string.IsNullOrWhiteSpace(meta.Reasoning.ApiKey))));
    }

    /// <summary>"muse-spark-1.3" → "Muse Spark 1.3", "sam-3.1" → "SAM 3.1".</summary>
    private static string DisplayName(string model, string fallback)
    {
        var parts = model.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || model == AiOptions.AutoModel) return fallback;
        return string.Join(' ', parts.Select(p => p.Equals("sam", StringComparison.OrdinalIgnoreCase) ? "SAM" : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    internal sealed record AiStatus(string Provider, ModelStatus? Segmentation, ModelStatus? Reasoning);

    /// <param name="Host">Host only: paths and query strings may carry tokens.</param>
    internal sealed record ModelStatus(string Name, string Model, string Service, string? Host, bool KeyConfigured);
}
