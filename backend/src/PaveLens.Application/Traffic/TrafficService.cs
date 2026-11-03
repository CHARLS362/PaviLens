using System.Collections.Concurrent;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using PaveLens.Domain.Inspections;

namespace PaveLens.Application.Traffic;

/// <param name="DurationH">Hours the work zone stays in place; scales the user cost.</param>
public sealed record SimulateWorkZoneCommand(
    string SegmentId, int LanesClosed, WorkPeriod Period = WorkPeriod.Peak, double DurationH = 8,
    ReportLanguage Language = ReportLanguage.Es);

public sealed record LinkComparisonDto(
    string SegmentId,
    string From,
    string To,
    double CapacityVph,
    double BaselineFlowVph,
    double BaselineTimeMin,
    double WorkZoneCapacityVph,
    double WorkZoneFlowVph,
    double WorkZoneTimeMin,
    bool Closed);

public sealed record ScenarioSummaryDto(double TotalTravelTimeVehH, int Iterations, double RelativeGap);

/// <summary>The same closure evaluated in one time window.</summary>
public sealed record WorkWindowDto(WorkPeriod Period, double DemandFactor, double DeltaVehH, double DeltaPct, double UserCostPen, int OverCapacityLinks);

public sealed record TrafficSimulationDto(
    string SegmentId,
    string SegmentName,
    int LanesClosed,
    int LanesPerDirection,
    WorkPeriod Period,
    double DemandFactor,
    double DurationH,
    double ValueOfTimePenPerVehH,
    ScenarioSummaryDto Baseline,
    ScenarioSummaryDto WorkZone,
    double DeltaVehH,
    double DeltaPct,
    double UserCostPen,
    int OverCapacityLinks,
    IReadOnlyList<LinkComparisonDto> Links,
    IReadOnlyList<string> DetourSegmentIds,
    IReadOnlyList<WorkWindowDto> Windows,
    WorkPeriod RecommendedPeriod,
    IReadOnlyList<string> Notes,
    IReadOnlyList<DetourRouteDto> DetourRoutes);

/// <summary>Compares the equilibrium with and without a work zone on the demo network.</summary>
public sealed class TrafficService
{
    private readonly RoadNetwork _network = PunoDemoNetwork.Create();
    private readonly TrafficOptions _options;

    // The baseline of a period never changes for a fixed network, so each is solved once.
    private readonly ConcurrentDictionary<WorkPeriod, Lazy<AssignmentResult>> _baselines = new();

    public TrafficService() : this(Options.Create(new TrafficOptions()))
    {
    }

    public TrafficService(IOptions<TrafficOptions> options)
    {
        _options = options.Value;
    }

    public RoadNetwork Network => _network;

