using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Models;
using UnifiedStorage.Mod.Network;
using UnifiedStorage.Mod.Pieces;
using UnityEngine;

namespace UnifiedStorage.Mod.Session;

public sealed class UnifiedTerminalSessionService
{
    private const float CloseGraceSeconds = 0.35f;
    private const float ReservationTtlSeconds = 3f;
    private const float TrackedChestRefreshSeconds = 1f;
    private static readonly FieldInfo? DragItemField = typeof(InventoryGui).GetField("m_dragItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly StorageConfig _config;
    private readonly IContainerScanner _scanner;
    private readonly TerminalRpcRoutes _routes;
    private readonly ManualLogSource _logger;

    private readonly Dictionary<ItemKey, int> _authoritativeTotals = new();
    private readonly Dictionary<ItemKey, int> _displayedTotals = new();
    private readonly Dictionary<ItemKey, ItemDrop.ItemData> _prototypes = new();
    private readonly Dictionary<string, int> _originalStackSizes = new();
    private readonly List<PendingReservation> _pendingReservations = new();
    private readonly List<PendingDeposit> _pendingDeposits = new();

    private Container? _terminal;
    private Player? _player;
    private List<ChestHandle> _trackedChests = new();
    private string _sessionId = string.Empty;
    private string _terminalUid = string.Empty;
    private string _searchQuery = string.Empty;
    private float _scanRadius;
    private float _pendingCloseSince = -1f;
    private float _nextTrackedChestRefreshAt;
    private float _nextSnapshotRetryAt;
    private bool _isApplyingProjection;
    private int _slotsTotalPhysical;
    private int _chestCount;
    private long _revision;
    private int _originalInventoryWidth;
    private int _originalInventoryHeight;
    private int _visibleRows;
    private int _contentRows;
    private int _uiRevision;

    public UnifiedTerminalSessionService(
        StorageConfig config,
        IContainerScanner scanner,
        TerminalRpcRoutes routes,
        ManualLogSource logger)
    {
        _config = config;
        _scanner = scanner;
        _routes = routes;
        _logger = logger;

        _routes.SessionSnapshotReceived += OnSessionSnapshotReceived;
        _routes.ReserveResultReceived += OnReserveResultReceived;
        _routes.ApplyResultReceived += OnApplyResultReceived;
        _routes.SessionDeltaReceived += OnSessionDeltaReceived;
    }

    public bool IsActive => _terminal != null && _player != null;
    public bool IsApplyingProjection => _isApplyingProjection;
    public int SlotsTotalPhysical => _slotsTotalPhysical;
    public int ChestsInRange => _chestCount;
    public int SlotsUsedVirtual => _authoritativeTotals.Where(kv => kv.Value > 0).Sum(kv => (int)Math.Ceiling(kv.Value / 999f));
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
            return false;
        }

        return _trackedChests.Any(chest => ReferenceEquals(inventory, chest.Container.GetInventory()));
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
        ForceReleaseContainerUse(terminal);

