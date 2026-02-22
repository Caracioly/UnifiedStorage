using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Domain;

public sealed class ItemAggregator : IItemAggregator
{
    public StorageSnapshot BuildSnapshot(IReadOnlyList<ChestHandle> containers, long revision, SnapshotBuildContext context)
    {
        context.ItemStacks.Clear();
        var totalSlots = 0;
        var usedSlots = 0;

        foreach (var handle in containers)
        {
            var inventory = handle.Container?.GetInventory();
            if (inventory == null)
            {
                continue;
            }

            totalSlots += GetInventoryTotalSlots(inventory);
            usedSlots += GetInventoryUsedSlots(inventory);

            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack <= 0)
                {
                    continue;
                }

                var key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
                context.ItemStacks.Add(new ItemStackReference
                {
                    Key = key,
                    DisplayName = item.m_shared.m_name,
                    Amount = item.m_stack,
                    StackSize = item.m_shared.m_maxStackSize,
                    Distance = handle.Distance,
                    SourceId = handle.SourceId
                });
            }
        }

        var aggregated = context.ItemStacks
            .GroupBy(s => s.Key)
            .Select(group => new AggregatedItem
            {
                Key = group.Key,
                DisplayName = group.Select(x => x.DisplayName).FirstOrDefault() ?? group.Key.PrefabName,
                TotalAmount = group.Sum(x => x.Amount),
                SourceCount = group.Select(x => x.SourceId).Distinct().Count(),
                StackSize = group.Max(x => x.StackSize)
            })
            .OrderBy(item => item.DisplayName)
            .ThenByDescending(item => item.TotalAmount)
            .ToList();

        return new StorageSnapshot
        {
            Revision = revision,
            TotalSlots = totalSlots,
            UsedSlots = usedSlots,
            ChestCount = containers.Count,
            Items = aggregated
        };
    }

    private static int GetInventoryUsedSlots(Inventory inventory)
    {
        var nrOfItems = typeof(Inventory).GetMethod("NrOfItems", BindingFlags.Instance | BindingFlags.Public);
        if (nrOfItems != null && nrOfItems.Invoke(inventory, null) is int used)
        {
            return used;
        }

        return inventory.GetAllItems().Count;
    }

    private static int GetInventoryTotalSlots(Inventory inventory)
    {
        var widthMethod = typeof(Inventory).GetMethod("GetWidth", BindingFlags.Instance | BindingFlags.Public);
        var heightMethod = typeof(Inventory).GetMethod("GetHeight", BindingFlags.Instance | BindingFlags.Public);
        if (widthMethod != null && heightMethod != null
            && widthMethod.Invoke(inventory, null) is int width
            && heightMethod.Invoke(inventory, null) is int height)
        {
            return width * height;
        }

        var mWidthField = typeof(Inventory).GetField("m_width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var mHeightField = typeof(Inventory).GetField("m_height", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (mWidthField?.GetValue(inventory) is int fieldWidth && mHeightField?.GetValue(inventory) is int fieldHeight)
        {
            return fieldWidth * fieldHeight;
        }

        return 0;
    }
}
