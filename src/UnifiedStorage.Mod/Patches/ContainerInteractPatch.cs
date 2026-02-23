using HarmonyLib;
using UnifiedStorage.Mod.Session;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Container), "Interact")]
public static class ContainerInteractPatch
{
    private static bool Prefix()
    {
        // Always allow vanilla interaction flow so the chest UI opens normally.
        return true;
    }

    private static void Postfix(Container __instance, Humanoid character, bool hold, bool __result)
    {
        if (!__result)
        {
            return;
        }

        var plugin = UnifiedStoragePlugin.Instance;
        if (plugin == null)
        {
            return;
        }

        plugin.TryHandleChestInteract(__instance, character, hold);
    }
}
