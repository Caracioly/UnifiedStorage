using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnifiedStorage.Core;

namespace UnifiedStorage.Mod.Network;

public static class StorageCodec
{
    public static string EncodeSnapshot(StorageSnapshot snapshot)
    {
        var lines = new List<string> { $"REV|{snapshot.Revision}" };
        lines.Add($"CAP|{snapshot.UsedSlots}|{snapshot.TotalSlots}|{snapshot.ChestCount}");
        lines.AddRange(snapshot.Items.Select(EncodeAggregatedItem));
        return string.Join("\n", lines);
    }

    public static StorageSnapshot DecodeSnapshot(string payload)
    {
        var snapshot = new StorageSnapshot();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return snapshot;
        }

        var lines = payload.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0] == "REV" && parts.Length >= 2 && long.TryParse(parts[1], out var rev))
            {
                snapshot.Revision = rev;
                continue;
            }

            if (parts[0] == "ITM" && parts.Length >= 8)
            {
                snapshot.Items.Add(new AggregatedItem
                {
                    Key = new ItemKey(Unescape(parts[1]), ParseInt(parts[2]), ParseInt(parts[3])),
                    DisplayName = Unescape(parts[4]),
                    TotalAmount = ParseInt(parts[5]),
                    SourceCount = ParseInt(parts[6]),
                    StackSize = ParseInt(parts[7])
                });
                continue;
            }

            if (parts[0] == "CAP" && parts.Length >= 4)
            {
                snapshot.UsedSlots = ParseInt(parts[1]);
                snapshot.TotalSlots = ParseInt(parts[2]);
                snapshot.ChestCount = ParseInt(parts[3]);
            }
        }

        snapshot.Items = snapshot.Items
            .OrderBy(item => item.DisplayName)
            .ThenByDescending(item => item.TotalAmount)
            .ToList();
        return snapshot;
    }

    public static string EncodeWithdrawRequest(WithdrawRequest request)
    {
        return string.Join("|",
            "REQ",
            Escape(request.Key.PrefabName),
            request.Key.Quality.ToString(CultureInfo.InvariantCulture),
            request.Key.Variant.ToString(CultureInfo.InvariantCulture),
            request.Amount.ToString(CultureInfo.InvariantCulture));
    }

    public static WithdrawRequest DecodeWithdrawRequest(string payload)
    {
        var parts = payload.Split('|');
        if (parts.Length < 5)
        {
            return new WithdrawRequest();
        }

        return new WithdrawRequest
        {
            Key = new ItemKey(Unescape(parts[1]), ParseInt(parts[2]), ParseInt(parts[3])),
            Amount = ParseInt(parts[4])
        };
    }

    public static string EncodeWithdrawResponse(WithdrawResult result, StorageSnapshot snapshot)
    {
        var head = string.Join("|",
            "RES",
            result.Success ? "1" : "0",
            result.Withdrawn.ToString(CultureInfo.InvariantCulture),
            Escape(result.Reason),
            result.Revision.ToString(CultureInfo.InvariantCulture));
        return head + "\n" + EncodeSnapshot(snapshot);
    }

    public static (WithdrawResult Result, StorageSnapshot Snapshot) DecodeWithdrawResponse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (new WithdrawResult { Success = false, Reason = "Empty payload" }, new StorageSnapshot());
        }

        var firstLineBreak = payload.IndexOf('\n');
        var head = firstLineBreak >= 0 ? payload.Substring(0, firstLineBreak) : payload;
        var snapshotPayload = firstLineBreak >= 0 ? payload.Substring(firstLineBreak + 1) : string.Empty;
        var headParts = head.Split('|');

        var result = new WithdrawResult
        {
            Success = headParts.Length >= 2 && headParts[1] == "1",
            Withdrawn = headParts.Length >= 3 ? ParseInt(headParts[2]) : 0,
            Reason = headParts.Length >= 4 ? Unescape(headParts[3]) : string.Empty,
            Revision = headParts.Length >= 5 && long.TryParse(headParts[4], out var rev) ? rev : 0
        };

        return (result, DecodeSnapshot(snapshotPayload));
    }

    public static string EncodeStoreRequest(StoreRequest request)
    {
        return string.Join("|",
            "STR",
            ((int)request.Mode).ToString(CultureInfo.InvariantCulture));
    }

    public static StoreRequest DecodeStoreRequest(string payload)
    {
        var parts = payload.Split('|');
        if (parts.Length < 2)
        {
            return new StoreRequest();
        }

        var modeInt = ParseInt(parts[1]);
        var mode = modeInt == (int)StoreMode.StoreAll ? StoreMode.StoreAll : StoreMode.PlaceStacks;
        return new StoreRequest { Mode = mode };
    }

    public static string EncodeStoreResponse(StoreResult result, StorageSnapshot snapshot)
    {
        var head = string.Join("|",
            "SRS",
            result.Success ? "1" : "0",
            result.Stored.ToString(CultureInfo.InvariantCulture),
            Escape(result.Reason),
            result.Revision.ToString(CultureInfo.InvariantCulture));
        return head + "\n" + EncodeSnapshot(snapshot);
    }

    public static (StoreResult Result, StorageSnapshot Snapshot) DecodeStoreResponse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (new StoreResult { Success = false, Reason = "Empty payload" }, new StorageSnapshot());
        }

        var firstLineBreak = payload.IndexOf('\n');
        var head = firstLineBreak >= 0 ? payload.Substring(0, firstLineBreak) : payload;
        var snapshotPayload = firstLineBreak >= 0 ? payload.Substring(firstLineBreak + 1) : string.Empty;
        var headParts = head.Split('|');

        var result = new StoreResult
        {
            Success = headParts.Length >= 2 && headParts[1] == "1",
            Stored = headParts.Length >= 3 ? ParseInt(headParts[2]) : 0,
            Reason = headParts.Length >= 4 ? Unescape(headParts[3]) : string.Empty,
            Revision = headParts.Length >= 5 && long.TryParse(headParts[4], out var rev) ? rev : 0
        };

        return (result, DecodeSnapshot(snapshotPayload));
    }

    private static string EncodeAggregatedItem(AggregatedItem item)
    {
        return string.Join("|",
            "ITM",
            Escape(item.Key.PrefabName),
            item.Key.Quality.ToString(CultureInfo.InvariantCulture),
            item.Key.Variant.ToString(CultureInfo.InvariantCulture),
            Escape(item.DisplayName),
            item.TotalAmount.ToString(CultureInfo.InvariantCulture),
            item.SourceCount.ToString(CultureInfo.InvariantCulture),
            item.StackSize.ToString(CultureInfo.InvariantCulture));
    }

    private static int ParseInt(string raw)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string Escape(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static string Unescape(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
        }
        catch
        {
            return string.Empty;
        }
    }
}
