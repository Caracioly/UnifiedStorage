using BepInEx.Configuration;

namespace UnifiedStorage.Mod.Config;

public sealed class StorageConfig
{
    private const int DefaultMaxContainersScanned = 128;
    private const bool DefaultRequireAccessCheck = true;
    private const string DefaultTerminalDisplayName = "Unified Chest";
    private const bool DefaultTerminalTintEnabled = true;
    private const string DefaultTerminalTintColor = "#6EA84A";
    private const float DefaultTerminalTintStrength = 0.55f;

    public StorageConfig(ConfigFile config)
    {
        ScanRadius = config.Bind("General", "ScanRadius", 20f, "Radius in meters used to include nearby chests.");
        MaxContainersScanned = config.Bind(
            "General",
            "MaxContainersScanned",
            DefaultMaxContainersScanned,
            "Maximum number of storage sources scanned per terminal refresh (containers + drawers). Default: 128. Increasing this value can significantly impact performance, especially in large bases.");
        EnableDevLogs = config.Bind("Debug", "EnableDevLogs", false, "Enable dev logs: session open/close, deposits, withdrawals, and storage updates.");
        EnablePerformanceLogs = config.Bind(
            "Debug",
            "EnablePerformanceLogs",
            false,
            "Enable periodic scan performance logs for benchmarking before/after optimizations.");
        PerformanceLogIntervalSeconds = config.Bind(
            "Debug",
            "PerformanceLogIntervalSeconds",
            20f,
            "Interval in seconds between aggregated scan performance log entries.");
        TerminalPieceEnabled = config.Bind("Terminal", "TerminalPieceEnabled", true, "Enable Unified Chest terminal piece.");
    }

    public ConfigEntry<float> ScanRadius { get; }
    public ConfigEntry<int> MaxContainersScanned { get; }
    public ConfigEntry<bool> EnableDevLogs { get; }
    public ConfigEntry<bool> EnablePerformanceLogs { get; }
    public ConfigEntry<float> PerformanceLogIntervalSeconds { get; }
    public ConfigEntry<bool> TerminalPieceEnabled { get; }

    public int MaxContainersScannedLimit
    {
        get
        {
            var configured = MaxContainersScanned?.Value ?? DefaultMaxContainersScanned;
            return configured > 0 ? configured : DefaultMaxContainersScanned;
        }
    }
    public bool RequireAccessCheck => DefaultRequireAccessCheck;
    public string TerminalDisplayName => DefaultTerminalDisplayName;
    public bool TerminalTintEnabled => DefaultTerminalTintEnabled;
    public string TerminalTintColor => DefaultTerminalTintColor;
    public float TerminalTintStrength => DefaultTerminalTintStrength;

    public float PerformanceLogInterval
    {
        get
        {
            var configured = PerformanceLogIntervalSeconds?.Value ?? 20f;
            return configured >= 1f ? configured : 1f;
        }
    }
}
