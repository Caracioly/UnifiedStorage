using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnifiedStorage.Mod.Shared;

public sealed class DrawerSnapshot
{
    public string SourceId { get; set; } = string.Empty;
    public ZNetView ZNetView { get; set; } = null!;
    public string Prefab { get; set; } = string.Empty;
    public int Amount { get; set; }
    public int Quality { get; set; }
    public Vector3 Position { get; set; }
}

// Reflects against API.ClientSideV2 in kg_ItemDrawers assembly.
// ClientSideV2.AllDrawers() returns List<ZNetView> — one per valid drawer.
public static class ItemDrawersApi
{
    private static bool _initialized;
    private static bool _available;
    private static MethodInfo? _allDrawersMethod;

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _available;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        var clientSideV2 = Type.GetType("API.ClientSideV2, kg_ItemDrawers");
        if (clientSideV2 == null)
        {
            _available = false;
            return;
        }

        _allDrawersMethod = clientSideV2.GetMethod("AllDrawers", BindingFlags.Public | BindingFlags.Static);
        _available = _allDrawersMethod != null;
    }

    public static List<DrawerSnapshot> GetAllDrawers()
    {
        EnsureInitialized();
        if (!_available || _allDrawersMethod == null) return new List<DrawerSnapshot>();

        var znvList = _allDrawersMethod.Invoke(null, null) as IEnumerable<ZNetView>;
        if (znvList == null) return new List<DrawerSnapshot>();

        var result = new List<DrawerSnapshot>();
        foreach (var znv in znvList)
        {
            var snapshot = TryBuildSnapshot(znv);
            if (snapshot != null) result.Add(snapshot);
        }
        return result;
    }

    public static List<DrawerSnapshot> GetDrawersInRange(Vector3 center, float radius)
    {
        var all = GetAllDrawers();
        var radiusSq = radius * radius;
        var result = new List<DrawerSnapshot>();
        foreach (var d in all)
        {
            if ((d.Position - center).sqrMagnitude <= radiusSq)
                result.Add(d);
        }
        return result;
    }

    public static void AddToDrawer(DrawerSnapshot snapshot, string prefab, int amount, int quality)
    {
        if (snapshot.ZNetView == null) return;
        snapshot.ZNetView.InvokeRPC("AddItem_Request", prefab, amount, quality);
    }

    public static void ForceRemoveFromDrawer(DrawerSnapshot snapshot, int amount)
    {
        if (snapshot.ZNetView == null) return;
        snapshot.ZNetView.ClaimOwnership();
        snapshot.ZNetView.InvokeRPC("ForceRemove", amount);
    }

    private static DrawerSnapshot? TryBuildSnapshot(ZNetView znv)
    {
        try
        {
            if (znv == null) return null;
            var zdo = znv.GetZDO();
            if (zdo == null) return null;

            return new DrawerSnapshot
            {
                SourceId = zdo.m_uid.ToString(),
                ZNetView = znv,
                Prefab = zdo.GetString("Prefab"),
                Amount = zdo.GetInt("Amount"),
                Quality = zdo.GetInt("Quality", 1),
                Position = znv.transform.position
            };
        }
        catch
        {
            return null;
        }
    }
}
