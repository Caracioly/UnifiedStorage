using UnifiedStorage.Mod.Shared;
using UnityEngine;

namespace UnifiedStorage.Mod.Pieces;

public sealed class UnifiedTerminal : MonoBehaviour
{
    public const string TerminalPrefabName = "piece_unified_chest_terminal";

    private bool _tintApplied;
    private bool _tintEnabled = true;
    private Color _tintColor = new(0.431f, 0.659f, 0.290f, 1f);
    private float _tintStrength = 0.55f;

    public static bool IsTerminal(Container? container)
    {
        return container != null && container.GetComponent<UnifiedTerminal>() != null;
    }

    public static bool IsTerminal(GameObject? go)
    {
        return go != null && go.GetComponent<UnifiedTerminal>() != null;
    }

    public void ConfigureVisuals(bool tintEnabled, string tintColorHex, float tintStrength)
    {
        _tintEnabled = tintEnabled;
        _tintStrength = Mathf.Clamp01(tintStrength);
        if (!string.IsNullOrWhiteSpace(tintColorHex) && ColorUtility.TryParseHtmlString(tintColorHex, out var parsed))
            _tintColor = parsed;
    }

    public Container? GetContainer() => GetComponent<Container>();

    private void Awake()
    {
        ApplyTintIfNeeded();
    }

    private void ApplyTintIfNeeded()
    {
        if (!_tintEnabled || _tintApplied || _tintStrength <= 0f)
            return;

        var effectiveTintStrength = Mathf.Clamp01(_tintStrength * 1.7f);
        var emissionBoost = 0.16f * effectiveTintStrength;

        _tintApplied = true;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetType().Name == "ParticleSystemRenderer")
                continue;

            var materials = renderer.materials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;

                if (material.HasProperty("_Color"))
                {
                    var current = material.GetColor("_Color");
                    material.SetColor("_Color", Color.Lerp(current, _tintColor, effectiveTintStrength));
                }

                if (material.HasProperty("_EmissionColor"))
                {
                    var emission = material.GetColor("_EmissionColor");
                    material.SetColor("_EmissionColor", emission + (_tintColor * emissionBoost));
                    material.EnableKeyword("_EMISSION");
                }
            }
        }
    }
}
