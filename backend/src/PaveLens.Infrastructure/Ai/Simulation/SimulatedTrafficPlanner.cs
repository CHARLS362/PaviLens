using System.Globalization;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Traffic;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Simulation;

/// <summary>
/// Rule-based plan from the code-computed candidates, without calling Muse Wonder: the fastest detour per direction
/// for everyone, signage at the closure ends and at every turn, and fixed phases. Labelled as simulation.
/// </summary>
internal sealed class SimulatedTrafficPlanner : ITrafficPlanner
{
    public const string Model = "simulation/traffic-rules";

    public AnalysisEngine Engine => AnalysisEngine.Simulation;

    public Task<TrafficPlanResult> PlanAsync(TrafficPlanRequest request, CancellationToken cancellationToken)
    {
        var inv = CultureInfo.InvariantCulture;
        var sim = request.Simulation;
        var network = request.Network;
        var nodes = network.Nodes.ToDictionary(n => n.Id);
        var names = network.Segments.ToDictionary(s => s.Id, s => s.Name);
        var closed = network.Segments.Single(s => s.Id == sim.SegmentId);
        var full = sim.LanesClosed == sim.LanesPerDirection;
        var en = request.Language == ReportLanguage.En;

        var primary = request.Candidates.Where(c => !char.IsLetter(c.Id[^1])).ToList();
        var routes = primary.Select(c =>
        {
            var streets = c.SegmentIds.Select(id => names[id]).Distinct().ToList();
            return en
                ? new PlannedRoute(c.Id, $"{EnglishLabel(c.Label)} via {string.Join(", ", streets)}", "All", c.Nodes,
                    string.Create(inv, $"Fastest path with the work zone: +{c.ExtraTimeMin:0.#} min versus going through {sim.SegmentName}."))
                : new PlannedRoute(c.Id, $"{c.Label} por {string.Join(", ", streets)}", "Todos", c.Nodes,
                    string.Create(inv, $"Camino más rápido con la obra: +{c.ExtraTimeMin:0.#} min frente a pasar por {sim.SegmentName}."));
        }).ToList();

        var regulatory = en ? "Regulatory" : "Reglamentaria";
        var closure = en
            ? (full ? $"{sim.SegmentName} CLOSED · follow the detour" : $"Lane closed on {sim.SegmentName} · slow to 30 km/h")
            : (full ? $"{sim.SegmentName} CERRADO · siga el desvío" : $"Carril cerrado en {sim.SegmentName} · reduzca a 30 km/h");
        var signage = new List<PlannedSign>
        {
            new(closed.From, regulatory, closure),
            new(closed.To, regulatory, closure)
        };
        foreach (var route in routes)
        {
            // A turn is any intermediate node where the street changes.
            for (var i = 1; i < route.Nodes.Count - 1; i++)
            {
                var before = Street(network, route.Nodes[i - 1], route.Nodes[i]);
                var after = Street(network, route.Nodes[i], route.Nodes[i + 1]);
                if (before != after && signage.All(s => s.NodeId != route.Nodes[i]))
                {
                    signage.Add(en
                        ? new PlannedSign(route.Nodes[i], "Guide", $"DETOUR → {after}")
                        : new PlannedSign(route.Nodes[i], "Informativa", $"DESVÍO → {after}"));
                }
            }
        }

        var recommended = Label(sim.RecommendedPeriod, en);
        var phases = en
            ? new List<PlannedPhase>
            {
                new(1, "Public notice", "48 h before", $"Announce the closure of {sim.SegmentName} and the detour routes."),
                new(2, "Device installation", "1 h before", "Warning signs at 50 m, cones and barriers at both ends of the work zone."),
                new(3, "Roadworks", recommended, string.Create(inv, $"Planned duration {sim.DurationH:0.#} h with flaggers at both ends.")),
                new(4, "Removal and reopening", "When finished", "Remove the devices in reverse order and check the surface before reopening.")
            }
            : new List<PlannedPhase>
            {
                new(1, "Aviso a la población", "48 h antes", $"Comunicar el cierre de {sim.SegmentName} y las rutas de desvío."),
                new(2, "Instalación de dispositivos", "1 h antes", "Señales preventivas a 50 m, conos y barreras en los extremos de la obra."),
                new(3, "Ejecución de la obra", recommended, string.Create(inv, $"Duración prevista {sim.DurationH:0.#} h con controladores en los extremos.")),
                new(4, "Retiro y reapertura", "Al terminar", "Retirar dispositivos en orden inverso y verificar la superficie antes de abrir.")
            };

        var risks = new List<string>();
        if (sim.OverCapacityLinks > 0)
        {
            risks.Add(en
                ? string.Create(inv, $"{Count(sim.OverCapacityLinks, "direction exceeds its", "directions exceed their")} capacity: consider flaggers at the detour intersections.")
                : string.Create(inv, $"{Count(sim.OverCapacityLinks, "sentido supera su", "sentidos superan su")} capacidad: considerar controladores en las intersecciones del desvío."));
        }

        if (sim.RecommendedPeriod == WorkPeriod.Night)
        {
            risks.Add(en
                ? "Night work: lighting, reflective vests and frost control for the prime coat."
                : "Trabajo nocturno: iluminación, chalecos reflectivos y control de heladas para la imprimación.");
        }

        var summary = en
            ? string.Create(inv,
                $"[Simulation] {(full ? "Full closure" : $"Closure of {Count(sim.LanesClosed, "lane", "lanes")} per direction")} of {sim.SegmentName}. " +
                $"Traffic is diverted along {Count(routes.Count, "route", "routes")} computed by the equilibrium model. " +
                $"Recommended window: {recommended.ToLowerInvariant()}. Add META_API_KEY to .env for the plan reasoned by Muse.")
            : string.Create(inv,
                $"[Simulación] {(full ? "Cierre total" : $"Cierre de {Count(sim.LanesClosed, "carril", "carriles")} por sentido")} de {sim.SegmentName}. " +
                $"El tránsito se desvía por {Count(routes.Count, "ruta calculada", "rutas calculadas")} por el modelo de equilibrio. " +
                $"Franja recomendada: {recommended.ToLowerInvariant()}. Añade META_API_KEY al .env para el plan razonado por Muse.");

        List<string> communication = en
            ? [$"Radio and municipal social media notice about the closure of {sim.SegmentName}.", "Coordination with urban bus companies to change their routes."]
            : [$"Aviso radial y en redes de la municipalidad sobre el cierre de {sim.SegmentName}.", "Coordinación con empresas de transporte urbano para el cambio de recorrido."];

        return Task.FromResult(new TrafficPlanResult(Model, summary, routes, signage, phases, communication, risks));
    }

