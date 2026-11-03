namespace PaveLens.Domain.Common;

public abstract class Entity
{
    // Version 7 GUIDs are time-ordered, which keeps clustered indexes compact.
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}
