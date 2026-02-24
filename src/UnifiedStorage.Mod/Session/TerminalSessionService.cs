using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Diagnostics;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Models;
using UnifiedStorage.Mod.Network;
using UnifiedStorage.Mod.Pieces;
using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Session;

public sealed class TerminalSessionService
{
    private const float CloseGraceSeconds = 0.35f;
    private const float ReservationTtlSeconds = 3f;
    private const float TrackedChestRefreshSeconds = 1f;
    private const float OpenSessionRetryBaseSeconds = 0.5f;
    private const float OpenSessionRetryMaxSeconds = 8f;

    private readonly StorageConfig _config;
    private readonly IContainerScanner _scanner;
    private readonly TerminalRpcRoutes _routes;
    private readonly ManualLogSource _logger;
    private readonly StorageTrace _trace;

    private readonly Dictionary<ItemKey, int> _authoritativeTotals = new();
    private readonly Dictionary<ItemKey, int> _displayedTotals = new();
    private readonly Dictionary<ItemKey, ItemDrop.ItemData> _prototypes = new();
    private readonly Dictionary<ItemKey, int> _stackSizes = new();
    private readonly List<PendingReservation> _pendingReservations = new();
    private readonly List<PendingDeposit> _pendingDeposits = new();
    private readonly Dictionary<ItemKey, CachedSortEntry> _sortCache = new();
    private List<KeyValuePair<ItemKey, int>> _sortedItems = new();
    private long _lastProjectedContentHash;

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
    private bool _hasAuthoritativeSnapshot;
    private int _slotsTotalPhysical;
    private int _chestCount;
    private long _revision;
    private int _openSessionFailureCount;
    private int _originalInventoryWidth;
    private int _originalInventoryHeight;
    private int _contentRows;
    private int _uiRevision;

    public TerminalSessionService(
        StorageConfig config,
        IContainerScanner scanner,
        TerminalRpcRoutes routes,
        ManualLogSource logger,
        StorageTrace trace)
    {
        _config = config;
        _scanner = scanner;
        _routes = routes;
        _logger = logger;
        _trace = trace;

        _routes.SessionSnapshotReceived += OnSessionSnapshotReceived;
        _routes.ReserveResultReceived += OnReserveResultReceived;
        _routes.ApplyResultReceived += OnApplyResultReceived;
        _routes.SessionDeltaReceived += OnSessionDeltaReceived;
    }

    public bool IsActive => _terminal != null && _player != null;
    public bool IsApplyingProjection => _isApplyingProjection;
    public int SlotsTotalPhysical => _slotsTotalPhysical;
    public int ChestsInRange => _chestCount;
    public int UiRevision => _uiRevision;
    public int ContentRows => _contentRows;
    public bool IsStorageFull => _slotsTotalPhysical > 0 && SlotsUsedVirtual >= _slotsTotalPhysical;

    public bool IsTerminalInventory(Inventory inventory)
    {
        if (!IsActive || _terminal == null || inventory == null) return false;
        return ReferenceEquals(inventory, _terminal.GetInventory());
    }

    public int SlotsUsedVirtual
    {
        get
        {
            var total = 0;
            foreach (var kv in _authoritativeTotals)
            {
                if (kv.Value <= 0) continue;
                var maxStack = _stackSizes.TryGetValue(kv.Key, out var s) && s > 0 ? s : 1;
                total += (int)Math.Ceiling(kv.Value / (double)maxStack);
            }
            return total;
        }
    }

    public bool IsTrackedInventory(Inventory inventory)
    {
        if (!IsActive || inventory == null || _terminal == null) return false;
        var terminalInventory = _terminal.GetInventory();
        if (ReferenceEquals(inventory, terminalInventory)) return false;
        return _trackedChests.Any(chest => ReferenceEquals(inventory, chest.Container.GetInventory()));
    }

    public bool HandleContainerInteract(Container container, Player player)
    {
        if (!UnifiedTerminal.IsTerminal(container)) return false;
        BeginSession(container, player);
        return true;
    }

