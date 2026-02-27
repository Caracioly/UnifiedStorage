using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using BepInEx.Logging;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Models;
using UnifiedStorage.Mod.Pieces;
using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Server;

public sealed class ContainerScanner : IContainerScanner
{
    private const double SlowScanThresholdMs = 8d;

    private readonly StorageConfig _config;
    private readonly ManualLogSource _logger;
    private readonly int[] _contextCallCounts = new int[(int)StorageScanContext.NearbyCount + 1];

    private float _nextPerformanceLogAt = -1f;
    private int _scanCalls;
    private double _scanTotalMs;
    private double _scanMaxMs;
    private int _slowScans;
    private int _containersSeenTotal;
    private int _containersInRangeTotal;
    private int _drawersSeenTotal;
    private int _drawersInRangeTotal;
    private int _sourcesBeforeCapTotal;
    private int _sourcesReturnedTotal;
    private int _capHitCount;
    private float _radiusTotal;

    public ContainerScanner(StorageConfig config, ManualLogSource logger)
    {
        _config = config;
        _logger = logger;
    }

    public IReadOnlyList<ChestHandle> GetNearbyContainers(
        Vector3 center,
        float radius,
        Container? ignoreContainer = null,
        StorageScanContext context = StorageScanContext.Unknown)
    {
        var stopwatch = _config.EnablePerformanceLogs.Value ? Stopwatch.StartNew() : null;
        var maxCount = _config.MaxContainersScannedLimit;
        var containersSeen = 0;
        var containersInRange = 0;
        var drawersSeen = 0;
        var drawersInRange = 0;

        var handles = new List<ChestHandle>();

        foreach (var container in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
        {
            if (container == null) continue;
            containersSeen++;
            if (!IsStaticChest(container)) continue;
            if (ignoreContainer != null && container == ignoreContainer) continue;
            if (UnifiedTerminal.IsTerminal(container)) continue;
            if (!ChestInclusionRules.IsIncludedInUnified(container)) continue;

            var distance = Vector3.Distance(center, container.transform.position);
            if (distance > radius) continue;

            containersInRange++;
            var sourceId = BuildSourceId(container);
            IStorageSource source = new ContainerStorageSource(sourceId, container, distance, _config);
            handles.Add(new ChestHandle(sourceId, source, distance));
        }

        var drawerHandles = GetDrawerHandles(center, radius, out drawersSeen, out drawersInRange);
        handles.AddRange(drawerHandles);

        var sourcesBeforeCap = handles.Count;
        var ordered = handles
            .OrderBy(handle => handle.Distance)
            .ThenBy(handle => handle.SourceId)
            .Take(maxCount)
            .ToList();

        if (stopwatch != null)
        {
            stopwatch.Stop();
            RecordScanMetrics(
                context,
                radius,
                maxCount,
                stopwatch.Elapsed.TotalMilliseconds,
                containersSeen,
                containersInRange,
                drawersSeen,
                drawersInRange,
                sourcesBeforeCap,
                ordered.Count);
        }

        return ordered;
    }

    private static List<ChestHandle> GetDrawerHandles(
        Vector3 center,
        float radius,
        out int drawersSeen,
        out int drawersInRange)
    {
        drawersSeen = 0;
        drawersInRange = 0;
        var result = new List<ChestHandle>();
        if (!ItemDrawersApi.IsAvailable) return result;

        var radiusSq = radius * radius;
        var allDrawers = ItemDrawersApi.GetAllDrawers();
        drawersSeen = allDrawers.Count;

        foreach (var drawer in allDrawers)
        {
            if (drawer == null) continue;
            if ((drawer.Position - center).sqrMagnitude > radiusSq) continue;
            drawersInRange++;
            if (!ChestInclusionRules.IsIncludedInUnified(drawer.ZNetView)) continue;
            var distance = Vector3.Distance(center, drawer.Position);
            IStorageSource source = new DrawerStorageSource(drawer, distance);
            result.Add(new ChestHandle(drawer.SourceId, source, distance));
        }

        return result;
    }

    private static bool IsStaticChest(Container container)
    {
        if (!container.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (container.GetComponentInParent<Vagon>() != null)
        {
            return false;
        }

        if (container.GetComponentInParent<Ship>() != null)
        {
            return false;
        }

        return true;
    }

    private static string BuildSourceId(Container container)
    {
        var znetView = container.GetComponent<ZNetView>();
        var zdo = znetView?.GetZDO();
        if (zdo != null)
        {
            return zdo.m_uid.ToString();
        }

        return container.GetInstanceID().ToString();
    }

    private void RecordScanMetrics(
        StorageScanContext context,
        float radius,
        int cap,
        double elapsedMs,
        int containersSeen,
        int containersInRange,
        int drawersSeen,
        int drawersInRange,
        int sourcesBeforeCap,
        int sourcesReturned)
    {
        _scanCalls++;
        _scanTotalMs += elapsedMs;
        if (elapsedMs > _scanMaxMs) _scanMaxMs = elapsedMs;
        if (elapsedMs >= SlowScanThresholdMs) _slowScans++;
        _containersSeenTotal += containersSeen;
        _containersInRangeTotal += containersInRange;
        _drawersSeenTotal += drawersSeen;
        _drawersInRangeTotal += drawersInRange;
        _sourcesBeforeCapTotal += sourcesBeforeCap;
        _sourcesReturnedTotal += sourcesReturned;
        if (sourcesBeforeCap > cap) _capHitCount++;
        _radiusTotal += radius;

        var contextIndex = (int)context;
        if (contextIndex < 0 || contextIndex >= _contextCallCounts.Length)
            contextIndex = (int)StorageScanContext.Unknown;
        _contextCallCounts[contextIndex]++;

        var now = Time.unscaledTime;
        if (_nextPerformanceLogAt < 0f)
        {
            _nextPerformanceLogAt = now + _config.PerformanceLogInterval;
            return;
        }

        if (now < _nextPerformanceLogAt)
            return;

        EmitPerformanceLog(cap);
        ResetPerformanceCounters();
        _nextPerformanceLogAt = now + _config.PerformanceLogInterval;
    }

    private void EmitPerformanceLog(int cap)
    {
        if (_scanCalls <= 0) return;

        var calls = _scanCalls;
        var avgMs = _scanTotalMs / calls;
        var avgContainersSeen = _containersSeenTotal / (double)calls;
        var avgContainersInRange = _containersInRangeTotal / (double)calls;
        var avgDrawersSeen = _drawersSeenTotal / (double)calls;
        var avgDrawersInRange = _drawersInRangeTotal / (double)calls;
        var avgSourcesBeforeCap = _sourcesBeforeCapTotal / (double)calls;
        var avgSourcesReturned = _sourcesReturnedTotal / (double)calls;
        var avgRadius = _radiusTotal / calls;
        var capHitPercent = (_capHitCount / (double)calls) * 100d;

        _logger.LogInfo(
            $"[UnifiedStorage][Perf] scan calls={calls} avg={avgMs:0.00}ms max={_scanMaxMs:0.00}ms " +
            $"slow(>{SlowScanThresholdMs:0}ms)={_slowScans}; " +
            $"ctx(session={_contextCallCounts[(int)StorageScanContext.SessionRefresh]}, " +
            $"authority={_contextCallCounts[(int)StorageScanContext.AuthoritySnapshot]}, " +
            $"hover={_contextCallCounts[(int)StorageScanContext.HoverPreview]}, " +
            $"interact={_contextCallCounts[(int)StorageScanContext.InteractCheck]}, " +
            $"nearby={_contextCallCounts[(int)StorageScanContext.NearbyCount]}, " +
            $"unknown={_contextCallCounts[(int)StorageScanContext.Unknown]}); " +
            $"containers(avgSeen={avgContainersSeen:0.0},avgInRange={avgContainersInRange:0.0}) " +
            $"drawers(avgSeen={avgDrawersSeen:0.0},avgInRange={avgDrawersInRange:0.0}) " +
            $"sources(avgBeforeCap={avgSourcesBeforeCap:0.0},avgReturned={avgSourcesReturned:0.0},capHit={capHitPercent:0.#}% @ cap={cap}) " +
            $"radius(avg={avgRadius:0.0})");

        if (avgMs >= SlowScanThresholdMs || _scanMaxMs >= 16d)
        {
            _logger.LogWarning(
                $"[UnifiedStorage][Perf] high scan cost detected: avg={avgMs:0.00}ms max={_scanMaxMs:0.00}ms over {calls} calls.");
        }
    }

    private void ResetPerformanceCounters()
    {
        _scanCalls = 0;
        _scanTotalMs = 0d;
        _scanMaxMs = 0d;
        _slowScans = 0;
        _containersSeenTotal = 0;
        _containersInRangeTotal = 0;
        _drawersSeenTotal = 0;
        _drawersInRangeTotal = 0;
        _sourcesBeforeCapTotal = 0;
        _sourcesReturnedTotal = 0;
        _capHitCount = 0;
        _radiusTotal = 0f;
        Array.Clear(_contextCallCounts, 0, _contextCallCounts.Length);
    }
}
