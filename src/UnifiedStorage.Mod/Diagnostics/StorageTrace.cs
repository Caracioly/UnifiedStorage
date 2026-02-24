using BepInEx.Logging;
using UnifiedStorage.Mod.Config;

namespace UnifiedStorage.Mod.Diagnostics;

public sealed class StorageTrace
{
    private readonly ManualLogSource _logger;
    private readonly StorageConfig _config;

    public StorageTrace(ManualLogSource logger, StorageConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public bool IsDevMode => _config.EnableDevLogs.Value;

    public void Dev(string message)
    {
        if (!IsDevMode) return;
        _logger.LogInfo($"[UnifiedStorage] {message}");
    }

    public void Warn(string message)
    {
        if (!IsDevMode) return;
        _logger.LogWarning($"[UnifiedStorage] {message}");
    }
}
