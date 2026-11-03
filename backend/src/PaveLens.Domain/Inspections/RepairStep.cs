using PaveLens.Domain.Common;

namespace PaveLens.Domain.Inspections;

public sealed class RepairStep : Entity
{
    private RepairStep()
    {
        // EF Core
    }

    public Guid InspectionId { get; private set; }
    public int Order { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Instructions { get; private set; } = string.Empty;
    public string[] Tools { get; private set; } = [];
    public RiskLevel Risk { get; private set; }

    /// <summary>Code of the distress this step acts on, or null for general steps.</summary>
    public string? TargetCode { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public static RepairStep Create(
        int order,
        string title,
        string instructions,
        IEnumerable<string> tools,
        RiskLevel risk,
        string? targetCode)
    {
        if (order < 1)
        {
            throw new DomainException("El orden del paso debe ser mayor que cero.");
        }

        return new RepairStep
        {
            Order = order,
            Title = Guard.NotBlank(title, "Title", 200),
            Instructions = Guard.NotBlank(instructions, "Instructions", 4000),
            Tools = tools
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Risk = risk,
            TargetCode = Guard.Optional(targetCode, "TargetCode", 16)
        };
    }

    internal void SetCompleted(bool completed, DateTime nowUtc)
    {
        CompletedAt = completed ? CompletedAt ?? nowUtc : null;
    }
}
