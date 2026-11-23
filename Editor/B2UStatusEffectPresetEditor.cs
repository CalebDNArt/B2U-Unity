#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Custom Inspector for authoring B2UStatusEffectPreset assets, dynamically adapting property labels,
    /// tooltips, and slider ranges to match the selected status effect type.
    /// </summary>
    [CustomEditor(typeof(B2UStatusEffectPreset))]
    public class B2UStatusEffectPresetEditor : Editor
    {
        private static string[] typeNames;
        private static string[] typeDescriptions;
        private static Dictionary<B2UStatusEffect, StatusEffectTypeDescriptorBlock> descriptorBlocks;
        private static GUIContent effectTypeLabel;
        private static GUIContent colorGradientLabel;
        private static GUIContent statusStrengthLabel;

        private void OnEnable()
        {
            InitializeData();
            UpdateIconIfNeeded();
            Undo.undoRedoEvent += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoEvent -= OnUndoRedo;
        }

        private void OnUndoRedo(in UndoRedoInfo info)
        {
            if (target is B2UStatusEffectPreset preset)
            {
                preset.RebuildGradientTexture();
                preset.Apply();
                UpdateIconIfNeeded();
                SceneView.RepaintAll();
            }
        }

        private void UpdateIconIfNeeded()
        {
            if (!(target is B2UStatusEffectPreset preset)) return;

            Texture2D expectedIcon = preset.GetIcon();
            Texture2D currentIcon = EditorGUIUtility.GetIconForObject(preset);

            if (currentIcon != expectedIcon && expectedIcon != null)
            {
                EditorGUIUtility.SetIconForObject(preset, expectedIcon);
                EditorUtility.SetDirty(preset);

                string assetPath = AssetDatabase.GetAssetPath(preset);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                        }
                    };
                }
            }
        }

        public override void OnInspectorGUI()
        {
            B2UStatusEffectPreset preset = (B2UStatusEffectPreset)target;

            if (!descriptorBlocks.TryGetValue(preset.statusEffectType, out StatusEffectTypeDescriptorBlock descriptorBlock))
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            B2UStatusEffect previousType = preset.statusEffectType;

            Undo.RecordObject(preset, "Change Status Effect Preset Property");

            preset.statusEffectType = (B2UStatusEffect)EditorGUILayout.Popup(effectTypeLabel, (int)preset.statusEffectType, typeNames);

            if (previousType != preset.statusEffectType)
            {
                UpdateIconIfNeeded();
                EditorUtility.SetDirty(preset);
                descriptorBlocks.TryGetValue(preset.statusEffectType, out descriptorBlock);
            }

            EditorGUILayout.HelpBox(typeDescriptions[(int)preset.statusEffectType], MessageType.None);
            EditorGUILayout.Space(8f);

            preset.gradient = EditorGUILayout.GradientField(colorGradientLabel, preset.gradient);
            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            preset.effectScale = DrawFloatField(preset, descriptorBlock.effectScaleBlock, 0);
            preset.effectGeneric1 = DrawFloatField(preset, descriptorBlock.effectGeneric1Block, 1);
            preset.effectGeneric2 = DrawFloatField(preset, descriptorBlock.effectGeneric2Block, 2);
            preset.effectGeneric3 = DrawFloatField(preset, descriptorBlock.effectGeneric3Block, 3);
            preset.effectGeneric4 = DrawFloatField(preset, descriptorBlock.effectGeneric4Block, 4);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);

            bool hasChanged = EditorGUI.EndChangeCheck();

            if (hasChanged)
            {
                EditorUtility.SetDirty(preset);
                preset.RebuildGradientTexture();
            }

            if (GUILayout.Button("Force Apply To Shader", GUILayout.Height(30f)) || hasChanged)
            {
                preset.Apply();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(16f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Global Testing", EditorStyles.boldLabel);

            B2UShaderProfile activeProfile = B2URuntimeUtility.ActiveProfile;
            if (activeProfile != null && activeProfile.propertyBlocks != null &&
                activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.StatusActive, out var statusBlock))
            {
                if (statusBlock.value is bool isActive && isActive)
                {
                    if (activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.StatusStrength, out var strengthBlock))
                    {
                        EditorGUI.BeginChangeCheck();
                        float newStrength = EditorGUILayout.Slider(statusStrengthLabel, (float)strengthBlock.value, 0f, 1f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.StatusStrength, newStrength);
                            SceneView.RepaintAll();
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("The active profile does not have fullscreen status effects enabled. Changing values will not be visible.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No active B2UShaderProfile is bound. Bind a profile to test fullscreen status effects in the viewport.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private static void InitializeData()
        {
            if (descriptorBlocks != null) return;

            effectTypeLabel = new GUIContent("Effect Type", "The procedural fullscreen status effect archetype to render.");
            colorGradientLabel = new GUIContent("Color Gradient", "The 1D color gradient sampled by the procedural status effect.");
            statusStrengthLabel = new GUIContent("Status Strength", "Controls the global intensity of the active fullscreen status effect (0 = off, 1 = maximum).");

            typeNames = new string[] { "Burning", "Freezing", "Shocked", "Poisoned", "Hexed", "Blessed", "Glitched", "Shielded" };
            typeDescriptions = new string[]
            {
                "Creates a procedural fire effect where flames climb from the bottom of the screen and heat distortion affects the background.",
                "Creates procedural frost and ice crystals that grow inward across the screen.",
                "Creates procedural lightning bolts that arc inward from the edges of the screen.",
                "Creates distortion waves at the edge of the screen that tint the background color with a darkening vignette.",
                "Creates an evolving procedural mandala seal across the view.",
                "Creates sunbursts at the corners of the screen with animated light rays.",
                "Creates a digital glitch effect with displaced screen-sampling blocks, background static, and depth-based chromatic aberration.",
                "Creates a procedural hexagonal energy shield overlay."
            };

            descriptorBlocks = new Dictionary<B2UStatusEffect, StatusEffectTypeDescriptorBlock>
            {
                [B2UStatusEffect.Burning] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Flame Scale", "The scale of the flames and heat distortion.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Flame Speed", "The upward speed of the flames and heat distortion.", new Vector2(0f, 2f)),
                    new StatusEffectPropertyDescriptorBlock("Flame Brightness", "The emissive brightness multiplier of the flames.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Distortion Strength", "The intensity of the background heat refraction.", new Vector2(0f, 1f)),
                    new StatusEffectPropertyDescriptorBlock("Flame Angle Offset Speed", "How rapidly Voronoi flame cells swirl around each other; higher values produce more chaotic fire.", new Vector2(0f, 5f))
                ),
                [B2UStatusEffect.Freezing] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Ice Scale", "The scale of the procedural ice crystals.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Cell Smoothing", "How softly ice crystal cells transition in and out across the screen.", new Vector2(0f, 1f)),
                    new StatusEffectPropertyDescriptorBlock("Ice Opacity", "The opacity of the ice crystal tint. Does not affect refractive distortion.", new Vector2(0f, 1f)),
                    new StatusEffectPropertyDescriptorBlock("Ice Distortion Strength", "The strength of the refractive distortion through the ice.", new Vector2(-0.5f, 0.5f)),
                    new StatusEffectPropertyDescriptorBlock("Frost Strength", "The intensity of the surface frost texture.", new Vector2(0f, 1f))
                ),
                [B2UStatusEffect.Shocked] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Bolt Scale", "The scale of the procedural lightning bolts.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Flash Speed", "How rapidly the lightning arcs flash and change.", new Vector2(-5f, 5f)),
                    new StatusEffectPropertyDescriptorBlock("Bolt Brightness", "The emissive brightness of the lightning bolts.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Bolt Thickness", "The line width of the lightning bolts.", new Vector2(0f, 0.01f)),
                    new StatusEffectPropertyDescriptorBlock("Vignette Falloff", "How rapidly the electric edge vignette falls off toward the center of the screen.", new Vector2(0f, 1f))
                ),
                [B2UStatusEffect.Poisoned] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Wave Scale", "The scale of the toxic distortion waves around the screen edges.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Wave Speed", "The animation speed of the distortion waves.", new Vector2(-5f, 5f)),
                    new StatusEffectPropertyDescriptorBlock("Color Spread", "How far inward the poison color tint spreads from the screen edges.", new Vector2(0f, 1f)),
                    new StatusEffectPropertyDescriptorBlock("Distortion Strength", "The intensity of the wavy screen distortion.", new Vector2(0f, 1f)),
                    new StatusEffectPropertyDescriptorBlock("Darken Power", "How strongly the poison vignette darkens the outer edges of the screen.", new Vector2(0f, 3f))
                ),
                [B2UStatusEffect.Hexed] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Seal Scale", "The overall size of the procedural mandala seal.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Seal Evolution Speed", "How rapidly the mandala geometry morphs and rotates.", new Vector2(0f, 5f)),
                    new StatusEffectPropertyDescriptorBlock("Seal Brightness", "The emissive brightness of the seal.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Seal Thickness", "The line thickness of the procedural seal.", new Vector2(0f, 0.1f)),
                    new StatusEffectPropertyDescriptorBlock("Color Shift Speed", "How rapidly the seal cycles through the Color Gradient.", new Vector2(-5f, 5f))
                ),
                [B2UStatusEffect.Blessed] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Ray Scale", "How far inward from the screen corners the light rays extend.", new Vector2(0f, 1f)),
                    new StatusEffectPropertyDescriptorBlock("Ray Speed", "How rapidly the light rays pulse and shimmer.", new Vector2(0f, 5f)),
                    new StatusEffectPropertyDescriptorBlock("Ray Brightness", "The emissive brightness of the light rays.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Ray Density", "How densely packed the angular ray layers are.", new Vector2(0f, 10f)),
                    new StatusEffectPropertyDescriptorBlock("Color Shift Speed", "How rapidly the rays cycle through the Color Gradient.", new Vector2(-5f, 5f))
                ),
                [B2UStatusEffect.Glitched] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Text Scale", "The scale of the procedural glyph/data overlay.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Static Strength", "The intensity of the background static noise.", new Vector2(0f, 2f)),
                    new StatusEffectPropertyDescriptorBlock("Glitch Speed", "The frequency at which displaced glitch blocks jump.", new Vector2(0f, 10f)),
                    new StatusEffectPropertyDescriptorBlock("Aberration Threshold", "The depth discontinuity threshold required to trigger edge chromatic aberration.", new Vector2(0f, 2f)),
                    new StatusEffectPropertyDescriptorBlock("Glitch Max Size", "The maximum normalized UV dimensions of displaced glitch blocks.", new Vector2(0f, 1f))
                ),
                [B2UStatusEffect.Shielded] = new StatusEffectTypeDescriptorBlock(
                    new StatusEffectPropertyDescriptorBlock("Hex Grid Scale", "The tiling density of the hexagonal shield grid.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Shield Alpha", "The base opacity of the shield hexes.", new Vector2(0f, 1f)),
                    new StatusEffectPropertyDescriptorBlock("Hex Brightness", "The emissive brightness of the hex edges and cells.", Vector2.zero),
                    new StatusEffectPropertyDescriptorBlock("Hex Spacing", "The gap width between adjacent hexagons relative to cell radius.", new Vector2(0f, 0.5f)),
                    new StatusEffectPropertyDescriptorBlock("Shield Curvature", "Applies pincushion (negative) or barrel (positive) UV distortion to give the shield 3D depth.", new Vector2(-0.25f, 0.25f))
                ),
            };
        }

        private float DrawFloatField(B2UStatusEffectPreset preset, StatusEffectPropertyDescriptorBlock propBlock, int genericID)
        {
            ref float property = ref preset.effectScale;
            switch (genericID)
            {
                case 1: property = ref preset.effectGeneric1; break;
                case 2: property = ref preset.effectGeneric2; break;
                case 3: property = ref preset.effectGeneric3; break;
                case 4: property = ref preset.effectGeneric4; break;
            }

            if (propBlock.minMax == Vector2.zero)
            {
                return EditorGUILayout.FloatField(propBlock.label, property);
            }
            return EditorGUILayout.Slider(propBlock.label, property, propBlock.minMax.x, propBlock.minMax.y);
        }

        private class StatusEffectTypeDescriptorBlock
        {
            public readonly StatusEffectPropertyDescriptorBlock effectScaleBlock;
            public readonly StatusEffectPropertyDescriptorBlock effectGeneric1Block;
            public readonly StatusEffectPropertyDescriptorBlock effectGeneric2Block;
            public readonly StatusEffectPropertyDescriptorBlock effectGeneric3Block;
            public readonly StatusEffectPropertyDescriptorBlock effectGeneric4Block;

            public StatusEffectTypeDescriptorBlock(
                StatusEffectPropertyDescriptorBlock scaleBlock,
                StatusEffectPropertyDescriptorBlock generic1Block,
                StatusEffectPropertyDescriptorBlock generic2Block,
                StatusEffectPropertyDescriptorBlock generic3Block,
                StatusEffectPropertyDescriptorBlock generic4Block)
            {
                effectScaleBlock = scaleBlock;
                effectGeneric1Block = generic1Block;
                effectGeneric2Block = generic2Block;
                effectGeneric3Block = generic3Block;
                effectGeneric4Block = generic4Block;
            }
        }

        private class StatusEffectPropertyDescriptorBlock
        {
            public readonly GUIContent label;
            public readonly Vector2 minMax;

            public StatusEffectPropertyDescriptorBlock(string uiName, string description, Vector2 minMax)
            {
                label = new GUIContent(uiName, description);
                this.minMax = minMax;
            }
        }
    }
}
#endif