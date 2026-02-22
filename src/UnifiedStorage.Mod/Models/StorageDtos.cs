using System.Collections.Generic;
using UnifiedStorage.Core;

namespace UnifiedStorage.Mod.Models;

public sealed class ChestHandle
{
    public ChestHandle(string sourceId, Container container, float distance)
    {
        SourceId = sourceId;
        Container = container;
        Distance = distance;
    }

    public string SourceId { get; }
    public Container Container { get; }
    public float Distance { get; }
}

public sealed class OpenSessionRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
    public float AnchorX { get; set; }
    public float AnchorY { get; set; }
    public float AnchorZ { get; set; }
    public float Radius { get; set; }
}

public sealed class OpenSessionResponseDto
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public SessionSnapshotDto Snapshot { get; set; } = new();
}

public sealed class ReserveWithdrawRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
    public long ExpectedRevision { get; set; }
    public ItemKey Key { get; set; }
    public int Amount { get; set; }
}

public sealed class ReserveWithdrawResultDto
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public ItemKey Key { get; set; }
    public int ReservedAmount { get; set; }
    public long Revision { get; set; }
    public SessionSnapshotDto Snapshot { get; set; } = new();
}

public sealed class CommitReservationRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
}

public sealed class CancelReservationRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public sealed class DepositRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
    public long ExpectedRevision { get; set; }
    public ItemKey Key { get; set; }
    public int Amount { get; set; }
}

public sealed class ApplyResultDto
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public int AppliedAmount { get; set; }
    public long Revision { get; set; }
    public SessionSnapshotDto Snapshot { get; set; } = new();
}

public sealed class CloseSessionRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
}

public sealed class SessionDeltaDto
{
    public string SessionId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
    public long Revision { get; set; }
    public SessionSnapshotDto Snapshot { get; set; } = new();
}

public sealed class SessionSnapshotDto
{
    public string SessionId { get; set; } = string.Empty;
    public string TerminalUid { get; set; } = string.Empty;
    public long Revision { get; set; }
    public int SlotsUsedVirtual { get; set; }
    public int SlotsTotalPhysical { get; set; }
    public int ChestCount { get; set; }
    public List<AggregatedItem> Items { get; set; } = new();
}
