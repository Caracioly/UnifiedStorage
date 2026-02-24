using BepInEx.Configuration;

namespace UnifiedStorage.Mod.Config;

public sealed class StorageConfig
{
    public StorageConfig(ConfigFile config)
    {
        ScanRadius = config.Bind("General", "ScanRadius", 20f, "Radius in meters used to include nearby chests.");
        MaxContainersScanned = config.Bind("General", "MaxContainersScanned", 128, "Hard cap for nearby containers scan.");
        RequireAccessCheck = config.Bind("General", "RequireAccessCheck", true, "If true, server checks chest access before reading/removing.");
        EnableDevLogs = config.Bind("Debug", "EnableDevLogs", false, "Enable dev logs: session open/close, deposits, withdrawals, and storage updates.");
        TerminalPieceEnabled = config.Bind("Terminal", "TerminalPieceEnabled", true, "Enable Unified Chest terminal piece.");
        TerminalDisplayName = config.Bind("Terminal", "TerminalDisplayName", "Unified Chest", "Display name for terminal piece.");
        TerminalRangeOverride = config.Bind("Terminal", "TerminalRangeOverride", 0f, "If > 0, overrides ScanRadius for terminal network.");
        TerminalTintEnabled = config.Bind("Terminal", "TerminalTintEnabled", true, "Apply a visual tint to the Unified Chest terminal for easier identification.");
        TerminalTintColor = config.Bind("Terminal", "TerminalTintColor", "#6EA84A", "Tint color in hex format (example: #6EA84A).");
        TerminalTintStrength = config.Bind("Terminal", "TerminalTintStrength", 0.35f, "Tint blend intensity from 0.0 to 1.0.");
    }

    public ConfigEntry<float> ScanRadius { get; }
    public ConfigEntry<int> MaxContainersScanned { get; }
    public ConfigEntry<bool> RequireAccessCheck { get; }
    public ConfigEntry<bool> EnableDevLogs { get; }
    public ConfigEntry<bool> TerminalPieceEnabled { get; }
    public ConfigEntry<string> TerminalDisplayName { get; }
    public ConfigEntry<float> TerminalRangeOverride { get; }
    public ConfigEntry<bool> TerminalTintEnabled { get; }
    public ConfigEntry<string> TerminalTintColor { get; }
    public ConfigEntry<float> TerminalTintStrength { get; }
}
