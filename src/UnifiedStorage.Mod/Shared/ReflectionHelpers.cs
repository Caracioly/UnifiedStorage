using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnifiedStorage.Core;
using UnityEngine;

namespace UnifiedStorage.Mod.Shared;

public static class ReflectionHelpers
{
    private static readonly FieldInfo? InventoryWidthField =
        typeof(Inventory).GetField("m_width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? InventoryHeightField =
        typeof(Inventory).GetField("m_height", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo? InventoryGetWidthMethod =
        typeof(Inventory).GetMethod("GetWidth", BindingFlags.Instance | BindingFlags.Public);

    private static readonly MethodInfo? InventoryGetHeightMethod =
        typeof(Inventory).GetMethod("GetHeight", BindingFlags.Instance | BindingFlags.Public);

    private static readonly MethodInfo? ContainerCheckAccessMethod =
        typeof(Container).GetMethod("CheckAccess", BindingFlags.Instance | BindingFlags.Public);

    private static readonly FieldInfo? ContainerNameField =
        typeof(Container).GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo? ContainerSetInUseMethod =
        typeof(Container).GetMethod("SetInUse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);

    private static readonly FieldInfo? ContainerInUseField =
        typeof(Container).GetField("m_inUse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? DragItemField =
        typeof(InventoryGui).GetField("m_dragItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? ContainerRectField =
        AccessTools.Field(typeof(InventoryGui), "m_container");

    private static readonly FieldInfo? CurrentContainerField =
        AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

    private static readonly FieldInfo? ContainerGridField =
        AccessTools.Field(typeof(InventoryGui), "m_containerGrid");

    private static readonly FieldInfo? ContainerNameTextField =
        AccessTools.Field(typeof(InventoryGui), "m_containerName");

    private static readonly FieldInfo? TakeAllButtonField =
        AccessTools.Field(typeof(InventoryGui), "m_takeAllButton");

    private static readonly FieldInfo? GridRootField =
        AccessTools.Field(typeof(InventoryGrid), "m_gridRoot");

    private static readonly FieldInfo? GridHeightField =
        AccessTools.Field(typeof(InventoryGrid), "m_height");

    private static readonly FieldInfo? GridElementPrefabField =
        AccessTools.Field(typeof(InventoryGrid), "m_elementPrefab");

    private static readonly FieldInfo? GridElementSpaceField =
        AccessTools.Field(typeof(InventoryGrid), "m_elementSpace");

    private static readonly FieldInfo? InventoryItemListField =
        AccessTools.Field(typeof(Inventory), "m_inventory");

    private static readonly MethodInfo? InventoryChangedMethod =
        typeof(Inventory).GetMethod("Changed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo?[] DropItemMethods = typeof(ItemDrop)
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Where(m => string.Equals(m.Name, "DropItem", StringComparison.Ordinal))
        .ToArray();

    public static int GetInventoryWidth(Inventory inventory)
    {
        if (InventoryGetWidthMethod != null && InventoryGetWidthMethod.Invoke(inventory, null) is int w)
            return w;
        return InventoryWidthField?.GetValue(inventory) is int fw ? fw : 1;
    }

    public static int GetInventoryHeight(Inventory inventory)
    {
        if (InventoryGetHeightMethod != null && InventoryGetHeightMethod.Invoke(inventory, null) is int h)
            return h;
        return InventoryHeightField?.GetValue(inventory) is int fh ? fh : 1;
    }

    public static void SetInventorySize(Inventory inventory, int width, int height)
    {
        InventoryWidthField?.SetValue(inventory, width);
        InventoryHeightField?.SetValue(inventory, height);
    }

    public static bool CanAccess(Container container, Player player)
    {
        if (ContainerCheckAccessMethod == null)
            return true;

        var parameters = ContainerCheckAccessMethod.GetParameters();
        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(long))
            return (bool)ContainerCheckAccessMethod.Invoke(container, new object[] { player.GetPlayerID() });
        if (parameters.Length == 0)
            return (bool)ContainerCheckAccessMethod.Invoke(container, null);
        return true;
    }

    public static void SetContainerDisplayName(Container container, string displayName)
    {
        if (ContainerNameField?.FieldType == typeof(string))
            ContainerNameField.SetValue(container, displayName);
    }

    public static void ForceReleaseContainerUse(Container container)
    {
        try { ContainerSetInUseMethod?.Invoke(container, new object[] { false }); } catch { }
        try { if (ContainerInUseField?.FieldType == typeof(bool)) ContainerInUseField.SetValue(container, false); } catch { }
    }

    public static string BuildContainerUid(Container container)
    {
        var znetView = container.GetComponent<ZNetView>();
        var zdo = znetView?.GetZDO();
        return zdo != null ? zdo.m_uid.ToString() : container.GetInstanceID().ToString();
    }

    public static bool MatchKey(ItemDrop.ItemData item, ItemKey key)
    {
        return item?.m_dropPrefab != null
               && item.m_dropPrefab.name == key.PrefabName
               && item.m_quality == key.Quality
               && item.m_variant == key.Variant
               && item.m_stack > 0;
    }

    public static int GetTotalAmount(Inventory inventory, ItemKey key)
    {
        var total = 0;
        foreach (var item in inventory.GetAllItems())
        {
            if (MatchKey(item, key))
                total += item.m_stack;
        }
        return total;
    }

    public static int TryAddItemMeasured(Inventory inventory, ItemKey key, ItemDrop.ItemData stack, int requestedAmount)
    {
        var before = GetTotalAmount(inventory, key);
        inventory.AddItem(stack);
        var after = GetTotalAmount(inventory, key);
        return ChunkedTransfer.ClampMeasuredMove(requestedAmount, before, after);
    }

    public static ItemDrop.ItemData? CreateItemStack(ItemKey key, int amount)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(key.PrefabName);
        var drop = prefab?.GetComponent<ItemDrop>();
        if (drop?.m_itemData == null)
            return null;

        var clone = drop.m_itemData.Clone();
        clone.m_quality = key.Quality;
        clone.m_variant = key.Variant;
        clone.m_stack = amount;
        return clone;
    }

    public static int GetMaxStackSize(ItemKey key)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(key.PrefabName);
        var drop = prefab?.GetComponent<ItemDrop>();
        return drop?.m_itemData?.m_shared?.m_maxStackSize ?? 1;
    }

    public static int ResolveMaxStackSize(string prefabName)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
        var drop = prefab?.GetComponent<ItemDrop>();
        return drop?.m_itemData?.m_shared?.m_maxStackSize ?? 1;
    }

    public static bool TryDropItem(ItemDrop.ItemData item, int amount, Vector3 position, Quaternion rotation)
    {
        foreach (var method in DropItemMethods)
        {
            if (method == null) continue;
            var parameters = method.GetParameters();
            try
            {
                if (parameters.Length == 4
                    && parameters[0].ParameterType == typeof(ItemDrop.ItemData)
                    && parameters[1].ParameterType == typeof(int)
                    && parameters[2].ParameterType == typeof(Vector3)
                    && parameters[3].ParameterType == typeof(Quaternion))
                {
                    method.Invoke(null, new object[] { item, amount, position, rotation });
                    return true;
                }

                if (parameters.Length == 3
                    && parameters[0].ParameterType == typeof(ItemDrop.ItemData)
                    && parameters[1].ParameterType == typeof(int)
                    && parameters[2].ParameterType == typeof(Vector3))
                {
                    method.Invoke(null, new object[] { item, amount, position });
                    return true;
                }
            }
            catch { }
        }
        return false;
    }

    public static int GetSubgroupOrder(ItemKey key)
    {
        if (string.IsNullOrWhiteSpace(key.PrefabName))
            return 999;

        var prefab = key.PrefabName.ToLowerInvariant();
        if (prefab.Contains("ore") || prefab.Contains("scrap") || prefab.Contains("metal") || prefab.Contains("ingot") || prefab.Contains("bar"))
            return 10;
        if (prefab.Contains("wood") || prefab.Contains("stone"))
            return 20;
        if (prefab.Contains("hide") || prefab.Contains("leather") || prefab.Contains("scale") || prefab.Contains("chitin"))
            return 30;
        if (prefab.Contains("food") || prefab.Contains("mead") || prefab.Contains("stew") || prefab.Contains("soup") || prefab.Contains("bread"))
            return 40;
        return 100;
    }

    public static List<ItemDrop.ItemData>? GetInventoryItemList(Inventory inventory) =>
        InventoryItemListField?.GetValue(inventory) as List<ItemDrop.ItemData>;

    public static void AddItemDirectly(Inventory inventory, ItemDrop.ItemData item, int x, int y)
    {
        item.m_gridPos = new Vector2i(x, y);
        var list = GetInventoryItemList(inventory);
        if (list != null)
            list.Add(item);
        else
            inventory.AddItem(item);
    }

    public static void NotifyInventoryChanged(Inventory inventory) =>
        InventoryChangedMethod?.Invoke(inventory, null);

    public static void ClearInventory(Inventory? inventory)
    {
        if (inventory == null) return;
        var removeAll = typeof(Inventory).GetMethod("RemoveAll", BindingFlags.Instance | BindingFlags.Public);
        if (removeAll != null)
        {
            removeAll.Invoke(inventory, null);
            return;
        }
        foreach (var item in inventory.GetAllItems().ToList())
            inventory.RemoveItem(item, item.m_stack);
    }

    public static bool IsDragInProgress()
    {
        if (InventoryGui.instance == null || DragItemField == null) return false;
        return DragItemField.GetValue(InventoryGui.instance) is ItemDrop.ItemData dragItem && dragItem.m_stack > 0;
    }

    public static RectTransform? GetContainerRect(InventoryGui gui) =>
        ContainerRectField?.GetValue(gui) as RectTransform;

    public static Container? GetCurrentContainer(InventoryGui gui) =>
        CurrentContainerField?.GetValue(gui) as Container;

    public static InventoryGrid? GetContainerGrid(InventoryGui gui) =>
        ContainerGridField?.GetValue(gui) as InventoryGrid;

    public static TMPro.TMP_Text? GetContainerName(InventoryGui gui) =>
        ContainerNameTextField?.GetValue(gui) as TMPro.TMP_Text;

    public static UnityEngine.UI.Button? GetTakeAllButton(InventoryGui gui) =>
        TakeAllButtonField?.GetValue(gui) as UnityEngine.UI.Button;

    public static RectTransform? GetGridRoot(InventoryGrid grid) =>
        GridRootField?.GetValue(grid) as RectTransform;

    public static int GetGridHeight(InventoryGrid grid) =>
        GridHeightField?.GetValue(grid) is int h ? h : 4;

    public static void SetGridHeight(InventoryGrid grid, int height) =>
        GridHeightField?.SetValue(grid, height);

    public static float GetGridElementSpace(InventoryGrid grid) =>
        GridElementSpaceField?.GetValue(grid) is float s ? s : 2f;

    public static GameObject? GetGridElementPrefab(InventoryGrid grid) =>
        GridElementPrefabField?.GetValue(grid) as GameObject;
}
