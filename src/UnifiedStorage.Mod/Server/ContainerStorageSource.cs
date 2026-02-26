using System;
using System.Collections.Generic;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Server;

public sealed class ContainerStorageSource : IStorageSource
{
    private readonly Container _container;
    private readonly StorageConfig _config;

    public ContainerStorageSource(string sourceId, Container container, float distance, StorageConfig config)
    {
        SourceId = sourceId;
        _container = container;
        Distance = distance;
        _config = config;
    }

    public string SourceId { get; }
    public float Distance { get; }
    public bool IsValid => _container != null && _container.gameObject.activeInHierarchy;

    public int PhysicalSlotCount
    {
        get
        {
            var inv = _container.GetInventory();
            if (inv == null) return 0;
            return ReflectionHelpers.GetInventoryWidth(inv) * ReflectionHelpers.GetInventoryHeight(inv);
        }
    }

    public IReadOnlyList<SourceStack> ReadStacks()
    {
        var inv = _container.GetInventory();
        if (inv == null) return Array.Empty<SourceStack>();

        var result = new List<SourceStack>();
        foreach (var item in inv.GetAllItems())
        {
            if (item?.m_dropPrefab == null || item.m_stack <= 0) continue;
            result.Add(new SourceStack
            {
                Key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant),
                DisplayName = item.m_shared.m_name,
                Amount = item.m_stack,
                StackSize = item.m_shared.m_maxStackSize,
                Distance = Distance,
                SourceId = SourceId
            });
        }
        return result;
    }

    public ItemDrop.ItemData? GetItemPrototype(ItemKey key)
    {
        var inv = _container.GetInventory();
        if (inv == null) return null;
        foreach (var item in inv.GetAllItems())
        {
            if (item?.m_dropPrefab == null) continue;
            var k = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
            if (k.Equals(key)) return item.Clone();
        }
        return null;
    }

    public bool CanPlayerAccess(Player? player)
    {
        if (!_config.RequireAccessCheck || player == null) return true;
        return ReflectionHelpers.CanAccess(_container, player);
    }

    public int DepositPriority(ItemKey key) => 1;

    public bool HasCapacityFor(ItemKey key, int maxStack)
    {
        var inv = _container.GetInventory();
        if (inv == null) return false;
        var items = inv.GetAllItems();
        foreach (var item in items)
        {
            if (ReflectionHelpers.MatchKey(item, key) && item.m_stack < maxStack) return true;
        }
        var totalSlots = ReflectionHelpers.GetInventoryWidth(inv) * ReflectionHelpers.GetInventoryHeight(inv);
        return items.Count < totalSlots;
    }

    public int RemoveItems(ItemKey key, int amount)
    {
        if (amount <= 0) return 0;
        var inv = _container.GetInventory();
        if (inv == null) return 0;
        var remaining = amount;
        foreach (var item in inv.GetAllItems().FindAll(i => ReflectionHelpers.MatchKey(i, key)))
        {
            if (remaining <= 0) break;
            var take = Math.Min(item.m_stack, remaining);
            inv.RemoveItem(item, take);
            remaining -= take;
        }
        return amount - remaining;
    }

    public int AddItems(ItemKey key, int amount)
    {
        if (amount <= 0) return 0;
        var inv = _container.GetInventory();
        if (inv == null) return 0;
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        return ChunkedTransfer.Move(amount, maxStack, chunkAmount =>
        {
            var stack = ReflectionHelpers.CreateItemStack(key, chunkAmount);
            if (stack == null) return 0;
            return ReflectionHelpers.TryAddItemMeasured(inv, key, stack, chunkAmount);
        });
    }

    public bool OwnsInventory(Inventory inventory)
    {
        return inventory != null && ReferenceEquals(inventory, _container.GetInventory());
    }
}
