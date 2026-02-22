using System.Collections.Generic;
using UnityEngine;
using UnifiedStorage.Mod.Models;

namespace UnifiedStorage.Mod.Domain;

public interface IContainerScanner
{
    IReadOnlyList<ChestHandle> GetNearbyContainers(Vector3 center, float radius, Container? ignoreContainer = null, bool onlyVanillaChests = false);
}
