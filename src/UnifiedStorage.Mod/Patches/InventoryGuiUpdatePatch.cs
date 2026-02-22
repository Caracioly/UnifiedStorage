using HarmonyLib;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(InventoryGui), "Awake")]
public static class InventoryGuiAwakePatch
{
    private static void Postfix(InventoryGui __instance)
    {
        UnifiedStoragePlugin.Instance?.OnInventoryGuiAwake(__instance);
    }
}

[HarmonyPatch(typeof(InventoryGui), "Update")]
public static class InventoryGuiUpdatePatch
{
    private static void Postfix(InventoryGui __instance)
    {
        UnifiedStoragePlugin.Instance?.OnInventoryGuiUpdate(__instance);
    }
}
