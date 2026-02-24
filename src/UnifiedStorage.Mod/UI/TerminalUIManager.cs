using System;
using TMPro;
using UnifiedStorage.Mod.Pieces;
using UnifiedStorage.Mod.Server;
using UnifiedStorage.Mod.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace UnifiedStorage.Mod.UI;

public sealed class TerminalUIManager
{
    private const int VisibleGridRows = 7;
    private const float FooterPanelBottomOffset = -70f;
    private const float FooterPanelHeight = 52f;
    private const float GridTopReserve = 124f;
    private const float ChestToggleTopOffset = -8f;

    private RectTransform? _nativeUiRoot;
    private TMP_Text? _metaText;
    private Image? _slotBarFill;
    private TMP_InputField? _searchInputField;
    private RectTransform? _chestToggleRoot;
    private Toggle? _chestIncludeToggle;
    private RectTransform? _containerRect;
    private Container? _boundChestForToggle;
    private Vector2 _originalContainerSize;
    private int _originalGridHeight;
    private Vector2 _originalGridOffsetMin;
    private Vector2 _originalGridOffsetMax;
    private bool _layoutCaptured;
    private bool _nativeBuilt;
    private bool _isApplyingChestToggle;
    private int _lastAppliedVisibleRows;

    public bool IsSearchFocused => _searchInputField != null && _searchInputField.isFocused;
    public string SearchText => _searchInputField?.text ?? string.Empty;

    public event Action<string>? SearchValueChanged;
    public event Action<bool>? ChestToggleChanged;

    public void EnsureNativeUi(InventoryGui gui)
    {
        if (_nativeBuilt) return;

        var container = ReflectionHelpers.GetContainerRect(gui);
        if (container == null) return;
        _containerRect = container;

        var containerName = ReflectionHelpers.GetContainerName(gui);
        var root = new GameObject("US_NativePanel", typeof(RectTransform));
        root.transform.SetParent(container, false);
        _nativeUiRoot = root.GetComponent<RectTransform>();
        _nativeUiRoot.anchorMin = new Vector2(0f, 0f);
        _nativeUiRoot.anchorMax = new Vector2(1f, 0f);
        _nativeUiRoot.pivot = new Vector2(0.5f, 0f);
        _nativeUiRoot.anchoredPosition = new Vector2(0f, FooterPanelBottomOffset);
        _nativeUiRoot.sizeDelta = new Vector2(0f, FooterPanelHeight);

        BuildSlotBar(root.transform, containerName);
        BuildSearchField(root.transform, containerName);

        EnsureChestToggleUi(container, containerName);

        _nativeBuilt = true;
        SetNativeUiVisible(false);
        SetChestToggleVisible(false);
    }

