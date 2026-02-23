using System;
using System.Collections.Generic;

namespace UnifiedStorage.Core;

public readonly struct ItemKey : IEquatable<ItemKey>
{
    public ItemKey(string prefabName, int quality, int variant)
    {
        PrefabName = prefabName ?? string.Empty;
        Quality = quality;
        Variant = variant;
    }

    public string PrefabName { get; }
    public int Quality { get; }
    public int Variant { get; }

    public bool Equals(ItemKey other)
    {
        return string.Equals(PrefabName, other.PrefabName, StringComparison.Ordinal)
               && Quality == other.Quality
               && Variant == other.Variant;
    }

    public override bool Equals(object? obj) => obj is ItemKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 23) + (PrefabName?.GetHashCode() ?? 0);
            hash = (hash * 23) + Quality.GetHashCode();
            hash = (hash * 23) + Variant.GetHashCode();
            return hash;
        }
    }
}

public sealed class AggregatedItem
{
    public ItemKey Key { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int TotalAmount { get; set; }
    public int SourceCount { get; set; }
    public int StackSize { get; set; }
}

public sealed class SearchResult
{
    public IReadOnlyList<AggregatedItem> Items { get; set; } = Array.Empty<AggregatedItem>();
}

public sealed class SourceStack
{
    public ItemKey Key { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Amount { get; set; }
    public int StackSize { get; set; }
    public float Distance { get; set; }
    public string SourceId { get; set; } = string.Empty;
}

public sealed class PlannedTake
{
    public string SourceId { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public sealed class WithdrawPlan
{
    public List<PlannedTake> Takes { get; set; } = new();
    public int RequestedAmount { get; set; }
    public int PlannedAmount { get; set; }
}

public sealed class PlannedDeposit
{
    public string SourceId { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public sealed class DepositPlan
{
    public List<PlannedDeposit> Deposits { get; set; } = new();
    public int RequestedAmount { get; set; }
    public int PlannedAmount { get; set; }
}