    public void BeginSession(Container terminal, Player player)
    {
        EndSession();
        ReflectionHelpers.ForceReleaseContainerUse(terminal);

        _terminal = terminal;
        _player = player;
        _searchQuery = string.Empty;
        _sessionId = string.Empty;
        _terminalUid = ReflectionHelpers.BuildContainerUid(terminal);
        _pendingCloseSince = -1f;
        _scanRadius = _config.TerminalRangeOverride.Value > 0 ? _config.TerminalRangeOverride.Value : _config.ScanRadius.Value;
        CaptureOriginalInventorySize(terminal.GetInventory());
        _revision = 0;
        _slotsTotalPhysical = 0;
        _chestCount = 0;
        _hasAuthoritativeSnapshot = false;
        _openSessionFailureCount = 0;
        _nextSnapshotRetryAt = Time.unscaledTime;
        _pendingReservations.Clear();
        _pendingDeposits.Clear();
        _authoritativeTotals.Clear();
        _displayedTotals.Clear();
        _prototypes.Clear();
        _stackSizes.Clear();
        _trace.Dev($"Terminal opened by {_player?.GetPlayerName() ?? "unknown"} (radius {_scanRadius:0.0}m).");
        RefreshTrackedChestHandles();
        RefreshTerminalInventoryFromAuthoritative();
        RequestSessionSnapshot();
        _uiRevision++;
    }

    public void Tick()
    {
        if (!IsActive || _terminal == null) return;

        if (InventoryGui.instance == null || !InventoryGui.IsVisible() || !InventoryGui.instance.IsContainerOpen())
        {
            if (_pendingCloseSince < 0f) { _pendingCloseSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _pendingCloseSince >= CloseGraceSeconds) EndSession();
            return;
        }

        _pendingCloseSince = -1f;
        if (Time.unscaledTime >= _nextTrackedChestRefreshAt) RefreshTrackedChestHandles();
        if (!_hasAuthoritativeSnapshot && Time.unscaledTime >= _nextSnapshotRetryAt) RequestSessionSnapshot();
        ExpireLocalReservations();
        if (!ReflectionHelpers.IsDragInProgress()) CommitPendingReservations();
    }

    public void SetSearchQuery(string query)
    {
        if (!IsActive || _terminal == null) return;
        var normalized = query?.Trim() ?? string.Empty;
        if (string.Equals(normalized, _searchQuery, StringComparison.Ordinal)) return;
        _searchQuery = normalized;
        _lastProjectedContentHash = 0;
        RefreshTerminalInventoryFromAuthoritative();
        _uiRevision++;
    }

    public void NotifyContainerInteraction()
    {
        if (!IsActive || _terminal == null) return;
        if (!IsSessionTerminal(_terminal)) { EndSession(); return; }

        var currentDisplayed = CaptureCurrentDisplayedTotals();
        ApplyDisplayedDeltaAsOperations(currentDisplayed);
        ReplaceDisplayedTotals(currentDisplayed);
        if (!ReflectionHelpers.IsDragInProgress()) CommitPendingReservations();
    }

    public void RefreshFromWorldChange()
    {
        if (!IsActive || _terminal == null) return;
        if (!IsSessionTerminal(_terminal)) { EndSession(); return; }
        RequestSessionSnapshot();
    }

    public void EndSession()
    {
        if (IsActive)
            _trace.Dev("Terminal closed.");
        if (_terminal != null && !string.IsNullOrWhiteSpace(_terminalUid))
        {
            _routes.RequestCloseSession(new CloseSessionRequestDto
            {
                RequestId = Guid.NewGuid().ToString("N"), SessionId = _sessionId,
                TerminalUid = _terminalUid, PlayerId = ResolveLocalPlayerId()
            });
        }

        if (_terminal != null)
        {
            ReflectionHelpers.ForceReleaseContainerUse(_terminal);
            ReflectionHelpers.ClearInventory(_terminal.GetInventory());
            RestoreTerminalInventorySize(_terminal.GetInventory());
        }

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
        _stackSizes.Clear();
        _sortCache.Clear();
        _sortedItems.Clear();
        _lastProjectedContentHash = 0;
        _slotsTotalPhysical = 0;
        _chestCount = 0;
        _revision = 0;
        _hasAuthoritativeSnapshot = false;
        _openSessionFailureCount = 0;
        _contentRows = 0;
        _uiRevision++;
    }

    private void OnSessionSnapshotReceived(OpenSessionResponseDto response)
    {
        if (!IsActive) return;
        if (!response.Success)
        {
            if (IsIdentityFailure(response.Reason)) { EndSession(); return; }
            _openSessionFailureCount = Math.Min(_openSessionFailureCount + 1, 8);
            _nextSnapshotRetryAt = Time.unscaledTime + GetRetryDelay(_openSessionFailureCount);
            return;
        }
        if (CanApplySnapshot(response.Snapshot)) ApplySnapshot(response.Snapshot);
    }