    private void BuildSlotBar(Transform parent, TMP_Text? containerName)
    {
        var barHeight = 20f;

        var barBg = new GameObject("US_SlotBarBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(parent, false);
        var barBgRect = barBg.GetComponent<RectTransform>();
        barBgRect.anchorMin = new Vector2(0f, 1f);
        barBgRect.anchorMax = new Vector2(1f, 1f);
        barBgRect.pivot = new Vector2(0.5f, 1f);
        barBgRect.anchoredPosition = new Vector2(0f, 0f);
        barBgRect.sizeDelta = new Vector2(-16f, barHeight);
        var barBgImg = barBg.GetComponent<Image>();
        barBgImg.color = new Color(0f, 0f, 0f, 0.55f);
        barBgImg.raycastTarget = false;

        var barFill = new GameObject("US_SlotBarFill", typeof(RectTransform), typeof(Image));
        barFill.transform.SetParent(barBg.transform, false);
        var barFillRect = barFill.GetComponent<RectTransform>();
        barFillRect.anchorMin = new Vector2(0f, 0f);
        barFillRect.anchorMax = new Vector2(0f, 1f);
        barFillRect.pivot = new Vector2(0f, 0.5f);
        barFillRect.offsetMin = new Vector2(2f, 2f);
        barFillRect.offsetMax = new Vector2(2f, -2f);
        _slotBarFill = barFill.GetComponent<Image>();
        _slotBarFill.color = new Color(0.85f, 0.75f, 0.2f, 0.85f);
        _slotBarFill.raycastTarget = false;

        var labelGo = new GameObject("US_SlotLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(barBg.transform, false);
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 0f);
        labelRect.offsetMax = new Vector2(-6f, 0f);
        _metaText = labelGo.GetComponent<TextMeshProUGUI>();
        if (containerName != null)
        {
            _metaText.font = containerName.font;
            _metaText.fontSharedMaterial = containerName.fontSharedMaterial;
        }
        _metaText.fontSize = 14f;
        _metaText.color = new Color(1f, 1f, 1f, 0.95f);
        _metaText.alignment = TextAlignmentOptions.Center;
        _metaText.raycastTarget = false;
    }

    private void BuildSearchField(Transform parent, TMP_Text? containerName)
    {
        var searchHeight = 22f;
        var topOffset = -24f;

        var searchRow = new GameObject("US_SearchRow", typeof(RectTransform));
        searchRow.transform.SetParent(parent, false);
        var searchRowRect = searchRow.GetComponent<RectTransform>();
        searchRowRect.anchorMin = new Vector2(0f, 1f);
        searchRowRect.anchorMax = new Vector2(1f, 1f);
        searchRowRect.pivot = new Vector2(0.5f, 1f);
        searchRowRect.anchoredPosition = new Vector2(0f, topOffset);
        searchRowRect.sizeDelta = new Vector2(-16f, searchHeight);

        var inputRoot = new GameObject("US_SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputRoot.transform.SetParent(searchRow.transform, false);
        var inputRect = inputRoot.GetComponent<RectTransform>();
        inputRect.anchorMin = Vector2.zero;
        inputRect.anchorMax = Vector2.one;
        inputRect.offsetMin = Vector2.zero;
        inputRect.offsetMax = Vector2.zero;
        var inputBg = inputRoot.GetComponent<Image>();
        inputBg.color = new Color(0f, 0f, 0f, 0.5f);
        _searchInputField = inputRoot.GetComponent<TMP_InputField>();

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(inputRoot.transform, false);
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(8f, 2f);
        viewportRect.offsetMax = new Vector2(-8f, -2f);

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
            inputText.fontSharedMaterial = containerName.fontSharedMaterial;
        }
        inputText.fontSize = 14f;
        inputText.color = new Color(1f, 1f, 1f, 0.9f);
        inputText.alignment = TextAlignmentOptions.MidlineLeft;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(viewportGo.transform, false);
        var placeholderRect = placeholderGo.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;
        var placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
        placeholder.text = "Search items...";
        if (containerName != null)
        {
            placeholder.font = containerName.font;
            placeholder.fontSharedMaterial = containerName.fontSharedMaterial;
        }
        placeholder.fontSize = 13f;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.fontStyle = FontStyles.Italic;

        _searchInputField.textViewport = viewportRect;
        _searchInputField.textComponent = inputText;
        _searchInputField.placeholder = placeholder;
        _searchInputField.lineType = TMP_InputField.LineType.SingleLine;
        _searchInputField.onValueChanged.AddListener(value => SearchValueChanged?.Invoke(value ?? string.Empty));
    }

    private void EnsureChestToggleUi(RectTransform container, TMP_Text? containerName)
    {
        if (_chestToggleRoot != null && _chestIncludeToggle != null) return;

        var toggleRootGo = new GameObject("US_ChestIncludeToggle", typeof(RectTransform), typeof(Toggle));
        toggleRootGo.transform.SetParent(container, false);
        _chestToggleRoot = toggleRootGo.GetComponent<RectTransform>();
        _chestToggleRoot.anchorMin = new Vector2(1f, 1f);
        _chestToggleRoot.anchorMax = new Vector2(1f, 1f);
        _chestToggleRoot.pivot = new Vector2(1f, 1f);
        _chestToggleRoot.anchoredPosition = new Vector2(-14f, ChestToggleTopOffset);
        _chestToggleRoot.sizeDelta = new Vector2(190f, 20f);

        var toggle = toggleRootGo.GetComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.isOn = true;
        _chestIncludeToggle = toggle;

        var backgroundGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundGo.transform.SetParent(toggleRootGo.transform, false);
        var backgroundRect = backgroundGo.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.5f);
        backgroundRect.anchorMax = new Vector2(0f, 0.5f);
        backgroundRect.pivot = new Vector2(0f, 0.5f);
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundRect.sizeDelta = new Vector2(16f, 16f);
        var backgroundImage = backgroundGo.GetComponent<Image>();
        backgroundImage.color = new Color(0f, 0f, 0f, 0.62f);

        var checkmarkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        checkmarkGo.transform.SetParent(backgroundGo.transform, false);
        var checkmarkRect = checkmarkGo.GetComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
        checkmarkRect.anchoredPosition = Vector2.zero;
        checkmarkRect.sizeDelta = new Vector2(11f, 11f);
        var checkmarkImage = checkmarkGo.GetComponent<Image>();
        checkmarkImage.color = new Color(0.82f, 0.93f, 0.66f, 1f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(toggleRootGo.transform, false);
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(1f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(22f, 0f);
        labelRect.sizeDelta = new Vector2(-22f, 18f);
        var labelText = labelGo.GetComponent<TextMeshProUGUI>();
        labelText.text = "Include In Unified";
        if (containerName != null)
        {
            labelText.font = containerName.font;
            labelText.fontSize = containerName.fontSize * 0.52f;
            labelText.color = containerName.color;
            labelText.fontSharedMaterial = containerName.fontSharedMaterial;
        }
        else
        {
            labelText.fontSize = 14f;
            labelText.color = Color.white;
        }
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        toggle.targetGraphic = backgroundImage;
        toggle.graphic = checkmarkImage;
        toggle.onValueChanged.AddListener(OnChestIncludeToggleChanged);
    }

    public void UpdateChestInclusionToggle(InventoryGui gui)
    {
        if (_chestToggleRoot == null || _chestIncludeToggle == null || !InventoryGui.IsVisible() || !gui.IsContainerOpen())
        {
            _boundChestForToggle = null;
            SetChestToggleVisible(false);
            return;
        }

        var currentContainer = ReflectionHelpers.GetCurrentContainer(gui);
        if (currentContainer == null
            || UnifiedTerminal.IsTerminal(currentContainer))
        {
            _boundChestForToggle = null;
            SetChestToggleVisible(false);
            return;
        }

        SetChestToggleVisible(true);
        var includeInUnified = ChestInclusionRules.IsIncludedInUnified(currentContainer);
        if (!ReferenceEquals(_boundChestForToggle, currentContainer) || _chestIncludeToggle.isOn != includeInUnified)
        {
            _isApplyingChestToggle = true;
            _chestIncludeToggle.isOn = includeInUnified;
            _isApplyingChestToggle = false;
            _boundChestForToggle = currentContainer;
        }
    }

    private void OnChestIncludeToggleChanged(bool includeInUnified)
    {
        if (_isApplyingChestToggle) return;

        var gui = InventoryGui.instance;
        var currentContainer = gui != null ? ReflectionHelpers.GetCurrentContainer(gui) : null;
        if (currentContainer == null
            || UnifiedTerminal.IsTerminal(currentContainer))
            return;

        ChestInclusionRules.TrySetIncludedInUnified(currentContainer, includeInUnified);
        ChestToggleChanged?.Invoke(includeInUnified);
    }

    public void UpdateContainerName(InventoryGui gui)
    {
        var nameText = ReflectionHelpers.GetContainerName(gui);
        if (nameText != null && !string.Equals(nameText.text, "Storage Interface", StringComparison.Ordinal))
            nameText.text = "Storage Interface";
    }

    public void UpdateMetaText(int slotsUsed, int slotsTotal, int chestCount)
    {
        if (_metaText != null)
            _metaText.text = $"{slotsUsed} / {slotsTotal}";

        if (_slotBarFill != null)
        {
            var ratio = slotsTotal > 0 ? Mathf.Clamp01((float)slotsUsed / slotsTotal) : 0f;
            var barBgRect = _slotBarFill.transform.parent as RectTransform;
            var barWidth = barBgRect != null ? barBgRect.rect.width - 4f : 100f;
            var fillRect = _slotBarFill.GetComponent<RectTransform>();
            fillRect.sizeDelta = new Vector2(barWidth * ratio, 0f);

            if (ratio >= 0.95f)
                _slotBarFill.color = new Color(0.85f, 0.25f, 0.2f, 0.85f);
            else if (ratio >= 0.75f)
                _slotBarFill.color = new Color(0.9f, 0.6f, 0.15f, 0.85f);
            else
                _slotBarFill.color = new Color(0.85f, 0.75f, 0.2f, 0.85f);
        }
    }

    public void UpdateSearchBinding(InventoryGui gui)
    {
        if (_searchInputField == null) return;

        if (_searchInputField.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape))
                _searchInputField.DeactivateInputField();
        }
    }

