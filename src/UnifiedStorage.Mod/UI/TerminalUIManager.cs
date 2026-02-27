using System;
using Jotunn.Managers;
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
    private const float FooterPanelBottomOffset = -52f;
    private const float FooterPanelHeight = 52f;
    private const float GridTopReserve = 124f;
    private const float ChestToggleTopOffset = -8f;
    private const float SearchWoodBarHeight = 40f;
    private const float SlotsPlaqueHorizontalOffset = -7f;
    private const float SlotsPlaqueVerticalOffset = 90f;
    private const float SlotsIconXOffset = 8f;
    private const float SlotsIconYOffset = 30f;
    private const float SlotsIconSize = -10f;
    private const float SlotsTextXOffsetWhenIcon = 7f;

    private RectTransform? _nativeUiRoot;
    private TMP_Text? _metaText;
    private RectTransform? _slotsPlaqueBkg;
    private RectTransform? _slotsPlaqueIconRoot;
    private RectTransform? _slotsPlaqueTextRoot;
    private TMP_InputField? _searchInputField;
    private Sprite? _woodPlaqueSprite;
    private Material? _woodPlaqueMaterial;
    private bool _woodPlaqueUseSliced;
    private Sprite? _woodPlaqueFrameSprite;
    private Material? _woodPlaqueFrameMaterial;
    private bool _woodPlaqueFrameUseSliced;
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
    private bool _slotsPlaqueBuilt;
    private bool _isApplyingChestToggle;
    private int _lastAppliedVisibleRows;
    private float _slotsBaseFontSize;

    public bool IsSearchFocused => _searchInputField != null && _searchInputField.isFocused;
    public string SearchText => _searchInputField?.text ?? string.Empty;

    public event Action<string>? SearchValueChanged;
    public event Action<bool>? ChestToggleChanged;

    public void EnsureNativeUi(InventoryGui gui)
    {
        if (_nativeBuilt)
        {
            if (!_slotsPlaqueBuilt && _containerRect != null)
                EnsureSlotsPlaqueUi(_containerRect);
            return;
        }

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
        _nativeUiRoot.SetAsFirstSibling();

        CacheWoodPlaqueStyle(container);
        BuildSearchField(root.transform, containerName);

        EnsureChestToggleUi(container, containerName);
        EnsureSlotsPlaqueUi(container);

        _nativeBuilt = true;
        SetNativeUiVisible(false);
        SetChestToggleVisible(false);
    }

    private void EnsureSlotsPlaqueUi(RectTransform container)
    {
        if (_slotsPlaqueBuilt) return;

        var weightRoot = container.Find("Weight") as RectTransform;
        if (weightRoot == null) return;
        var templateBkg = weightRoot.Find("bkg") as RectTransform;
        var templateText = weightRoot.Find("weight_text") as RectTransform;
        if (templateBkg == null || templateText == null) return;

        var clonedBkg = UnityEngine.Object.Instantiate(templateBkg.gameObject, weightRoot, false);
        clonedBkg.name = "US_Slots_bkg";
        _slotsPlaqueBkg = clonedBkg.GetComponent<RectTransform>();
        _slotsPlaqueBkg.anchoredPosition = templateBkg.anchoredPosition + new Vector2(SlotsPlaqueHorizontalOffset, SlotsPlaqueVerticalOffset);
        _slotsPlaqueBkg.SetAsLastSibling();

        var slotsIconSprite = ResolveSlotsIconSprite();
        if (slotsIconSprite != null)
        {
            _slotsPlaqueIconRoot = CreateSlotsIcon(weightRoot, templateBkg, slotsIconSprite);
        }
        else
        {
            var templateIcon = FindSlotsIconTemplate(weightRoot, templateBkg, templateText);
            if (templateIcon != null)
            {
                var clonedIcon = UnityEngine.Object.Instantiate(templateIcon.gameObject, weightRoot, false);
                clonedIcon.name = "US_Slots_icon";
                _slotsPlaqueIconRoot = clonedIcon.GetComponent<RectTransform>();
                if (_slotsPlaqueIconRoot != null)
                    ApplySlotsIconLayout(_slotsPlaqueIconRoot, templateBkg);
            }
            else
            {
                _slotsPlaqueIconRoot = CreateFallbackSlotsIcon(weightRoot, templateBkg);
            }
        }

        var clonedText = UnityEngine.Object.Instantiate(templateText.gameObject, weightRoot, false);
        clonedText.name = "US_Slots_text";
        _slotsPlaqueTextRoot = clonedText.GetComponent<RectTransform>();
        var textXOffset = _slotsPlaqueIconRoot != null ? SlotsTextXOffsetWhenIcon : 0f;
        _slotsPlaqueTextRoot.anchoredPosition = templateText.anchoredPosition + new Vector2(SlotsPlaqueHorizontalOffset + textXOffset, SlotsPlaqueVerticalOffset);
        _slotsPlaqueTextRoot.SetAsLastSibling();
        _metaText = clonedText.GetComponent<TMP_Text>();
        if (_metaText != null)
        {
            _slotsBaseFontSize = _metaText.fontSize;
            _metaText.enableAutoSizing = false;
        }

        _slotsPlaqueBuilt = _slotsPlaqueBkg != null && _slotsPlaqueTextRoot != null && _metaText != null;
        SetSlotsPlaqueVisible(false);
    }

    private void BuildSearchField(Transform parent, TMP_Text? containerName)
    {
        var searchHeight = SearchWoodBarHeight;
        var topOffset = 0f;

        var searchRow = new GameObject("US_SearchRow", typeof(RectTransform));
        searchRow.transform.SetParent(parent, false);
        var searchRowRect = searchRow.GetComponent<RectTransform>();
        searchRowRect.anchorMin = new Vector2(0f, 1f);
        searchRowRect.anchorMax = new Vector2(1f, 1f);
        searchRowRect.pivot = new Vector2(0.5f, 1f);
        searchRowRect.anchoredPosition = new Vector2(0f, topOffset);
        searchRowRect.sizeDelta = new Vector2(-2f, searchHeight);

        var woodBar = new GameObject("US_SearchWoodBar", typeof(RectTransform), typeof(Image));
        woodBar.transform.SetParent(searchRow.transform, false);
        var woodBarRect = woodBar.GetComponent<RectTransform>();
        woodBarRect.anchorMin = Vector2.zero;
        woodBarRect.anchorMax = Vector2.one;
        woodBarRect.offsetMin = new Vector2(2f, 2f);
        woodBarRect.offsetMax = new Vector2(-2f, -2f);
        var woodBarImage = woodBar.GetComponent<Image>();
        if (_woodPlaqueSprite != null)
        {
            woodBarImage.sprite = _woodPlaqueSprite;
            woodBarImage.material = _woodPlaqueMaterial;
            woodBarImage.type = _woodPlaqueUseSliced ? Image.Type.Sliced : Image.Type.Simple;
            woodBarImage.color = new Color(1f, 1f, 1f, 0.98f);
        }
        else
        {
            woodBarImage.color = new Color(0.22f, 0.14f, 0.08f, 0.95f);
        }

        var woodFrame = new GameObject("US_SearchWoodFrame", typeof(RectTransform), typeof(Image));
        woodFrame.transform.SetParent(searchRow.transform, false);
        var woodFrameRect = woodFrame.GetComponent<RectTransform>();
        woodFrameRect.anchorMin = Vector2.zero;
        woodFrameRect.anchorMax = Vector2.one;
        woodFrameRect.offsetMin = Vector2.zero;
        woodFrameRect.offsetMax = Vector2.zero;
        var woodFrameImage = woodFrame.GetComponent<Image>();
        if (_woodPlaqueFrameSprite != null)
        {
            woodFrameImage.sprite = _woodPlaqueFrameSprite;
            woodFrameImage.material = _woodPlaqueFrameMaterial;
            woodFrameImage.type = _woodPlaqueFrameUseSliced ? Image.Type.Sliced : Image.Type.Sliced;
            woodFrameImage.color = new Color(1f, 1f, 1f, 0.98f);
        }
        else
        {
            woodFrameImage.color = new Color(0.30f, 0.20f, 0.12f, 0.70f);
        }

        var inputRoot = new GameObject("US_SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputRoot.transform.SetParent(searchRow.transform, false);
        var inputRect = inputRoot.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 0f);
        inputRect.anchorMax = new Vector2(1f, 1f);
        inputRect.offsetMin = new Vector2(12f, 6f);
        inputRect.offsetMax = new Vector2(-12f, -6f);
        var inputBg = inputRoot.GetComponent<Image>();
        inputBg.color = new Color(0f, 0f, 0f, 0f);
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
        inputText.fontSize = 15f;
        inputText.color = new Color(0.95f, 0.9f, 0.74f, 0.95f);
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
        placeholder.fontSize = 14f;
        placeholder.color = new Color(0.9f, 0.84f, 0.64f, 0.55f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.fontStyle = FontStyles.Italic;

        _searchInputField.textViewport = viewportRect;
        _searchInputField.textComponent = inputText;
        _searchInputField.placeholder = placeholder;
        _searchInputField.lineType = TMP_InputField.LineType.SingleLine;
        _searchInputField.onValueChanged.AddListener(value => SearchValueChanged?.Invoke(value ?? string.Empty));
    }

    private void CacheWoodPlaqueStyle(RectTransform container)
    {
        var weightRoot = container.Find("Weight") as RectTransform;
        var weightBkg = weightRoot?.Find("bkg");
        var weightImage = weightBkg != null ? weightBkg.GetComponent<Image>() : null;
        if (weightImage != null && weightImage.sprite != null)
        {
            _woodPlaqueSprite = weightImage.sprite;
            _woodPlaqueMaterial = weightImage.material;
            _woodPlaqueUseSliced = weightImage.type == Image.Type.Sliced;
        }

        var containerImage = container.GetComponent<Image>();
        if (containerImage != null && containerImage.sprite != null)
        {
            _woodPlaqueFrameSprite = containerImage.sprite;
            _woodPlaqueFrameMaterial = containerImage.material;
            _woodPlaqueFrameUseSliced = containerImage.type == Image.Type.Sliced;
            return;
        }

        if (weightBkg == null) return;
        weightImage = weightBkg.GetComponent<Image>();
        if (weightImage == null || weightImage.sprite == null) return;

        _woodPlaqueFrameSprite = weightImage.sprite;
        _woodPlaqueFrameMaterial = weightImage.material;
        _woodPlaqueFrameUseSliced = weightImage.type == Image.Type.Sliced;
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
        if (_metaText == null) return;

        var used = Mathf.Max(0, slotsUsed);
        var total = Mathf.Max(0, slotsTotal);
        _metaText.text = $"{used}/{total}";
        ApplySlotsFontSizing(used, total);
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
        SetSlotsPlaqueVisible(visible);
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

    private void SetSlotsPlaqueVisible(bool visible)
    {
        if (_slotsPlaqueBkg != null)
            _slotsPlaqueBkg.gameObject.SetActive(visible);
        if (_slotsPlaqueIconRoot != null)
            _slotsPlaqueIconRoot.gameObject.SetActive(visible);
        if (_slotsPlaqueTextRoot != null)
            _slotsPlaqueTextRoot.gameObject.SetActive(visible);
    }

    private static RectTransform? FindSlotsIconTemplate(RectTransform weightRoot, RectTransform templateBkg, RectTransform templateText)
    {
        foreach (var name in new[] { "slot_icon", "slots_icon", "inventory_icon", "bag_icon" })
        {
            var direct = weightRoot.Find(name) as RectTransform;
            if (IsLikelySmallIcon(direct, templateBkg, templateText)) return direct;
        }

        for (var i = 0; i < weightRoot.childCount; i++)
        {
            if (weightRoot.GetChild(i) is not RectTransform child) continue;
            if (ReferenceEquals(child, templateBkg) || ReferenceEquals(child, templateText)) continue;
            if (child.GetComponent<Image>() == null) continue;
            var lower = child.name.ToLowerInvariant();
            if (lower.Contains("slot") || lower.Contains("inventory") || lower.Contains("bag"))
            {
                if (!IsLikelySmallIcon(child, templateBkg, templateText)) continue;
                return child;
            }
        }

        return null;
    }

    private static bool IsLikelySmallIcon(RectTransform? candidate, RectTransform templateBkg, RectTransform templateText)
    {
        if (candidate == null) return false;
        if (ReferenceEquals(candidate, templateBkg) || ReferenceEquals(candidate, templateText)) return false;
        if (candidate.GetComponent<Image>() == null) return false;

        var size = candidate.sizeDelta;
        if (size.x <= 0f || size.y <= 0f) return false;

        return size.x <= 40f && size.y <= 40f;
    }

    private static Sprite? ResolveSlotsIconSprite()
    {
        foreach (var prefabName in new[] { "piece_chest_wood", "piece_chest_private", "piece_chest_blackmetal", "piece_chest" })
        {
            var prefab = PrefabManager.Instance.GetPrefab(prefabName);
            var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece?.m_icon != null)
                return piece.m_icon;
        }

        return null;
    }

    private static RectTransform CreateSlotsIcon(RectTransform weightRoot, RectTransform templateBkg, Sprite iconSprite)
    {
        var iconGo = new GameObject("US_Slots_icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(weightRoot, false);

        var iconRect = iconGo.GetComponent<RectTransform>();
        ApplySlotsIconLayout(iconRect, templateBkg);

        var image = iconGo.GetComponent<Image>();
        image.sprite = iconSprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = new Color(0.96f, 0.88f, 0.62f, 1f);

        return iconRect;
    }

    private static RectTransform CreateFallbackSlotsIcon(RectTransform weightRoot, RectTransform templateBkg)
    {
        var rootGo = new GameObject("US_Slots_icon", typeof(RectTransform));
        rootGo.transform.SetParent(weightRoot, false);

        var rootRect = rootGo.GetComponent<RectTransform>();
        ApplySlotsIconLayout(rootRect, templateBkg);

        const float cell = 5f;
        const float gap = 1f;
        var startX = -(cell + gap) * 0.5f;
        var startY = (cell + gap) * 0.5f;
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                var cellGo = new GameObject($"Cell_{x}_{y}", typeof(RectTransform), typeof(Image));
                cellGo.transform.SetParent(rootGo.transform, false);
                var cellRect = cellGo.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(0.5f, 0.5f);
                cellRect.anchorMax = new Vector2(0.5f, 0.5f);
                cellRect.pivot = new Vector2(0.5f, 0.5f);
                cellRect.sizeDelta = new Vector2(cell, cell);
                cellRect.anchoredPosition = new Vector2(startX + x * (cell + gap), startY - y * (cell + gap));
                var image = cellGo.GetComponent<Image>();
                image.color = new Color(0.96f, 0.88f, 0.62f, 0.98f);
                image.raycastTarget = false;
            }
        }

        return rootRect;
    }

    private static void ApplySlotsIconLayout(RectTransform iconRect, RectTransform templateBkg)
    {
        iconRect.anchorMin = templateBkg.anchorMin;
        iconRect.anchorMax = templateBkg.anchorMax;
        iconRect.pivot = templateBkg.pivot;
        iconRect.anchoredPosition = templateBkg.anchoredPosition + new Vector2(SlotsPlaqueHorizontalOffset + SlotsIconXOffset, SlotsPlaqueVerticalOffset + SlotsIconYOffset);
        iconRect.sizeDelta = new Vector2(SlotsIconSize, SlotsIconSize);
        iconRect.SetAsLastSibling();
    }

    private void ApplySlotsFontSizing(int used, int total)
    {
        if (_metaText == null) return;

        var digitCount = CountDigits(used) + CountDigits(total);
        var baseSize = _slotsBaseFontSize > 0f ? _slotsBaseFontSize : _metaText.fontSize;

        if (digitCount <= 6)
        {
            _metaText.fontSize = baseSize;
            return;
        }

        if (digitCount == 7)
        {
            _metaText.fontSize = baseSize - 1f;
            return;
        }

        if (digitCount == 8)
        {
            _metaText.fontSize = baseSize - 2f;
            return;
        }

        // 9+ digits (10k+ slots range): keep a readable lower bound and allow overflow if needed.
        _metaText.fontSize = Mathf.Max(10f, baseSize - 2f);
    }

    private static int CountDigits(int value)
    {
        if (value < 10) return 1;
        if (value < 100) return 2;
        if (value < 1000) return 3;
        if (value < 10000) return 4;
        if (value < 100000) return 5;
        if (value < 1000000) return 6;
        return value.ToString().Length;
    }
}
