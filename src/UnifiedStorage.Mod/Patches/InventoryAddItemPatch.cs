using HarmonyLib;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
public static class InventoryAddItemPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (UnifiedStoragePlugin.Instance == null) return true;
        if (!UnifiedStoragePlugin.Instance.ShouldBlockDeposit(__instance, item)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int))]
public static class InventoryAddItemAtPositionPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (UnifiedStoragePlugin.Instance == null) return true;
        if (!UnifiedStoragePlugin.Instance.ShouldBlockDeposit(__instance, item)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int))]
public static class InventoryMoveItemToThisPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (UnifiedStoragePlugin.Instance == null) return true;
        if (!UnifiedStoragePlugin.Instance.ShouldBlockDeposit(__instance, item)) return true;
        __result = false;
        return false;
    }
}
