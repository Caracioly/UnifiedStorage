using System;
using System.Collections.Generic;
using System.Linq;

namespace UnifiedStorage.Core;

public static class WithdrawPlanner
{
    public static WithdrawPlan Plan(
        IReadOnlyList<SourceStack> sourceStacks,
        ItemKey key,
        int requestedAmount,
        int maxReceivable)
    {
        var clampedRequested = Math.Max(0, requestedAmount);
        var target = Math.Max(0, Math.Min(clampedRequested, maxReceivable));
        var remaining = target;

        var plan = new WithdrawPlan
        {
            RequestedAmount = requestedAmount
        };

        if (remaining <= 0)
        {
            return plan;
        }

        foreach (var stack in sourceStacks
                     .Where(s => s.Key.Equals(key) && s.Amount > 0)
                     .OrderBy(s => s.Distance)
                     .ThenBy(s => s.SourceId, StringComparer.Ordinal))
        {
            if (remaining == 0)
            {
                break;
            }

            var take = Math.Min(stack.Amount, remaining);
            if (take <= 0)
            {
                continue;
            }

            plan.Takes.Add(new PlannedTake
            {
                SourceId = stack.SourceId,
                Amount = take
            });
            remaining -= take;
        }

        plan.PlannedAmount = target - remaining;
        return plan;
    }
}
