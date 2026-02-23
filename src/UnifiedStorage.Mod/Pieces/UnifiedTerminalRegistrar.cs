using System.Reflection;
using BepInEx.Logging;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnifiedStorage.Mod.Config;
using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Pieces;

public sealed class UnifiedTerminalRegistrar
{
    private readonly StorageConfig _config;
    private readonly ManualLogSource _logger;
    private bool _registered;

    public UnifiedTerminalRegistrar(StorageConfig config, ManualLogSource logger)
    {
        _config = config;
        _logger = logger;
    }

    public void RegisterPiece()
    {
        if (_registered || !_config.TerminalPieceEnabled.Value)
            return;

        var basePrefabName = ResolveBaseChestPrefabName();
        if (string.IsNullOrWhiteSpace(basePrefabName))
        {
            _logger.LogError("Unified Terminal: could not resolve a vanilla chest prefab to clone.");
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

        var customPiece = new CustomPiece(UnifiedTerminal.TerminalPrefabName, basePrefabName, pieceConfig);
        if (!customPiece.IsValid())
        {
            _logger.LogError($"Unified Terminal: failed to create CustomPiece from base '{basePrefabName}'.");
            return;
        }

        var prefab = customPiece.PiecePrefab;
        if (prefab == null)
        {
            _logger.LogError("Unified Terminal: PiecePrefab is null after CustomPiece creation.");
            return;
        }

        prefab.name = UnifiedTerminal.TerminalPrefabName;

        var terminal = prefab.GetComponent<UnifiedTerminal>();
        if (terminal == null)
            terminal = prefab.AddComponent<UnifiedTerminal>();

        terminal.ConfigureVisuals(
            _config.TerminalTintEnabled.Value,
            _config.TerminalTintColor.Value,
            _config.TerminalTintStrength.Value);

        var piece = prefab.GetComponent<Piece>();
        if (piece != null)
        {
            piece.m_name = _config.TerminalDisplayName.Value;
            piece.m_description = "Access nearby chests in a unified view.";
        }

        var container = prefab.GetComponent<Container>();
        if (container != null)
            ReflectionHelpers.SetContainerDisplayName(container, _config.TerminalDisplayName.Value);

        PieceManager.Instance.AddPiece(customPiece);
        _registered = true;
        _logger.LogInfo($"Unified Terminal piece registered (base: {basePrefabName}).");
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
                return candidate;
        }

        return null;
    }
}
