using System;
using System.Collections.Generic;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Server;

public sealed class StorageServerService
{
    private readonly StorageConfig _config;
    private readonly IContainerScanner _containerScanner;
    private readonly IItemAggregator _itemAggregator;
    private readonly IWithdrawService _withdrawService;
    private readonly IStoreService _storeService;
    private readonly SnapshotBuildContext _buildContext = new();
    private readonly object _sync = new();
    private long _revision;

    public StorageServerService(
        StorageConfig config,
        IContainerScanner containerScanner,
        IItemAggregator itemAggregator,
        IWithdrawService withdrawService,
        IStoreService storeService)
    {
        _config = config;
        _containerScanner = containerScanner;
        _itemAggregator = itemAggregator;
        _withdrawService = withdrawService;
        _storeService = storeService;
    }

    public SnapshotResponseDto HandleSnapshotRequest(long sender)
    {
        lock (_sync)
        {
            var player = FindPlayer(sender);
            if (player == null)
            {
                return new SnapshotResponseDto { Snapshot = EmptySnapshot() };
            }

            var containers = _containerScanner.GetNearbyContainers(player.transform.position, _config.ScanRadius.Value);
            var snapshot = _itemAggregator.BuildSnapshot(containers, _revision, _buildContext);
            return new SnapshotResponseDto { Snapshot = snapshot };
        }
    }

    public WithdrawResponseDto HandleWithdrawRequest(long sender, WithdrawRequest request)
    {
        lock (_sync)
        {
            var player = FindPlayer(sender);
            if (player == null)
            {
                return FailedResponse("Player not found");
            }

            var containers = _containerScanner.GetNearbyContainers(player.transform.position, _config.ScanRadius.Value);
            var result = _withdrawService.Withdraw(
                player,
                containers,
                request,
                _revision + 1,
                _config.RequireAccessCheck.Value);

            if (result.Success)
            {
                _revision = result.Revision;
            }

            var snapshot = _itemAggregator.BuildSnapshot(containers, _revision, _buildContext);
            return new WithdrawResponseDto
            {
                Result = result,
                Snapshot = snapshot
            };
        }
    }

    public StoreResponseDto HandleStoreRequest(long sender, StoreRequest request)
    {
        lock (_sync)
        {
            var player = FindPlayer(sender);
            if (player == null)
            {
                return FailedStoreResponse("Player not found");
            }

            var containers = _containerScanner.GetNearbyContainers(player.transform.position, _config.ScanRadius.Value);
            var result = _storeService.Store(
                player,
                containers,
                request,
                _revision + 1,
                _config.RequireAccessCheck.Value);

            if (result.Success)
            {
                _revision = result.Revision;
            }

            var snapshot = _itemAggregator.BuildSnapshot(containers, _revision, _buildContext);
            return new StoreResponseDto
            {
                Result = result,
                Snapshot = snapshot
            };
        }
    }

    private static Player? FindPlayer(long sender)
    {
        foreach (var player in Player.GetAllPlayers())
        {
            if (player.GetPlayerID() == sender)
            {
                return player;
            }
        }

        if (Player.m_localPlayer != null)
        {
            return Player.m_localPlayer;
        }

        return null;
    }

    private static WithdrawResponseDto FailedResponse(string reason)
    {
        return new WithdrawResponseDto
        {
            Result = new WithdrawResult
            {
                Success = false,
                Reason = reason,
                Withdrawn = 0
            },
            Snapshot = EmptySnapshot()
        };
    }

    private static StoreResponseDto FailedStoreResponse(string reason)
    {
        return new StoreResponseDto
        {
            Result = new StoreResult
            {
                Success = false,
                Reason = reason,
                Stored = 0
            },
            Snapshot = EmptySnapshot()
        };
    }

    private static StorageSnapshot EmptySnapshot()
    {
        return new StorageSnapshot
        {
            Revision = 0,
            Items = new List<AggregatedItem>()
        };
    }
}
