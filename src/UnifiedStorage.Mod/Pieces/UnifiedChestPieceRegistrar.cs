using BepInEx.Logging;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnifiedStorage.Mod.Config;
using UnityEngine;

namespace UnifiedStorage.Mod.Pieces;

public sealed class UnifiedChestPieceRegistrar
{
    private readonly StorageConfig _config;
    private readonly ManualLogSource _logger;
    private bool _registered;

    public UnifiedChestPieceRegistrar(StorageConfig config, ManualLogSource logger)
    {
        _config = config;
        _logger = logger;
    }

    public void RegisterPiece()
    {
        if (_registered || !_config.TerminalPieceEnabled.Value)
        {
            return;
        }

        var basePrefabName = ResolveBaseChestPrefabName();
        if (string.IsNullOrWhiteSpace(basePrefabName))
        {
            _logger.LogError("Unified Chest Terminal: could not resolve a vanilla chest prefab to clone.");
            return;
        }

        var pieceConfig = new PieceConfig
        {
            Name = _config.TerminalDisplayName.Value,
            PieceTable = "_HammerPieceTable",
            Category = "Furniture",
            CraftingStation = "piece_workbench"
        };
        pieceConfig.AddRequirement("Wood", 10, true);

        var customPiece = new CustomPiece(UnifiedChestTerminalMarker.TerminalPrefabName, basePrefabName, pieceConfig);
        if (!customPiece.IsValid())
        {
            _logger.LogError($"Unified Chest Terminal: failed to create CustomPiece from base '{basePrefabName}'.");
            return;
        }

        var prefab = customPiece.PiecePrefab;
        if (prefab == null)
        {
            _logger.LogError("Unified Chest Terminal: PiecePrefab is null after CustomPiece creation.");
            return;
        }

        prefab.name = UnifiedChestTerminalMarker.TerminalPrefabName;
        if (prefab.GetComponent<UnifiedChestTerminalMarker>() == null)
        {
            prefab.AddComponent<UnifiedChestTerminalMarker>();
        }

        var piece = prefab.GetComponent<Piece>();
        if (piece != null)
        {
            piece.m_name = _config.TerminalDisplayName.Value;
            piece.m_description = "Access nearby chests in a unified view.";
        }

        PieceManager.Instance.AddPiece(customPiece);
        _registered = true;
        _logger.LogInfo($"Unified Chest Terminal piece registered (base: {basePrefabName}).");
    }

    private static string? ResolveBaseChestPrefabName()
    {
        var candidates = new[]
        {
            "piece_chest",
            "piece_chest_wood",
            "piece_chest_private",
            "piece_chest_blackmetal"
        };

        foreach (var candidate in candidates)
        {
            if (PrefabManager.Instance.GetPrefab(candidate) != null)
            {
                return candidate;
            }
        }

        return null;
    }
}
