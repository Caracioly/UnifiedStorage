using System;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Diagnostics;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Network;
using UnifiedStorage.Mod.Patches;
using UnifiedStorage.Mod.Pieces;
using UnifiedStorage.Mod.Server;
using UnifiedStorage.Mod.Session;
using UnifiedStorage.Mod.Shared;
using UnifiedStorage.Mod.UI;
using UnityEngine;

namespace UnifiedStorage.Mod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
public sealed class UnifiedStoragePlugin : BaseUnityPlugin
{
    public const string PluginGuid = "andre.valheim.unifiedstorage";
    public const string PluginName = "Unified Storage";
    public const string PluginVersion = "1.1.0";
    private const float WorldChangeRefreshMinInterval = 0.2f;

    private static bool _blockGameInput;
    internal static UnifiedStoragePlugin? Instance { get; private set; }

    private StorageConfig? _config;
    private StorageTrace? _trace;
    private Harmony? _harmony;
    private IContainerScanner? _scanner;
    private TerminalSessionService? _session;
    private TerminalAuthorityService? _authority;
    private TerminalRpcRoutes? _rpcRoutes;
    private UnifiedTerminalRegistrar? _pieceRegistrar;
    private TerminalUIManager? _ui;

    private bool _trackedInventoryDirty;
    private bool _isApplyingRefresh;
    private bool _wasDragging;
    private float _nextAllowedWorldRefreshAt;
    private string _lastSearch = string.Empty;
    private int _lastUiRevision = -1;

    private void Awake()
    {
        Instance = this;
        _config = new StorageConfig(Config);
        _trace = new StorageTrace(Logger, _config);

        _scanner = new ContainerScanner(_config);
        _authority = new TerminalAuthorityService(_config, _scanner, Logger);
        _rpcRoutes = new TerminalRpcRoutes(_authority, Logger);
        _rpcRoutes.EnsureRegistered();
        _session = new TerminalSessionService(_config, _scanner, _rpcRoutes, Logger, _trace);
        _pieceRegistrar = new UnifiedTerminalRegistrar(_config, Logger);
        _ui = new TerminalUIManager();

        PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;

        _harmony = new Harmony($"{PluginGuid}.patches");
        _harmony.PatchAll(typeof(ContainerInteractPatch));
        _harmony.PatchAll(typeof(InventoryAddItemPatch));
        _harmony.PatchAll(typeof(InventoryAddItemAtPositionPatch));
        _harmony.PatchAll(typeof(InventoryMoveItemToThisPatch));
        _harmony.PatchAll(typeof(InventoryGuiHidePatch));
        _harmony.PatchAll(typeof(InventoryGuiAwakePatch));
        _harmony.PatchAll(typeof(InventoryGuiUpdatePatch));
        _harmony.PatchAll(typeof(InventoryGridOnLeftClickPatch));
        _harmony.PatchAll(typeof(InventoryGridOnRightClickPatch));
        _harmony.PatchAll(typeof(InventoryGuiOnTakeAllPatch));
        _harmony.PatchAll(typeof(InventoryGuiOnStackAllPatch));
        _harmony.PatchAll(typeof(InventoryGuiOnDropOutsidePatch));
        _harmony.PatchAll(typeof(InventoryChangedPatch));
        _harmony.PatchAll(typeof(ZInputGetButtonPatch));
        _harmony.PatchAll(typeof(ZInputGetButtonDownPatch));
        _harmony.PatchAll(typeof(ZInputGetButtonUpPatch));
        _harmony.PatchAll(typeof(ZInputGetKeyPatch));
        _harmony.PatchAll(typeof(ZInputGetKeyDownPatch));
        _harmony.PatchAll(typeof(ZInputGetKeyUpPatch));
        _harmony.PatchAll(typeof(ContainerHoverTextPatch));

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void Update()
    {
        _rpcRoutes?.EnsureRegistered();
        _authority?.Tick();
        _session?.Tick();
        TryProcessWorldChangeRefresh();
    }

    private void OnDestroy()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
        _harmony?.UnpatchSelf();
        _session?.EndSession();
        _blockGameInput = false;
        Instance = null;
    }

    private void OnVanillaPrefabsAvailable()
    {
        _pieceRegistrar?.RegisterPiece();
    }

    internal void TryHandleChestInteract(Container container, Humanoid character)
    {
        if (_session == null || character is not Player player || player != Player.m_localPlayer)
            return;

        var handled = _session.HandleContainerInteract(container, player);
        if (handled)
        {
            _lastSearch = string.Empty;
            _ui?.ClearSearch();
        }
    }

    internal void OnInventoryGuiClosing()
    {
        _blockGameInput = false;
        _lastSearch = string.Empty;
        _trackedInventoryDirty = false;
        _isApplyingRefresh = false;
        _wasDragging = false;
        _lastUiRevision = -1;
        _session?.EndSession();
        _ui?.Reset();
    }

