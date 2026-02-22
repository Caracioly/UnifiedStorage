namespace UnifiedStorage.Mod.Models;

public sealed class ChestHandle
{
    public ChestHandle(string sourceId, Container container, float distance)
    {
        SourceId = sourceId;
        Container = container;
        Distance = distance;
    }

    public string SourceId { get; }
    public Container Container { get; }
    public float Distance { get; }
}