        _terminal = terminal;
        _player = player;
        _searchQuery = string.Empty;
        _sessionId = Guid.NewGuid().ToString("N");
        _terminalUid = BuildContainerUid(terminal);
        _pendingCloseSince = -1f;
        _scanRadius = _config.TerminalRangeOverride.Value > 0 ? _config.TerminalRangeOverride.Value : _config.ScanRadius.Value;
        CaptureOriginalInventorySize(terminal.GetInventory());
        _visibleRows = Math.Max(1, _originalInventoryHeight + 2);
        _revision = 0;
        _slotsTotalPhysical = 0;
        _chestCount = 0;
        _nextSnapshotRetryAt = Time.unscaledTime;
        _pendingReservations.Clear();
        _pendingDeposits.Clear();
        _authoritativeTotals.Clear();
        _displayedTotals.Clear();
        _prototypes.Clear();
        RefreshTrackedChestHandles();
        RefreshTerminalInventoryFromAuthoritative();
        RequestSessionSnapshot();
        _uiRevision++;
    }

    public void Tick()
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        if (InventoryGui.instance == null || !InventoryGui.IsVisible() || !InventoryGui.instance.IsContainerOpen())
        {
            if (_pendingCloseSince < 0f)
            {
                _pendingCloseSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _pendingCloseSince >= CloseGraceSeconds)
            {
                EndSession();
            }

            return;
        }

        _pendingCloseSince = -1f;
        if (Time.unscaledTime >= _nextTrackedChestRefreshAt)
        {
            RefreshTrackedChestHandles();
        }

        if (_revision == 0 && Time.unscaledTime >= _nextSnapshotRetryAt)
        {
            RequestSessionSnapshot();
            _nextSnapshotRetryAt = Time.unscaledTime + 0.5f;
        }

        ExpireLocalReservations();
        if (!IsDragInProgress())
        {
            CommitPendingReservations();
        }
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

        _searchQuery = normalized;
        RefreshTerminalInventoryFromAuthoritative();
        _uiRevision++;
    }

    public void NotifyContainerInteraction()
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        if (!IsSessionTerminal(_terminal))
        {
            EndSession();
            return;
        }

        var currentDisplayed = CaptureCurrentDisplayedTotals();
        ApplyDisplayedDeltaAsOperations(currentDisplayed);
        ReplaceDisplayedTotals(currentDisplayed);

        if (!IsDragInProgress())
        {
            CommitPendingReservations();
        }
    }

    public void RefreshFromWorldChange()
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        if (!IsSessionTerminal(_terminal))
        {
            EndSession();
            return;
        }

        RequestSessionSnapshot();
    }

    public void EndSession()
    {
        if (_terminal != null && !string.IsNullOrWhiteSpace(_terminalUid))
        {
            _routes.RequestCloseSession(new CloseSessionRequestDto
            {
                RequestId = Guid.NewGuid().ToString("N"),
                SessionId = _sessionId,
                TerminalUid = _terminalUid,
                PlayerId = ResolveLocalPlayerId()
            });
        }

        if (_terminal == null || _player == null)
        {
            RestoreStackSizes();
            return;
        }

        ForceReleaseContainerUse(_terminal);
        ClearInventory(_terminal.GetInventory());
        RestoreTerminalInventorySize(_terminal.GetInventory());
        RestoreStackSizes();

        _terminal = null;
        _player = null;
        _trackedChests.Clear();
        _sessionId = string.Empty;
        _terminalUid = string.Empty;
        _searchQuery = string.Empty;
        _scanRadius = 0f;
        _pendingCloseSince = -1f;
        _nextTrackedChestRefreshAt = 0f;
        _nextSnapshotRetryAt = 0f;
        _pendingReservations.Clear();
        _pendingDeposits.Clear();
        _authoritativeTotals.Clear();
        _displayedTotals.Clear();
        _prototypes.Clear();
        _slotsTotalPhysical = 0;
        _chestCount = 0;
        _revision = 0;
        _visibleRows = 0;
        _contentRows = 0;
        _uiRevision++;
    }

    private void OnSessionSnapshotReceived(OpenSessionResponseDto response)
    {
        if (!IsActive)
        {
            return;
        }

        if (!response.Success)
        {
            if (!string.IsNullOrWhiteSpace(response.Reason))
            {
                _logger.LogDebug($"OpenSession rejected: {response.Reason}");
            }

            return;
        }

        if (!CanApplySnapshot(response.Snapshot))
        {
            return;
        }

        ApplySnapshot(response.Snapshot);
    }

    private void OnReserveResultReceived(ReserveWithdrawResultDto result)
    {
        if (!IsActive || !CanApplySnapshot(result.Snapshot))
        {
            return;
        }

        if (result.Success && !string.IsNullOrWhiteSpace(result.TokenId) && result.ReservedAmount > 0)
        {
            _pendingReservations.Add(new PendingReservation
            {
                TokenId = result.TokenId,
                Key = result.Key,
                ReservedAmount = result.ReservedAmount,
                ExpiresAt = Time.unscaledTime + ReservationTtlSeconds
            });
        }

        ApplySnapshot(result.Snapshot);
    }

    private void OnApplyResultReceived(ApplyResultDto result)
    {
        if (!IsActive || !CanApplySnapshot(result.Snapshot))
        {
            return;
        }

        if (string.Equals(result.OperationType, "commit", StringComparison.Ordinal))
        {
            if (result.Success)
            {
                RemovePendingToken(result.TokenId);
            }
            else
            {
                MarkPendingCommitAsRetryable(result.TokenId);
                RequestSessionSnapshot();
            }
        }
        else if (string.Equals(result.OperationType, "cancel", StringComparison.Ordinal) && !result.Success)
        {
            RemovePendingToken(result.TokenId);
            RequestSessionSnapshot();
        }
        else if (string.Equals(result.OperationType, "deposit", StringComparison.Ordinal))
        {
            HandleDepositApplyResult(result);
        }

        ApplySnapshot(result.Snapshot);
    }

    private void OnSessionDeltaReceived(SessionDeltaDto delta)
    {
        if (!IsActive)
        {
            return;
        }

        if (!string.Equals(delta.TerminalUid, _terminalUid, StringComparison.Ordinal))
        {
            return;
        }

        if (delta.Revision < _revision)
        {
            return;
        }

        ApplySnapshot(delta.Snapshot);
    }

    private bool CanApplySnapshot(SessionSnapshotDto snapshot)
    {
        if (!IsActive || _terminal == null)
        {
            return false;
        }

        if (!string.Equals(snapshot.TerminalUid, _terminalUid, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private void ApplySnapshot(SessionSnapshotDto snapshot)
    {
        if (!CanApplySnapshot(snapshot))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SessionId))
        {
            _sessionId = snapshot.SessionId;
        }

        _revision = snapshot.Revision;
        _slotsTotalPhysical = snapshot.SlotsTotalPhysical;
        _chestCount = snapshot.ChestCount;
        _nextSnapshotRetryAt = Time.unscaledTime + 60f;

        _authoritativeTotals.Clear();
        _prototypes.Clear();
        foreach (var item in snapshot.Items)
        {
            if (item.TotalAmount <= 0)
            {
                continue;
            }

            _authoritativeTotals[item.Key] = item.TotalAmount;
            if (!_prototypes.ContainsKey(item.Key))
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(item.Key.PrefabName);
                var drop = prefab?.GetComponent<ItemDrop>();
                if (drop?.m_itemData != null)
                {
                    var proto = drop.m_itemData.Clone();
                    proto.m_quality = item.Key.Quality;
                    proto.m_variant = item.Key.Variant;
                    _prototypes[item.Key] = proto;
                }
            }
        }

        RefreshTrackedChestHandles();
        RefreshTerminalInventoryFromAuthoritative();
        _uiRevision++;
    }

    private void ApplyDisplayedDeltaAsOperations(Dictionary<ItemKey, int> currentDisplayed)
    {
        var keys = new HashSet<ItemKey>(_displayedTotals.Keys);
        keys.UnionWith(currentDisplayed.Keys);
        foreach (var key in keys)
        {
            var previous = _displayedTotals.TryGetValue(key, out var prev) ? prev : 0;
            var current = currentDisplayed.TryGetValue(key, out var now) ? now : 0;
            var delta = current - previous;
            if (delta < 0)
            {
                RequestReserveWithdraw(key, -delta);
            }
            else if (delta > 0)
            {
                RequestCancelAndOrDeposit(key, delta);
            }
        }
    }

    private void RequestReserveWithdraw(ItemKey key, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        _routes.RequestReserveWithdraw(new ReserveWithdrawRequestDto
        {
            RequestId = Guid.NewGuid().ToString("N"),
            SessionId = _sessionId,
            OperationId = Guid.NewGuid().ToString("N"),
            TerminalUid = _terminalUid,
            PlayerId = ResolveLocalPlayerId(),
            ExpectedRevision = _revision,
            Key = key,
            Amount = amount
        });
    }

    private void RequestCancelAndOrDeposit(ItemKey key, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var remaining = amount;
        foreach (var pending in _pendingReservations.Where(r => r.Key.Equals(key) && r.ReservedAmount > 0).ToList())
        {
            if (remaining <= 0)
            {
                break;
            }

            var cancelAmount = Math.Min(remaining, pending.ReservedAmount);
            if (cancelAmount <= 0)
            {
                continue;
            }

            _routes.RequestCancelReservation(new CancelReservationRequestDto
            {
                RequestId = Guid.NewGuid().ToString("N"),
                SessionId = _sessionId,
                OperationId = Guid.NewGuid().ToString("N"),
                TerminalUid = _terminalUid,
                PlayerId = ResolveLocalPlayerId(),
                TokenId = pending.TokenId,
                Amount = cancelAmount
            });

            pending.ReservedAmount -= cancelAmount;
            remaining -= cancelAmount;
            if (pending.ReservedAmount <= 0)
            {
                _pendingReservations.Remove(pending);
            }
        }

        if (remaining <= 0)
        {
            return;
        }

        var requestId = Guid.NewGuid().ToString("N");
        _pendingDeposits.Add(new PendingDeposit
        {
            RequestId = requestId,
            Key = key,
            Amount = remaining
        });

        _routes.RequestDeposit(new DepositRequestDto
        {
            RequestId = requestId,
            SessionId = _sessionId,
            OperationId = Guid.NewGuid().ToString("N"),
            TerminalUid = _terminalUid,
            PlayerId = ResolveLocalPlayerId(),
            ExpectedRevision = _revision,
            Key = key,
            Amount = remaining
        });
    }

    private void CommitPendingReservations()
    {
        foreach (var pending in _pendingReservations.Where(r => !r.CommitRequested && r.ReservedAmount > 0).ToList())
        {
            _routes.RequestCommitReservation(new CommitReservationRequestDto
            {
                RequestId = Guid.NewGuid().ToString("N"),
                SessionId = _sessionId,
                OperationId = Guid.NewGuid().ToString("N"),
                TerminalUid = _terminalUid,
                TokenId = pending.TokenId
            });
            pending.CommitRequested = true;
        }
    }

    private void ExpireLocalReservations()
    {
        var now = Time.unscaledTime;
        var removed = false;
        for (var i = _pendingReservations.Count - 1; i >= 0; i--)
        {
            if (_pendingReservations[i].ExpiresAt > now)
            {
                continue;
            }

            _pendingReservations.RemoveAt(i);
            removed = true;
        }

        if (removed)
        {
            RequestSessionSnapshot();
        }
    }

    private void RequestSessionSnapshot()
    {
        if (!IsActive || _terminal == null)
        {
            return;
        }

        _routes.RequestOpenSession(new OpenSessionRequestDto
        {
            RequestId = Guid.NewGuid().ToString("N"),
            SessionId = _sessionId,
            TerminalUid = _terminalUid,
            PlayerId = ResolveLocalPlayerId(),
            AnchorX = _terminal.transform.position.x,
            AnchorY = _terminal.transform.position.y,
            AnchorZ = _terminal.transform.position.z,
            Radius = _scanRadius
        });
        _nextSnapshotRetryAt = Time.unscaledTime + 0.5f;
    }

    private Dictionary<ItemKey, int> CaptureCurrentDisplayedTotals()
    {
        var displayed = new Dictionary<ItemKey, int>();
        if (_terminal == null)
        {
            return displayed;
        }

        var inventory = _terminal.GetInventory();
        if (inventory == null)
        {
            return displayed;
        }

        foreach (var item in inventory.GetAllItems())
        {
            if (item?.m_dropPrefab == null || item.m_stack <= 0)
            {
                continue;
            }

            var key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
            displayed[key] = displayed.TryGetValue(key, out var existing) ? existing + item.m_stack : item.m_stack;
        }

        return displayed;
    }

    private void ReplaceDisplayedTotals(Dictionary<ItemKey, int> totals)
    {
        _displayedTotals.Clear();
        foreach (var kv in totals)
        {
            _displayedTotals[kv.Key] = kv.Value;
        }
    }

    private void RefreshTerminalInventoryFromAuthoritative()
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

        _isApplyingProjection = true;
        try
        {
            ClearInventory(inventory);
            _displayedTotals.Clear();

            var width = Math.Max(1, GetInventoryWidth(inventory));
            var filtered = _authoritativeTotals
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
        finally
        {
            _isApplyingProjection = false;
        }
    }

    private bool MatchesSearch(string displayName)
    {
        return string.IsNullOrWhiteSpace(_searchQuery)
               || displayName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void RefreshTrackedChestHandles()
    {
        if (_terminal == null)
        {
            _trackedChests.Clear();
            _nextTrackedChestRefreshAt = Time.unscaledTime + TrackedChestRefreshSeconds;
            return;
        }

        _trackedChests = _scanner
            .GetNearbyContainers(_terminal.transform.position, _scanRadius, _terminal, onlyVanillaChests: true)
            .ToList();
        _nextTrackedChestRefreshAt = Time.unscaledTime + TrackedChestRefreshSeconds;
    }

    private static string BuildContainerUid(Container container)
    {
        var znetView = container.GetComponent<ZNetView>();
        var zdo = znetView?.GetZDO();
        if (zdo != null)
        {
            return zdo.m_uid.ToString();
        }

        return container.GetInstanceID().ToString();
    }

    private bool IsSessionTerminal(Container container)
    {
        if (container == null || string.IsNullOrWhiteSpace(_terminalUid))
        {
            return false;
        }

        return string.Equals(BuildContainerUid(container), _terminalUid, StringComparison.Ordinal);
    }

    private static void ForceReleaseContainerUse(Container container)
    {
        if (container == null)
        {
            return;
        }

        try
        {
            var setInUse = typeof(Container).GetMethod("SetInUse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            setInUse?.Invoke(container, new object[] { false });
        }
        catch
        {
            // Best effort only.
        }

        try
        {
            var inUseField = typeof(Container).GetField("m_inUse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (inUseField?.FieldType == typeof(bool))
            {
                inUseField.SetValue(container, false);
            }
        }
        catch
        {
            // Best effort only.
        }

        try
        {
            var closeMethod = typeof(Container).GetMethod("Close", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            closeMethod?.Invoke(container, null);
        }
        catch
        {
            // Best effort only.
        }
    }

    private bool IsDragInProgress()
    {
        if (InventoryGui.instance == null || DragItemField == null)
        {
            return false;
        }

        return DragItemField.GetValue(InventoryGui.instance) is ItemDrop.ItemData dragItem && dragItem != null && dragItem.m_stack > 0;
    }

    private void RemovePendingToken(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return;
        }

        _pendingReservations.RemoveAll(r => string.Equals(r.TokenId, tokenId, StringComparison.Ordinal));
    }

    private void HandleDepositApplyResult(ApplyResultDto result)
    {
        var pending = _pendingDeposits.FirstOrDefault(p => string.Equals(p.RequestId, result.RequestId, StringComparison.Ordinal));
        if (pending == null)
        {
            return;
        }

        _pendingDeposits.Remove(pending);

        var toRestore = 0;
        if (!result.Success && ShouldRestoreFailedDeposit(result.Reason))
        {
            toRestore = pending.Amount;
        }

        if (toRestore <= 0)
        {
            return;
        }

        if (IsLocalServer())
        {
            return;
        }

        RestoreToLocalPlayerInventory(pending.Key, toRestore);
    }

    private static bool ShouldRestoreFailedDeposit(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return string.Equals(reason, "Conflict", StringComparison.Ordinal)
               || string.Equals(reason, "Player not found", StringComparison.Ordinal)
               || string.Equals(reason, "Player has no matching item", StringComparison.Ordinal)
               || string.Equals(reason, "Session not found", StringComparison.Ordinal);
    }

    private void MarkPendingCommitAsRetryable(string tokenId)
    {
        foreach (var pending in _pendingReservations.Where(r => string.Equals(r.TokenId, tokenId, StringComparison.Ordinal)))
        {
            pending.CommitRequested = false;
        }
    }

    private static bool IsLocalServer()
    {
        if (ZNet.instance == null)
        {
            return true;
        }

        var isServerMethod = typeof(ZNet).GetMethod("IsServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (isServerMethod != null && isServerMethod.Invoke(ZNet.instance, null) is bool isServer)
        {
            return isServer;
        }

        var isServerField = typeof(ZNet).GetField("m_isServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return isServerField?.GetValue(ZNet.instance) as bool? ?? true;
    }

    private void RestoreToLocalPlayerInventory(ItemKey key, int amount)
    {
        if (amount <= 0 || _player == null)
        {
            return;
        }

        var remaining = amount;
        var inventory = _player.GetInventory();
        while (remaining > 0)
        {
            var stackSize = Math.Min(999, remaining);
            var stack = CreateItemStack(key, stackSize);
            if (stack == null)
            {
                break;
            }

            if (inventory.AddItem(stack))
            {
                remaining -= stackSize;
                continue;
            }

            var moved = false;
            for (var split = stackSize / 2; split >= 1; split /= 2)
            {
                var partial = CreateItemStack(key, split);
                if (partial == null || !inventory.AddItem(partial))
                {
                    continue;
                }

                remaining -= split;
                moved = true;
                break;
            }

            if (!moved)
            {
                break;
            }
        }

        if (remaining > 0)
        {
            DropNearPlayer(key, remaining);
        }
    }

    private void DropNearPlayer(ItemKey key, int amount)
    {
        if (_player == null || amount <= 0)
        {
            return;
        }

        var remaining = amount;
        while (remaining > 0)
        {
            var stackAmount = Math.Min(999, remaining);
            var stack = CreateItemStack(key, stackAmount);
            if (stack == null)
            {
                break;
            }

            var pos = _player.transform.position + _player.transform.forward + Vector3.up;
            if (!TryDropItem(stack, stackAmount, pos, Quaternion.identity))
            {
                break;
            }

            remaining -= stackAmount;
        }
    }

    private static bool TryDropItem(ItemDrop.ItemData item, int amount, Vector3 position, Quaternion rotation)
    {
        var methods = typeof(ItemDrop).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, "DropItem", StringComparison.Ordinal))
            .ToList();

        foreach (var method in methods)
        {
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
            catch
            {
                // Try next signature.
            }
        }

        return false;
    }

    private long ResolveLocalPlayerId()
    {
        return _player != null ? _player.GetPlayerID() : 0L;
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

    private sealed class PendingReservation
    {
        public string TokenId { get; set; } = string.Empty;
        public ItemKey Key { get; set; }
        public int ReservedAmount { get; set; }
        public float ExpiresAt { get; set; }
        public bool CommitRequested { get; set; }
    }

    private sealed class PendingDeposit
    {
        public string RequestId { get; set; } = string.Empty;
        public ItemKey Key { get; set; }
        public int Amount { get; set; }
    }
}
