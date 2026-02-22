using System.Collections.Generic;
using System.Linq;
using UnifiedStorage.Core;

namespace UnifiedStorage.Mod.UI;

public sealed class ItemListView
{
    public IReadOnlyList<AggregatedItem> FilterAndSort(IReadOnlyList<AggregatedItem> items, string query)
    {
        var filtered = SearchService.Filter(items, query).Items;
        return filtered
            .OrderBy(item => item.DisplayName)
            .ThenByDescending(item => item.TotalAmount)
            .ToList();
    }
}
