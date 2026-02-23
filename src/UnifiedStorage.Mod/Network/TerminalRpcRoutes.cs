using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnifiedStorage.Mod.Diagnostics;
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
    private readonly StorageTrace _trace;
    private bool _registered;

    public TerminalRpcRoutes(TerminalAuthorityService authority, ManualLogSource logger, StorageTrace trace)
    {
        _authority = authority;
        _logger = logger;
        _trace = trace;
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
        _trace.Info("RPC", $"C->S OpenSession req={request.RequestId} session={request.SessionId} term={request.TerminalUid} peer={peer} player={request.PlayerId}");
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
        _trace.Info("RPC", $"C->S Reserve req={request.RequestId} op={request.OperationId} session={request.SessionId} term={request.TerminalUid} key={StorageTrace.Item(request.Key)} amount={request.Amount} rev={request.ExpectedRevision} peer={peer}");
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
        _trace.Info("RPC", $"C->S Commit req={request.RequestId} op={request.OperationId} session={request.SessionId} term={request.TerminalUid} token={request.TokenId} peer={peer}");
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
        _trace.Info("RPC", $"C->S Cancel req={request.RequestId} op={request.OperationId} session={request.SessionId} term={request.TerminalUid} token={request.TokenId} amount={request.Amount} peer={peer}");
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
        _trace.Info("RPC", $"C->S Deposit req={request.RequestId} op={request.OperationId} session={request.SessionId} term={request.TerminalUid} key={StorageTrace.Item(request.Key)} amount={request.Amount} rev={request.ExpectedRevision} peer={peer}");
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
        _trace.Info("RPC", $"C->S Close req={request.RequestId} session={request.SessionId} term={request.TerminalUid} player={request.PlayerId} peer={peer}");
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
            _trace.Verbose("RPC", $"S->C Delta session={delta.SessionId} term={delta.TerminalUid} rev={delta.Revision} peer={peer}");
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
            _trace.Info("RPC", $"S recv OpenSession req={request.RequestId} session={request.SessionId} term={request.TerminalUid} sender={sender} player={request.PlayerId}");
            var response = _authority.HandleOpenSession(sender, request);
            var responsePayload = TerminalCodec.EncodeOpenSessionResponse(response);
            _trace.Info("RPC", $"S send SessionSnapshot req={response.RequestId} success={response.Success} reason={response.Reason}");
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
            _trace.Info("RPC", $"S recv Reserve req={request.RequestId} op={request.OperationId} term={request.TerminalUid} sender={sender} key={StorageTrace.Item(request.Key)} amount={request.Amount}");
            var result = _authority.HandleReserveWithdraw(sender, request);
            var responsePayload = TerminalCodec.EncodeReserveWithdrawResult(result);
            _trace.Info("RPC", $"S send ReserveResult req={result.RequestId} success={result.Success} reason={result.Reason} token={result.TokenId} reserved={result.ReservedAmount} rev={result.Revision}");
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
            _trace.Info("RPC", $"S recv Commit req={request.RequestId} op={request.OperationId} term={request.TerminalUid} sender={sender} token={request.TokenId}");
            var result = _authority.HandleCommitReservation(sender, request);
            var responsePayload = TerminalCodec.EncodeApplyResult(result);
            _trace.Info("RPC", $"S send ApplyResult(commit) req={result.RequestId} success={result.Success} reason={result.Reason} token={result.TokenId} applied={result.AppliedAmount} rev={result.Revision}");
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
            _trace.Info("RPC", $"S recv Cancel req={request.RequestId} op={request.OperationId} term={request.TerminalUid} sender={sender} token={request.TokenId} amount={request.Amount}");
            var result = _authority.HandleCancelReservation(sender, request);
            var responsePayload = TerminalCodec.EncodeApplyResult(result);
            _trace.Info("RPC", $"S send ApplyResult(cancel) req={result.RequestId} success={result.Success} reason={result.Reason} token={result.TokenId} applied={result.AppliedAmount} rev={result.Revision}");
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
            _trace.Info("RPC", $"S recv Deposit req={request.RequestId} op={request.OperationId} term={request.TerminalUid} sender={sender} key={StorageTrace.Item(request.Key)} amount={request.Amount}");
            var result = _authority.HandleDeposit(sender, request);
            var responsePayload = TerminalCodec.EncodeApplyResult(result);
            _trace.Info("RPC", $"S send ApplyResult(deposit) req={result.RequestId} success={result.Success} reason={result.Reason} applied={result.AppliedAmount} rev={result.Revision}");
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
            _trace.Info("RPC", $"S recv Close req={request.RequestId} session={request.SessionId} term={request.TerminalUid} sender={sender} player={request.PlayerId}");
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
            _trace.Info("RPC", $"C recv SessionSnapshot req={dto.RequestId} success={dto.Success} reason={dto.Reason} {_trace.SnapshotSummary(dto.Snapshot)}");
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
            _trace.Info("RPC", $"C recv ReserveResult req={dto.RequestId} success={dto.Success} reason={dto.Reason} token={dto.TokenId} key={StorageTrace.Item(dto.Key)} reserved={dto.ReservedAmount} rev={dto.Revision}");
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
            _trace.Info("RPC", $"C recv ApplyResult({dto.OperationType}) req={dto.RequestId} success={dto.Success} reason={dto.Reason} token={dto.TokenId} applied={dto.AppliedAmount} rev={dto.Revision}");
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
            _trace.Verbose("RPC", $"C recv Delta {_trace.SnapshotSummary(dto.Snapshot)}");
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
