using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnifiedStorage.Mod.Models;
using UnifiedStorage.Mod.Server;

namespace UnifiedStorage.Mod.Network;

public sealed class TerminalRpcRoutes
{
    public const string OpenSessionRoute = "US_OpenSession";
    public const string SessionSnapshotRoute = "US_SessionSnapshot";
    public const string ReserveWithdrawRoute = "US_ReserveWithdraw";
    public const string ReserveResultRoute = "US_ReserveResult";
    public const string CommitReservationRoute = "US_CommitReservation";
    public const string CancelReservationRoute = "US_CancelReservation";
    public const string DepositRequestRoute = "US_DepositRequest";
    public const string ApplyResultRoute = "US_ApplyResult";
    public const string CloseSessionRoute = "US_CloseSession";
    public const string SessionDeltaRoute = "US_SessionDelta";

    private readonly TerminalAuthorityService _authority;
    private readonly ManualLogSource _logger;
    private bool _registered;

    public TerminalRpcRoutes(TerminalAuthorityService authority, ManualLogSource logger)
    {
        _authority = authority;
        _logger = logger;
        _authority.DeltaReady += OnAuthorityDeltaReady;
    }

    public event Action<OpenSessionResponseDto>? SessionSnapshotReceived;
    public event Action<ReserveWithdrawResultDto>? ReserveResultReceived;
    public event Action<ApplyResultDto>? ApplyResultReceived;
    public event Action<SessionDeltaDto>? SessionDeltaReceived;

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