    internal void OnInventoryGuiAwake(InventoryGui gui)
    {
        _ui?.EnsureNativeUi(gui);
    }

    internal void OnInventoryGuiUpdate(InventoryGui gui)
    {
        if (_ui == null || _session == null) return;

        _ui.EnsureNativeUi(gui);
        _ui.UpdateChestInclusionToggle(gui);

        if (!_session.IsActive || !InventoryGui.IsVisible() || !gui.IsContainerOpen())
        {
            _blockGameInput = false;
            _wasDragging = false;
            _ui.SetTakeAllButtonEnabled(gui, true);
            _ui.SetNativeUiVisible(false);
            _ui.RestoreLayout();
            _lastUiRevision = -1;
            return;
        }

        _ui.SetTakeAllButtonEnabled(gui, false);
        _ui.SetNativeUiVisible(true);
        _ui.UpdateContainerName(gui);

        var isDragging = ReflectionHelpers.IsDragInProgress();
        if (isDragging && !_wasDragging)
            OnContainerInteraction();
        _wasDragging = isDragging;

        if (!_ui.IsLayoutCaptured)
            _ui.ApplyExpandedLayout(gui);

        if (_session.UiRevision != _lastUiRevision)
        {
            _lastUiRevision = _session.UiRevision;
            _ui.UpdateMetaText(_session.SlotsUsedVirtual, _session.SlotsTotalPhysical, _session.ChestsInRange);
            _ui.RefreshContainerGrid(gui);
        }

        _ui.UpdateSearchBinding(gui);
        UpdateSearchFocusState();
        ProcessSearchChanges(gui);
    }

    internal static bool ShouldBlockGameInput() => _blockGameInput;
    internal bool IsUnifiedSessionActive() => _session != null && _session.IsActive;

    internal bool ShouldBlockDeposit(Inventory targetInventory)
    {
        if (_session == null || !_session.IsActive || _session.IsApplyingProjection) return false;
        if (!_session.IsTerminalInventory(targetInventory)) return false;
        return _session.IsStorageFull;
    }

    internal int GetNearbyChestCount(Vector3 position)
    {
        if (_scanner == null || _config == null) return 0;
        return _scanner.GetNearbyContainers(position, _config.ScanRadius.Value).Count;
    }

    internal void OnTrackedInventoryChanged(Inventory inventory)
    {
        if (_isApplyingRefresh || _session == null || !_session.IsActive || inventory == null || _session.IsApplyingProjection)
            return;
        if (!_session.IsTrackedInventory(inventory))
            return;
        _trackedInventoryDirty = true;
    }

    internal void OnContainerInteraction()
    {
        if (_session == null || !_session.IsActive || InventoryGui.instance == null)
            return;
        _trackedInventoryDirty = false;
        ExecuteSessionRefresh(() => _session.NotifyContainerInteraction());
        _ui?.UpdateMetaText(_session.SlotsUsedVirtual, _session.SlotsTotalPhysical, _session.ChestsInRange);
        _ui?.RefreshContainerGrid(InventoryGui.instance);
    }

    internal void OnInventoryGridInteraction(InventoryGrid grid)
    {
        if (_session == null || !_session.IsActive || InventoryGui.instance == null || grid == null)
            return;
        OnContainerInteraction();
    }

    private void ProcessSearchChanges(InventoryGui gui)
    {
        if (_ui == null || _session == null) return;
        var currentSearch = _ui.SearchText;
        if (!string.Equals(currentSearch, _lastSearch, StringComparison.Ordinal))
        {
            _lastSearch = currentSearch;
            _session.SetSearchQuery(_lastSearch);
            _ui.RefreshContainerGrid(gui);
        }
    }

    private void UpdateSearchFocusState()
    {
        _blockGameInput = _ui != null && _ui.IsSearchFocused;
    }

    private void TryProcessWorldChangeRefresh()
    {
        if (!_trackedInventoryDirty || _session == null || !_session.IsActive || InventoryGui.instance == null)
            return;
        if (!InventoryGui.IsVisible() || !InventoryGui.instance.IsContainerOpen())
            return;
        if (Time.unscaledTime < _nextAllowedWorldRefreshAt)
            return;
        if (ReflectionHelpers.IsDragInProgress())
            return;

        _nextAllowedWorldRefreshAt = Time.unscaledTime + WorldChangeRefreshMinInterval;
        _trackedInventoryDirty = false;
        ExecuteSessionRefresh(() => _session.RefreshFromWorldChange());
        _ui?.UpdateMetaText(_session.SlotsUsedVirtual, _session.SlotsTotalPhysical, _session.ChestsInRange);
        _ui?.RefreshContainerGrid(InventoryGui.instance);
    }

    private void ExecuteSessionRefresh(Action refreshAction)
    {
        if (_isApplyingRefresh) return;
        _isApplyingRefresh = true;
        try { refreshAction(); }
        finally { _isApplyingRefresh = false; }
    }
}
