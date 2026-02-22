namespace UnifiedStorage.Mod.Pieces;

public sealed class UnifiedChestTerminalMarker : UnityEngine.MonoBehaviour
{
    public const string TerminalPrefabName = "piece_unified_chest_terminal";

    public static bool IsTerminalContainer(Container? container)
    {
        return container != null && container.GetComponent<UnifiedChestTerminalMarker>() != null;
    }
}