    public TrafficSimulationDto Simulate(SimulateWorkZoneCommand command)
    {
        var segment = _network.Segments.FirstOrDefault(s => s.Id == command.SegmentId)
            ?? throw Invalid("segmentId", $"El tramo '{command.SegmentId}' no existe en la red.");
        if (command.LanesClosed < 1 || command.LanesClosed > segment.LanesPerDirection)
        {
            throw Invalid("lanesClosed", segment.LanesPerDirection == 1
                ? "Ese tramo tiene 1 carril por sentido; solo se puede cerrar 1."
                : $"Ese tramo tiene {segment.LanesPerDirection} carriles por sentido; cierra entre 1 y {segment.LanesPerDirection}.");
        }

        if (command.DurationH is <= 0 or > 24 * 30)
        {
            throw Invalid("durationH", "La duración de la obra debe estar entre 0 y 720 horas.");
        }

        if (!Enum.IsDefined(command.Period))
        {
            throw Invalid("period", "Franja horaria no válida.");
        }

        var zone = new WorkZone(segment.Id, command.LanesClosed);
        var runs = Enum.GetValues<WorkPeriod>().ToDictionary(p => p, p => Run(p, zone));
        var (baseline, workZone) = runs[command.Period];

        var after = workZone.Links.ToDictionary(l => (l.SegmentId, l.From, l.To));
        var links = baseline.Links.Select(b =>
        {
            var w = after.GetValueOrDefault((b.SegmentId, b.From, b.To));
            return new LinkComparisonDto(
                b.SegmentId, b.From, b.To, b.CapacityVph,
                Math.Round(b.FlowVph, 1), Math.Round(b.TimeH * 60, 3),
                w?.CapacityVph ?? 0, Math.Round(w?.FlowVph ?? 0, 1), Math.Round((w?.TimeH ?? 0) * 60, 3),
                w is null);
        }).ToList();

        // Detour: segments absorbing a meaningful share of the displaced traffic.
        var detour = links
            .Where(l => l.SegmentId != segment.Id && l.WorkZoneFlowVph - l.BaselineFlowVph > Math.Max(25, 0.05 * l.BaselineFlowVph))
            .Select(l => l.SegmentId)
            .Distinct()
            .ToList();

        var windows = runs.Select(r => Window(r.Key, r.Value.Baseline, r.Value.WorkZone, command.DurationH)).ToList();
        var selected = windows.Single(w => w.Period == command.Period);
        var recommended = windows.MinBy(w => w.UserCostPen)!.Period;

        return new TrafficSimulationDto(
            segment.Id,
            segment.Name,
            command.LanesClosed,
            segment.LanesPerDirection,
            command.Period,
            selected.DemandFactor,
            command.DurationH,
            _options.ValueOfTimePenPerVehH,
            Summary(baseline),
            Summary(workZone),
            selected.DeltaVehH,
            selected.DeltaPct,
            selected.UserCostPen,
            selected.OverCapacityLinks,
            links,
            detour,
            windows,
            recommended,
            [
                "Red y demanda sintéticas de demostración: los valores ilustran el método, no la ciudad real.",
                "BPR (α = 0.15, β = 4) es una función de demora para planificación; no modela colas ni ondas de congestión.",
                "Equilibrio de usuario de Wardrop resuelto con Frank-Wolfe.",
                $"Perfil horario ilustrativo: valle {_options.OffPeakDemandFactor:P0} y noche {_options.NightDemandFactor:P0} de la demanda punta.",
                $"Costo para los usuarios = Δ veh·h por hora × {command.DurationH:0.#} h × S/ {_options.ValueOfTimePenPerVehH:0.00} por veh·h (valor referencial; usar el valor social del tiempo vigente del MEF).",
                "Rutas de desvío: camino más rápido alrededor del tramo cerrado (Dijkstra) con los tiempos de viaje del escenario con obra."
            ],
            DetourRouteFinder.Find(_network, links, segment.Id));
    }

    /// <summary>Fastest route and alternatives per direction, offered to the traffic planner to choose from.</summary>
    public IReadOnlyList<DetourRouteDto> CandidateRoutes(TrafficSimulationDto simulation) =>
        DetourRouteFinder.Find(_network, simulation.Links, simulation.SegmentId, alternatives: 2);

    private (AssignmentResult Baseline, AssignmentResult WorkZone) Run(WorkPeriod period, WorkZone zone)
    {
        var network = Scaled(period);
        var baseline = _baselines.GetOrAdd(period, p => new Lazy<AssignmentResult>(() => TrafficAssignment.Solve(Scaled(p)))).Value;
        try
        {
            return (baseline, TrafficAssignment.Solve(network, zone));
        }
        catch (InvalidOperationException ex)
        {
            throw Invalid("segmentId", ex.Message);
        }
    }

    private RoadNetwork Scaled(WorkPeriod period)
    {
        var factor = _options.DemandFactor(period);
        return _network with { Demand = _network.Demand.Select(d => d with { Vph = d.Vph * factor }).ToList() };
    }

    private WorkWindowDto Window(WorkPeriod period, AssignmentResult baseline, AssignmentResult workZone, double durationH)
    {
        // Flows are hourly, so the totals are vehicle-hours spent per hour of operation.
        var delta = workZone.TotalTravelTimeVehH - baseline.TotalTravelTimeVehH;
        return new WorkWindowDto(
            period,
            _options.DemandFactor(period),
            Math.Round(delta, 2),
            Math.Round(100 * delta / baseline.TotalTravelTimeVehH, 2),
            Math.Round(delta * durationH * _options.ValueOfTimePenPerVehH, 2),
            workZone.Links.Count(l => l.FlowVph > l.CapacityVph));
    }

    private static ScenarioSummaryDto Summary(AssignmentResult r) =>
        new(Math.Round(r.TotalTravelTimeVehH, 2), r.Iterations, Math.Round(r.RelativeGap, 6));

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
