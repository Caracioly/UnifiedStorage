using System.Collections.Generic;
using System.Linq;

namespace UnifiedStorage.Core;

public static class AggregationService
{
    public static IReadOnlyList<AggregatedItem> Aggregate(IReadOnlyList<SourceStack> sourceStacks)
    {
        return sourceStacks
            .Where(s => s.Amount > 0)
            .GroupBy(s => s.Key)
            .Select(group => new AggregatedItem
            {
                Key = group.Key,
                DisplayName = group.Select(s => s.DisplayName).FirstOrDefault() ?? group.Key.PrefabName,
                TotalAmount = group.Sum(s => s.Amount),
                SourceCount = group.Select(s => s.SourceId).Distinct().Count(),
                StackSize = group.Max(s => s.StackSize)
            })
            .OrderBy(item => item.DisplayName)
            .ThenByDescending(item => item.TotalAmount)
            .ToList();
    }
}
