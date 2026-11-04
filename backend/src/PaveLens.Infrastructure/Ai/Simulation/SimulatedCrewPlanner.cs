using System.Globalization;
using PaveLens.Application.Maintenance;
using PaveLens.Domain.Inspections;

namespace PaveLens.Infrastructure.Ai.Simulation;

/// <summary>Deterministic day plan for demo mode: keeps the code's order and applies fixed closure rules.</summary>
internal sealed class SimulatedCrewPlanner : ICrewDayPlanner
{
    public AnalysisEngine Engine => AnalysisEngine.Simulation;

    public Task<CrewPlanResult> PlanAsync(CrewPlanRequest request, CancellationToken cancellationToken)
    {
        var en = request.Language == ReportLanguage.En;
        var clock = TimeSpan.FromHours(9); // after the morning peak
        var sites = new List<CrewSitePlan>();
        foreach (var s in request.Sites)
        {
            clock += TimeSpan.FromMinutes(Math.Ceiling(s.LegDurationMin));
            var total = s.Pci is < 25 || s.DistressCount >= 6;
            var work = TimeSpan.FromMinutes(total ? 120 : 75);
            var window = string.Create(CultureInfo.InvariantCulture, $"{clock:hh\\:mm}–{clock + work:hh\\:mm}");
            clock += work;
            sites.Add(new CrewSitePlan(s.Id,
                total ? (en ? "Full closure with detour" : "Cierre total con desvío") : (en ? "One lane closed, alternating flow" : "Cierre de un carril con paso alterno"),
                window,
                total
                    ? (en ? "Extensive damage: the whole width is patched at once." : "Deterioro extenso: se interviene todo el ancho de una vez.")
                    : (en ? "Isolated damage: one lane is enough." : "Deterioro aislado: basta con un carril.")));
        }
        var summary = en
            ? $"Demo plan: {request.Sites.Count} sites in the code's order, worst pavement first, starting after the morning peak."
            : $"Plan de demostración: {request.Sites.Count} obras en el orden del código, primero el pavimento más dañado, desde el fin de la hora punta.";
        return Task.FromResult(new CrewPlanResult("simulation", summary, request.Sites.Select(s => s.Id).ToList(), sites,
            [en ? "Simulated plan: no Meta model was called." : "Plan simulado: no se consultó un modelo de Meta."],
            [en ? "Notify neighbours the day before." : "Avisar a los vecinos el día anterior."]));
    }
}
