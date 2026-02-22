using HarmonyLib;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(InventoryGrid), "OnLeftClick")]
public static class InventoryGridOnLeftClickPatch
{
    private static void Postfix()
    {
        UnifiedStoragePlugin.Instance?.OnContainerInteraction();
    }
}

[HarmonyPatch(typeof(InventoryGrid), "OnRightClick")]
public static class InventoryGridOnRightClickPatch
{
    private static void Postfix()
    {
        UnifiedStoragePlugin.Instance?.OnContainerInteraction();
    }
}

[HarmonyPatch(typeof(InventoryGui), "OnTakeAll")]
public static class InventoryGuiOnTakeAllPatch
{
    private static bool Prefix()
    {
        return !UnifiedStoragePlugin.Instance?.IsUnifiedSessionActive() ?? true;
    }

    private static void Postfix()
    {
        UnifiedStoragePlugin.Instance?.OnContainerInteraction();
    }
}

[HarmonyPatch(typeof(InventoryGui), "OnStackAll")]
public static class InventoryGuiOnStackAllPatch
{
    private static void Postfix()
    {
        UnifiedStoragePlugin.Instance?.OnContainerInteraction();
    }
}

[HarmonyPatch(typeof(InventoryGui), "OnDropOutside")]
public static class InventoryGuiOnDropOutsidePatch
{
    private static void Postfix()
    {
        UnifiedStoragePlugin.Instance?.OnContainerInteraction();
    }
}
