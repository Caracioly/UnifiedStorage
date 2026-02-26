using System.Collections.Generic;
using System.Linq;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Models;
using UnifiedStorage.Mod.Pieces;
using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Server;

public sealed class ContainerScanner : IContainerScanner
{
    private readonly StorageConfig _config;

    public ContainerScanner(StorageConfig config)
    {
        _config = config;
    }

    public IReadOnlyList<ChestHandle> GetNearbyContainers(Vector3 center, float radius, Container? ignoreContainer = null)
    {
        var maxCount = _config.MaxContainersScanned;

        var vanillaHandles = Object
            .FindObjectsByType<Container>(FindObjectsSortMode.None)
            .Where(container => container != null)
            .Where(IsStaticChest)
            .Where(container => ignoreContainer == null || container != ignoreContainer)
            .Where(container => !UnifiedTerminal.IsTerminal(container))
            .Where(ChestInclusionRules.IsIncludedInUnified)
            .Select(container =>
            {
                var distance = Vector3.Distance(center, container.transform.position);
                var sourceId = BuildSourceId(container);
                IStorageSource source = new ContainerStorageSource(sourceId, container, distance, _config);
                return new ChestHandle(sourceId, source, distance);
            })
            .Where(handle => handle.Distance <= radius);

        var drawerHandles = BuildDrawerHandles(center, radius);

        return vanillaHandles
            .Concat(drawerHandles)
            .OrderBy(handle => handle.Distance)
            .ThenBy(handle => handle.SourceId)
            .Take(maxCount)
            .ToList();
    }

    private static IEnumerable<ChestHandle> BuildDrawerHandles(Vector3 center, float radius)
    {
        if (!ItemDrawersApi.IsAvailable) yield break;

        foreach (var drawer in ItemDrawersApi.GetDrawersInRange(center, radius))
        {
            if (!ChestInclusionRules.IsIncludedInUnified(drawer.ZNetView)) continue;
            var distance = Vector3.Distance(center, drawer.Position);
            IStorageSource source = new DrawerStorageSource(drawer, distance);
            yield return new ChestHandle(drawer.SourceId, source, distance);
        }
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
}
