using System;
using System.Reflection;
using BepInEx.Logging;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Client;
using UnifiedStorage.Mod.Server;

namespace UnifiedStorage.Mod.Network;

public sealed class RpcRoutes
{
    public const string RequestSnapshotRoute = "US_RequestSnapshot";
    public const string SnapshotResponseRoute = "US_SnapshotResponse";
    public const string WithdrawRequestRoute = "US_WithdrawRequest";
    public const string WithdrawResponseRoute = "US_WithdrawResponse";
    public const string StoreRequestRoute = "US_StoreRequest";
    public const string StoreResponseRoute = "US_StoreResponse";

    private readonly StorageServerService _server;
    private readonly StorageClientService _client;
    private readonly ManualLogSource _logger;
    private bool _registered;

    public RpcRoutes(StorageServerService server, StorageClientService client, ManualLogSource logger)
    {
        _server = server;
        _client = client;
        _logger = logger;
    }

    public bool EnsureRegistered()
    {
        if (_registered)
        {
            return true;
        }

        if (ZRoutedRpc.instance == null)
        {
            return false;
        }

        ZRoutedRpc.instance.Register<string>(RequestSnapshotRoute, OnRequestSnapshot);
        ZRoutedRpc.instance.Register<string>(SnapshotResponseRoute, OnSnapshotResponse);
        ZRoutedRpc.instance.Register<string>(WithdrawRequestRoute, OnWithdrawRequest);
        ZRoutedRpc.instance.Register<string>(WithdrawResponseRoute, OnWithdrawResponse);
        ZRoutedRpc.instance.Register<string>(StoreRequestRoute, OnStoreRequest);
        ZRoutedRpc.instance.Register<string>(StoreResponseRoute, OnStoreResponse);
        _registered = true;
        _logger.LogInfo("Unified Storage RPC routes registered.");
        return true;
    }

    public void RequestSnapshot()
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        ZRoutedRpc.instance.InvokeRoutedRPC(peer, RequestSnapshotRoute, string.Empty);
    }

    public void RequestWithdraw(WithdrawRequest request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = StorageCodec.EncodeWithdrawRequest(request);
        ZRoutedRpc.instance.InvokeRoutedRPC(peer, WithdrawRequestRoute, payload);
    }

    public void RequestStore(StoreRequest request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = StorageCodec.EncodeStoreRequest(request);
        ZRoutedRpc.instance.InvokeRoutedRPC(peer, StoreRequestRoute, payload);
    }

    private void OnRequestSnapshot(long sender, string _)
    {
        try
        {
            var response = _server.HandleSnapshotRequest(sender);
            var payload = StorageCodec.EncodeSnapshot(response.Snapshot);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, SnapshotResponseRoute, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Snapshot request failed: {ex}");
        }
    }

    private void OnSnapshotResponse(long _, string payload)
    {
        _client.ApplySnapshotPayload(payload);
    }

    private void OnWithdrawRequest(long sender, string payload)
    {
        try
        {
            var request = StorageCodec.DecodeWithdrawRequest(payload);
            var response = _server.HandleWithdrawRequest(sender, request);
            var responsePayload = StorageCodec.EncodeWithdrawResponse(response.Result, response.Snapshot);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, WithdrawResponseRoute, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Withdraw request failed: {ex}");
        }
    }

    private void OnWithdrawResponse(long _, string payload)
    {
        _client.ApplyWithdrawPayload(payload);
    }

    private void OnStoreRequest(long sender, string payload)
    {
        try
        {
            var request = StorageCodec.DecodeStoreRequest(payload);
            var response = _server.HandleStoreRequest(sender, request);
            var responsePayload = StorageCodec.EncodeStoreResponse(response.Result, response.Snapshot);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, StoreResponseRoute, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Store request failed: {ex}");
        }
    }

    private void OnStoreResponse(long _, string payload)
    {
        _client.ApplyStorePayload(payload);
    }

    private static long ResolveServerPeerId()
    {
        if (ZNet.instance != null)
        {
            var znetMethod = typeof(ZNet).GetMethod("GetServerPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (znetMethod != null)
            {
                var result = znetMethod.Invoke(ZNet.instance, null);
                if (result is long znetPeerId)
                {
                    return znetPeerId;
                }
            }
        }

        var routedMethod = typeof(ZRoutedRpc).GetMethod("GetServerPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (routedMethod != null)
        {
            var result = routedMethod.Invoke(ZRoutedRpc.instance, null);
            if (result is long routedPeerId)
            {
                return routedPeerId;
            }
        }

        var peerField = typeof(ZRoutedRpc).GetField("m_serverPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? typeof(ZRoutedRpc).GetField("m_serverUID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (peerField != null)
        {
            var value = peerField.GetValue(ZRoutedRpc.instance);
            if (value is long fieldPeerId)
            {
                return fieldPeerId;
            }
        }

        return 0L;
    }
}
