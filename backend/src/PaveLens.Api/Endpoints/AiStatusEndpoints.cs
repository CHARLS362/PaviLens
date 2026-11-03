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
        return TypedResults.Ok(new AiStatus(
            "Meta",
            new ModelStatus(
                "SAM 3",
                meta.Segmentation.Model,
                meta.SegmentationService.ToString(),
                meta.Segmentation.BaseUrl?.Host,
                !string.IsNullOrWhiteSpace(meta.Segmentation.ApiKey)),
            new ModelStatus(
                "Muse Wonder",
                meta.Reasoning.Model,
                "OpenAI-compatible",
                meta.Reasoning.BaseUrl?.Host,
                !string.IsNullOrWhiteSpace(meta.Reasoning.ApiKey))));
    }

    internal sealed record AiStatus(string Provider, ModelStatus? Segmentation, ModelStatus? Reasoning);

    /// <param name="Host">Host only: paths and query strings may carry tokens.</param>
    internal sealed record ModelStatus(string Name, string Model, string Service, string? Host, bool KeyConfigured);
}
