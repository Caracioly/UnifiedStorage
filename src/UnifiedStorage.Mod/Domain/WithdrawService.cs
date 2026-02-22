using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Domain;

public sealed class WithdrawService : IWithdrawService
{
    public WithdrawResult Withdraw(
        Player player,
        IReadOnlyList<ChestHandle> containers,
        WithdrawRequest request,
        long revision,
        bool requireAccessCheck)
    {
        if (request.Amount <= 0)
        {
            return Fail("Invalid amount", revision);
        }

        var playerInventory = player.GetInventory();
        var totalWithdrawn = 0;
        var remaining = request.Amount;

        foreach (var container in containers.OrderBy(c => c.Distance).ThenBy(c => c.SourceId))
        {
            if (remaining <= 0)
            {
                break;
            }

            if (requireAccessCheck && !CanAccess(container.Container, player))
            {
                continue;
            }

            var chestInventory = container.Container.GetInventory();
            if (chestInventory == null)
            {
                continue;
            }

            var matchingStacks = chestInventory.GetAllItems()
                .Where(item => item?.m_dropPrefab != null
                               && item.m_dropPrefab.name == request.Key.PrefabName
                               && item.m_quality == request.Key.Quality
                               && item.m_variant == request.Key.Variant
                               && item.m_stack > 0)
                .ToList();

            foreach (var sourceStack in matchingStacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var take = sourceStack.m_stack < remaining ? sourceStack.m_stack : remaining;
                if (take <= 0)
                {
                    continue;
                }

                var accepted = TryTransferToPlayer(playerInventory, sourceStack, take);
                if (accepted <= 0)
                {
                    continue;
                }

                chestInventory.RemoveItem(sourceStack, accepted);
                remaining -= accepted;
                totalWithdrawn += accepted;
            }
        }

        if (totalWithdrawn <= 0)
        {
            return Fail("No items transferred", revision);
        }

        return new WithdrawResult
        {
            Success = true,
            Withdrawn = totalWithdrawn,
            Reason = string.Empty,
            Revision = revision
        };
    }

    private static WithdrawResult Fail(string reason, long revision)
    {
        return new WithdrawResult
        {
            Success = false,
            Withdrawn = 0,
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

        if (accessMethod.GetParameters().Length == 1)
        {
            var parameterType = accessMethod.GetParameters()[0].ParameterType;
            if (parameterType == typeof(long))
            {
                return (bool)accessMethod.Invoke(container, new object[] { player.GetPlayerID() });
            }
        }

        if (accessMethod.GetParameters().Length == 0)
        {
            return (bool)accessMethod.Invoke(container, null);
        }

        return true;
    }

    private static int TryTransferToPlayer(Inventory playerInventory, ItemDrop.ItemData sourceStack, int amount)
    {
        var clone = sourceStack.Clone();
        clone.m_stack = amount;

        if (!CanAddItem(playerInventory, clone, amount))
        {
            return 0;
        }

        return playerInventory.AddItem(clone) ? amount : 0;
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
