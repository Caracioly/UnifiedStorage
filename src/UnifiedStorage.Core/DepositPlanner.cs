using System;
using System.Collections.Generic;
using System.Linq;

namespace UnifiedStorage.Core;

public sealed class ContainerSlot
{
    public string SourceId { get; set; } = string.Empty;
    public int FreeSpace { get; set; }
    public float Distance { get; set; }
}

public static class DepositPlanner
{
    public static DepositPlan Plan(
        IReadOnlyList<ContainerSlot> containers,
        int requestedAmount)
    {
        var plan = new DepositPlan { RequestedAmount = requestedAmount };
        var clamped = Math.Max(0, requestedAmount);
        if (clamped <= 0 || containers.Count == 0)
        {
            return plan;
        }

        var remaining = clamped;
        foreach (var slot in containers
                     .Where(c => c.FreeSpace > 0)
                     .OrderBy(c => c.Distance)
                     .ThenBy(c => c.SourceId, StringComparer.Ordinal))
        {
            if (remaining <= 0)
            {
                break;
            }

            var deposit = Math.Min(slot.FreeSpace, remaining);
            if (deposit <= 0)
            {
                continue;
            }

            plan.Deposits.Add(new PlannedDeposit
            {
                SourceId = slot.SourceId,
                Amount = deposit
            });
            remaining -= deposit;
        }

        plan.PlannedAmount = clamped - remaining;
        return plan;
    }
}
