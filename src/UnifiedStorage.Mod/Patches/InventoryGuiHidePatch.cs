using HarmonyLib;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(InventoryGui), "Hide")]
public static class InventoryGuiHidePatch
{
    private static void Prefix()
    {
        UnifiedStoragePlugin.Instance?.OnInventoryGuiClosing();
    }
}