    /// <summary>Candidate labels come from the code in Spanish ("Desvío hacia el norte"); the plan text needs them in English.</summary>
    private static string EnglishLabel(string label) =>
        label.Replace("Desvío", "Detour", StringComparison.Ordinal)
            .Replace("Alterna", "Alternative", StringComparison.Ordinal)
            .Replace(" hacia el ", " heading ", StringComparison.Ordinal)
            .Replace("norte", "north", StringComparison.Ordinal)
            .Replace("sur", "south", StringComparison.Ordinal)
            .Replace("oeste", "west", StringComparison.Ordinal) // before "este", which it contains
            .Replace("este", "east", StringComparison.Ordinal);

    private static string Count(int n, string one, string many) =>
        n == 1 ? $"1 {one}" : string.Create(CultureInfo.InvariantCulture, $"{n} {many}");

    private static string Street(RoadNetwork network, string a, string b) =>
        network.Segments.First(s => (s.From == a && s.To == b) || (s.From == b && s.To == a)).Name;

    private static string Label(WorkPeriod period, bool en) => period switch
    {
        WorkPeriod.Night => en ? "Night (22–05 h)" : "Noche (22–05 h)",
        WorkPeriod.OffPeak => en ? "Off-peak (09–17 h)" : "Valle (09–17 h)",
        _ => en ? "Peak hour" : "Hora punta"
    };
}
