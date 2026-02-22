using System.Collections.Generic;
using System.Linq;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Domain;
using UnifiedStorage.Mod.Models;
using UnifiedStorage.Mod.Pieces;
using UnityEngine;

namespace UnifiedStorage.Mod.Server;

public sealed class ContainerScanner : IContainerScanner
{
    private readonly StorageConfig _config;

    public ContainerScanner(StorageConfig config)
    {
        _config = config;
    }

    public IReadOnlyList<ChestHandle> GetNearbyContainers(Vector3 center, float radius, Container? ignoreContainer = null, bool onlyVanillaChests = false)
    {
        var maxCount = _config.MaxContainersScanned.Value;
        var nearby = Object
            .FindObjectsByType<Container>(FindObjectsSortMode.None)
            .Where(container => container != null)
            .Where(IsStaticChest)
            .Where(container => ignoreContainer == null || container != ignoreContainer)
            .Where(container => !UnifiedChestTerminalMarker.IsTerminalContainer(container))
            .Where(container => !onlyVanillaChests || ChestInclusionRules.IsVanillaChest(container))
            .Where(ChestInclusionRules.IsIncludedInUnified)
            .Select(container =>
            {
                var distance = Vector3.Distance(center, container.transform.position);
                return new ChestHandle(BuildSourceId(container), container, distance);
            })
            .Where(handle => handle.Distance <= radius)
            .OrderBy(handle => handle.Distance)
            .ThenBy(handle => handle.SourceId)
            .Take(maxCount)
            .ToList();

        return nearby;
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
