using HarmonyLib;
using UnityEngine;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(ZInput), "GetButton", typeof(string))]
public static class ZInputGetButtonPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!UnifiedStoragePlugin.ShouldBlockGameInput())
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ZInput), "GetButtonDown", typeof(string))]
public static class ZInputGetButtonDownPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!UnifiedStoragePlugin.ShouldBlockGameInput())
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ZInput), "GetButtonUp", typeof(string))]
public static class ZInputGetButtonUpPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!UnifiedStoragePlugin.ShouldBlockGameInput())
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ZInput), "GetKey", typeof(KeyCode), typeof(bool))]
public static class ZInputGetKeyPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!UnifiedStoragePlugin.ShouldBlockGameInput())
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ZInput), "GetKeyDown", typeof(KeyCode), typeof(bool))]
public static class ZInputGetKeyDownPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!UnifiedStoragePlugin.ShouldBlockGameInput())
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ZInput), "GetKeyUp", typeof(KeyCode), typeof(bool))]
public static class ZInputGetKeyUpPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!UnifiedStoragePlugin.ShouldBlockGameInput())
        {
            return true;
        }

        __result = false;
        return false;
    }
}
