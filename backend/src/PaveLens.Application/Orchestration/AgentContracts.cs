using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PaveLens.Application.Orchestration;

public sealed class OrchestrationOptions
{
    public const string SectionName = "Orchestration";
    [Range(9, 40)] public int MaxToolCalls { get; set; } = 16;
    [Range(30, 900)] public int TimeoutSeconds { get; set; } = 300;
    [Range(512, 8192)] public int MaxOutputTokens { get; set; } = 2048;
    [Range(1, 3)] public int MaxAttemptsPerTool { get; set; } = 2;
    [Range(10, 500)] public int MaxSegments { get; set; } = 200;
}

public sealed record AgentTool(string Name, string Description, JsonElement Parameters);
public sealed record AgentToolResult(bool Success, string Evidence);
public sealed record AgentUsage(string Model, int Calls, long InputTokens, long OutputTokens);
public delegate Task<AgentToolResult> ExecuteAgentTool(string name, JsonElement arguments, CancellationToken cancellationToken);

public interface IInspectionCoordinator
{
    string Engine { get; }
    string Model { get; }
    Task<AgentUsage> RunAsync(string task, IReadOnlyList<AgentTool> tools, ExecuteAgentTool execute, CancellationToken cancellationToken);
}

public sealed record AgentAudit(string Status, IReadOnlyList<string> Issues, string Model);
public interface IInspectionAuditor
{
    Task<AgentAudit> ReviewAsync(string evidence, CancellationToken cancellationToken);
}

public static class InspectionTools
{
    public static readonly string[] Order = ["load_region", "load_city_network", "segment_image", "measure_segments", "diagnose_distresses", "compute_report", "assess_mobility", "verify_report", "finish"];
    public static IReadOnlyList<AgentTool> Definitions { get; } = Order.Select(name => new AgentTool(name, name switch
    {
        "load_region" => "Load the immutable regional profile and its sources before any diagnosis or cost calculation.",
        "load_city_network" => "Retrieve a real directed street network when explicit inspection coordinates exist. Otherwise record missing coordinates; never infer a photo position from the regional center.",
        "segment_image" => "Call the perception specialist to segment the photo with every enabled visual concept (pothole, crack, cracked asphalt, rutting); the diagnosis assigns the distress type. Takes no arguments. Repeating this tool invalidates all dependent results.",
        "measure_segments" => "Measure every mask through the validated ground projection, using code. Requires segmentation.",
        "diagnose_distresses" => "Delegate all measured segments to the diagnostic specialist. Requires region and measurements. Every segment must receive a finding.",
        "compute_report" => "Compute quantities and PCI using the regional configuration and deterministic code. Requires diagnosis.",
        "assess_mobility" => "Delegate closure planning to the mobility specialist when an assigned demo segment is applicable; otherwise record the missing network or traffic evidence explicitly.",
        "verify_report" => "Run deterministic checks and an independent specialist review. Requires computed report and mobility assessment.",
        _ => "Finish only after all required tools have succeeded and the verification result is recorded. NeedsReview is a valid preliminary report outcome."
    }, JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, additionalProperties = false }))).ToArray();
}