        ZRoutedRpc.instance.Register<string>(OpenSessionRoute, OnOpenSessionRequest);
        ZRoutedRpc.instance.Register<string>(SessionSnapshotRoute, OnSessionSnapshot);
        ZRoutedRpc.instance.Register<string>(ReserveWithdrawRoute, OnReserveWithdrawRequest);
        ZRoutedRpc.instance.Register<string>(ReserveResultRoute, OnReserveResult);
        ZRoutedRpc.instance.Register<string>(CommitReservationRoute, OnCommitReservationRequest);
        ZRoutedRpc.instance.Register<string>(CancelReservationRoute, OnCancelReservationRequest);
        ZRoutedRpc.instance.Register<string>(DepositRequestRoute, OnDepositRequest);
        ZRoutedRpc.instance.Register<string>(ApplyResultRoute, OnApplyResult);
        ZRoutedRpc.instance.Register<string>(CloseSessionRoute, OnCloseSessionRequest);
        ZRoutedRpc.instance.Register<string>(SessionDeltaRoute, OnSessionDelta);
        _registered = true;
        _logger.LogInfo("Unified Storage terminal RPC routes registered.");
        return true;
    }

    public void RequestOpenSession(OpenSessionRequestDto request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = TerminalCodec.EncodeOpenSessionRequest(request);
        InvokeToPeer(peer, OpenSessionRoute, payload);
    }

    public void RequestReserveWithdraw(ReserveWithdrawRequestDto request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = TerminalCodec.EncodeReserveWithdrawRequest(request);
        InvokeToPeer(peer, ReserveWithdrawRoute, payload);
    }

    public void RequestCommitReservation(CommitReservationRequestDto request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = TerminalCodec.EncodeCommitReservationRequest(request);
        InvokeToPeer(peer, CommitReservationRoute, payload);
    }

    public void RequestCancelReservation(CancelReservationRequestDto request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = TerminalCodec.EncodeCancelReservationRequest(request);
        InvokeToPeer(peer, CancelReservationRoute, payload);
    }

    public void RequestDeposit(DepositRequestDto request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = TerminalCodec.EncodeDepositRequest(request);
        InvokeToPeer(peer, DepositRequestRoute, payload);
    }

    public void RequestCloseSession(CloseSessionRequestDto request)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var peer = ResolveServerPeerId();
        var payload = TerminalCodec.EncodeCloseSessionRequest(request);
        InvokeToPeer(peer, CloseSessionRoute, payload);
    }

    private void OnAuthorityDeltaReady(IReadOnlyCollection<long> peers, SessionDeltaDto delta)
    {
        if (!EnsureRegistered())
        {
            return;
        }

        var payload = TerminalCodec.EncodeSessionDelta(delta);
        foreach (var peer in peers)
        {
            InvokeToPeer(peer, SessionDeltaRoute, payload);
        }
    }

    private void OnOpenSessionRequest(long sender, string payload)
    {
        if (!IsServer())
        {
            return;
        }

        try
        {
            var request = TerminalCodec.DecodeOpenSessionRequest(payload);
            var response = _authority.HandleOpenSession(sender, request);
            var responsePayload = TerminalCodec.EncodeOpenSessionResponse(response);
            InvokeToPeer(sender, SessionSnapshotRoute, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Open session request failed: {ex}");
        }
    }

    private void OnReserveWithdrawRequest(long sender, string payload)
    {
        if (!IsServer())
        {
            return;
        }

        try
        {
            var request = TerminalCodec.DecodeReserveWithdrawRequest(payload);
            var result = _authority.HandleReserveWithdraw(sender, request);
            var responsePayload = TerminalCodec.EncodeReserveWithdrawResult(result);
            InvokeToPeer(sender, ReserveResultRoute, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Reserve withdraw request failed: {ex}");
        }
    }

    private void OnCommitReservationRequest(long sender, string payload)
    {
        if (!IsServer())
        {
            return;
        }

        try
        {
            var request = TerminalCodec.DecodeCommitReservationRequest(payload);
            var result = _authority.HandleCommitReservation(sender, request);
            var responsePayload = TerminalCodec.EncodeApplyResult(result);
            InvokeToPeer(sender, ApplyResultRoute, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Commit reservation request failed: {ex}");
        }
    }

    private void OnCancelReservationRequest(long sender, string payload)
    {
        if (!IsServer())
        {
            return;
        }

        try
        {
            var request = TerminalCodec.DecodeCancelReservationRequest(payload);
            var result = _authority.HandleCancelReservation(sender, request);
            var responsePayload = TerminalCodec.EncodeApplyResult(result);
            InvokeToPeer(sender, ApplyResultRoute, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Cancel reservation request failed: {ex}");
        }
    }

    private void OnDepositRequest(long sender, string payload)
    {
        if (!IsServer())
        {
            return;
        }

        try
        {
            var request = TerminalCodec.DecodeDepositRequest(payload);
            var result = _authority.HandleDeposit(sender, request);
            var responsePayload = TerminalCodec.EncodeApplyResult(result);
            InvokeToPeer(sender, ApplyResultRoute, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Deposit request failed: {ex}");
        }
    }

    private void OnCloseSessionRequest(long sender, string payload)
    {
        if (!IsServer())
        {
            return;
        }

        try
        {
            var request = TerminalCodec.DecodeCloseSessionRequest(payload);
            _authority.HandleCloseSession(sender, request);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Close session request failed: {ex}");
        }
    }

    private void OnSessionSnapshot(long _, string payload)
    {
        try
        {
            var dto = TerminalCodec.DecodeOpenSessionResponse(payload);
            SessionSnapshotReceived?.Invoke(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Session snapshot decode failed: {ex}");
        }
    }

    private void OnReserveResult(long _, string payload)
    {
        try
        {
            var dto = TerminalCodec.DecodeReserveWithdrawResult(payload);
            ReserveResultReceived?.Invoke(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Reserve result decode failed: {ex}");
        }
    }

    private void OnApplyResult(long _, string payload)
    {
        try
        {
            var dto = TerminalCodec.DecodeApplyResult(payload);
            ApplyResultReceived?.Invoke(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Apply result decode failed: {ex}");
        }
    }

    private void OnSessionDelta(long _, string payload)
    {
        try
        {
            var dto = TerminalCodec.DecodeSessionDelta(payload);
            SessionDeltaReceived?.Invoke(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Session delta decode failed: {ex}");
        }
    }

    private static void InvokeToPeer(long peerId, string route, string payload)
    {
        if (ZRoutedRpc.instance == null)
        {
            return;
        }

        ZRoutedRpc.instance.InvokeRoutedRPC(peerId, route, payload);
    }

    private static long ResolveServerPeerId()
    {
        if (ZNet.instance != null)
        {
            var znetMethod = typeof(ZNet).GetMethod("GetServerPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (znetMethod != null && znetMethod.Invoke(ZNet.instance, null) is long znetPeerId)
            {
                return znetPeerId;
            }
        }

        var routedMethod = typeof(ZRoutedRpc).GetMethod("GetServerPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (routedMethod != null && routedMethod.Invoke(ZRoutedRpc.instance, null) is long routedPeerId)
        {
            return routedPeerId;
        }

        var peerField = typeof(ZRoutedRpc).GetField("m_serverPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? typeof(ZRoutedRpc).GetField("m_serverUID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (peerField != null && peerField.GetValue(ZRoutedRpc.instance) is long fieldPeerId)
        {
            return fieldPeerId;
        }

        return 0L;
    }

    private static bool IsServer()
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
        if (isServerField != null && isServerField.GetValue(ZNet.instance) is bool fieldValue)
        {
            return fieldValue;
        }

        return true;
    }
}
