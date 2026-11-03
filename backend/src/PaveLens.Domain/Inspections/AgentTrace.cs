namespace PaveLens.Domain.Inspections;

/// <summary>Observable actions and evidence; never stores a model's private reasoning or credentials.</summary>
/// <param name="Summary">One line in the report language saying what the step found, for people rather than auditors.</param>
/// <param name="Tokens">Meta model tokens spent inside this step (specialists), 0 for code.</param>
public sealed record AgentActionTrace(string Agent, string Tool, string Model, string Status, long DurationMs, string Evidence,
    string? Summary = null, long Tokens = 0);

/// <param name="Warnings">Assumptions of the method (calibration, assumed depth, reference profile...), raised by code.</param>
/// <param name="ReviewIssues">What the independent reviewer found beyond those assumptions; it alone decides <c>NeedsReview</c>.</param>
/// <param name="ModelCalls">Every Meta call of the run (coordinator, specialists and SAM); tokens likewise.</param>
public sealed record AgentRunTrace(
    Guid RunId, string Engine, string CoordinatorModel, string Status,
    string RegionalProfileJson, double? Latitude, double? Longitude,
    IReadOnlyList<AgentActionTrace> Actions, IReadOnlyList<string> Warnings,
    int ModelCalls = 0, long InputTokens = 0, long OutputTokens = 0,
    string? MobilityJson = null, IReadOnlyList<string>? ReviewIssues = null);
