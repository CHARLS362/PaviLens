using PaveLens.Domain.Common;

namespace PaveLens.Domain.Inspections;

/// <summary>One turn of the inspector's conversation with the maintenance assistant.</summary>
public sealed class ConversationMessage : Entity
{
    private ConversationMessage()
    {
        // EF Core
    }

    public Guid InspectionId { get; private set; }
    public Guid? DistressId { get; private set; }
    public MessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public AnalysisEngine? Engine { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static ConversationMessage FromUser(Guid inspectionId, Guid? distressId, string content, DateTime nowUtc) =>
        new()
        {
            InspectionId = inspectionId,
            DistressId = distressId,
            Role = MessageRole.User,
            Content = Guard.NotBlank(content, "Question", 1000),
            CreatedAt = nowUtc
        };

    public static ConversationMessage FromAssistant(
        Guid inspectionId, Guid? distressId, string content, AnalysisEngine engine, DateTime nowUtc) =>
        new()
        {
            InspectionId = inspectionId,
            DistressId = distressId,
            Role = MessageRole.Assistant,
            Content = Guard.NotBlank(content, "Answer", 8000),
            Engine = engine,
            CreatedAt = nowUtc
        };
}