    public void ClearSearch()
    {
        if (_searchInputField != null)
            _searchInputField.text = string.Empty;
    }

    public void ApplyExpandedLayout(InventoryGui gui, int contentRows)
    {
        if (_containerRect == null) return;

        var grid = ReflectionHelpers.GetContainerGrid(gui);
        if (grid == null) return;

        if (!_layoutCaptured)
        {
            _originalContainerSize = _containerRect.sizeDelta;
            _originalGridHeight = ReflectionHelpers.GetGridHeight(grid);
            var gridRoot = ReflectionHelpers.GetGridRoot(grid);
            if (gridRoot != null)
            {
                _originalGridOffsetMin = gridRoot.offsetMin;
                _originalGridOffsetMax = gridRoot.offsetMax;
            }
            _layoutCaptured = true;
            _lastAppliedVisibleRows = -1;
        }

        var visibleRows = Math.Max(1, Math.Min(contentRows, VisibleGridRows));
        if (visibleRows == _lastAppliedVisibleRows) return;
        _lastAppliedVisibleRows = visibleRows;

        ReflectionHelpers.SetGridHeight(grid, visibleRows);

        var elementSpace = ReflectionHelpers.GetGridElementSpace(grid);
        var elementPrefab = ReflectionHelpers.GetGridElementPrefab(grid);
        var cellHeight = 64f;
        if (elementPrefab != null && elementPrefab.TryGetComponent<RectTransform>(out var elementRect))
            cellHeight = elementRect.sizeDelta.y;

        var addedRows = Math.Max(0, visibleRows - _originalGridHeight);
        var addedHeight = (cellHeight + elementSpace) * addedRows;
        _containerRect.sizeDelta = new Vector2(_originalContainerSize.x, _originalContainerSize.y + addedHeight);

        var gridRootRef = ReflectionHelpers.GetGridRoot(grid);
        if (gridRootRef != null)
        {
            gridRootRef.offsetMax = new Vector2(_originalGridOffsetMax.x, -GridTopReserve);
            gridRootRef.offsetMin = _originalGridOffsetMin;
        }
    }

