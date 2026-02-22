using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Models;
using UnifiedStorage.Mod.Pieces;

namespace UnifiedStorage.Mod.Session;

public sealed class UnifiedTerminalSessionService
{
    private readonly StorageConfig _config;
    private readonly IContainerScanner _scanner;
    private readonly ManualLogSource _logger;

    private readonly Dictionary<ItemKey, int> _baseTotals = new();
    private readonly Dictionary<ItemKey, int> _workingTotals = new();
    private readonly Dictionary<ItemKey, int> _displayedTotals = new();
    private readonly Dictionary<ItemKey, ItemDrop.ItemData> _prototypes = new();
    private readonly Dictionary<string, int> _originalStackSizes = new();

    private Container? _terminal;
    private Player? _player;
    private List<ChestHandle> _chests = new();
    private string _searchQuery = string.Empty;
    private float _scanRadius;
    private int _slotsTotalPhysical;
    private int _chestCount;
    private int _originalInventoryWidth;
    private int _originalInventoryHeight;
    private int _visibleRows;
    private int _contentRows;
    private string? _originalContainerName;
    private int _uiRevision;

    public UnifiedTerminalSessionService(StorageConfig config, IContainerScanner scanner, ManualLogSource logger)
    {
        _config = config;
        _scanner = scanner;
        _logger = logger;
    }

    public bool IsActive => _terminal != null && _player != null;
    public int SlotsTotalPhysical => _slotsTotalPhysical;
    public int ChestsInRange => _chestCount;
    public int SlotsUsedVirtual => _workingTotals.Where(kv => kv.Value > 0).Sum(kv => (int)Math.Ceiling(kv.Value / 999f));
    public string SearchQuery => _searchQuery;
    public int VisibleRows => _visibleRows;
    public int ContentRows => _contentRows;
    public int UiRevision => _uiRevision;

    public bool IsTrackedInventory(Inventory inventory)
    {
        if (!IsActive || inventory == null || _terminal == null)
        {
            return false;
        }

        var terminalInventory = _terminal.GetInventory();
        if (ReferenceEquals(inventory, terminalInventory))
        {
            return true;
        }

        foreach (var chest in _chests)
        {
            if (ReferenceEquals(inventory, chest.Container.GetInventory()))
            {
                return true;
            }
        }

        return false;
    }

    public bool HandleTerminalInteract(Container container, Player player, bool hold)
    {
        if (hold || !UnifiedChestTerminalMarker.IsTerminalContainer(container))
        {
            return false;
        }

        BeginSession(container, player);
        return true;
    }

    public void BeginSession(Container terminal, Player player)
    {
        EndSession();

        _terminal = terminal;
        _player = player;
        _searchQuery = string.Empty;
        _originalContainerName = GetContainerName(terminal);
        SetContainerName(terminal, "Storage Interface");
        CaptureOriginalInventorySize(terminal.GetInventory());
        _visibleRows = Math.Max(1, _originalInventoryHeight + 2);
        _uiRevision++;

        _scanRadius = _config.TerminalRangeOverride.Value > 0 ? _config.TerminalRangeOverride.Value : _config.ScanRadius.Value;
        RefreshChestHandles();
        RebuildTotalsFromPhysicalStorage();

        RefreshTerminalInventoryFromWorking();
        _logger.LogDebug($"Unified terminal session started. Chests={_chestCount}, Slots={_slotsTotalPhysical}");
    }

    public void Tick()
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        if (InventoryGui.instance == null || !InventoryGui.IsVisible())
        {
            EndSession();
            return;
        }

