using System;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using TMPro;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Patches;
using UnifiedStorage.Mod.Pieces;
using UnifiedStorage.Mod.Server;
using UnifiedStorage.Mod.Session;
using UnityEngine;
using UnityEngine.UI;

namespace UnifiedStorage.Mod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
public sealed class UnifiedStoragePlugin : BaseUnityPlugin
{
    public const string PluginGuid = "andre.valheim.unifiedstorage";
    public const string PluginName = "Unified Storage";
    public const string PluginVersion = "1.0.2";
    private const int VisibleGridRows = 7;
    private const float FooterPanelBottomOffset = -62f;
    private const float FooterPanelHeight = 44f;
    private const float GridTopReserve = 124f;
    private const float WorldChangeRefreshMinInterval = 0.2f;

    private static readonly System.Reflection.FieldInfo? ContainerField = AccessTools.Field(typeof(InventoryGui), "m_container");
    private static readonly System.Reflection.FieldInfo? ContainerGridField = AccessTools.Field(typeof(InventoryGui), "m_containerGrid");
    private static readonly System.Reflection.FieldInfo? ContainerNameField = AccessTools.Field(typeof(InventoryGui), "m_containerName");
    private static readonly System.Reflection.FieldInfo? TakeAllButtonField = AccessTools.Field(typeof(InventoryGui), "m_takeAllButton");
    private static readonly System.Reflection.FieldInfo? DragItemField = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
    private static readonly System.Reflection.FieldInfo? GridRootField = AccessTools.Field(typeof(InventoryGrid), "m_gridRoot");
    private static readonly System.Reflection.FieldInfo? GridHeightField = AccessTools.Field(typeof(InventoryGrid), "m_height");
    private static readonly System.Reflection.FieldInfo? GridElementPrefabField = AccessTools.Field(typeof(InventoryGrid), "m_elementPrefab");
    private static readonly System.Reflection.FieldInfo? GridElementSpaceField = AccessTools.Field(typeof(InventoryGrid), "m_elementSpace");

    private static bool _blockGameInput;
    internal static UnifiedStoragePlugin? Instance { get; private set; }

    private StorageConfig? _config;
    private Harmony? _harmony;
    private UnifiedTerminalSessionService? _session;
    private UnifiedChestPieceRegistrar? _pieceRegistrar;

    private RectTransform? _nativeUiRoot;
    private TMP_Text? _metaText;
    private TMP_InputField? _searchInputField;
    private RectTransform? _containerRect;
    private Vector2 _originalContainerSize;
    private int _originalGridHeight;
    private Vector2 _originalGridOffsetMin;
    private Vector2 _originalGridOffsetMax;
    private bool _layoutCaptured;
    private bool _nativeBuilt;
    private string _lastSearch = string.Empty;
    private int _lastUiRevision = -1;
    private bool _trackedInventoryDirty;
    private float _nextAllowedWorldRefreshAt;

    private void Awake()
    {
        Instance = this;
        _config = new StorageConfig(Config);

        var scanner = new ContainerScanner(_config);
        _session = new UnifiedTerminalSessionService(_config, scanner, Logger);
        _pieceRegistrar = new UnifiedChestPieceRegistrar(_config, Logger);
        PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;

        _harmony = new Harmony($"{PluginGuid}.patches");
        _harmony.PatchAll(typeof(ContainerInteractPatch));
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

        Logger.LogInfo($"{PluginName} loaded.");
    }

