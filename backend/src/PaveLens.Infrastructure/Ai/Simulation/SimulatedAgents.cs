using System.Text.Json;
using PaveLens.Application.Common;
using PaveLens.Application.Orchestration;

namespace PaveLens.Infrastructure.Ai.Simulation;

/// <summary>Exercises the same tools without pretending a model selected the plan.</summary>
internal sealed class SimulatedInspectionCoordinator : IInspectionCoordinator
{
    public string Engine => "Simulation";
    public string Model => "deterministic-demo-coordinator";
    public async Task<AgentUsage> RunAsync(string task, IReadOnlyList<AgentTool> tools, ExecuteAgentTool execute, CancellationToken cancellationToken)
    {
        foreach (var name in InspectionTools.Order)
        {
            var result = await execute(name, JsonSerializer.SerializeToElement(new { }), cancellationToken);
            if (!result.Success) throw new ModelOutputException(result.Evidence);
        }
        return new AgentUsage(Model, 0, 0, 0);
    }
}

internal sealed class SimulatedInspectionAuditor : IInspectionAuditor
{
    public Task<AgentAudit> ReviewAsync(string evidence, CancellationToken cancellationToken) =>
        Task.FromResult(new AgentAudit("NeedsReview", ["Auditoría de demostración: no se ha consultado un modelo Meta real."], "deterministic-demo-auditor"));
}
