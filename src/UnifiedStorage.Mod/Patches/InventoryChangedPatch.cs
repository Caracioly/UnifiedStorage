using HarmonyLib;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Inventory), "Changed")]
public static class InventoryChangedPatch
{
    private static void Postfix(Inventory __instance)
    {
        UnifiedStoragePlugin.Instance?.OnTrackedInventoryChanged(__instance);
    }
}

