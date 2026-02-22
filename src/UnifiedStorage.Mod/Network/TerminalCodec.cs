using System;
using System.Collections.Generic;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Network;

public static class TerminalCodec
{
    public static string EncodeOpenSessionRequest(OpenSessionRequestDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.SessionId ?? string.Empty);
            pkg.Write(dto.TerminalUid ?? string.Empty);
            pkg.Write(dto.PlayerId);
            pkg.Write(dto.AnchorX);
            pkg.Write(dto.AnchorY);
            pkg.Write(dto.AnchorZ);
            pkg.Write(dto.Radius);
        });
    }

    public static OpenSessionRequestDto DecodeOpenSessionRequest(string payload)
    {
        var dto = new OpenSessionRequestDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.SessionId = pkg.ReadString();
            dto.TerminalUid = pkg.ReadString();
            dto.PlayerId = pkg.ReadLong();
            dto.AnchorX = pkg.ReadSingle();
            dto.AnchorY = pkg.ReadSingle();
            dto.AnchorZ = pkg.ReadSingle();
            dto.Radius = pkg.ReadSingle();
        });
        return dto;
    }

    public static string EncodeOpenSessionResponse(OpenSessionResponseDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.Success);
            pkg.Write(dto.Reason ?? string.Empty);
            WriteSnapshot(pkg, dto.Snapshot);
        });
    }

    public static OpenSessionResponseDto DecodeOpenSessionResponse(string payload)
    {
        var dto = new OpenSessionResponseDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.Success = pkg.ReadBool();
            dto.Reason = pkg.ReadString();
            dto.Snapshot = ReadSnapshot(pkg);
        });
        return dto;
    }

    public static string EncodeReserveWithdrawRequest(ReserveWithdrawRequestDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.SessionId ?? string.Empty);
            pkg.Write(dto.OperationId ?? string.Empty);
            pkg.Write(dto.TerminalUid ?? string.Empty);
            pkg.Write(dto.PlayerId);
            pkg.Write(dto.ExpectedRevision);
            WriteItemKey(pkg, dto.Key);
            pkg.Write(dto.Amount);
        });
    }

    public static ReserveWithdrawRequestDto DecodeReserveWithdrawRequest(string payload)
    {
        var dto = new ReserveWithdrawRequestDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.SessionId = pkg.ReadString();
            dto.OperationId = pkg.ReadString();
            dto.TerminalUid = pkg.ReadString();
            dto.PlayerId = pkg.ReadLong();
            dto.ExpectedRevision = pkg.ReadLong();
            dto.Key = ReadItemKey(pkg);
            dto.Amount = pkg.ReadInt();
        });
        return dto;
    }

    public static string EncodeReserveWithdrawResult(ReserveWithdrawResultDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.Success);
            pkg.Write(dto.Reason ?? string.Empty);
            pkg.Write(dto.TokenId ?? string.Empty);
            WriteItemKey(pkg, dto.Key);
            pkg.Write(dto.ReservedAmount);
            pkg.Write(dto.Revision);
            WriteSnapshot(pkg, dto.Snapshot);
        });
    }

    public static ReserveWithdrawResultDto DecodeReserveWithdrawResult(string payload)
    {
        var dto = new ReserveWithdrawResultDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.Success = pkg.ReadBool();
            dto.Reason = pkg.ReadString();
            dto.TokenId = pkg.ReadString();
            dto.Key = ReadItemKey(pkg);
            dto.ReservedAmount = pkg.ReadInt();
            dto.Revision = pkg.ReadLong();
            dto.Snapshot = ReadSnapshot(pkg);
        });
        return dto;
    }

    public static string EncodeCommitReservationRequest(CommitReservationRequestDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.SessionId ?? string.Empty);
            pkg.Write(dto.OperationId ?? string.Empty);
            pkg.Write(dto.TerminalUid ?? string.Empty);
            pkg.Write(dto.TokenId ?? string.Empty);
        });
    }

    public static CommitReservationRequestDto DecodeCommitReservationRequest(string payload)
    {
        var dto = new CommitReservationRequestDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.SessionId = pkg.ReadString();
            dto.OperationId = pkg.ReadString();
            dto.TerminalUid = pkg.ReadString();
            dto.TokenId = pkg.ReadString();
        });
        return dto;
    }

    public static string EncodeCancelReservationRequest(CancelReservationRequestDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.SessionId ?? string.Empty);
            pkg.Write(dto.OperationId ?? string.Empty);
            pkg.Write(dto.TerminalUid ?? string.Empty);
            pkg.Write(dto.PlayerId);
            pkg.Write(dto.TokenId ?? string.Empty);
            pkg.Write(dto.Amount);
        });
    }

    public static CancelReservationRequestDto DecodeCancelReservationRequest(string payload)
    {
        var dto = new CancelReservationRequestDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.SessionId = pkg.ReadString();
            dto.OperationId = pkg.ReadString();
            dto.TerminalUid = pkg.ReadString();
            dto.PlayerId = pkg.ReadLong();
            dto.TokenId = pkg.ReadString();
            dto.Amount = pkg.ReadInt();
        });
        return dto;
    }

    public static string EncodeDepositRequest(DepositRequestDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.SessionId ?? string.Empty);
            pkg.Write(dto.OperationId ?? string.Empty);
            pkg.Write(dto.TerminalUid ?? string.Empty);
            pkg.Write(dto.PlayerId);
            pkg.Write(dto.ExpectedRevision);
            WriteItemKey(pkg, dto.Key);
            pkg.Write(dto.Amount);
        });
    }

    public static DepositRequestDto DecodeDepositRequest(string payload)
    {
        var dto = new DepositRequestDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.SessionId = pkg.ReadString();
            dto.OperationId = pkg.ReadString();
            dto.TerminalUid = pkg.ReadString();
            dto.PlayerId = pkg.ReadLong();
            dto.ExpectedRevision = pkg.ReadLong();
            dto.Key = ReadItemKey(pkg);
            dto.Amount = pkg.ReadInt();
        });
        return dto;
    }

    public static string EncodeApplyResult(ApplyResultDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.Success);
            pkg.Write(dto.Reason ?? string.Empty);
            pkg.Write(dto.OperationType ?? string.Empty);
            pkg.Write(dto.TokenId ?? string.Empty);
            pkg.Write(dto.AppliedAmount);
            pkg.Write(dto.Revision);
            WriteSnapshot(pkg, dto.Snapshot);
        });
    }

    public static ApplyResultDto DecodeApplyResult(string payload)
    {
        var dto = new ApplyResultDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.Success = pkg.ReadBool();
            dto.Reason = pkg.ReadString();
            dto.OperationType = pkg.ReadString();
            dto.TokenId = pkg.ReadString();
            dto.AppliedAmount = pkg.ReadInt();
            dto.Revision = pkg.ReadLong();
            dto.Snapshot = ReadSnapshot(pkg);
        });
        return dto;
    }

    public static string EncodeCloseSessionRequest(CloseSessionRequestDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.RequestId ?? string.Empty);
            pkg.Write(dto.SessionId ?? string.Empty);
            pkg.Write(dto.TerminalUid ?? string.Empty);
            pkg.Write(dto.PlayerId);
        });
    }

    public static CloseSessionRequestDto DecodeCloseSessionRequest(string payload)
    {
        var dto = new CloseSessionRequestDto();
        ReadPayload(payload, pkg =>
        {
            dto.RequestId = pkg.ReadString();
            dto.SessionId = pkg.ReadString();
            dto.TerminalUid = pkg.ReadString();
            dto.PlayerId = pkg.ReadLong();
        });
        return dto;
    }

    public static string EncodeSessionDelta(SessionDeltaDto dto)
    {
        return WritePayload(pkg =>
        {
            pkg.Write(dto.SessionId ?? string.Empty);
            pkg.Write(dto.TerminalUid ?? string.Empty);
            pkg.Write(dto.Revision);
            WriteSnapshot(pkg, dto.Snapshot);
        });
    }

    public static SessionDeltaDto DecodeSessionDelta(string payload)
    {
        var dto = new SessionDeltaDto();
        ReadPayload(payload, pkg =>
        {
            dto.SessionId = pkg.ReadString();
            dto.TerminalUid = pkg.ReadString();
            dto.Revision = pkg.ReadLong();
            dto.Snapshot = ReadSnapshot(pkg);
        });
        return dto;
    }

    private static string WritePayload(Action<ZPackage> writer)
    {
        var pkg = new ZPackage();
        writer(pkg);
        return Convert.ToBase64String(pkg.GetArray());
    }

    private static void ReadPayload(string payload, Action<ZPackage> reader)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var bytes = Convert.FromBase64String(payload);
        var pkg = new ZPackage();
        pkg.Load(bytes);
        reader(pkg);
    }

    private static void WriteSnapshot(ZPackage pkg, SessionSnapshotDto snapshot)
    {
        pkg.Write(snapshot.SessionId ?? string.Empty);
        pkg.Write(snapshot.TerminalUid ?? string.Empty);
        pkg.Write(snapshot.Revision);
        pkg.Write(snapshot.SlotsUsedVirtual);
        pkg.Write(snapshot.SlotsTotalPhysical);
        pkg.Write(snapshot.ChestCount);
        pkg.Write(snapshot.Items.Count);
        foreach (var item in snapshot.Items)
        {
            WriteItemKey(pkg, item.Key);
            pkg.Write(item.DisplayName ?? string.Empty);
            pkg.Write(item.TotalAmount);
            pkg.Write(item.SourceCount);
            pkg.Write(item.StackSize);
        }
    }

    private static SessionSnapshotDto ReadSnapshot(ZPackage pkg)
    {
        var snapshot = new SessionSnapshotDto
        {
            SessionId = pkg.ReadString(),
            TerminalUid = pkg.ReadString(),
            Revision = pkg.ReadLong(),
            SlotsUsedVirtual = pkg.ReadInt(),
            SlotsTotalPhysical = pkg.ReadInt(),
            ChestCount = pkg.ReadInt()
        };

        var count = pkg.ReadInt();
        if (count < 0)
        {
            count = 0;
        }

        var items = new List<AggregatedItem>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(new AggregatedItem
            {
                Key = ReadItemKey(pkg),
                DisplayName = pkg.ReadString(),
                TotalAmount = pkg.ReadInt(),
                SourceCount = pkg.ReadInt(),
                StackSize = pkg.ReadInt()
            });
        }

        snapshot.Items = items;
        return snapshot;
    }

    private static void WriteItemKey(ZPackage pkg, ItemKey key)
    {
        pkg.Write(key.PrefabName ?? string.Empty);
        pkg.Write(key.Quality);
        pkg.Write(key.Variant);
    }

    private static ItemKey ReadItemKey(ZPackage pkg)
    {
        var prefabName = pkg.ReadString();
        var quality = pkg.ReadInt();
        var variant = pkg.ReadInt();
        return new ItemKey(prefabName, quality, variant);
    }
}
