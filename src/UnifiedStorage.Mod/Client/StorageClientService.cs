using System;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Network;

namespace UnifiedStorage.Mod.Client;

public sealed class StorageClientService
{
    private StorageSnapshot _snapshot = new();
    public event Action<StorageSnapshot>? SnapshotUpdated;
    public event Action<WithdrawResult>? WithdrawProcessed;
    public event Action<StoreResult>? StoreProcessed;

    public StorageSnapshot CurrentSnapshot => _snapshot;

    public void ApplySnapshotPayload(string payload)
    {
        _snapshot = StorageCodec.DecodeSnapshot(payload);
        SnapshotUpdated?.Invoke(_snapshot);
    }

    public void ApplyWithdrawPayload(string payload)
    {
        var decoded = StorageCodec.DecodeWithdrawResponse(payload);
        _snapshot = decoded.Snapshot;
        SnapshotUpdated?.Invoke(_snapshot);
        WithdrawProcessed?.Invoke(decoded.Result);
    }

    public void ApplyStorePayload(string payload)
    {
        var decoded = StorageCodec.DecodeStoreResponse(payload);
        _snapshot = decoded.Snapshot;
        SnapshotUpdated?.Invoke(_snapshot);
        StoreProcessed?.Invoke(decoded.Result);
    }
}