        // Intentionally no per-frame sync to avoid heavy inventory rebuilds.
    }

    public void SetSearchQuery(string query)
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        var normalized = query?.Trim() ?? string.Empty;
        if (string.Equals(normalized, _searchQuery, StringComparison.Ordinal))
        {
            return;
        }

        SyncDisplayedDeltaToWorking();
        _searchQuery = normalized;
        RefreshTerminalInventoryFromWorking();
        _uiRevision++;
    }

    public void NotifyContainerInteraction()
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        SyncDisplayedDeltaToWorking();
        CommitDeltasToPhysicalStorage();
        RefreshChestHandles();
        RebuildTotalsFromPhysicalStorage();
        RefreshTerminalInventoryFromWorking();
        _uiRevision++;
    }

    public void RefreshFromWorldChange()
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        SyncDisplayedDeltaToWorking();
        CommitDeltasToPhysicalStorage();
        RefreshChestHandles();
        RebuildTotalsFromPhysicalStorage();
        RefreshTerminalInventoryFromWorking();
        _uiRevision++;
    }

    public void EndSession()
    {
        if (_terminal == null || _player == null)
        {
            RestoreStackSizes();
            return;
        }

        SyncDisplayedDeltaToWorking();
        CommitDeltasToPhysicalStorage();
        ClearInventory(_terminal.GetInventory());
        RestoreTerminalInventorySize(_terminal.GetInventory());
        if (!string.IsNullOrWhiteSpace(_originalContainerName))
        {
            SetContainerName(_terminal, _originalContainerName!);
        }

        RestoreStackSizes();

        _terminal = null;
        _player = null;
        _chests.Clear();
        _searchQuery = string.Empty;
        _baseTotals.Clear();
        _workingTotals.Clear();
        _displayedTotals.Clear();
        _prototypes.Clear();
        _originalContainerName = null;
        _visibleRows = 0;
        _contentRows = 0;
        _scanRadius = 0f;
        _uiRevision++;
    }

    private void RefreshChestHandles()
    {
        if (_terminal == null)
        {
            _chests.Clear();
            _chestCount = 0;
            _slotsTotalPhysical = 0;
            return;
        }

        _chests = _scanner.GetNearbyContainers(_terminal.transform.position, _scanRadius, _terminal, onlyVanillaChests: true).ToList();
        _chestCount = _chests.Count;
        _slotsTotalPhysical = _chests.Sum(chest => GetInventoryTotalSlots(chest.Container.GetInventory()));
    }

    private void RebuildTotalsFromPhysicalStorage()
    {
        _baseTotals.Clear();
        _workingTotals.Clear();
        _displayedTotals.Clear();

        foreach (var chest in _chests)
        {
            var inv = chest.Container.GetInventory();
            if (inv == null)
            {
                continue;
            }

            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack <= 0)
                {
                    continue;
                }

                var key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
                _baseTotals[key] = _baseTotals.TryGetValue(key, out var existing) ? existing + item.m_stack : item.m_stack;
                _workingTotals[key] = _baseTotals[key];
                if (!_prototypes.ContainsKey(key))
                {
                    _prototypes[key] = item.Clone();
                }

                EnsureStack999(item);
            }
        }
    }

    private void RefreshTerminalInventoryFromWorking()
    {
        if (_terminal == null)
        {
            return;
        }

        var inventory = _terminal.GetInventory();
        if (inventory == null)
        {
            return;
        }

        ClearInventory(inventory);
        _displayedTotals.Clear();

        var width = Math.Max(1, GetInventoryWidth(inventory));
        var filtered = _workingTotals
            .Where(kvp => kvp.Value > 0 && MatchesSearch(GetDisplayName(kvp.Key)))
            .Select(kvp => new
            {
                Entry = kvp,
                DisplayName = GetDisplayName(kvp.Key),
                TypeOrder = GetItemTypeOrder(kvp.Key),
                SubgroupOrder = GetSubgroupOrder(kvp.Key)
            })
            .OrderBy(x => x.TypeOrder)
            .ThenBy(x => x.SubgroupOrder)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.Entry.Value)
            .Select(x => x.Entry)
            .ToList();

        var totalVirtualStacks = filtered.Sum(kvp => (int)Math.Ceiling(kvp.Value / 999f));
        var reserveEmptySlots = _slotsTotalPhysical > SlotsUsedVirtual ? 1 : 0;
        var requiredSlots = totalVirtualStacks + reserveEmptySlots;
        _contentRows = Math.Max(_visibleRows, (int)Math.Ceiling(requiredSlots / (float)width));
        SetInventorySize(inventory, width, _contentRows);

        foreach (var kvp in filtered)
        {
            var remaining = kvp.Value;
            while (remaining > 0)
            {
                var stack = Math.Min(999, remaining);
                var item = CreateItemStack(kvp.Key, stack);
                if (item == null || !inventory.AddItem(item))
                {
                    break;
                }

                remaining -= stack;
            }

            _displayedTotals[kvp.Key] = kvp.Value - remaining;
        }
    }

    private bool MatchesSearch(string displayName)
    {
        return string.IsNullOrWhiteSpace(_searchQuery)
               || displayName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void SyncDisplayedDeltaToWorking()
    {
        if (_terminal == null)
        {
            return;
        }

        var inventory = _terminal.GetInventory();
        if (inventory == null)
        {
            return;
        }

        var currentDisplayed = new Dictionary<ItemKey, int>();
        foreach (var item in inventory.GetAllItems())
        {
            if (item?.m_dropPrefab == null || item.m_stack <= 0)
            {
                continue;
            }

            var key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
            currentDisplayed[key] = currentDisplayed.TryGetValue(key, out var existing) ? existing + item.m_stack : item.m_stack;
        }

        var keys = new HashSet<ItemKey>(_displayedTotals.Keys);
        keys.UnionWith(currentDisplayed.Keys);
        foreach (var key in keys)
        {
            var previous = _displayedTotals.TryGetValue(key, out var prev) ? prev : 0;
            var current = currentDisplayed.TryGetValue(key, out var now) ? now : 0;
            var delta = current - previous;
            if (delta == 0)
            {
                continue;
            }

            var existing = _workingTotals.TryGetValue(key, out var value) ? value : 0;
            _workingTotals[key] = Math.Max(0, existing + delta);
        }

        _displayedTotals.Clear();
        foreach (var kv in currentDisplayed)
        {
            _displayedTotals[kv.Key] = kv.Value;
        }
    }

    private void CommitDeltasToPhysicalStorage()
    {
        if (_player == null)
        {
            return;
        }

        foreach (var key in _workingTotals.Keys.Union(_baseTotals.Keys).ToList())
        {
            var original = _baseTotals.TryGetValue(key, out var o) ? o : 0;
            var current = _workingTotals.TryGetValue(key, out var c) ? c : 0;
            var delta = current - original;
            if (delta == 0)
            {
                continue;
            }

            if (delta < 0)
            {
                var expectedRemoved = -delta;
                var actualRemoved = RemoveFromChests(key, expectedRemoved);
                if (actualRemoved < expectedRemoved)
                {
                    RemoveFromPlayerInventory(key, expectedRemoved - actualRemoved);
                }
            }
            else
            {
                var expectedStored = delta;
                var actualStored = AddToChests(key, expectedStored);
                if (actualStored < expectedStored)
                {
                    AddToPlayerInventory(key, expectedStored - actualStored);
                }
            }
        }
    }

    private int RemoveFromChests(ItemKey key, int amount)
    {
        var remaining = amount;
        foreach (var chest in _chests.OrderBy(c => c.Distance).ThenBy(c => c.SourceId))
        {
            if (remaining <= 0)
            {
                break;
            }

            var inv = chest.Container.GetInventory();
            if (inv == null || (_config.RequireAccessCheck.Value && !CanAccess(chest.Container, _player!)))
            {
                continue;
            }

            foreach (var item in inv.GetAllItems().Where(i => MatchKey(i, key)).ToList())
            {
                if (remaining <= 0)
                {
                    break;
                }

                var take = Math.Min(item.m_stack, remaining);
                inv.RemoveItem(item, take);
                remaining -= take;
            }
        }

        return amount - remaining;
    }

    private int AddToChests(ItemKey key, int amount)
    {
        var remaining = amount;
        foreach (var chest in _chests.OrderBy(c => c.Distance).ThenBy(c => c.SourceId))
        {
            if (remaining <= 0)
            {
                break;
            }

            var inv = chest.Container.GetInventory();
            if (inv == null || (_config.RequireAccessCheck.Value && !CanAccess(chest.Container, _player!)))
            {
                continue;
            }

            while (remaining > 0)
            {
                var stack = CreateItemStack(key, Math.Min(999, remaining));
                if (stack == null || !inv.AddItem(stack))
                {
                    break;
                }

                remaining -= stack.m_stack;
            }
        }

        return amount - remaining;
    }

    private void RemoveFromPlayerInventory(ItemKey key, int amount)
    {
        if (_player == null || amount <= 0)
        {
            return;
        }

        var remaining = amount;
        var playerInventory = _player.GetInventory();
        foreach (var item in playerInventory.GetAllItems().Where(i => i != null && MatchKey(i, key)).ToList())
        {
            if (remaining <= 0)
            {
                break;
            }

            var remove = Math.Min(remaining, item.m_stack);
            playerInventory.RemoveItem(item, remove);
            remaining -= remove;
        }
    }

    private void AddToPlayerInventory(ItemKey key, int amount)
    {
        if (_player == null || amount <= 0)
        {
            return;
        }

        var remaining = amount;
        var playerInventory = _player.GetInventory();
        while (remaining > 0)
        {
            var stack = CreateItemStack(key, Math.Min(999, remaining));
            if (stack == null || !playerInventory.AddItem(stack))
            {
                break;
            }

            remaining -= stack.m_stack;
        }
    }

    private ItemDrop.ItemData? CreateItemStack(ItemKey key, int amount)
    {
        if (!_prototypes.TryGetValue(key, out var prototype))
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(key.PrefabName);
            var drop = prefab?.GetComponent<ItemDrop>();
            if (drop?.m_itemData == null)
            {
                return null;
            }

            prototype = drop.m_itemData.Clone();
            prototype.m_quality = key.Quality;
            prototype.m_variant = key.Variant;
            _prototypes[key] = prototype;
        }

        var item = prototype.Clone();
        item.m_stack = amount;
        EnsureStack999(item);
        return item;
    }

    private string GetDisplayName(ItemKey key)
    {
        if (_prototypes.TryGetValue(key, out var prototype))
        {
            return prototype.m_shared.m_name;
        }

        return key.PrefabName;
    }

    private int GetItemTypeOrder(ItemKey key)
    {
        if (_prototypes.TryGetValue(key, out var prototype))
        {
            return (int)prototype.m_shared.m_itemType;
        }

        return int.MaxValue;
    }

    private static int GetSubgroupOrder(ItemKey key)
    {
        if (string.IsNullOrWhiteSpace(key.PrefabName))
        {
            return 999;
        }

        var prefab = key.PrefabName.ToLowerInvariant();

        // Mantem minérios/metais próximos sem custo alto: só checks simples de string.
        if (prefab.Contains("ore") || prefab.Contains("scrap") || prefab.Contains("metal") || prefab.Contains("ingot") || prefab.Contains("bar"))
        {
            return 10;
        }

        if (prefab.Contains("wood") || prefab.Contains("stone"))
        {
            return 20;
        }

        if (prefab.Contains("hide") || prefab.Contains("leather") || prefab.Contains("scale") || prefab.Contains("chitin"))
        {
            return 30;
        }

        if (prefab.Contains("food") || prefab.Contains("mead") || prefab.Contains("stew") || prefab.Contains("soup") || prefab.Contains("bread"))
        {
            return 40;
        }

        return 100;
    }

    private void EnsureStack999(ItemDrop.ItemData item)
    {
        var prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return;
        }

        var safePrefabName = prefabName!;
        if (!_originalStackSizes.ContainsKey(safePrefabName))
        {
            _originalStackSizes[safePrefabName] = item.m_shared.m_maxStackSize;
        }

        item.m_shared.m_maxStackSize = 999;
    }

    private void RestoreStackSizes()
    {
        foreach (var kv in _originalStackSizes)
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(kv.Key);
            var drop = prefab?.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared != null)
            {
                drop.m_itemData.m_shared.m_maxStackSize = kv.Value;
            }
        }

        _originalStackSizes.Clear();
    }

    private static int GetInventoryTotalSlots(Inventory? inventory)
    {
        if (inventory == null)
        {
            return 0;
        }

        return GetInventoryWidth(inventory) * GetInventoryHeight(inventory);
    }

    private static int GetInventoryWidth(Inventory inventory)
    {
        var widthMethod = typeof(Inventory).GetMethod("GetWidth", BindingFlags.Instance | BindingFlags.Public);
        if (widthMethod != null && widthMethod.Invoke(inventory, null) is int width)
        {
            return width;
        }

        var widthField = typeof(Inventory).GetField("m_width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return widthField?.GetValue(inventory) is int fieldWidth ? fieldWidth : 1;
    }

    private static int GetInventoryHeight(Inventory inventory)
    {
        var heightMethod = typeof(Inventory).GetMethod("GetHeight", BindingFlags.Instance | BindingFlags.Public);
        if (heightMethod != null && heightMethod.Invoke(inventory, null) is int height)
        {
            return height;
        }

        var heightField = typeof(Inventory).GetField("m_height", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return heightField?.GetValue(inventory) is int fieldHeight ? fieldHeight : 1;
    }

    private static void SetInventorySize(Inventory inventory, int width, int height)
    {
        var widthField = typeof(Inventory).GetField("m_width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var heightField = typeof(Inventory).GetField("m_height", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        widthField?.SetValue(inventory, width);
        heightField?.SetValue(inventory, height);
    }

    private void CaptureOriginalInventorySize(Inventory? inventory)
    {
        if (inventory == null)
        {
            _originalInventoryWidth = 8;
            _originalInventoryHeight = 4;
            return;
        }

        _originalInventoryWidth = GetInventoryWidth(inventory);
        _originalInventoryHeight = GetInventoryHeight(inventory);
    }

    private void RestoreTerminalInventorySize(Inventory? inventory)
    {
        if (inventory == null || _originalInventoryWidth <= 0 || _originalInventoryHeight <= 0)
        {
            return;
        }

        SetInventorySize(inventory, _originalInventoryWidth, _originalInventoryHeight);
    }

    private static void ClearInventory(Inventory? inventory)
    {
        if (inventory == null)
        {
            return;
        }

        var removeAll = typeof(Inventory).GetMethod("RemoveAll", BindingFlags.Instance | BindingFlags.Public);
        if (removeAll != null)
        {
            removeAll.Invoke(inventory, null);
            return;
        }

        foreach (var item in inventory.GetAllItems().ToList())
        {
            inventory.RemoveItem(item, item.m_stack);
        }
    }

    private static bool MatchKey(ItemDrop.ItemData item, ItemKey key)
    {
        return item?.m_dropPrefab != null
               && item.m_dropPrefab.name == key.PrefabName
               && item.m_quality == key.Quality
               && item.m_variant == key.Variant
               && item.m_stack > 0;
    }

    private static bool CanAccess(Container container, Player player)
    {
        var method = typeof(Container).GetMethod("CheckAccess", BindingFlags.Instance | BindingFlags.Public);
        if (method == null)
        {
            return true;
        }

        if (method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(long))
        {
            return (bool)method.Invoke(container, new object[] { player.GetPlayerID() });
        }

        if (method.GetParameters().Length == 0)
        {
            return (bool)method.Invoke(container, null);
        }

        return true;
    }

    private static string? GetContainerName(Container container)
    {
        var field = typeof(Container).GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(container) as string;
    }

    private static void SetContainerName(Container container, string name)
    {
        var field = typeof(Container).GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(container, name);
    }
}
