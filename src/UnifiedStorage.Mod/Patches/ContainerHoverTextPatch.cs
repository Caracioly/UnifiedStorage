using HarmonyLib;
using UnifiedStorage.Mod.Pieces;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Container), "GetHoverText")]
public static class ContainerHoverTextPatch
{
    private static bool Prefix(Container __instance, ref string __result)
    {
        if (!UnifiedTerminal.IsTerminal(__instance))
            return true;

        var plugin = UnifiedStoragePlugin.Instance;
        var chestCount = plugin?.GetNearbyChestCount(__instance.transform.position) ?? 0;
        var chestLabel = chestCount == 1 ? "chest" : "chests";

        __result = Localization.instance.Localize(
            $"Unified Storage Terminal\n[<color=yellow><b>$KEY_Use</b></color>] Open ({chestCount} {chestLabel} in range)");
        return false;
    }
}
