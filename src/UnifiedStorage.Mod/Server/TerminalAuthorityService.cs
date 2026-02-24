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
using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Server;

public sealed class TerminalAuthorityService
{
    private const float ReservationTtlSeconds = 3f;
    private const float SnapshotCacheSeconds = 0.2f;
    private const int MaxOperationHistory = 2048;

    private readonly object _sync = new();
    private readonly StorageConfig _config;
    private readonly IContainerScanner _scanner;
    private readonly ManualLogSource _logger;
    private readonly Dictionary<string, TerminalState> _states = new(StringComparer.Ordinal);

    public TerminalAuthorityService(StorageConfig config, IContainerScanner scanner, ManualLogSource logger)
    {
        _config = config;
        _scanner = scanner;
        _logger = logger;
    }

    public event Action<IReadOnlyCollection<long>, SessionDeltaDto>? DeltaReady;

    public void Tick()
    {
        List<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas = new();
        lock (_sync)
        {
            if (_states.Count == 0) return;

            var now = Time.unscaledTime;
            foreach (var state in _states.Values.ToList())
            {
                state.Subscribers.RemoveWhere(peer => !IsPeerConnected(peer));
                foreach (var stalePeer in state.PeerPlayerIds.Keys.Where(peer => !IsPeerConnected(peer)).ToList())
                    state.PeerPlayerIds.Remove(stalePeer);

                var expiredOrDisconnected = state.Reservations.Values
                    .Where(r => r.ExpiresAt <= now || !IsPeerConnected(r.PeerId))
                    .ToList();

                var anyChange = false;
                foreach (var reservation in expiredOrDisconnected)
                {
                    var restored = AddToChests(state, reservation.Key, reservation.Amount, null);
                    var unresolved = reservation.Amount - restored;
                    if (unresolved > 0) DropNearTerminal(state, reservation.Key, unresolved);
                    state.Reservations.Remove(reservation.TokenId);
                    anyChange = anyChange || restored > 0 || unresolved > 0;
                }

                if (anyChange)
                {
                    MarkSnapshotDirty(state);
                    state.Revision++;
                    var snapshot = BuildSnapshot(state);
                    deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));
                }

                if (state.Subscribers.Count == 0 && state.Reservations.Count == 0)
                    _states.Remove(state.TerminalUid);
            }
        }
        EmitDeltas(deltas);
    }

    public OpenSessionResponseDto HandleOpenSession(long sender, OpenSessionRequestDto request)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(request.TerminalUid))
                return new OpenSessionResponseDto { RequestId = request.RequestId, Success = false, Reason = "Invalid terminal" };

            var state = GetOrCreateState(request);
            if (!TryResolveAndBindPlayerId(state, sender, request.PlayerId, out var resolvedPlayerId))
                return new OpenSessionResponseDto { RequestId = request.RequestId, Success = false, Reason = "Player identity mismatch" };
            if (resolvedPlayerId <= 0)
                return new OpenSessionResponseDto { RequestId = request.RequestId, Success = false, Reason = "Unable to resolve player identity" };

            state.Subscribers.Add(sender);
            var snapshot = BuildSnapshot(state);
            return new OpenSessionResponseDto { RequestId = request.RequestId, Success = true, Snapshot = snapshot };
        }
    }

    public ReserveWithdrawResultDto HandleReserveWithdraw(long sender, ReserveWithdrawRequestDto request)
    {
        List<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas = new();
        ReserveWithdrawResultDto result;
        lock (_sync)
        {
            var state = GetState(request.TerminalUid);
            if (state == null) return ReserveFailure(request, "Session not found");

            if (IsDuplicateOperation(state, request.OperationId))
                return new ReserveWithdrawResultDto { RequestId = request.RequestId, Success = true, Reason = "Duplicate", Key = request.Key, Revision = state.Revision, Snapshot = BuildSnapshot(state) };

            if (request.ExpectedRevision >= 0 && request.ExpectedRevision != state.Revision)
                return ReserveFailure(request, "Conflict", BuildSnapshot(state), state.Revision);

            if (!TryGetMappedPlayerId(state, sender, request.PlayerId, out var playerId))
                return ReserveFailure(request, "Player identity mismatch", BuildSnapshot(state), state.Revision);

            var player = FindPlayerById(playerId);
            var removed = RemoveFromChests(state, request.Key, request.Amount, player);
            if (removed <= 0)
            {
                RememberOperation(state, request.OperationId);
                return ReserveFailure(request, "Not enough items", BuildSnapshot(state), state.Revision);
            }

            var token = Guid.NewGuid().ToString("N");
            state.Reservations[token] = new ReservationRecord
            {
                TokenId = token, PeerId = sender, SessionId = request.SessionId ?? string.Empty,
                Key = request.Key, Amount = removed, ExpiresAt = Time.unscaledTime + ReservationTtlSeconds
            };

            state.Revision++;
            MarkSnapshotDirty(state);
            RememberOperation(state, request.OperationId);
            var snapshot = BuildSnapshot(state);
            deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));

            result = new ReserveWithdrawResultDto
            {
                RequestId = request.RequestId, Success = true, TokenId = token, Key = request.Key,
                ReservedAmount = removed, Revision = state.Revision, Snapshot = snapshot
            };
        }
        EmitDeltas(deltas);
        return result;
    }

    public ApplyResultDto HandleCommitReservation(long sender, CommitReservationRequestDto request)
    {
        lock (_sync)
        {
            var state = GetState(request.TerminalUid);
            if (state == null) return ApplyFailure(request.RequestId, request.TokenId ?? "", "Session not found", operationType: "commit");

            if (IsDuplicateOperation(state, request.OperationId))
                return new ApplyResultDto { RequestId = request.RequestId, Success = true, TokenId = request.TokenId ?? "", Reason = "Duplicate", OperationType = "commit", Revision = state.Revision, Snapshot = BuildSnapshot(state) };

            if (!state.Reservations.TryGetValue(request.TokenId ?? "", out var reservation))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? "", "Reservation not found", BuildSnapshot(state), state.Revision, "commit");
            }

            if (reservation.PeerId != sender)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? "", "Reservation owner mismatch", BuildSnapshot(state), state.Revision, "commit");
            }

            var applied = reservation.Amount;
            state.Reservations.Remove(reservation.TokenId);
            RememberOperation(state, request.OperationId);

            return new ApplyResultDto
            {
                RequestId = request.RequestId, Success = true, OperationType = "commit",
                TokenId = reservation.TokenId, AppliedAmount = applied, Revision = state.Revision, Snapshot = BuildSnapshot(state)
            };
        }
    }

    public ApplyResultDto HandleCancelReservation(long sender, CancelReservationRequestDto request)
    {
        List<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas = new();
        ApplyResultDto result;
        lock (_sync)
        {
            var state = GetState(request.TerminalUid);
            if (state == null) return ApplyFailure(request.RequestId, request.TokenId ?? "", "Session not found", operationType: "cancel");

            if (IsDuplicateOperation(state, request.OperationId))
                return new ApplyResultDto { RequestId = request.RequestId, Success = true, TokenId = request.TokenId ?? "", Reason = "Duplicate", OperationType = "cancel", Revision = state.Revision, Snapshot = BuildSnapshot(state) };

            if (!state.Reservations.TryGetValue(request.TokenId ?? "", out var reservation))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? "", "Reservation not found", BuildSnapshot(state), state.Revision, "cancel");
            }

            if (reservation.PeerId != sender)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? "", "Reservation owner mismatch", BuildSnapshot(state), state.Revision, "cancel");
            }

            var cancelAmount = request.Amount > 0 ? Math.Min(request.Amount, reservation.Amount) : reservation.Amount;
            if (!TryGetMappedPlayerId(state, sender, request.PlayerId, out var playerId))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? "", "Player identity mismatch", BuildSnapshot(state), state.Revision, "cancel");
            }

            var player = FindPlayerById(playerId);
            var restored = AddToChests(state, reservation.Key, cancelAmount, player);
            var unresolved = cancelAmount - restored;
            if (unresolved > 0) DropNearTerminal(state, reservation.Key, unresolved);

            reservation.Amount -= cancelAmount;
            if (reservation.Amount <= 0) state.Reservations.Remove(reservation.TokenId);

            if (restored > 0 || unresolved > 0)
            {
                MarkSnapshotDirty(state);
                state.Revision++;
                var snapshot = BuildSnapshot(state);
                deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));
                RememberOperation(state, request.OperationId);
                result = new ApplyResultDto { RequestId = request.RequestId, Success = true, OperationType = "cancel", TokenId = request.TokenId ?? "", AppliedAmount = cancelAmount, Revision = state.Revision, Snapshot = snapshot };
                goto EmitAndReturn;
            }

            RememberOperation(state, request.OperationId);
            result = ApplyFailure(request.RequestId, request.TokenId ?? "", "Nothing restored", BuildSnapshot(state), state.Revision, "cancel");
        }
    EmitAndReturn:
        EmitDeltas(deltas);
        return result;
    }

    public ApplyResultDto HandleDeposit(long sender, DepositRequestDto request)
    {
        List<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas = new();
        ApplyResultDto result;
        lock (_sync)
        {
            var state = GetState(request.TerminalUid);
            if (state == null) return ApplyFailure(request.RequestId, "", "Session not found", operationType: "deposit");

            if (IsDuplicateOperation(state, request.OperationId))
                return new ApplyResultDto { RequestId = request.RequestId, Success = true, Reason = "Duplicate", OperationType = "deposit", Revision = state.Revision, Snapshot = BuildSnapshot(state) };

            if (request.ExpectedRevision >= 0 && request.ExpectedRevision != state.Revision)
                return ApplyFailure(request.RequestId, "", "Conflict", BuildSnapshot(state), state.Revision, "deposit");

            if (!TryGetMappedPlayerId(state, sender, request.PlayerId, out var playerId))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, "", "Player identity mismatch", BuildSnapshot(state), state.Revision, "deposit");
            }

            var player = FindPlayerById(playerId);
            if (player == null)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, "", "Player not found", BuildSnapshot(state), state.Revision, "deposit");
            }

            if (!HasAnyCapacityFor(state, request.Key, player))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, "", "No storage space", BuildSnapshot(state), state.Revision, "deposit");
            }

            var terminal = FindTerminalByUid(state.TerminalUid);
            var removedAmount = RemoveFromTerminalInventory(terminal, request.Key, request.Amount);
            if (removedAmount <= 0)
                removedAmount = RemoveFromPlayerInventory(player, request.Key, request.Amount);

            if (removedAmount <= 0)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, "", "Player/terminal has no matching item", BuildSnapshot(state), state.Revision, "deposit");
            }

            var stored = AddToChests(state, request.Key, removedAmount, player);
            if (stored <= 0)
            {
                RememberOperation(state, request.OperationId);
                result = new ApplyResultDto
                {
                    RequestId = request.RequestId, Success = false,
                    Reason = "No storage space", OperationType = "deposit",
                    AppliedAmount = 0, Revision = state.Revision, Snapshot = BuildSnapshot(state)
                };
                goto EmitAndReturn;
            }

            var notStored = removedAmount - stored;
            if (notStored > 0)
            {
                var restoredToPlayer = AddToPlayerInventory(player, request.Key, notStored);
                var unresolved = notStored - restoredToPlayer;
                if (unresolved > 0) DropNearTerminal(state, request.Key, unresolved);
            }

            MarkSnapshotDirty(state);
            state.Revision++;
            RememberOperation(state, request.OperationId);
            var snapshot = BuildSnapshot(state);
            deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));

            result = new ApplyResultDto
            {
                RequestId = request.RequestId, Success = true,
                Reason = "", OperationType = "deposit",
                AppliedAmount = stored, Revision = state.Revision, Snapshot = snapshot
            };
        }
    EmitAndReturn:
        EmitDeltas(deltas);
        return result;
    }

    public void HandleCloseSession(long sender, CloseSessionRequestDto request)
    {
        List<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas = new();
        lock (_sync)
        {
            var state = GetState(request.TerminalUid);
            if (state == null) return;

            state.PeerPlayerIds.TryGetValue(sender, out var mappedPlayerId);
            state.Subscribers.Remove(sender);
            state.PeerPlayerIds.Remove(sender);

            var cancelledReservations = state.Reservations.Values.Where(r => r.PeerId == sender).ToList();
            var effectivePlayerId = mappedPlayerId > 0 ? mappedPlayerId : request.PlayerId;
            var player = FindPlayerById(effectivePlayerId);
            var anyChanged = false;
            foreach (var reservation in cancelledReservations)
            {
                var restored = AddToChests(state, reservation.Key, reservation.Amount, player);
                var unresolved = reservation.Amount - restored;
                if (unresolved > 0) DropNearTerminal(state, reservation.Key, unresolved);
                state.Reservations.Remove(reservation.TokenId);
                anyChanged = anyChanged || restored > 0 || unresolved > 0;
            }

            if (anyChanged)
            {
                MarkSnapshotDirty(state);
                state.Revision++;
                var snapshot = BuildSnapshot(state);
                deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));
            }

            if (state.Subscribers.Count == 0 && state.Reservations.Count == 0)
                _states.Remove(state.TerminalUid);
        }
        EmitDeltas(deltas);
    }

    private SessionSnapshotDto BuildSnapshot(TerminalState state)
    {
        var now = Time.unscaledTime;
        if (!state.SnapshotDirty && state.CachedSnapshot != null && now - state.CachedSnapshotAt <= SnapshotCacheSeconds)
            return state.CachedSnapshot;

        RefreshChestHandles(state);

        var sourceStacks = new List<SourceStack>();
        var prototypes = new Dictionary<ItemKey, ItemDrop.ItemData>();
        var slotsTotal = 0;
        foreach (var chest in state.Chests)
        {
            var inventory = chest.Container.GetInventory();
            if (inventory == null) continue;
            slotsTotal += ReflectionHelpers.GetInventoryWidth(inventory) * ReflectionHelpers.GetInventoryHeight(inventory);

            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack <= 0) continue;
                var key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
                if (!prototypes.ContainsKey(key)) prototypes[key] = item.Clone();

                sourceStacks.Add(new SourceStack
                {
                    Key = key, DisplayName = item.m_shared.m_name,
                    Amount = item.m_stack, StackSize = item.m_shared.m_maxStackSize,
                    Distance = chest.Distance, SourceId = chest.SourceId
                });
            }
        }

        var aggregated = AggregationService.Aggregate(sourceStacks, ReflectionHelpers.ResolveMaxStackSize);

        var orderedItems = aggregated
            .Select(a => new
            {
                Item = a,
                TypeOrder = prototypes.TryGetValue(a.Key, out var p) ? (int)p.m_shared.m_itemType : int.MaxValue,
                SubgroupOrder = ReflectionHelpers.GetSubgroupOrder(a.Key)
            })
            .OrderBy(x => x.TypeOrder)
            .ThenBy(x => x.SubgroupOrder)
            .ThenBy(x => x.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.Item.TotalAmount)
            .Select(x => x.Item)
            .ToList();

        state.ChestCount = state.Chests.Count;
        state.SlotsTotalPhysical = slotsTotal;

        var snapshot = new SessionSnapshotDto
        {
            SessionId = state.SessionId, TerminalUid = state.TerminalUid,
            Revision = state.Revision, SlotsTotalPhysical = slotsTotal,
            SlotsUsedVirtual = AggregationService.CalculateVirtualSlots(orderedItems),
            ChestCount = state.ChestCount, Items = orderedItems
        };

        state.CachedSnapshot = snapshot;
        state.CachedSnapshotAt = now;
        state.SnapshotDirty = false;
        return snapshot;
    }

    private void RefreshChestHandles(TerminalState state)
    {
        var terminal = FindTerminalByUid(state.TerminalUid);
        if (terminal != null) state.AnchorPosition = terminal.transform.position;
        state.Chests = _scanner.GetNearbyContainers(state.AnchorPosition, state.Radius, terminal).ToList();
    }

    private int RemoveFromChests(TerminalState state, ItemKey key, int amount, Player? player)
    {
        if (amount <= 0) return 0;
        var remaining = amount;
        foreach (var chest in state.Chests.OrderBy(c => c.Distance).ThenBy(c => c.SourceId, StringComparer.Ordinal))
        {
            if (remaining <= 0) break;
            var inv = chest.Container.GetInventory();
            if (inv == null) continue;
            if (_config.RequireAccessCheck && player != null && !ReflectionHelpers.CanAccess(chest.Container, player)) continue;

            foreach (var item in inv.GetAllItems().Where(i => ReflectionHelpers.MatchKey(i, key)).ToList())
            {
                if (remaining <= 0) break;
                var take = Math.Min(item.m_stack, remaining);
                inv.RemoveItem(item, take);
                remaining -= take;
            }
        }
        return amount - remaining;
    }

    private int AddToChests(TerminalState state, ItemKey key, int amount, Player? player)
    {
        if (amount <= 0) return 0;
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        var remaining = amount;
        foreach (var chest in state.Chests.OrderBy(c => c.Distance).ThenBy(c => c.SourceId, StringComparer.Ordinal))
        {
            if (remaining <= 0) break;
            var inv = chest.Container.GetInventory();
            if (inv == null) continue;
            if (_config.RequireAccessCheck && player != null && !ReflectionHelpers.CanAccess(chest.Container, player)) continue;

            var movedInChest = ChunkedTransfer.Move(remaining, maxStack, chunkAmount =>
            {
                var stack = ReflectionHelpers.CreateItemStack(key, chunkAmount);
                if (stack == null) return 0;
                return ReflectionHelpers.TryAddItemMeasured(inv, key, stack, chunkAmount);
            });
            remaining -= movedInChest;
        }
        return amount - remaining;
    }

    private static int RemoveFromPlayerInventory(Player player, ItemKey key, int amount)
    {
        if (amount <= 0) return 0;
        var remaining = amount;
        var inventory = player.GetInventory();
        foreach (var item in inventory.GetAllItems().Where(i => ReflectionHelpers.MatchKey(i, key)).ToList())
        {
            if (remaining <= 0) break;
            var take = Math.Min(item.m_stack, remaining);
            inventory.RemoveItem(item, take);
            remaining -= take;
        }
        return amount - remaining;
    }

    private static int RemoveFromTerminalInventory(Container? terminal, ItemKey key, int amount)
    {
        if (terminal == null || amount <= 0) return 0;
        var inventory = terminal.GetInventory();
        if (inventory == null) return 0;
        var remaining = amount;
        foreach (var item in inventory.GetAllItems().Where(i => ReflectionHelpers.MatchKey(i, key)).ToList())
        {
            if (remaining <= 0) break;
            var take = Math.Min(item.m_stack, remaining);
            inventory.RemoveItem(item, take);
            remaining -= take;
        }
        return amount - remaining;
    }

    private int AddToPlayerInventory(Player player, ItemKey key, int amount)
    {
        if (amount <= 0) return 0;
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        var inventory = player.GetInventory();
        return ChunkedTransfer.Move(amount, maxStack, chunkAmount =>
        {
            var stack = ReflectionHelpers.CreateItemStack(key, chunkAmount);
            if (stack == null) return 0;
            return ReflectionHelpers.TryAddItemMeasured(inventory, key, stack, chunkAmount);
        });
    }

    private void DropNearTerminal(TerminalState state, ItemKey key, int amount)
    {
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        var remaining = amount;
        while (remaining > 0)
        {
            var stackAmount = Math.Min(maxStack, remaining);
            var stack = ReflectionHelpers.CreateItemStack(key, stackAmount);
            if (stack == null) break;
            if (!ReflectionHelpers.TryDropItem(stack, stackAmount, state.AnchorPosition + Vector3.up, Quaternion.identity)) break;
            remaining -= stackAmount;
        }
    }

    private int AddToTerminalInventory(Container terminal, ItemKey key, int amount)
    {
        if (amount <= 0) return 0;
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        var inventory = terminal.GetInventory();
        if (inventory == null) return 0;
        return ChunkedTransfer.Move(amount, maxStack, chunkAmount =>
        {
            var stack = ReflectionHelpers.CreateItemStack(key, chunkAmount);
            if (stack == null) return 0;
            return ReflectionHelpers.TryAddItemMeasured(inventory, key, stack, chunkAmount);
        });
    }

    private bool HasAnyCapacityFor(TerminalState state, ItemKey key, Player? player)
    {
        var maxStack = ReflectionHelpers.GetMaxStackSize(key);
        if (maxStack <= 0) maxStack = 1;
        foreach (var chest in state.Chests)
        {
            var inv = chest.Container.GetInventory();
            if (inv == null) continue;
            if (_config.RequireAccessCheck && player != null && !ReflectionHelpers.CanAccess(chest.Container, player)) continue;
            var items = inv.GetAllItems();
            foreach (var item in items)
            {
                if (ReflectionHelpers.MatchKey(item, key) && item.m_stack < maxStack) return true;
            }
            var totalSlots = ReflectionHelpers.GetInventoryWidth(inv) * ReflectionHelpers.GetInventoryHeight(inv);
            if (items.Count < totalSlots) return true;
        }
        return false;
    }

    private static Container? FindTerminalByUid(string terminalUid)
    {
        if (string.IsNullOrWhiteSpace(terminalUid)) return null;
        foreach (var container in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
        {
            if (container == null || !UnifiedTerminal.IsTerminal(container)) continue;
            if (string.Equals(ReflectionHelpers.BuildContainerUid(container), terminalUid, StringComparison.Ordinal))
                return container;
        }
        return null;
    }

    private static void MarkSnapshotDirty(TerminalState state) { state.SnapshotDirty = true; state.CachedSnapshot = null; }

    private TerminalState GetOrCreateState(OpenSessionRequestDto request)
    {
        if (_states.TryGetValue(request.TerminalUid, out var existing))
        {
            var anchor = new Vector3(request.AnchorX, request.AnchorY, request.AnchorZ);
            if ((existing.AnchorPosition - anchor).sqrMagnitude > 0.0001f || !Mathf.Approximately(existing.Radius, request.Radius))
            {
                existing.AnchorPosition = anchor;
                existing.Radius = request.Radius;
                MarkSnapshotDirty(existing);
            }
            return existing;
        }
        var created = new TerminalState
        {
            TerminalUid = request.TerminalUid, SessionId = Guid.NewGuid().ToString("N"),
            AnchorPosition = new Vector3(request.AnchorX, request.AnchorY, request.AnchorZ), Radius = request.Radius
        };
        _states[request.TerminalUid] = created;
        return created;
    }

    private TerminalState? GetState(string terminalUid) =>
        !string.IsNullOrWhiteSpace(terminalUid) && _states.TryGetValue(terminalUid, out var state) ? state : null;

    private static ReserveWithdrawResultDto ReserveFailure(ReserveWithdrawRequestDto req, string reason, SessionSnapshotDto? snapshot = null, long revision = 0) =>
        new() { RequestId = req.RequestId, Success = false, Reason = reason, Key = req.Key, Revision = revision, Snapshot = snapshot ?? new() };

    private static ApplyResultDto ApplyFailure(string reqId, string tokenId, string reason, SessionSnapshotDto? snapshot = null, long revision = 0, string operationType = "") =>
        new() { RequestId = reqId, Success = false, Reason = reason, OperationType = operationType, TokenId = tokenId, Revision = revision, Snapshot = snapshot ?? new() };

    private static bool TryResolveAndBindPlayerId(TerminalState state, long sender, long requestedPlayerId, out long resolvedPlayerId)
    {
        resolvedPlayerId = 0;
        var runtimePlayerId = ResolvePlayerIdFromPeer(sender);
        if (runtimePlayerId > 0)
        {
            if (requestedPlayerId > 0 && requestedPlayerId != runtimePlayerId) return false;
            state.PeerPlayerIds[sender] = runtimePlayerId;
            resolvedPlayerId = runtimePlayerId;
            return true;
        }
        if (state.PeerPlayerIds.TryGetValue(sender, out var mapped))
        {
            if (requestedPlayerId > 0 && requestedPlayerId != mapped) return false;
            resolvedPlayerId = mapped;
            return true;
        }
        if (requestedPlayerId > 0 && sender <= 0)
        {
            state.PeerPlayerIds[sender] = requestedPlayerId;
            resolvedPlayerId = requestedPlayerId;
            return true;
        }
        return false;
    }

    private static bool TryGetMappedPlayerId(TerminalState state, long sender, long requestedPlayerId, out long playerId)
    {
        playerId = 0;
        if (!state.PeerPlayerIds.TryGetValue(sender, out var mapped)) return false;
        if (requestedPlayerId > 0 && requestedPlayerId != mapped) return false;
        playerId = mapped;
        return true;
    }

    private static long ResolvePlayerIdFromPeer(long sender)
    {
        if (sender <= 0) return Player.m_localPlayer?.GetPlayerID() ?? 0L;
        foreach (var player in Player.GetAllPlayers())
        {
            if (player == null) continue;
            var nview = player.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo == null) continue;
            var zdoType = zdo.GetType();
            var getOwnerMethod = zdoType.GetMethod("GetOwner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getOwnerMethod != null && getOwnerMethod.Invoke(zdo, null) is long ownerByMethod && ownerByMethod == sender)
                return player.GetPlayerID();
            var ownerField = zdoType.GetField("m_owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ownerField != null && ownerField.GetValue(zdo) is long ownerByField && ownerByField == sender)
                return player.GetPlayerID();
        }
        return 0L;
    }

    private static Player? FindPlayerById(long playerId)
    {
        if (playerId <= 0) return Player.m_localPlayer;
        foreach (var player in Player.GetAllPlayers())
            if (player != null && player.GetPlayerID() == playerId) return player;
        if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId) return Player.m_localPlayer;
        return null;
    }

    private static bool IsPeerConnected(long peerId)
    {
        if (peerId <= 0 || ZNet.instance == null) return true;
        var getPeerMethod = typeof(ZNet).GetMethod("GetPeer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(long) }, null);
        if (getPeerMethod != null && getPeerMethod.Invoke(ZNet.instance, new object[] { peerId }) != null) return true;
        var peersField = typeof(ZNet).GetField("m_peers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (peersField?.GetValue(ZNet.instance) is System.Collections.IEnumerable peers)
        {
            foreach (var peer in peers)
            {
                if (peer == null) continue;
                var uidField = peer.GetType().GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (uidField?.GetValue(peer) is long uid && uid == peerId) return true;
            }
        }
        return false;
    }

    private static bool IsDuplicateOperation(TerminalState state, string operationId) =>
        !string.IsNullOrWhiteSpace(operationId) && state.ProcessedOperations.Contains(operationId);

    private static void RememberOperation(TerminalState state, string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;
        if (state.ProcessedOperations.Add(operationId)) state.OperationOrder.Enqueue(operationId);
        while (state.OperationOrder.Count > MaxOperationHistory)
        {
            var stale = state.OperationOrder.Dequeue();
            state.ProcessedOperations.Remove(stale);
        }
    }

    private static SessionDeltaDto BuildDelta(TerminalState state, SessionSnapshotDto snapshot) =>
        new() { SessionId = state.SessionId, TerminalUid = state.TerminalUid, Revision = state.Revision, Snapshot = snapshot };

    private void EmitDeltas(IEnumerable<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas)
    {
        foreach (var delta in deltas)
        {
            try { DeltaReady?.Invoke(delta.Peers, delta.Delta); }
            catch (Exception ex) { _logger.LogError($"Failed to emit session delta: {ex}"); }
        }
    }

    private sealed class TerminalState
    {
        public string SessionId { get; set; } = string.Empty;
        public string TerminalUid { get; set; } = string.Empty;
        public Vector3 AnchorPosition { get; set; }
        public float Radius { get; set; }
        public long Revision { get; set; }
        public int SlotsTotalPhysical { get; set; }
        public int ChestCount { get; set; }
        public List<ChestHandle> Chests { get; set; } = new();
        public HashSet<long> Subscribers { get; } = new();
        public Dictionary<long, long> PeerPlayerIds { get; } = new();
        public Dictionary<string, ReservationRecord> Reservations { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ProcessedOperations { get; } = new(StringComparer.Ordinal);
        public Queue<string> OperationOrder { get; } = new();
        public bool SnapshotDirty { get; set; } = true;
        public float CachedSnapshotAt { get; set; }
        public SessionSnapshotDto? CachedSnapshot { get; set; }
    }

    private sealed class ReservationRecord
    {
        public string TokenId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public long PeerId { get; set; }
        public ItemKey Key { get; set; }
        public int Amount { get; set; }
        public float ExpiresAt { get; set; }
    }
}
