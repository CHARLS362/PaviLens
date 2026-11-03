using PaveLens.Domain.Inspections;
using PaveLens.Application.Traffic;
using Microsoft.AspNetCore.Http.HttpResults;

namespace PaveLens.Api.Endpoints;

internal static class TrafficEndpoints
{
    public static RouteGroupBuilder MapTrafficEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/network", GetNetwork)
            .WithTags("Traffic")
            .WithSummary("Red vial sintética de demostración (nodos, tramos y demanda)");

        api.MapPost("/traffic/simulate", Simulate)
            .WithTags("Traffic")
            .WithSummary("Compara el equilibrio de Wardrop con y sin cierre de carriles en un tramo");

        api.MapPost("/traffic/plan", PlanAsync)
            .WithTags("Traffic")
            .RequireRateLimiting(RateLimitPolicies.Ai)
            .WithSummary("Plan de manejo de tránsito razonado por Muse Wonder sobre el equilibrio y las rutas candidatas");

        return api;
    }

    private static async Task<Ok<TrafficPlanDto>> PlanAsync(SimulateRequest request, TrafficPlanService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.PlanAsync(new SimulateWorkZoneCommand(
            request.SegmentId, request.LanesClosed, request.Period ?? WorkPeriod.Peak, request.DurationH ?? 8,
            request.Language ?? ReportLanguage.Es), cancellationToken));

    private static Ok<RoadNetwork> GetNetwork(TrafficService service) => TypedResults.Ok(service.Network);

    private static Ok<TrafficSimulationDto> Simulate(SimulateRequest request, TrafficService service) =>
        TypedResults.Ok(service.Simulate(new SimulateWorkZoneCommand(
            request.SegmentId, request.LanesClosed, request.Period ?? WorkPeriod.Peak, request.DurationH ?? 8)));

    /// <param name="Period">Peak (default), OffPeak or Night.</param>
    /// <param name="DurationH">Hours the work zone stays in place (default 8).</param>
    /// <param name="Language">Es (default) or En: the language of the plan written by the planner.</param>
    internal sealed record SimulateRequest(string SegmentId, int LanesClosed, WorkPeriod? Period, double? DurationH, ReportLanguage? Language = null);
}
