using HarmonyLib;
using UnifiedStorage.Mod.Pieces;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Container), "Interact")]
public static class ContainerInteractPatch
{
    private static bool Prefix(Container __instance, Humanoid character, bool hold, ref bool __result)
    {
        if (hold || !UnifiedTerminal.IsTerminal(__instance) || character is not Player player)
            return true;

        var plugin = UnifiedStoragePlugin.Instance;
        if (plugin != null && plugin.GetNearbyChestCount(__instance.transform.position) == 0)
        {
            player.Message(MessageHud.MessageType.Center, "No chests in range.");
            __result = false;
            return false;
        }

        return true;
    }

    private static void Postfix(Container __instance, Humanoid character, bool hold, bool __result)
    {
        if (!__result || hold)
            return;

        UnifiedStoragePlugin.Instance?.TryHandleChestInteract(__instance, character);
    }
}
