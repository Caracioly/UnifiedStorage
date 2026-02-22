using System;
using System.Collections.Generic;
using System.Linq;

namespace UnifiedStorage.Core;

public static class SearchService
{
    public static SearchResult Filter(IReadOnlyList<AggregatedItem> items, string query)
    {
        if (items.Count == 0)
        {
            return new SearchResult();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResult { Items = items };
        }

        var trimmed = query.Trim();
        var filtered = items.Where(item => item.DisplayName.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        return new SearchResult { Items = filtered };
    }
}
