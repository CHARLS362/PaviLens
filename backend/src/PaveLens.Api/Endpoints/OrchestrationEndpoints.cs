using PaveLens.Application.Geography;
using PaveLens.Application.Orchestration;

namespace PaveLens.Api.Endpoints;

public static class OrchestrationEndpoints
{
    public static RouteGroupBuilder MapOrchestrationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/regions", (RegionalCatalog regions) => regions.List()).WithTags("Regional profiles");
        api.MapGet("/geography/network", async (double latitude, double longitude, IGeographicNetworkProvider provider, CancellationToken cancellationToken, int radiusM = 600) =>
            await provider.LoadAsync(latitude, longitude, radiusM, cancellationToken))
            .RequireRateLimiting(RateLimitPolicies.Ai).WithTags("Geography");
        api.MapPost("/geography/networks/{id}/closure", (string id, GeographicClosureRequest request, IGeographicNetworkProvider provider) =>
            GeographicRouting.Close(provider.FindSnapshot(id), request.EdgeId)).WithTags("Geography");
        return api;
    }

    public sealed record GeographicClosureRequest(string EdgeId);
}
