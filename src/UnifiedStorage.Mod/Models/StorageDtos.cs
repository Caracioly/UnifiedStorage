using System.Collections.Generic;
using UnifiedStorage.Core;

namespace UnifiedStorage.Mod.Models;

public sealed class SnapshotRequestDto
{
}

public sealed class SnapshotResponseDto
{
    public StorageSnapshot Snapshot { get; set; } = new();
}

public sealed class WithdrawRequestDto
{
    public WithdrawRequest Request { get; set; } = new();
}

public sealed class WithdrawResponseDto
{
    public WithdrawResult Result { get; set; } = new();
    public StorageSnapshot Snapshot { get; set; } = new();
}

public sealed class StoreResponseDto
{
    public StoreResult Result { get; set; } = new();
    public StorageSnapshot Snapshot { get; set; } = new();
}

public sealed class ItemStackReference
{
    public ItemKey Key { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Amount { get; set; }
    public int StackSize { get; set; }
    public float Distance { get; set; }
    public string SourceId { get; set; } = string.Empty;
}

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

public sealed class SnapshotBuildContext
{
    public List<ItemStackReference> ItemStacks { get; } = new();
}
