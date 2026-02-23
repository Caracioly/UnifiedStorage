using UnityEngine;
using System.Reflection;

namespace UnifiedStorage.Mod.Pieces;

public sealed class UnifiedChestTerminalMarker : MonoBehaviour
{
    public const string TerminalPrefabName = "piece_unified_chest_terminal";
    private static string _defaultDisplayName = "Unified Chest";
    private static bool _defaultTintEnabled = true;
    private static Color _defaultTintColor = new(0.431f, 0.659f, 0.290f, 1f);
    private static float _defaultTintStrength = 0.35f;

    private bool _tintEnabled = true;
    private Color _tintColor = new(0.431f, 0.659f, 0.290f, 1f);
    private float _tintStrength = 0.35f;
    private bool _tintApplied;

    public static bool IsTerminalContainer(Container? container)
    {
        return container != null && container.GetComponent<UnifiedChestTerminalMarker>() != null;
    }

    public void ConfigureVisuals(bool tintEnabled, string tintColorHex, float tintStrength)
    {
        _tintEnabled = tintEnabled;
        _tintStrength = Mathf.Clamp01(tintStrength);
        if (!string.IsNullOrWhiteSpace(tintColorHex) && ColorUtility.TryParseHtmlString(tintColorHex, out var parsed))
        {
            _tintColor = parsed;
        }
    }

    public static void ConfigureDefaults(string displayName, bool tintEnabled, string tintColorHex, float tintStrength)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            _defaultDisplayName = displayName;
        }

        _defaultTintEnabled = tintEnabled;
        _defaultTintStrength = Mathf.Clamp01(tintStrength);
        if (!string.IsNullOrWhiteSpace(tintColorHex) && ColorUtility.TryParseHtmlString(tintColorHex, out var parsed))
        {
            _defaultTintColor = parsed;
        }
    }

    private void Awake()
    {
        ConfigureVisuals(_defaultTintEnabled, $"#{ColorUtility.ToHtmlStringRGB(_defaultTintColor)}", _defaultTintStrength);
        TryApplyDisplayName();
        ApplyTintIfNeeded();
    }

    private void TryApplyDisplayName()
    {
        var container = GetComponent<Container>();
        if (container == null || string.IsNullOrWhiteSpace(_defaultDisplayName))
        {
            return;
        }

        var nameField = typeof(Container).GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (nameField?.FieldType == typeof(string))
        {
            nameField.SetValue(container, _defaultDisplayName);
        }
    }

    private void ApplyTintIfNeeded()
    {
        if (!_tintEnabled || _tintApplied || _tintStrength <= 0f)
        {
            return;
        }

        _tintApplied = true;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetType().Name == "ParticleSystemRenderer")
            {
                continue;
            }

            var materials = renderer.materials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty("_Color"))
                {
                    var current = material.GetColor("_Color");
                    material.SetColor("_Color", Color.Lerp(current, _tintColor, _tintStrength));
                }

                if (material.HasProperty("_EmissionColor"))
                {
                    var emission = material.GetColor("_EmissionColor");
                    material.SetColor("_EmissionColor", emission + (_tintColor * (0.10f * _tintStrength)));
                    material.EnableKeyword("_EMISSION");
                }
            }
        }
    }
}
