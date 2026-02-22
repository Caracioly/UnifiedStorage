using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Network;

namespace UnifiedStorage.Mod.UI;

public sealed class UnifiedStorageWindow
{
    private readonly StorageConfig _config;
    private readonly RpcRoutes _rpc;
    private readonly ItemListView _listView;
    private readonly ManualLogSource _logger;
    private readonly Rect _windowRect = new(320, 90, 820, 640);
    private readonly Dictionary<string, Texture> _iconCache = new();

    private GUIStyle? _titleStyle;
    private GUIStyle? _slotButtonStyle;
    private GUIStyle? _slotAmountStyle;
    private GUIStyle? _metaStyle;
    private bool _isOpen;
    private string _search = string.Empty;
    private Vector2 _scrollPosition;
    private float _nextAllowedSearchUpdate;
    private float _nextSnapshotRefreshAt;
    private StorageSnapshot _snapshot = new();

    public UnifiedStorageWindow(StorageConfig config, RpcRoutes rpc, ItemListView listView, ManualLogSource logger)
    {
        _config = config;
        _rpc = rpc;
        _listView = listView;
        _logger = logger;
    }

    public void OpenFromChest()
    {
        _isOpen = true;
        _nextSnapshotRefreshAt = Time.unscaledTime;
        _rpc.RequestSnapshot();
    }

    public void Toggle()
    {
        if (_isOpen)
        {
            Close();
            return;
        }

        OpenFromChest();
    }

    public void Close()
    {
        _isOpen = false;
    }

    public void SetSnapshot(StorageSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public void Tick()
    {
        if (!_isOpen)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        if (Time.unscaledTime >= _nextSnapshotRefreshAt)
        {
            _nextSnapshotRefreshAt = Time.unscaledTime + (_config.SnapshotRefreshMs.Value / 1000f);
            _rpc.RequestSnapshot();
        }
    }

    public void OnGui()
    {
        if (!_isOpen)
        {
            return;
        }

        EnsureStyles();

        var previousColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.42f, 0.30f, 0.20f, 0.95f);
        GUILayout.BeginArea(_windowRect, GUI.skin.window);
        GUI.backgroundColor = previousColor;

        DrawTopBar();
        DrawStorageMeta();
        DrawSearchBar();
        DrawGrid();
        DrawFooter();

        GUILayout.EndArea();
    }

    private void DrawTopBar()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Take all", GUILayout.Width(160), GUILayout.Height(38)))
        {
            TakeAllFiltered();
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label("UNIFIED CHEST", _titleStyle ?? GUI.skin.label, GUILayout.Height(38));
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Place stacks", GUILayout.Width(160), GUILayout.Height(38)))
        {
            _rpc.RequestStore(new StoreRequest { Mode = StoreMode.PlaceStacks });
        }

        GUILayout.EndHorizontal();
    }

    private void DrawStorageMeta()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            $"Storage: {_snapshot.UsedSlots}/{_snapshot.TotalSlots} slots  |  Chests in range: {_snapshot.ChestCount}",
            _metaStyle ?? GUI.skin.label);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(100)))
        {
            Close();
        }

        GUILayout.EndHorizontal();
    }

    private void DrawSearchBar()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(55));
        var next = GUILayout.TextField(_search, GUILayout.Width(280));
        if (!string.Equals(next, _search, StringComparison.Ordinal))
        {
            var now = Time.unscaledTime;
            if (now >= _nextAllowedSearchUpdate)
            {
                _search = next;
                _nextAllowedSearchUpdate = now + (_config.SearchDebounceMs.Value / 1000f);
            }
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh", GUILayout.Width(100)))
        {
            _rpc.RequestSnapshot();
        }

        GUILayout.EndHorizontal();
    }

    private void DrawGrid()
    {
        var filtered = _listView.FilterAndSort(_snapshot.Items, _search);
        const int columns = 8;

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(470));

        for (var i = 0; i < filtered.Count; i += columns)
        {
            GUILayout.BeginHorizontal();
            for (var c = 0; c < columns; c++)
            {
                var index = i + c;
                if (index >= filtered.Count)
                {
                    GUILayout.Space(93);
                    continue;
                }

                DrawItemSlot(filtered[index]);
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }

    private void DrawItemSlot(AggregatedItem item)
    {
        GUILayout.BeginVertical("box", GUILayout.Width(88), GUILayout.Height(98));
        var icon = ResolveIcon(item);
        var shortLabel = item.DisplayName.Length > 2 ? item.DisplayName.Substring(0, 2) : item.DisplayName;
        var content = icon != null ? new GUIContent(icon, item.DisplayName) : new GUIContent(shortLabel, item.DisplayName);
        if (GUILayout.Button(content, _slotButtonStyle ?? GUI.skin.button, GUILayout.Width(76), GUILayout.Height(76)))
        {
            var amount = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? Math.Min(item.StackSize, item.TotalAmount)
                : 1;
            _rpc.RequestWithdraw(new WithdrawRequest
            {
                Key = item.Key,
                Amount = amount
            });
        }

        GUILayout.Label(item.TotalAmount.ToString(), _slotAmountStyle ?? GUI.skin.label, GUILayout.Width(76));
        GUILayout.EndVertical();
    }

    private void DrawFooter()
    {
        GUILayout.Label("Click item: take 1. Shift+click: take stack. Search and scroll to browse all nearby storage.");
    }

    private void TakeAllFiltered()
    {
        foreach (var item in _listView.FilterAndSort(_snapshot.Items, _search).ToList())
        {
            _rpc.RequestWithdraw(new WithdrawRequest
            {
                Key = item.Key,
                Amount = item.TotalAmount
            });
        }
    }

    public void OnWithdrawProcessed(WithdrawResult result)
    {
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Reason))
        {
            _logger.LogInfo($"Withdraw failed: {result.Reason}");
        }
    }

    public void OnStoreProcessed(StoreResult result)
    {
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Reason))
        {
            _logger.LogInfo($"Store failed: {result.Reason}");
        }
    }

    private Texture? ResolveIcon(AggregatedItem item)
    {
        var key = item.Key.PrefabName;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (_iconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var prefab = ObjectDB.instance?.GetItemPrefab(key);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        var icon = drop?.m_itemData?.GetIcon();
        if (icon != null)
        {
            var texture = icon.texture;
            if (texture != null)
            {
                _iconCache[key] = texture;
                return texture;
            }
        }

        return null;
    }

    private void EnsureStyles()
    {
        if (_titleStyle != null)
        {
            return;
        }

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _slotButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            imagePosition = ImagePosition.ImageOnly
        };
        _slotAmountStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        _metaStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
    }
}
