using HarmonyLib;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Container), "Interact")]
public static class ContainerInteractPatch
{
    private static void Postfix(Container __instance, Humanoid character, bool hold, bool __result)
    {
        if (!__result || hold)
            return;

        UnifiedStoragePlugin.Instance?.TryHandleChestInteract(__instance, character);
    }
}