    private void Update()
    {
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

    internal bool TryHandleChestInteract(Container container, Humanoid character, bool hold)
    {
        if (_session == null || character is not Player player || player != Player.m_localPlayer)
        {
            return false;
        }

        var handled = _session.HandleTerminalInteract(container, player, hold);
        if (handled)
        {
            _lastSearch = string.Empty;
            if (_searchInputField != null)
            {
                _searchInputField.text = string.Empty;
            }
        }

        return handled;
    }

    internal void OnInventoryGuiClosing()
    {
        _blockGameInput = false;
        _lastSearch = string.Empty;
        _trackedInventoryDirty = false;
        if (_searchInputField != null)
        {
            _searchInputField.text = string.Empty;
        }

        _session?.EndSession();
        RestoreLayout();
        SetNativeUiVisible(false);
        _lastUiRevision = -1;
    }

    internal void OnInventoryGuiAwake(InventoryGui gui)
    {
        EnsureNativeUi(gui);
    }

    internal void OnInventoryGuiUpdate(InventoryGui gui)
    {
        EnsureNativeUi(gui);
        if (_session == null || !_session.IsActive || !InventoryGui.IsVisible() || !gui.IsContainerOpen())
        {
            _blockGameInput = false;
            SetTakeAllButtonEnabled(gui, true);
            SetNativeUiVisible(false);
            RestoreLayout();
            _lastUiRevision = -1;
            return;
        }

        SetTakeAllButtonEnabled(gui, false);
        SetNativeUiVisible(true);
        UpdateContainerName(gui);
        if (!_layoutCaptured)
        {
            ApplyExpandedLayout(gui);
        }

        if (_session.UiRevision != _lastUiRevision)
        {
            _lastUiRevision = _session.UiRevision;
            UpdateMetaText();
            RefreshContainerGrid(gui);
        }

        UpdateSearchBinding(gui);
        UpdateSearchFocusState();
    }

    internal static bool ShouldBlockGameInput() => _blockGameInput;
    internal bool IsUnifiedSessionActive() => _session != null && _session.IsActive;

    internal void OnTrackedInventoryChanged(Inventory inventory)
    {
        if (_session == null || !_session.IsActive || inventory == null)
        {
            return;
        }

        if (!_session.IsTrackedInventory(inventory))
        {
            return;
        }

        _trackedInventoryDirty = true;
    }

    private void EnsureNativeUi(InventoryGui gui)
    {
        if (_nativeBuilt)
        {
            return;
        }

        var container = ContainerField?.GetValue(gui) as RectTransform;
        if (container == null)
        {
            return;
        }

        _containerRect = container;

        var containerName = ContainerNameField?.GetValue(gui) as TMP_Text;
        var root = new GameObject("US_NativePanel", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(container, false);
        _nativeUiRoot = root.GetComponent<RectTransform>();
        _nativeUiRoot.anchorMin = new Vector2(0.02f, 0f);
        _nativeUiRoot.anchorMax = new Vector2(0.98f, 0f);
        _nativeUiRoot.pivot = new Vector2(0.5f, 0f);
        _nativeUiRoot.anchoredPosition = new Vector2(0f, FooterPanelBottomOffset);
        _nativeUiRoot.sizeDelta = new Vector2(0f, FooterPanelHeight);
        var bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);
        bg.raycastTarget = false;

        var metaBgGo = new GameObject("US_MetaBg", typeof(RectTransform), typeof(Image));
        metaBgGo.transform.SetParent(root.transform, false);
        var metaBgRect = metaBgGo.GetComponent<RectTransform>();
        metaBgRect.anchorMin = new Vector2(0f, 1f);
        metaBgRect.anchorMax = new Vector2(1f, 1f);
        metaBgRect.pivot = new Vector2(0.5f, 1f);
        metaBgRect.anchoredPosition = new Vector2(0f, -1f);
        metaBgRect.sizeDelta = new Vector2(0f, 18f);
        metaBgGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

        var metaGo = new GameObject("US_Meta", typeof(RectTransform), typeof(TextMeshProUGUI));
        metaGo.transform.SetParent(root.transform, false);
        var metaRect = metaGo.GetComponent<RectTransform>();
        metaRect.anchorMin = new Vector2(0f, 1f);
        metaRect.anchorMax = new Vector2(1f, 1f);
        metaRect.pivot = new Vector2(0f, 1f);
        metaRect.anchoredPosition = new Vector2(10f, -2f);
        metaRect.sizeDelta = new Vector2(-20f, 17f);
        _metaText = metaGo.GetComponent<TextMeshProUGUI>();
        if (containerName != null)
        {
            _metaText.font = containerName.font;
            _metaText.fontSize = containerName.fontSize * 0.54f;
            _metaText.color = containerName.color;
            _metaText.fontSharedMaterial = containerName.fontSharedMaterial;
        }

        var searchLabelGo = new GameObject("US_SearchLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        searchLabelGo.transform.SetParent(root.transform, false);
        var labelRect = searchLabelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(10f, -19f);
        labelRect.sizeDelta = new Vector2(50f, 16f);
        var searchLabel = searchLabelGo.GetComponent<TextMeshProUGUI>();
        searchLabel.text = "Search";
        if (containerName != null)
        {
            searchLabel.font = containerName.font;
            searchLabel.fontSize = containerName.fontSize * 0.5f;
            searchLabel.color = containerName.color;
            searchLabel.fontSharedMaterial = containerName.fontSharedMaterial;
        }

        var inputRoot = new GameObject("US_SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputRoot.transform.SetParent(root.transform, false);
        var inputRect = inputRoot.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 1f);
        inputRect.anchorMax = new Vector2(1f, 1f);
        inputRect.pivot = new Vector2(0f, 1f);
        inputRect.anchoredPosition = new Vector2(62f, -18f);
        inputRect.sizeDelta = new Vector2(-72f, 14f);
        var inputBg = inputRoot.GetComponent<Image>();
        inputBg.color = new Color(0f, 0f, 0f, 0.62f);
        _searchInputField = inputRoot.GetComponent<TMP_InputField>();

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(inputRoot.transform, false);
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(6f, 1f);
        viewportRect.offsetMax = new Vector2(-6f, -1f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(viewportGo.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var inputText = textGo.GetComponent<TextMeshProUGUI>();
        if (containerName != null)
        {
            inputText.font = containerName.font;
            inputText.fontSize = containerName.fontSize * 0.5f;
            inputText.color = containerName.color;
            inputText.fontSharedMaterial = containerName.fontSharedMaterial;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(viewportGo.transform, false);
        var placeholderRect = placeholderGo.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;
        var placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
        placeholder.text = string.Empty;
        if (containerName != null)
        {
            placeholder.font = containerName.font;
            placeholder.fontSize = containerName.fontSize * 0.48f;
            placeholder.color = new Color(containerName.color.r, containerName.color.g, containerName.color.b, 0f);
            placeholder.fontSharedMaterial = containerName.fontSharedMaterial;
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        }

        _searchInputField.textViewport = viewportRect;
        _searchInputField.textComponent = inputText;
        _searchInputField.placeholder = placeholder;
        _searchInputField.lineType = TMP_InputField.LineType.SingleLine;
        _searchInputField.onValueChanged.AddListener(OnSearchValueChanged);
        _searchInputField.onSelect.AddListener(_ => { _blockGameInput = true; });
        _searchInputField.onDeselect.AddListener(_ => { _blockGameInput = false; });

        _nativeBuilt = true;
        SetNativeUiVisible(false);
    }

    private void UpdateContainerName(InventoryGui gui)
    {
        var nameText = ContainerNameField?.GetValue(gui) as TMP_Text;
        if (nameText != null && !string.Equals(nameText.text, "Storage Interface", System.StringComparison.Ordinal))
        {
            nameText.text = "Storage Interface";
        }
    }

    private void UpdateMetaText()
    {
        if (_metaText == null || _session == null)
        {
            return;
        }

        _metaText.text = $"Storage: {_session.SlotsUsedVirtual}/{_session.SlotsTotalPhysical} slots  |  Chests in range: {_session.ChestsInRange}";
    }

    private void UpdateSearchBinding(InventoryGui gui)
    {
        if (_searchInputField == null || _session == null)
        {
            return;
        }

        if (_searchInputField.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape))
            {
                _searchInputField.DeactivateInputField();
                _blockGameInput = false;
            }
        }

        if (!_searchInputField.isFocused && !string.Equals(_searchInputField.text, _lastSearch, System.StringComparison.Ordinal))
        {
            _lastSearch = _searchInputField.text;
            _session.SetSearchQuery(_lastSearch);
            RefreshContainerGrid(gui);
        }
    }

    private void OnSearchValueChanged(string value)
    {
        _lastSearch = value ?? string.Empty;
        _session?.SetSearchQuery(_lastSearch);
        if (InventoryGui.instance != null)
        {
            RefreshContainerGrid(InventoryGui.instance);
        }
    }

    internal void OnContainerInteraction()
    {
        if (_session == null || !_session.IsActive || InventoryGui.instance == null)
        {
            return;
        }

        if (IsDragInProgress(InventoryGui.instance))
        {
            return;
        }

        _session.NotifyContainerInteraction();
        UpdateMetaText();
        RefreshContainerGrid(InventoryGui.instance);
    }

    internal void OnInventoryGridInteraction(InventoryGrid grid)
    {
        if (_session == null || !_session.IsActive || InventoryGui.instance == null || grid == null)
        {
            return;
        }

        var containerGrid = ContainerGridField?.GetValue(InventoryGui.instance) as InventoryGrid;
        if (!ReferenceEquals(grid, containerGrid))
        {
            return;
        }

        OnContainerInteraction();
    }

    private static bool IsDragInProgress(InventoryGui gui)
    {
        return DragItemField?.GetValue(gui) is ItemDrop.ItemData dragItem && dragItem != null && dragItem.m_stack > 0;
    }

    private void UpdateSearchFocusState()
    {
        _blockGameInput = _searchInputField != null && _searchInputField.isFocused;
    }

    private void ApplyExpandedLayout(InventoryGui gui)
    {
        if (_session == null || _containerRect == null)
        {
            return;
        }

        var grid = ContainerGridField?.GetValue(gui) as InventoryGrid;
        if (grid == null)
        {
            return;
        }

        if (!_layoutCaptured)
        {
            _originalContainerSize = _containerRect.sizeDelta;
            _originalGridHeight = GridHeightField?.GetValue(grid) is int h ? h : 4;
            var gridRootCached = GridRootField?.GetValue(grid) as RectTransform;
            if (gridRootCached != null)
            {
                _originalGridOffsetMin = gridRootCached.offsetMin;
                _originalGridOffsetMax = gridRootCached.offsetMax;
            }
            _layoutCaptured = true;
        }

        var targetRows = VisibleGridRows;
        GridHeightField?.SetValue(grid, targetRows);

        var elementSpace = GridElementSpaceField?.GetValue(grid) is float s ? s : 2f;
        var elementPrefab = GridElementPrefabField?.GetValue(grid) as GameObject;
        var cellHeight = 64f;
        if (elementPrefab != null && elementPrefab.TryGetComponent<RectTransform>(out var elementRect))
        {
            cellHeight = elementRect.sizeDelta.y;
        }

        var addedRows = Math.Max(0, targetRows - _originalGridHeight);
        var addedHeight = (cellHeight + elementSpace) * addedRows;
        _containerRect.sizeDelta = new Vector2(_originalContainerSize.x, _originalContainerSize.y + addedHeight);

        var gridRoot = GridRootField?.GetValue(grid) as RectTransform;
        if (gridRoot != null)
        {
            gridRoot.offsetMax = new Vector2(_originalGridOffsetMax.x, -GridTopReserve);
            gridRoot.offsetMin = _originalGridOffsetMin;
        }
    }

    private void RefreshContainerGrid(InventoryGui gui)
    {
        var grid = ContainerGridField?.GetValue(gui) as InventoryGrid;
        if (grid == null)
        {
            return;
        }

        var gridInventory = grid.GetInventory();
        if (gridInventory != null)
        {
            grid.UpdateInventory(gridInventory, Player.m_localPlayer, null);
        }
    }

    private void RestoreLayout()
    {
        if (!_layoutCaptured || InventoryGui.instance == null)
        {
            return;
        }

        var grid = ContainerGridField?.GetValue(InventoryGui.instance) as InventoryGrid;
        if (grid != null)
        {
            GridHeightField?.SetValue(grid, _originalGridHeight);
            var gridRoot = GridRootField?.GetValue(grid) as RectTransform;
            if (gridRoot != null)
            {
                gridRoot.offsetMin = _originalGridOffsetMin;
                gridRoot.offsetMax = _originalGridOffsetMax;
            }
            var gridInventory = grid.GetInventory();
            if (gridInventory != null)
            {
                grid.UpdateInventory(gridInventory, Player.m_localPlayer, null);
            }
        }

        if (_containerRect != null)
        {
            _containerRect.sizeDelta = _originalContainerSize;
        }

        _layoutCaptured = false;
    }

    private void SetNativeUiVisible(bool visible)
    {
        if (_nativeUiRoot != null)
        {
            _nativeUiRoot.gameObject.SetActive(visible);
        }
    }

    private void TryProcessWorldChangeRefresh()
    {
        if (!_trackedInventoryDirty || _session == null || !_session.IsActive || InventoryGui.instance == null)
        {
            return;
        }

        if (!InventoryGui.IsVisible() || !InventoryGui.instance.IsContainerOpen())
        {
            return;
        }

        if (Time.unscaledTime < _nextAllowedWorldRefreshAt)
        {
            return;
        }

        if (IsDragInProgress(InventoryGui.instance))
        {
            return;
        }

        _nextAllowedWorldRefreshAt = Time.unscaledTime + WorldChangeRefreshMinInterval;
        _trackedInventoryDirty = false;
        _session.RefreshFromWorldChange();
        UpdateMetaText();
        RefreshContainerGrid(InventoryGui.instance);
    }

    private static void SetTakeAllButtonEnabled(InventoryGui gui, bool enabled)
    {
        if (TakeAllButtonField?.GetValue(gui) is Button takeAllButton && takeAllButton != null)
        {
            takeAllButton.interactable = enabled;
        }
    }
}
