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
using UnityEngine;

namespace UnifiedStorage.Mod.Server;

public sealed class TerminalAuthorityService
{
    private const float ReservationTtlSeconds = 3f;
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
            if (_states.Count == 0)
            {
                return;
            }

            var now = Time.unscaledTime;
            foreach (var state in _states.Values.ToList())
            {
                state.Subscribers.RemoveWhere(peer => !IsPeerConnected(peer));
                foreach (var stalePeer in state.PeerPlayerIds.Keys.Where(peer => !IsPeerConnected(peer)).ToList())
                {
                    state.PeerPlayerIds.Remove(stalePeer);
                }

                var expiredOrDisconnected = state.Reservations.Values
                    .Where(r => r.ExpiresAt <= now || !IsPeerConnected(r.PeerId))
                    .ToList();

                var anyChange = false;
                foreach (var reservation in expiredOrDisconnected)
                {
                    var restored = AddToChests(state, reservation.Key, reservation.Amount, null);
                    var unresolved = reservation.Amount - restored;
                    if (unresolved > 0)
                    {
                        DropNearTerminal(state, reservation.Key, unresolved);
                    }

                    state.Reservations.Remove(reservation.TokenId);
                    anyChange = anyChange || restored > 0 || unresolved > 0;
                }

                if (anyChange)
                {
                    state.Revision++;
                    var snapshot = BuildSnapshot(state);
                    deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));
                }

                if (state.Subscribers.Count == 0 && state.Reservations.Count == 0)
                {
                    _states.Remove(state.TerminalUid);
                }
            }
        }

        EmitDeltas(deltas);
    }

    public OpenSessionResponseDto HandleOpenSession(long sender, OpenSessionRequestDto request)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(request.TerminalUid))
            {
                return new OpenSessionResponseDto
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Reason = "Invalid terminal",
                    Snapshot = new SessionSnapshotDto()
                };
            }

            var state = GetOrCreateState(request);
            if (!TryResolveAndBindPlayerId(state, sender, request.PlayerId, out var resolvedPlayerId))
            {
                return new OpenSessionResponseDto
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Reason = "Player identity mismatch",
                    Snapshot = new SessionSnapshotDto()
                };
            }

            if (resolvedPlayerId <= 0)
            {
                return new OpenSessionResponseDto
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Reason = "Unable to resolve player identity",
                    Snapshot = new SessionSnapshotDto()
                };
            }

            state.Subscribers.Add(sender);

            var snapshot = BuildSnapshot(state);
            return new OpenSessionResponseDto
            {
                RequestId = request.RequestId,
                Success = true,
                Snapshot = snapshot
            };
        }
    }

    public ReserveWithdrawResultDto HandleReserveWithdraw(long sender, ReserveWithdrawRequestDto request)
    {
        List<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas = new();
        ReserveWithdrawResultDto result;
        lock (_sync)
        {
            var state = GetState(request.TerminalUid);
            if (state == null)
            {
                return ReserveFailure(request, "Session not found");
            }

            if (IsDuplicateOperation(state, request.OperationId))
            {
                result = new ReserveWithdrawResultDto
                {
                    RequestId = request.RequestId,
                    Success = true,
                    Reason = "Duplicate operation ignored",
                    Key = request.Key,
                    ReservedAmount = 0,
                    Revision = state.Revision,
                    Snapshot = BuildSnapshot(state)
                };
                return result;
            }

            if (request.ExpectedRevision >= 0 && request.ExpectedRevision != state.Revision)
            {
                result = ReserveFailure(request, "Conflict", BuildSnapshot(state), state.Revision);
                return result;
            }

            if (!TryGetMappedPlayerId(state, sender, request.PlayerId, out var playerId))
            {
                result = ReserveFailure(request, "Player identity mismatch", BuildSnapshot(state), state.Revision);
                return result;
            }

            var player = FindPlayerById(playerId);
            var removed = RemoveFromChests(state, request.Key, request.Amount, player);
            if (removed <= 0)
            {
                RememberOperation(state, request.OperationId);
                result = ReserveFailure(request, "Not enough items", BuildSnapshot(state), state.Revision);
                return result;
            }

            var token = Guid.NewGuid().ToString("N");
            state.Reservations[token] = new ReservationRecord
            {
                TokenId = token,
                PeerId = sender,
                SessionId = request.SessionId ?? string.Empty,
                Key = request.Key,
                Amount = removed,
                ExpiresAt = Time.unscaledTime + ReservationTtlSeconds
            };

            state.Revision++;
            RememberOperation(state, request.OperationId);
            var snapshot = BuildSnapshot(state);
            deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));

            result = new ReserveWithdrawResultDto
            {
                RequestId = request.RequestId,
                Success = true,
                TokenId = token,
                Key = request.Key,
                ReservedAmount = removed,
                Revision = state.Revision,
                Snapshot = snapshot
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
            if (state == null)
            {
                return ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Session not found", operationType: "commit");
            }

            if (IsDuplicateOperation(state, request.OperationId))
            {
                return new ApplyResultDto
                {
                    RequestId = request.RequestId,
                    Success = true,
                    TokenId = request.TokenId ?? string.Empty,
                    Reason = "Duplicate operation ignored",
                    OperationType = "commit",
                    Revision = state.Revision,
                    Snapshot = BuildSnapshot(state)
                };
            }

            if (!state.Reservations.TryGetValue(request.TokenId ?? string.Empty, out var reservation))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Reservation not found", BuildSnapshot(state), state.Revision, "commit");
            }

            if (reservation.PeerId != sender)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Reservation owner mismatch", BuildSnapshot(state), state.Revision, "commit");
            }

            var applied = reservation.Amount;
            state.Reservations.Remove(reservation.TokenId);
            RememberOperation(state, request.OperationId);

            return new ApplyResultDto
            {
                RequestId = request.RequestId,
                Success = true,
                OperationType = "commit",
                TokenId = reservation.TokenId,
                AppliedAmount = applied,
                Revision = state.Revision,
                Snapshot = BuildSnapshot(state)
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
            if (state == null)
            {
                return ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Session not found", operationType: "cancel");
            }

            if (IsDuplicateOperation(state, request.OperationId))
            {
                return new ApplyResultDto
                {
                    RequestId = request.RequestId,
                    Success = true,
                    TokenId = request.TokenId ?? string.Empty,
                    Reason = "Duplicate operation ignored",
                    OperationType = "cancel",
                    Revision = state.Revision,
                    Snapshot = BuildSnapshot(state)
                };
            }

            if (!state.Reservations.TryGetValue(request.TokenId ?? string.Empty, out var reservation))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Reservation not found", BuildSnapshot(state), state.Revision, "cancel");
            }

            if (reservation.PeerId != sender)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Reservation owner mismatch", BuildSnapshot(state), state.Revision, "cancel");
            }

            var cancelAmount = request.Amount > 0 ? Math.Min(request.Amount, reservation.Amount) : reservation.Amount;
            if (!TryGetMappedPlayerId(state, sender, request.PlayerId, out var playerId))
            {
                RememberOperation(state, request.OperationId);
                result = ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Player identity mismatch", BuildSnapshot(state), state.Revision, "cancel");
                return result;
            }

            var player = FindPlayerById(playerId);
            var restored = AddToChests(state, reservation.Key, cancelAmount, player);
            var unresolved = cancelAmount - restored;
            if (unresolved > 0)
            {
                DropNearTerminal(state, reservation.Key, unresolved);
            }

            reservation.Amount -= cancelAmount;
            if (reservation.Amount <= 0)
            {
                state.Reservations.Remove(reservation.TokenId);
            }
            else
            {
                state.Reservations[reservation.TokenId] = reservation;
            }

            if (restored > 0 || unresolved > 0)
            {
                state.Revision++;
                var snapshot = BuildSnapshot(state);
                deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));
                RememberOperation(state, request.OperationId);
                result = new ApplyResultDto
                {
                    RequestId = request.RequestId,
                    Success = true,
                    OperationType = "cancel",
                    TokenId = request.TokenId ?? string.Empty,
                    AppliedAmount = cancelAmount,
                    Revision = state.Revision,
                    Snapshot = snapshot
                };
                goto EmitAndReturn;
            }

            RememberOperation(state, request.OperationId);
            result = ApplyFailure(request.RequestId, request.TokenId ?? string.Empty, "Nothing restored", BuildSnapshot(state), state.Revision, "cancel");
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
            if (state == null)
            {
                return ApplyFailure(request.RequestId, string.Empty, "Session not found", operationType: "deposit");
            }

            if (IsDuplicateOperation(state, request.OperationId))
            {
                return new ApplyResultDto
                {
                    RequestId = request.RequestId,
                    Success = true,
                    Reason = "Duplicate operation ignored",
                    OperationType = "deposit",
                    Revision = state.Revision,
                    Snapshot = BuildSnapshot(state)
                };
            }

            if (request.ExpectedRevision >= 0 && request.ExpectedRevision != state.Revision)
            {
                return ApplyFailure(request.RequestId, string.Empty, "Conflict", BuildSnapshot(state), state.Revision, "deposit");
            }

            if (!TryGetMappedPlayerId(state, sender, request.PlayerId, out var playerId))
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, string.Empty, "Player identity mismatch", BuildSnapshot(state), state.Revision, "deposit");
            }

            var player = FindPlayerById(playerId);
            if (player == null)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, string.Empty, "Player not found", BuildSnapshot(state), state.Revision, "deposit");
            }

            var removedFromPlayer = RemoveFromPlayerInventory(player, request.Key, request.Amount);
            if (removedFromPlayer <= 0)
            {
                RememberOperation(state, request.OperationId);
                return ApplyFailure(request.RequestId, string.Empty, "Player has no matching item", BuildSnapshot(state), state.Revision, "deposit");
            }

            var stored = AddToChests(state, request.Key, removedFromPlayer, player);
            var notStored = removedFromPlayer - stored;
            if (notStored > 0)
            {
                var restored = AddToPlayerInventory(player, request.Key, notStored);
                var unresolved = notStored - restored;
                if (unresolved > 0)
                {
                    DropNearTerminal(state, request.Key, unresolved);
                }
            }

            if (stored > 0)
            {
                state.Revision++;
            }

            RememberOperation(state, request.OperationId);
            var snapshot = BuildSnapshot(state);
            if (stored > 0)
            {
                deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));
            }

            result = new ApplyResultDto
            {
                RequestId = request.RequestId,
                Success = stored > 0,
                Reason = stored > 0 ? string.Empty : "No storage space",
                OperationType = "deposit",
                AppliedAmount = stored,
                Revision = state.Revision,
                Snapshot = snapshot
            };
        }

        EmitDeltas(deltas);
        return result;
    }

    public void HandleCloseSession(long sender, CloseSessionRequestDto request)
    {
        List<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas = new();
        lock (_sync)
        {
            var state = GetState(request.TerminalUid);
            if (state == null)
            {
                return;
            }

            state.PeerPlayerIds.TryGetValue(sender, out var mappedPlayerId);
            state.Subscribers.Remove(sender);
            state.PeerPlayerIds.Remove(sender);

            var cancelledReservations = state.Reservations.Values
                .Where(r => r.PeerId == sender)
                .ToList();

            var effectivePlayerId = mappedPlayerId > 0 ? mappedPlayerId : request.PlayerId;
            var player = FindPlayerById(effectivePlayerId);
            var anyChanged = false;
            foreach (var reservation in cancelledReservations)
            {
                var restored = AddToChests(state, reservation.Key, reservation.Amount, player);
                var unresolved = reservation.Amount - restored;
                if (unresolved > 0)
                {
                    DropNearTerminal(state, reservation.Key, unresolved);
                }

                state.Reservations.Remove(reservation.TokenId);
                anyChanged = anyChanged || restored > 0 || unresolved > 0;
            }

            if (anyChanged)
            {
                state.Revision++;
                var snapshot = BuildSnapshot(state);
                deltas.Add((state.Subscribers.ToList(), BuildDelta(state, snapshot)));
            }

            if (state.Subscribers.Count == 0 && state.Reservations.Count == 0)
            {
                _states.Remove(state.TerminalUid);
            }
        }

        EmitDeltas(deltas);
    }

    private static ReserveWithdrawResultDto ReserveFailure(
        ReserveWithdrawRequestDto request,
        string reason,
        SessionSnapshotDto? snapshot = null,
        long revision = 0)
    {
        return new ReserveWithdrawResultDto
        {
            RequestId = request.RequestId,
            Success = false,
            Reason = reason,
            Key = request.Key,
            ReservedAmount = 0,
            Revision = revision,
            Snapshot = snapshot ?? new SessionSnapshotDto()
        };
    }

    private static ApplyResultDto ApplyFailure(
        string requestId,
        string tokenId,
        string reason,
        SessionSnapshotDto? snapshot = null,
        long revision = 0,
        string operationType = "")
    {
        return new ApplyResultDto
        {
            RequestId = requestId,
            Success = false,
            Reason = reason,
            OperationType = operationType ?? string.Empty,
            TokenId = tokenId ?? string.Empty,
            Revision = revision,
            Snapshot = snapshot ?? new SessionSnapshotDto()
        };
    }

    private TerminalState GetOrCreateState(OpenSessionRequestDto request)
    {
        if (_states.TryGetValue(request.TerminalUid, out var existing))
        {
            existing.AnchorPosition = new Vector3(request.AnchorX, request.AnchorY, request.AnchorZ);
            existing.Radius = request.Radius;

            return existing;
        }

        var created = new TerminalState
        {
            TerminalUid = request.TerminalUid,
            SessionId = Guid.NewGuid().ToString("N"),
            AnchorPosition = new Vector3(request.AnchorX, request.AnchorY, request.AnchorZ),
            Radius = request.Radius
        };
        _states[request.TerminalUid] = created;
        return created;
    }

    private TerminalState? GetState(string terminalUid)
    {
        if (string.IsNullOrWhiteSpace(terminalUid))
        {
            return null;
        }

        return _states.TryGetValue(terminalUid, out var state) ? state : null;
    }

    private SessionSnapshotDto BuildSnapshot(TerminalState state)
    {
        RefreshChestHandles(state);

        var totals = new Dictionary<ItemKey, int>();
        var prototypes = new Dictionary<ItemKey, ItemDrop.ItemData>();
        var slotsTotal = 0;
        foreach (var chest in state.Chests)
        {
            var inventory = chest.Container.GetInventory();
            if (inventory == null)
            {
                continue;
            }

            slotsTotal += GetInventoryWidth(inventory) * GetInventoryHeight(inventory);
            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack <= 0)
                {
                    continue;
                }

                var key = new ItemKey(item.m_dropPrefab.name, item.m_quality, item.m_variant);
                totals[key] = totals.TryGetValue(key, out var existing) ? existing + item.m_stack : item.m_stack;
                if (!prototypes.ContainsKey(key))
                {
                    prototypes[key] = item.Clone();
                }
            }
        }

        var items = totals
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => new
            {
                Key = kvp.Key,
                Total = kvp.Value,
                Display = GetDisplayName(kvp.Key, prototypes),
                Type = GetItemTypeOrder(kvp.Key, prototypes),
                Subgroup = GetSubgroupOrder(kvp.Key)
            })
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Subgroup)
            .ThenBy(x => x.Display, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.Total)
            .Select(x => new AggregatedItem
            {
                Key = x.Key,
                DisplayName = x.Display,
                TotalAmount = x.Total,
                SourceCount = CountSourcesForKey(state.Chests, x.Key),
                StackSize = 999
            })
            .ToList();

        state.ChestCount = state.Chests.Count;
        state.SlotsTotalPhysical = slotsTotal;

        return new SessionSnapshotDto
        {
            SessionId = state.SessionId,
            TerminalUid = state.TerminalUid,
            Revision = state.Revision,
            SlotsTotalPhysical = slotsTotal,
            SlotsUsedVirtual = items.Sum(i => (int)Math.Ceiling(i.TotalAmount / 999f)),
            ChestCount = state.ChestCount,
            Items = items
        };
    }

    private void RefreshChestHandles(TerminalState state)
    {
        var terminal = FindTerminalByUid(state.TerminalUid);
        if (terminal != null)
        {
            state.AnchorPosition = terminal.transform.position;
        }

        state.Chests = _scanner
            .GetNearbyContainers(state.AnchorPosition, state.Radius, terminal, onlyVanillaChests: true)
            .ToList();
    }

    private static int CountSourcesForKey(IEnumerable<ChestHandle> chests, ItemKey key)
    {
        var count = 0;
        foreach (var chest in chests)
        {
            var inv = chest.Container.GetInventory();
            if (inv == null)
            {
                continue;
            }

            if (inv.GetAllItems().Any(item => MatchKey(item, key)))
            {
                count++;
            }
        }

        return count;
    }

    private int RemoveFromChests(TerminalState state, ItemKey key, int amount, Player? player)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var remaining = amount;
        foreach (var chest in state.Chests.OrderBy(c => c.Distance).ThenBy(c => c.SourceId, StringComparer.Ordinal))
        {
            if (remaining <= 0)
            {
                break;
            }

            var inv = chest.Container.GetInventory();
            if (inv == null)
            {
                continue;
            }

            if (_config.RequireAccessCheck.Value && player != null && !CanAccess(chest.Container, player))
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

    private int AddToChests(TerminalState state, ItemKey key, int amount, Player? player)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var remaining = amount;
        foreach (var chest in state.Chests.OrderBy(c => c.Distance).ThenBy(c => c.SourceId, StringComparer.Ordinal))
        {
            if (remaining <= 0)
            {
                break;
            }

            var inv = chest.Container.GetInventory();
            if (inv == null)
            {
                continue;
            }

            if (_config.RequireAccessCheck.Value && player != null && !CanAccess(chest.Container, player))
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

    private static int RemoveFromPlayerInventory(Player player, ItemKey key, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var removed = 0;
        var remaining = amount;
        var inventory = player.GetInventory();
        foreach (var item in inventory.GetAllItems().Where(i => MatchKey(i, key)).ToList())
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(item.m_stack, remaining);
            inventory.RemoveItem(item, take);
            removed += take;
            remaining -= take;
        }

        return removed;
    }

    private int AddToPlayerInventory(Player player, ItemKey key, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var added = 0;
        var remaining = amount;
        var inventory = player.GetInventory();
        while (remaining > 0)
        {
            var stackAmount = Math.Min(999, remaining);
            var stack = CreateItemStack(key, stackAmount);
            if (stack == null)
            {
                break;
            }

            if (inventory.AddItem(stack))
            {
                added += stackAmount;
                remaining -= stackAmount;
                continue;
            }

            var movedChunk = 0;
            var chunk = Math.Max(1, stackAmount / 2);
            while (chunk >= 1)
            {
                var smaller = CreateItemStack(key, chunk);
                if (smaller != null && inventory.AddItem(smaller))
                {
                    movedChunk = chunk;
                    break;
                }

                chunk /= 2;
            }

            if (movedChunk <= 0)
            {
                break;
            }

            added += movedChunk;
            remaining -= movedChunk;
        }

        return added;
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

        var parameters = method.GetParameters();
        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(long))
        {
            return (bool)method.Invoke(container, new object[] { player.GetPlayerID() });
        }

        if (parameters.Length == 0)
        {
            return (bool)method.Invoke(container, null);
        }

        return true;
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

    private static string GetDisplayName(ItemKey key, IReadOnlyDictionary<ItemKey, ItemDrop.ItemData> prototypes)
    {
        if (prototypes.TryGetValue(key, out var item))
        {
            return item.m_shared.m_name;
        }

        var prefab = ObjectDB.instance?.GetItemPrefab(key.PrefabName);
        var drop = prefab?.GetComponent<ItemDrop>();
        if (drop?.m_itemData != null)
        {
            return drop.m_itemData.m_shared.m_name;
        }

        return key.PrefabName;
    }

    private static int GetItemTypeOrder(ItemKey key, IReadOnlyDictionary<ItemKey, ItemDrop.ItemData> prototypes)
    {
        if (prototypes.TryGetValue(key, out var item))
        {
            return (int)item.m_shared.m_itemType;
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

    private static Player? FindPlayerById(long playerId)
    {
        if (playerId <= 0)
        {
            return Player.m_localPlayer;
        }

        foreach (var player in Player.GetAllPlayers())
        {
            if (player != null && player.GetPlayerID() == playerId)
            {
                return player;
            }
        }

        if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
        {
            return Player.m_localPlayer;
        }

        return null;
    }

    private static bool TryResolveAndBindPlayerId(TerminalState state, long sender, long requestedPlayerId, out long resolvedPlayerId)
    {
        resolvedPlayerId = 0;
        var runtimePlayerId = ResolvePlayerIdFromPeer(sender);
        if (runtimePlayerId > 0)
        {
            if (requestedPlayerId > 0 && requestedPlayerId != runtimePlayerId)
            {
                return false;
            }

            state.PeerPlayerIds[sender] = runtimePlayerId;
            resolvedPlayerId = runtimePlayerId;
            return true;
        }

        if (state.PeerPlayerIds.TryGetValue(sender, out var mapped))
        {
            if (requestedPlayerId > 0 && requestedPlayerId != mapped)
            {
                return false;
            }

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
        if (!state.PeerPlayerIds.TryGetValue(sender, out var mapped))
        {
            return false;
        }

        if (requestedPlayerId > 0 && requestedPlayerId != mapped)
        {
            return false;
        }

        playerId = mapped;
        return true;
    }

    private static long ResolvePlayerIdFromPeer(long sender)
    {
        if (sender <= 0)
        {
            return Player.m_localPlayer?.GetPlayerID() ?? 0L;
        }

        foreach (var player in Player.GetAllPlayers())
        {
            if (player == null)
            {
                continue;
            }

            var nview = player.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo == null)
            {
                continue;
            }

            var zdoType = zdo.GetType();
            var getOwnerMethod = zdoType.GetMethod("GetOwner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getOwnerMethod != null && getOwnerMethod.Invoke(zdo, null) is long ownerByMethod && ownerByMethod == sender)
            {
                return player.GetPlayerID();
            }

            var ownerField = zdoType.GetField("m_owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ownerField != null && ownerField.GetValue(zdo) is long ownerByField && ownerByField == sender)
            {
                return player.GetPlayerID();
            }
        }

        return 0L;
    }

    private static bool IsPeerConnected(long peerId)
    {
        if (peerId <= 0 || ZNet.instance == null)
        {
            return true;
        }

        var getPeerMethod = typeof(ZNet).GetMethod("GetPeer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(long) }, null);
        if (getPeerMethod != null)
        {
            var peer = getPeerMethod.Invoke(ZNet.instance, new object[] { peerId });
            if (peer != null)
            {
                return true;
            }
        }

        var peersField = typeof(ZNet).GetField("m_peers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (peersField?.GetValue(ZNet.instance) is System.Collections.IEnumerable peers)
        {
            foreach (var peer in peers)
            {
                if (peer == null)
                {
                    continue;
                }

                var uidField = peer.GetType().GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (uidField?.GetValue(peer) is long uid && uid == peerId)
                {
                    return true;
                }
            }
        }

        return false;
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

    private static Container? FindTerminalByUid(string terminalUid)
    {
        if (string.IsNullOrWhiteSpace(terminalUid))
        {
            return null;
        }

        foreach (var container in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
        {
            if (container == null || !UnifiedChestTerminalMarker.IsTerminalContainer(container))
            {
                continue;
            }

            if (string.Equals(BuildContainerUid(container), terminalUid, StringComparison.Ordinal))
            {
                return container;
            }
        }

        return null;
    }

    private ItemDrop.ItemData? CreateItemStack(ItemKey key, int amount)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(key.PrefabName);
        var drop = prefab?.GetComponent<ItemDrop>();
        if (drop?.m_itemData == null)
        {
            return null;
        }

        var clone = drop.m_itemData.Clone();
        clone.m_quality = key.Quality;
        clone.m_variant = key.Variant;
        clone.m_stack = amount;
        clone.m_shared.m_maxStackSize = Math.Max(clone.m_shared.m_maxStackSize, 999);
        return clone;
    }

    private void DropNearTerminal(TerminalState state, ItemKey key, int amount)
    {
        var remaining = amount;
        while (remaining > 0)
        {
            var stackAmount = Math.Min(999, remaining);
            var stack = CreateItemStack(key, stackAmount);
            if (stack == null)
            {
                break;
            }

            var pos = state.AnchorPosition + Vector3.up;
            var rot = Quaternion.identity;
            if (!TryDropItem(stack, stackAmount, pos, rot))
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
                // Try next known signature.
            }
        }

        return false;
    }

    private static bool IsDuplicateOperation(TerminalState state, string operationId)
    {
        return !string.IsNullOrWhiteSpace(operationId) && state.ProcessedOperations.Contains(operationId);
    }

    private static void RememberOperation(TerminalState state, string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return;
        }

        if (state.ProcessedOperations.Add(operationId))
        {
            state.OperationOrder.Enqueue(operationId);
        }

        while (state.OperationOrder.Count > MaxOperationHistory)
        {
            var stale = state.OperationOrder.Dequeue();
            state.ProcessedOperations.Remove(stale);
        }
    }

    private static SessionDeltaDto BuildDelta(TerminalState state, SessionSnapshotDto snapshot)
    {
        return new SessionDeltaDto
        {
            SessionId = state.SessionId,
            TerminalUid = state.TerminalUid,
            Revision = state.Revision,
            Snapshot = snapshot
        };
    }

    private void EmitDeltas(IEnumerable<(IReadOnlyCollection<long> Peers, SessionDeltaDto Delta)> deltas)
    {
        foreach (var delta in deltas)
        {
            try
            {
                DeltaReady?.Invoke(delta.Peers, delta.Delta);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to emit session delta: {ex}");
            }
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
