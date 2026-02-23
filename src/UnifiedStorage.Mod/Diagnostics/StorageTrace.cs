using System;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using UnifiedStorage.Core;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Diagnostics;

public sealed class StorageTrace
{
    private readonly ManualLogSource _logger;
    private readonly StorageConfig _config;

    public StorageTrace(ManualLogSource logger, StorageConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public bool IsEnabled => _config.EnableOperationLogs.Value;
    public bool IsVerbose => IsEnabled && _config.EnableVerboseOperationLogs.Value;
    public int SnapshotItemLogLimit => Math.Max(0, _config.SnapshotItemLogLimit.Value);

    public void Info(string scope, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        _logger.LogInfo($"[US:{scope}] {message}");
    }

    public void Verbose(string scope, string message)
    {
        if (!IsVerbose)
        {
            return;
        }

        _logger.LogInfo($"[US:{scope}:VERBOSE] {message}");
    }

    public void Warn(string scope, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        _logger.LogWarning($"[US:{scope}] {message}");
    }

    public static string Item(ItemKey key)
    {
        return $"{key.PrefabName}[q{key.Quality}/v{key.Variant}]";
    }

    public static string Item(AggregatedItem item)
    {
        return $"{Item(item.Key)} x{item.TotalAmount}";
    }

    public static string Chest(ChestHandle chest)
    {
        return $"{chest.SourceId}@{chest.Distance.ToString("0.0", CultureInfo.InvariantCulture)}m";
    }

    public string SnapshotSummary(SessionSnapshotDto snapshot)
    {
        var baseSummary =
            $"session={snapshot.SessionId} term={snapshot.TerminalUid} rev={snapshot.Revision} " +
            $"chests={snapshot.ChestCount} slots={snapshot.SlotsUsedVirtual}/{snapshot.SlotsTotalPhysical} items={snapshot.Items.Count}";
        if (!IsVerbose || snapshot.Items.Count == 0 || SnapshotItemLogLimit <= 0)
        {
            return baseSummary;
        }

        var preview = string.Join(
            ", ",
            snapshot.Items
                .Where(i => i.TotalAmount > 0)
                .OrderByDescending(i => i.TotalAmount)
                .Take(SnapshotItemLogLimit)
                .Select(Item));

        return string.IsNullOrWhiteSpace(preview) ? baseSummary : $"{baseSummary} top=[{preview}]";
    }
}
