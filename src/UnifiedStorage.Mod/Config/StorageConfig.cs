using BepInEx.Configuration;
using UnityEngine;

namespace UnifiedStorage.Mod.Config;

public sealed class StorageConfig
{
    public StorageConfig(ConfigFile config)
    {
        HotkeyOpen = config.Bind("General", "HotkeyOpen", KeyCode.F8, "Hotkey to open unified storage window.");
        SearchDebounceMs = config.Bind("General", "SearchDebounceMs", 80, "Search input debounce in milliseconds.");
        ScanRadius = config.Bind("General", "ScanRadius", 20f, "Radius in meters used to include nearby chests.");
        SnapshotRefreshMs = config.Bind("General", "SnapshotRefreshMs", 750, "Snapshot auto refresh interval in milliseconds.");
        MaxContainersScanned = config.Bind("General", "MaxContainersScanned", 128, "Hard cap for nearby containers scan.");
        RequireAccessCheck = config.Bind("General", "RequireAccessCheck", true, "If true, server checks chest access before reading/removing.");
        TerminalPieceEnabled = config.Bind("Terminal", "TerminalPieceEnabled", true, "Enable Unified Chest terminal piece.");
        TerminalDisplayName = config.Bind("Terminal", "TerminalDisplayName", "Unified Chest", "Display name for terminal piece.");
        TerminalRangeOverride = config.Bind("Terminal", "TerminalRangeOverride", 0f, "If > 0, overrides ScanRadius for terminal network.");
    }

    public ConfigEntry<KeyCode> HotkeyOpen { get; }
    public ConfigEntry<int> SearchDebounceMs { get; }
    public ConfigEntry<float> ScanRadius { get; }
    public ConfigEntry<int> SnapshotRefreshMs { get; }
    public ConfigEntry<int> MaxContainersScanned { get; }
    public ConfigEntry<bool> RequireAccessCheck { get; }
    public ConfigEntry<bool> TerminalPieceEnabled { get; }
    public ConfigEntry<string> TerminalDisplayName { get; }
    public ConfigEntry<float> TerminalRangeOverride { get; }
}