    public void RestoreLayout()
    {
        if (!_layoutCaptured || InventoryGui.instance == null) return;

        var grid = ReflectionHelpers.GetContainerGrid(InventoryGui.instance);
        if (grid != null)
        {
            ReflectionHelpers.SetGridHeight(grid, _originalGridHeight);
            var gridRoot = ReflectionHelpers.GetGridRoot(grid);
            if (gridRoot != null)
            {
                gridRoot.offsetMin = _originalGridOffsetMin;
                gridRoot.offsetMax = _originalGridOffsetMax;
            }
            var gridInventory = grid.GetInventory();
            if (gridInventory != null)
                grid.UpdateInventory(gridInventory, Player.m_localPlayer, null);
        }

        if (_containerRect != null)
            _containerRect.sizeDelta = _originalContainerSize;

        _layoutCaptured = false;
        _lastAppliedVisibleRows = -1;
    }

    public bool IsLayoutCaptured => _layoutCaptured;

    public void RefreshContainerGrid(InventoryGui gui)
    {
        var grid = ReflectionHelpers.GetContainerGrid(gui);
        if (grid == null) return;
        var gridInventory = grid.GetInventory();
        if (gridInventory != null)
            grid.UpdateInventory(gridInventory, Player.m_localPlayer, null);
    }

    public void SetNativeUiVisible(bool visible)
    {
        if (_nativeUiRoot != null)
            _nativeUiRoot.gameObject.SetActive(visible);
    }

    public void SetChestToggleVisible(bool visible)
    {
        if (_chestToggleRoot != null)
            _chestToggleRoot.gameObject.SetActive(visible);
    }

    public void SetTakeAllButtonEnabled(InventoryGui gui, bool enabled)
    {
        var takeAllButton = ReflectionHelpers.GetTakeAllButton(gui);
        if (takeAllButton != null)
            takeAllButton.interactable = enabled;
    }

    public void Reset()
    {
        ClearSearch();
        _boundChestForToggle = null;
        SetNativeUiVisible(false);
        SetChestToggleVisible(false);
        RestoreLayout();
    }
}