    private void OnReserveResultReceived(ReserveWithdrawResultDto result)
    {
        if (!IsActive || !CanApplySnapshot(result.Snapshot)) return;
        if (result.Success && !string.IsNullOrWhiteSpace(result.TokenId) && result.ReservedAmount > 0)
        {
            _trace.Dev($"Withdrew {result.ReservedAmount}x {GetDisplayName(result.Key)}.");
            _pendingReservations.Add(new PendingReservation
            {
                TokenId = result.TokenId, Key = result.Key,
                ReservedAmount = result.ReservedAmount, ExpiresAt = Time.unscaledTime + ReservationTtlSeconds
            });
        }
        ApplySnapshot(result.Snapshot);
    }

    private void OnApplyResultReceived(ApplyResultDto result)
    {
        if (!IsActive) return;
        if (string.Equals(result.OperationType, "deposit", StringComparison.Ordinal)) HandleDepositResult(result);
        if (string.Equals(result.OperationType, "commit", StringComparison.Ordinal))
        {
            if (result.Success) RemovePendingToken(result.TokenId);
            else { MarkPendingCommitAsRetryable(result.TokenId); RequestSessionSnapshot(); }
        }
        else if (string.Equals(result.OperationType, "cancel", StringComparison.Ordinal) && !result.Success)
        {
            RemovePendingToken(result.TokenId);
            RequestSessionSnapshot();
        }
        if (CanApplySnapshot(result.Snapshot)) ApplySnapshot(result.Snapshot);
        else if (!result.Success) RequestSessionSnapshot();
    }

    private void OnSessionDeltaReceived(SessionDeltaDto delta)
    {
        if (!IsActive) return;
        if (!string.Equals(delta.TerminalUid, _terminalUid, StringComparison.Ordinal)) return;
        if (!string.IsNullOrWhiteSpace(delta.SessionId) && !string.IsNullOrWhiteSpace(_sessionId) && !string.Equals(delta.SessionId, _sessionId, StringComparison.Ordinal)) return;
        if (delta.Revision < _revision) return;
        ApplySnapshot(delta.Snapshot);
    }

