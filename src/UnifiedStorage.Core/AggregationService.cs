using System;
using System.Collections.Generic;
using System.Linq;

namespace UnifiedStorage.Core;

public static class AggregationService
{
    public static IReadOnlyList<AggregatedItem> Aggregate(
        IReadOnlyList<SourceStack> sourceStacks,
        Func<string, int>? resolveMaxStackSize = null)
    {
        return sourceStacks
            .Where(s => s.Amount > 0)
            .GroupBy(s => s.Key)
            .Select(group =>
            {
                var maxStack = resolveMaxStackSize != null
                    ? resolveMaxStackSize(group.Key.PrefabName)
                    : group.Max(s => s.StackSize);
                if (maxStack <= 0) maxStack = 1;

                return new AggregatedItem
                {
                    Key = group.Key,
                    DisplayName = group.Select(s => s.DisplayName).FirstOrDefault() ?? group.Key.PrefabName,
                    TotalAmount = group.Sum(s => s.Amount),
                    SourceCount = group.Select(s => s.SourceId).Distinct().Count(),
                    StackSize = maxStack
                };
            })
            .OrderBy(item => item.DisplayName)
            .ThenByDescending(item => item.TotalAmount)
            .ToList();
    }

    public static int CalculateVirtualSlots(IReadOnlyList<AggregatedItem> items)
    {
        return items.Sum(i => i.StackSize > 0 ? (int)Math.Ceiling(i.TotalAmount / (double)i.StackSize) : 1);
    }
}
