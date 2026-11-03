namespace PaveLens.Domain.Inspections;

/// <summary>Observable actions and evidence; never stores a model's private reasoning or credentials.</summary>
public sealed record AgentActionTrace(string Agent, string Tool, string Model, string Status, long DurationMs, string Evidence);

public sealed record AgentRunTrace(
    Guid RunId, string Engine, string CoordinatorModel, string Status,
    string RegionalProfileJson, double? Latitude, double? Longitude,
    IReadOnlyList<AgentActionTrace> Actions, IReadOnlyList<string> Warnings,
    int ModelCalls = 0, long InputTokens = 0, long OutputTokens = 0,
    string? MobilityJson = null);