    private bool CanApplySnapshot(SessionSnapshotDto snapshot)
    {
        if (!IsActive || _terminal == null) return false;
        if (!string.Equals(snapshot.TerminalUid, _terminalUid, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(snapshot.SessionId) && !string.IsNullOrWhiteSpace(_sessionId) && !string.Equals(snapshot.SessionId, _sessionId, StringComparison.Ordinal)) return false;
        return true;
    }

    private void ApplySnapshot(SessionSnapshotDto snapshot)
    {
        if (!CanApplySnapshot(snapshot) || snapshot.Revision < _revision) return;
        if (!string.IsNullOrWhiteSpace(snapshot.SessionId)) _sessionId = snapshot.SessionId;
        _revision = snapshot.Revision;
        _slotsTotalPhysical = snapshot.SlotsTotalPhysical;
        _chestCount = snapshot.ChestCount;
        _hasAuthoritativeSnapshot = true;
        _openSessionFailureCount = 0;
        _nextSnapshotRetryAt = Time.unscaledTime + 60f;

        var previousKeyCount = _authoritativeTotals.Count;
        _authoritativeTotals.Clear();
        _stackSizes.Clear();
        foreach (var item in snapshot.Items)
        {
            if (item.TotalAmount <= 0) continue;
            _authoritativeTotals[item.Key] = item.TotalAmount;
            _stackSizes[item.Key] = item.StackSize > 0 ? item.StackSize : 1;
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

        if (_authoritativeTotals.Count != previousKeyCount)
            _sortCache.Clear();

        _trace.Dev($"Storage updated: {_chestCount} chest(s), {_authoritativeTotals.Count} item type(s).");
        RefreshTrackedChestHandles();
        RefreshTerminalInventoryFromAuthoritative();
        _uiRevision++;
    }

    private void RefreshTerminalInventoryFromAuthoritative()
    {
        if (_terminal == null) return;
        var inventory = _terminal.GetInventory();
        if (inventory == null) return;

        var contentHash = ComputeContentHash();
        if (contentHash == _lastProjectedContentHash) return;
        _lastProjectedContentHash = contentHash;

        _isApplyingProjection = true;
        try
        {
            ReflectionHelpers.ClearInventory(inventory);
            _displayedTotals.Clear();
            var width = Math.Max(1, ReflectionHelpers.GetInventoryWidth(inventory));

            RebuildSortedItems();

            var totalVirtualStacks = 0;
            foreach (var kvp in _sortedItems)
            {
                var maxStack = _stackSizes.TryGetValue(kvp.Key, out var s) && s > 0 ? s : 1;
                totalVirtualStacks += (int)Math.Ceiling(kvp.Value / (double)maxStack);
            }

            var reserveOneSlot = _slotsTotalPhysical > SlotsUsedVirtual ? 1 : 0;
            var requiredSlots = Math.Max(1, totalVirtualStacks + reserveOneSlot);
            _contentRows = Math.Max(1, (int)Math.Ceiling(requiredSlots / (float)width));
            ReflectionHelpers.SetInventorySize(inventory, width, _contentRows);

            var slotIndex = 0;
            foreach (var kvp in _sortedItems)
            {
                var remaining = kvp.Value;
                var maxStack = _stackSizes.TryGetValue(kvp.Key, out var ss) && ss > 0 ? ss : 1;
                while (remaining > 0)
                {
                    var stack = Math.Min(maxStack, remaining);
                    var item = CreateProjectedItem(kvp.Key, stack, maxStack);
                    if (item == null) break;
                    var x = slotIndex % width;
                    var y = slotIndex / width;
                    ReflectionHelpers.AddItemDirectly(inventory, item, x, y);
                    slotIndex++;
                    remaining -= stack;
                }
                _displayedTotals[kvp.Key] = kvp.Value - remaining;
            }
            ReflectionHelpers.NotifyInventoryChanged(inventory);
        }
        finally { _isApplyingProjection = false; }
    }

    private long ComputeContentHash()
    {
        unchecked
        {
            long hash = 17;
            foreach (var kvp in _authoritativeTotals)
            {
                hash = hash * 31 + kvp.Key.GetHashCode();
                hash = hash * 31 + kvp.Value;
            }
            hash = hash * 31 + _searchQuery.GetHashCode();
            return hash;
        }
    }

    private void RebuildSortedItems()
    {
        _sortedItems.Clear();
        foreach (var kvp in _authoritativeTotals)
        {
            if (kvp.Value <= 0) continue;
            EnsureSortCacheEntry(kvp.Key);
            var cached = _sortCache[kvp.Key];
            if (!MatchesSearch(cached.DisplayName)) continue;
            _sortedItems.Add(kvp);
        }
        _sortedItems.Sort((a, b) =>
        {
            var ca = _sortCache[a.Key];
            var cb = _sortCache[b.Key];
            var cmp = ca.TypeOrder.CompareTo(cb.TypeOrder);
            if (cmp != 0) return cmp;
            cmp = ca.SubgroupOrder.CompareTo(cb.SubgroupOrder);
            if (cmp != 0) return cmp;
            cmp = string.Compare(ca.DisplayName, cb.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
            return b.Value.CompareTo(a.Value);
        });
    }

    private void EnsureSortCacheEntry(ItemKey key)
    {
        if (_sortCache.ContainsKey(key)) return;
        _sortCache[key] = new CachedSortEntry
        {
            DisplayName = GetDisplayName(key),
            TypeOrder = GetItemTypeOrder(key),
            SubgroupOrder = ReflectionHelpers.GetSubgroupOrder(key)
        };
    }

    private ItemDrop.ItemData? CreateProjectedItem(ItemKey key, int amount, int maxStackSize)
    {
        if (!_prototypes.TryGetValue(key, out var prototype))
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(key.PrefabName);
            var drop = prefab?.GetComponent<ItemDrop>();
            if (drop?.m_itemData == null) return null;
            prototype = drop.m_itemData.Clone();
            prototype.m_quality = key.Quality;
            prototype.m_variant = key.Variant;
            _prototypes[key] = prototype;
        }
        var item = prototype.Clone();
        item.m_stack = amount;
        return item;
    }

    private bool MatchesSearch(string displayName) =>
        string.IsNullOrWhiteSpace(_searchQuery) || displayName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;

    private void RefreshTrackedChestHandles()
    {
        if (_terminal == null) { _trackedChests.Clear(); _nextTrackedChestRefreshAt = Time.unscaledTime + TrackedChestRefreshSeconds; return; }
        _trackedChests = _scanner.GetNearbyContainers(_terminal.transform.position, _scanRadius, _terminal).ToList();
        _nextTrackedChestRefreshAt = Time.unscaledTime + TrackedChestRefreshSeconds;
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
            if (delta < 0) RequestReserveWithdraw(key, -delta);
            else if (delta > 0) RequestCancelAndOrDeposit(key, delta);
        }
    }

    private void RequestReserveWithdraw(ItemKey key, int amount)
    {
        if (amount <= 0) return;
        _routes.RequestReserveWithdraw(new ReserveWithdrawRequestDto
        {
            RequestId = Guid.NewGuid().ToString("N"), SessionId = _sessionId,
            OperationId = Guid.NewGuid().ToString("N"), TerminalUid = _terminalUid,
            PlayerId = ResolveLocalPlayerId(), ExpectedRevision = _revision, Key = key, Amount = amount
        });
    }

    private void RequestCancelAndOrDeposit(ItemKey key, int amount)
    {
        if (amount <= 0) return;
        var remaining = amount;
        foreach (var pending in _pendingReservations.Where(r => r.Key.Equals(key) && r.ReservedAmount > 0).ToList())
        {
            if (remaining <= 0) break;
            var cancelAmount = Math.Min(remaining, pending.ReservedAmount);
            if (cancelAmount <= 0) continue;
            _routes.RequestCancelReservation(new CancelReservationRequestDto
            {
                RequestId = Guid.NewGuid().ToString("N"), SessionId = _sessionId,
                OperationId = Guid.NewGuid().ToString("N"), TerminalUid = _terminalUid,
                PlayerId = ResolveLocalPlayerId(), TokenId = pending.TokenId, Amount = cancelAmount
            });
            pending.ReservedAmount -= cancelAmount;
            remaining -= cancelAmount;
            if (pending.ReservedAmount <= 0) _pendingReservations.Remove(pending);
        }
        if (remaining <= 0) return;

        var requestId = Guid.NewGuid().ToString("N");
        _pendingDeposits.Add(new PendingDeposit { RequestId = requestId, Key = key, Amount = remaining });
        _routes.RequestDeposit(new DepositRequestDto
        {
            RequestId = requestId, SessionId = _sessionId,
            OperationId = Guid.NewGuid().ToString("N"), TerminalUid = _terminalUid,
            PlayerId = ResolveLocalPlayerId(), ExpectedRevision = _revision, Key = key, Amount = remaining
        });
    }

    private void CommitPendingReservations()
    {
        foreach (var pending in _pendingReservations.Where(r => !r.CommitRequested && r.ReservedAmount > 0).ToList())
        {
            _routes.RequestCommitReservation(new CommitReservationRequestDto
            {
                RequestId = Guid.NewGuid().ToString("N"), SessionId = _sessionId,
                OperationId = Guid.NewGuid().ToString("N"), TerminalUid = _terminalUid, TokenId = pending.TokenId
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
            if (_pendingReservations[i].ExpiresAt > now) continue;
            _pendingReservations.RemoveAt(i);
            removed = true;
        }
        if (removed) RequestSessionSnapshot();
    }

    private void RequestSessionSnapshot()
    {
        if (!IsActive || _terminal == null) return;
        _routes.RequestOpenSession(new OpenSessionRequestDto
        {
            RequestId = Guid.NewGuid().ToString("N"), SessionId = _sessionId,
            TerminalUid = _terminalUid, PlayerId = ResolveLocalPlayerId(),
            AnchorX = _terminal.transform.position.x, AnchorY = _terminal.transform.position.y,
            AnchorZ = _terminal.transform.position.z, Radius = _scanRadius
        });
        _nextSnapshotRetryAt = Time.unscaledTime + 0.5f;
    }

    private Dictionary<ItemKey, int> CaptureCurrentDisplayedTotals()
    {
        var displayed = new Dictionary<ItemKey, int>();
        if (_terminal == null) return displayed;
        var inventory = _terminal.GetInventory();
        if (inventory == null) return displayed;
        foreach (var item in inventory.GetAllItems())
        {
            if (item?.m_dropPrefab == null || item.m_stack <= 0) continue;
            var key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
            displayed[key] = displayed.TryGetValue(key, out var existing) ? existing + item.m_stack : item.m_stack;
        }
        return displayed;
    }

    private void ReplaceDisplayedTotals(Dictionary<ItemKey, int> totals)
    {
        _displayedTotals.Clear();
        foreach (var kv in totals) _displayedTotals[kv.Key] = kv.Value;
    }

    private bool IsSessionTerminal(Container container) =>
        container != null && !string.IsNullOrWhiteSpace(_terminalUid) && string.Equals(ReflectionHelpers.BuildContainerUid(container), _terminalUid, StringComparison.Ordinal);

    private void HandleDepositResult(ApplyResultDto result)
    {
        var pending = _pendingDeposits.FirstOrDefault(p => string.Equals(p.RequestId, result.RequestId, StringComparison.Ordinal));
        if (pending == null) return;
        _pendingDeposits.Remove(pending);
        if (result.Success)
            _trace.Dev($"Deposited {result.AppliedAmount}x {GetDisplayName(pending.Key)}.");
        else if (ShouldRestoreFailedDeposit(result.Reason))
            RestoreToLocalPlayerInventory(pending.Key, pending.Amount);
    }

    private void RestoreToLocalPlayerInventory(ItemKey key, int amount)
    {
        if (amount <= 0 || _player == null) return;
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        var inventory = _player.GetInventory();
        var movedTotal = ChunkedTransfer.Move(amount, maxStack, chunkAmount =>
        {
            var stack = ReflectionHelpers.CreateItemStack(key, chunkAmount);
            if (stack == null) return 0;
            return ReflectionHelpers.TryAddItemMeasured(inventory, key, stack, chunkAmount);
        });
        var remaining = amount - movedTotal;
        if (remaining > 0) DropNearPlayer(key, remaining);
    }

    private void DropNearPlayer(ItemKey key, int amount)
    {
        if (_player == null || amount <= 0) return;
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        var remaining = amount;
        while (remaining > 0)
        {
            var stackAmount = Math.Min(maxStack, remaining);
            var stack = ReflectionHelpers.CreateItemStack(key, stackAmount);
            if (stack == null) break;
            if (!ReflectionHelpers.TryDropItem(stack, stackAmount, _player.transform.position + _player.transform.forward + Vector3.up, Quaternion.identity)) break;
            remaining -= stackAmount;
        }
    }

    private void RemovePendingToken(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId)) return;
        _pendingReservations.RemoveAll(r => string.Equals(r.TokenId, tokenId, StringComparison.Ordinal));
    }

    private void MarkPendingCommitAsRetryable(string tokenId)
    {
        foreach (var pending in _pendingReservations.Where(r => string.Equals(r.TokenId, tokenId, StringComparison.Ordinal)))
            pending.CommitRequested = false;
    }

    private long ResolveLocalPlayerId() => _player != null ? _player.GetPlayerID() : 0L;

    private string GetDisplayName(ItemKey key)
    {
        if (_prototypes.TryGetValue(key, out var prototype)) return prototype.m_shared.m_name;
        return key.PrefabName;
    }

    private int GetItemTypeOrder(ItemKey key)
    {
        if (_prototypes.TryGetValue(key, out var prototype)) return (int)prototype.m_shared.m_itemType;
        return int.MaxValue;
    }

    private void CaptureOriginalInventorySize(Inventory? inventory)
    {
        if (inventory == null) { _originalInventoryWidth = 8; _originalInventoryHeight = 4; return; }
        _originalInventoryWidth = ReflectionHelpers.GetInventoryWidth(inventory);
        _originalInventoryHeight = ReflectionHelpers.GetInventoryHeight(inventory);
    }

    private void RestoreTerminalInventorySize(Inventory? inventory)
    {
        if (inventory == null || _originalInventoryWidth <= 0 || _originalInventoryHeight <= 0) return;
        ReflectionHelpers.SetInventorySize(inventory, _originalInventoryWidth, _originalInventoryHeight);
    }

    private static bool ShouldRestoreFailedDeposit(string reason) =>
        !string.IsNullOrWhiteSpace(reason) && (
            reason == "Conflict" || reason == "Player not found" ||
            reason == "Player has no matching item" || reason == "Player/terminal has no matching item" ||
            reason == "Session not found" || reason == "No storage space" ||
            reason == "Player identity mismatch" || reason == "Unable to resolve player identity");

    private static bool IsIdentityFailure(string reason) =>
        reason == "Player identity mismatch" || reason == "Unable to resolve player identity";

    private static float GetRetryDelay(int failureCount)
    {
        if (failureCount <= 0) return OpenSessionRetryBaseSeconds;
        var exponent = Math.Min(6, failureCount - 1);
        var delay = OpenSessionRetryBaseSeconds * Mathf.Pow(2f, exponent);
        return Mathf.Min(OpenSessionRetryMaxSeconds, delay);
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

    private struct CachedSortEntry
    {
        public string DisplayName;
        public int TypeOrder;
        public int SubgroupOrder;
    }
}
