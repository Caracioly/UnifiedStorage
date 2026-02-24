using HarmonyLib;
using UnifiedStorage.Mod.Pieces;
using UnityEngine;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Container), "GetHoverText")]
public static class ContainerHoverTextPatch
{
    private static int _cachedCount;
    private static float _cacheExpiry;
    private static int _cachedInstanceId;

    private static bool Prefix(Container __instance, ref string __result)
    {
        if (!UnifiedTerminal.IsTerminal(__instance))
            return true;

        var instanceId = __instance.GetInstanceID();
        var now = Time.unscaledTime;
        if (instanceId != _cachedInstanceId || now >= _cacheExpiry)
        {
            var plugin = UnifiedStoragePlugin.Instance;
            _cachedCount = plugin?.GetNearbyChestCount(__instance.transform.position) ?? 0;
            _cacheExpiry = now + 2f;
            _cachedInstanceId = instanceId;
        }

        var chestLabel = _cachedCount == 1 ? "chest" : "chests";
        __result = Localization.instance.Localize(
            $"Unified Storage Terminal\n[<color=yellow><b>$KEY_Use</b></color>] Open ({_cachedCount} {chestLabel} in range)");
        return false;
    }
}
