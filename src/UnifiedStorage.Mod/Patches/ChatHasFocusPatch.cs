using HarmonyLib;

namespace UnifiedStorage.Mod.Patches;

[HarmonyPatch(typeof(Chat), "HasFocus")]
public static class ChatHasFocusPatch
{
    private static void Postfix(ref bool __result)
    {
        if (UnifiedStoragePlugin.ShouldBlockGameInput())
        {
            __result = true;
        }
    }
}
