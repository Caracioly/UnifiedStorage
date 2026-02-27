using System.Collections.Generic;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Models;
using UnityEngine;

namespace UnifiedStorage.Mod.Domain;

public interface IStorageSource
{
    string SourceId { get; }
    float Distance { get; }
    bool IsValid { get; }
    int PhysicalSlotCount { get; }
    int DisplaySlotsUsed { get; }
    IReadOnlyList<SourceStack> ReadStacks();
    ItemDrop.ItemData? GetItemPrototype(ItemKey key);
    bool CanPlayerAccess(Player? player);
    bool HasCapacityFor(ItemKey key, int maxStack);
    int DepositPriority(ItemKey key);
    int RemoveItems(ItemKey key, int amount);
    int AddItems(ItemKey key, int amount);
    bool OwnsInventory(Inventory inventory);
}

public interface IContainerScanner
{
    IReadOnlyList<ChestHandle> GetNearbyContainers(
        Vector3 center,
        float radius,
        Container? ignoreContainer = null,
        StorageScanContext context = StorageScanContext.Unknown);
}
