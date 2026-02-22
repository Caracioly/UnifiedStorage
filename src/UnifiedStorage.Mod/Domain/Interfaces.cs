using System.Collections.Generic;
using UnityEngine;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Domain;

public interface IContainerScanner
{
    IReadOnlyList<ChestHandle> GetNearbyContainers(Vector3 center, float radius, Container? ignoreContainer = null, bool onlyVanillaChests = false);
}

public interface IItemAggregator
{
    StorageSnapshot BuildSnapshot(IReadOnlyList<ChestHandle> containers, long revision, SnapshotBuildContext context);
}

public interface IWithdrawService
{
    WithdrawResult Withdraw(
        Player player,
        IReadOnlyList<ChestHandle> containers,
        WithdrawRequest request,
        long revision,
        bool requireAccessCheck);
}

public interface IStoreService
{
    StoreResult Store(
        Player player,
        IReadOnlyList<ChestHandle> containers,
        StoreRequest request,
        long revision,
        bool requireAccessCheck);
}
