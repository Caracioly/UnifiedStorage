using System.Collections.Generic;

namespace UnifiedStorage.Mod.Server;

public static class ChestInclusionRules
{
    private const string IncludeInUnifiedKey = "US_IncludeInUnified";

    private static readonly HashSet<string> VanillaChestPrefabNames = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "piece_chest",
        "piece_chest_wood",
        "piece_chest_private",
        "piece_chest_blackmetal"
    };

    public static bool IsVanillaChest(Container? container)
    {
        if (container == null)
        {
            return false;
        }

        var prefabName = NormalizePrefabName(container.gameObject.name);
        return VanillaChestPrefabNames.Contains(prefabName);
    }

    public static bool IsIncludedInUnified(Container? container)
    {
        if (container == null)
        {
            return false;
        }

        var zdo = container.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null)
        {
            return true;
        }

        return zdo.GetBool(IncludeInUnifiedKey, true);
    }

    public static bool TrySetIncludedInUnified(Container? container, bool includeInUnified)
    {
        if (container == null)
        {
            return false;
        }

        var znetView = container.GetComponent<ZNetView>();
        var zdo = znetView?.GetZDO();
        if (zdo == null)
        {
            return false;
        }

        try
        {
            if (znetView != null && !znetView.IsOwner())
            {
                znetView.ClaimOwnership();
            }
        }
        catch
        {
            // Best effort only.
        }

        zdo.Set(IncludeInUnifiedKey, includeInUnified);
        return true;
    }

    private static string NormalizePrefabName(string name)
    {
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix, System.StringComparison.Ordinal))
        {
            return name.Substring(0, name.Length - cloneSuffix.Length);
        }

        return name;
    }
}
