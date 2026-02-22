using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Domain;

public sealed class StoreService : IStoreService
{
    public StoreResult Store(
        Player player,
        IReadOnlyList<ChestHandle> containers,
        StoreRequest request,
        long revision,
        bool requireAccessCheck)
    {
        var playerInventory = player.GetInventory();
        var playerItems = playerInventory.GetAllItems().Where(item => item != null && item.m_stack > 0).ToList();
        if (playerItems.Count == 0)
        {
            return Fail("No items in player inventory", revision);
        }

        var orderedContainers = containers.OrderBy(c => c.Distance).ThenBy(c => c.SourceId).ToList();
        var allowedKeys = request.Mode == StoreMode.PlaceStacks
            ? BuildStoredKeySet(orderedContainers)
            : null;

        var totalStored = 0;

        foreach (var playerItem in playerItems)
        {
            if (playerItem?.m_dropPrefab == null || playerItem.m_stack <= 0)
            {
                continue;
            }

            var key = new ItemKey(playerItem.m_dropPrefab.name, playerItem.m_quality, playerItem.m_variant);
            if (allowedKeys != null && !allowedKeys.Contains(key))
            {
                continue;
            }

            var moved = MoveItemIntoStorage(playerInventory, playerItem, orderedContainers, player, requireAccessCheck);
            totalStored += moved;
        }

        if (totalStored <= 0)
        {
            return Fail("No space in nearby storage", revision);
        }

        return new StoreResult
        {
            Success = true,
            Stored = totalStored,
            Revision = revision
        };
    }

    private static int MoveItemIntoStorage(
        Inventory playerInventory,
        ItemDrop.ItemData sourceItem,
        IReadOnlyList<ChestHandle> containers,
        Player player,
        bool requireAccessCheck)
    {
        var remaining = sourceItem.m_stack;
        var moved = 0;

        foreach (var chest in containers)
        {
            if (remaining <= 0)
            {
                break;
            }

            if (requireAccessCheck && !CanAccess(chest.Container, player))
            {
                continue;
            }

            var chestInventory = chest.Container.GetInventory();
            if (chestInventory == null)
            {
                continue;
            }

            var movedToThisChest = 0;
            while (remaining > 0)
            {
                var one = sourceItem.Clone();
                one.m_stack = 1;

                if (!CanAddItem(chestInventory, one, 1) || !chestInventory.AddItem(one))
                {
                    break;
                }

                remaining -= 1;
                moved += 1;
                movedToThisChest += 1;
            }

            if (movedToThisChest > 0)
            {
                playerInventory.RemoveItem(sourceItem, movedToThisChest);
            }
        }

        return moved;
    }

    private static HashSet<ItemKey> BuildStoredKeySet(IReadOnlyList<ChestHandle> containers)
    {
        var keys = new HashSet<ItemKey>();
        foreach (var chest in containers)
        {
            var inventory = chest.Container.GetInventory();
            if (inventory == null)
            {
                continue;
            }

            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack <= 0)
                {
                    continue;
                }

                keys.Add(new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant));
            }
        }

        return keys;
    }

    private static StoreResult Fail(string reason, long revision)
    {
        return new StoreResult
        {
            Success = false,
            Stored = 0,
            Reason = reason,
            Revision = revision
        };
    }

    private static bool CanAccess(Container container, Player player)
    {
        var accessMethod = typeof(Container).GetMethod("CheckAccess", BindingFlags.Instance | BindingFlags.Public);
        if (accessMethod == null)
        {
            return true;
        }

        if (accessMethod.GetParameters().Length == 1 && accessMethod.GetParameters()[0].ParameterType == typeof(long))
        {
            return (bool)accessMethod.Invoke(container, new object[] { player.GetPlayerID() });
        }

        if (accessMethod.GetParameters().Length == 0)
        {
            return (bool)accessMethod.Invoke(container, null);
        }

        return true;
    }

    private static bool CanAddItem(Inventory inventory, ItemDrop.ItemData item, int amount)
    {
        var method = typeof(Inventory).GetMethod("CanAddItem", new[] { typeof(ItemDrop.ItemData), typeof(int) });
        if (method != null)
        {
            return (bool)method.Invoke(inventory, new object[] { item, amount });
        }

        method = typeof(Inventory).GetMethod("CanAddItem", new[] { typeof(ItemDrop.ItemData) });
        if (method != null)
        {
            return (bool)method.Invoke(inventory, new object[] { item });
        }

        return true;
    }
}
