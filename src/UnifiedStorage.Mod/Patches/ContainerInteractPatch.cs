using HarmonyLib;
using UnifiedStorage.Mod.Session;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Container), "Interact")]
public static class ContainerInteractPatch
{
    private static bool Prefix(Container __instance, Humanoid character, bool hold, ref bool __result)
    {
        var plugin = UnifiedStoragePlugin.Instance;
        if (plugin == null)
        {
            return true;
        }

        if (!plugin.TryHandleChestInteract(__instance, character, hold))
        {
            return true;
        }

        __result = true;
        return true;
    }
}
