using System;
using System.Collections.Generic;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Server;

public sealed class DrawerStorageSource : IStorageSource
{
    private readonly DrawerSnapshot _snapshot;

    public DrawerStorageSource(DrawerSnapshot snapshot, float distance)
    {
        _snapshot = snapshot;
        SourceId = snapshot.SourceId;
        Distance = distance;
    }

    public string SourceId { get; }
    public float Distance { get; }

    public bool IsValid =>
        _snapshot.ZNetView != null
        && _snapshot.ZNetView.isActiveAndEnabled
        && _snapshot.ZNetView.GetZDO() != null;

    public int PhysicalSlotCount => 1;

    public IReadOnlyList<SourceStack> ReadStacks()
    {
        if (!IsValid) return Array.Empty<SourceStack>();

        var zdo = _snapshot.ZNetView.GetZDO();
        var prefab = zdo.GetString("Prefab");
        var amount = zdo.GetInt("Amount");
        var quality = zdo.GetInt("Quality", 1);

        if (string.IsNullOrEmpty(prefab) || amount <= 0)
            return Array.Empty<SourceStack>();

        var stackSize = ReflectionHelpers.ResolveMaxStackSize(prefab);
        if (stackSize <= 0) stackSize = int.MaxValue;

        return new[]
        {
            new SourceStack
            {
                Key = new ItemKey(prefab, quality, 0),
                DisplayName = LocalizeName(prefab),
                Amount = amount,
                StackSize = stackSize,
                Distance = Distance,
                SourceId = SourceId
            }
        };
    }

    public ItemDrop.ItemData? GetItemPrototype(ItemKey key)
    {
        return ReflectionHelpers.CreateItemStack(key, 1);
    }

    public bool CanPlayerAccess(Player? player) => true;

    public bool HasCapacityFor(ItemKey key, int maxStack)
    {
        if (!IsValid) return false;

        var zdo = _snapshot.ZNetView.GetZDO();
        var currentPrefab = zdo.GetString("Prefab");
        var currentQuality = zdo.GetInt("Quality", 1);

        if (string.IsNullOrEmpty(currentPrefab))
            return true;

        return string.Equals(currentPrefab, key.PrefabName, StringComparison.Ordinal)
               && currentQuality == key.Quality;
    }

    public int RemoveItems(ItemKey key, int amount)
    {
        if (!IsValid || amount <= 0) return 0;

        var zdo = _snapshot.ZNetView.GetZDO();
        var currentPrefab = zdo.GetString("Prefab");
        var currentQuality = zdo.GetInt("Quality", 1);

        if (!string.Equals(currentPrefab, key.PrefabName, StringComparison.Ordinal)
            || currentQuality != key.Quality)
            return 0;

        var available = zdo.GetInt("Amount");
        var toRemove = Math.Min(available, amount);
        if (toRemove <= 0) return 0;

        ItemDrawersApi.ForceRemoveFromDrawer(_snapshot, toRemove);
        return toRemove;
    }

    public int AddItems(ItemKey key, int amount)
    {
        if (!IsValid || amount <= 0) return 0;
        if (!HasCapacityFor(key, int.MaxValue)) return 0;

        ItemDrawersApi.AddToDrawer(_snapshot, key.PrefabName, amount, key.Quality);
        return amount;
    }

    public bool OwnsInventory(Inventory inventory) => false;

    private static string LocalizeName(string prefabName)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
        var drop = prefab?.GetComponent<ItemDrop>();
        return drop?.m_itemData?.m_shared?.m_name ?? prefabName;
    }
}
