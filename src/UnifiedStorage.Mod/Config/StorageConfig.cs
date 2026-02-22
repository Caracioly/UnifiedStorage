using BepInEx.Configuration;

namespace UnifiedStorage.Mod.Config;

public sealed class StorageConfig
{
    public StorageConfig(ConfigFile config)
    {
        ScanRadius = config.Bind("General", "ScanRadius", 20f, "Radius in meters used to include nearby chests.");
        MaxContainersScanned = config.Bind("General", "MaxContainersScanned", 128, "Hard cap for nearby containers scan.");
        RequireAccessCheck = config.Bind("General", "RequireAccessCheck", true, "If true, server checks chest access before reading/removing.");
        TerminalPieceEnabled = config.Bind("Terminal", "TerminalPieceEnabled", true, "Enable Unified Chest terminal piece.");
        TerminalDisplayName = config.Bind("Terminal", "TerminalDisplayName", "Unified Chest", "Display name for terminal piece.");
        TerminalRangeOverride = config.Bind("Terminal", "TerminalRangeOverride", 0f, "If > 0, overrides ScanRadius for terminal network.");
    }

    public ConfigEntry<float> ScanRadius { get; }
    public ConfigEntry<int> MaxContainersScanned { get; }
    public ConfigEntry<bool> RequireAccessCheck { get; }
    public ConfigEntry<bool> TerminalPieceEnabled { get; }
    public ConfigEntry<string> TerminalDisplayName { get; }
    public ConfigEntry<float> TerminalRangeOverride { get; }
}
