#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Custom Inspector for authoring and live-testing B2UStylizePreset assets.
    /// </summary>
    [CustomEditor(typeof(B2UStylizePreset))]
    public class B2UStylizePresetEditor : Editor
    {
        private static string[] offsetModes;
        private static string[] styleNames;
        private static string[] mattingNames;
        private static GUIContent[] descriptions;
        private static GUIContent stylizeBlendLabel;

        private void OnEnable()
        {
            InitializeData();
            Undo.undoRedoEvent += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoEvent -= OnUndoRedo;
        }

        private void OnUndoRedo(in UndoRedoInfo info)
        {
            if (target is B2UStylizePreset preset)
            {
                preset.Apply();
                SceneView.RepaintAll();
            }
        }

        public override void OnInspectorGUI()
        {
            B2UStylizePreset preset = (B2UStylizePreset)target;

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(preset, "Change Stylize Preset Property");

            preset.stylizeTextureArray = (Texture2DArray)EditorGUILayout.ObjectField(descriptions[0], preset.stylizeTextureArray, typeof(Texture2DArray), false);
            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            preset.baseIndex = (B2UStylizeIndex)EditorGUILayout.Popup(descriptions[1], (int)preset.baseIndex, styleNames);
            preset.baseScale = EditorGUILayout.FloatField(descriptions[2], preset.baseScale);
            preset.baseIntensity = EditorGUILayout.Slider(descriptions[3], preset.baseIntensity, 0f, 1f);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            preset.shadowIndex = (B2UStylizeIndex)EditorGUILayout.Popup(descriptions[6], (int)preset.shadowIndex, styleNames);
            preset.shadowBands = EditorGUILayout.IntSlider(descriptions[4], preset.shadowBands, 1, 8);
            preset.shadowScale = EditorGUILayout.FloatField(descriptions[7], preset.shadowScale);
            preset.shadowIntensity = EditorGUILayout.Slider(descriptions[8], preset.shadowIntensity, 0f, 1f);
            preset.shadowMinMax = EditorGUILayout.Vector2Field(descriptions[9], preset.shadowMinMax);
            preset.shadowTint = EditorGUILayout.ColorField(descriptions[10], preset.shadowTint);
            preset.shadowOffsetMode = EditorGUILayout.Popup(descriptions[5], preset.shadowOffsetMode ? 1 : 0, offsetModes) == 1;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            preset.mattingStyle = (B2UStylizeMattingStyle)EditorGUILayout.Popup(descriptions[11], (int)preset.mattingStyle, mattingNames);
            if (preset.mattingStyle != B2UStylizeMattingStyle.None)
            {
                preset.mattingSize = EditorGUILayout.Slider(descriptions[12], preset.mattingSize, 0f, 1f);
                preset.mattingFalloff = EditorGUILayout.Slider(descriptions[13], preset.mattingFalloff, 0f, 1f);
                preset.mattingColor = EditorGUILayout.ColorField(descriptions[14], preset.mattingColor);
            }
            EditorGUILayout.EndVertical();

            bool hasChanged = EditorGUI.EndChangeCheck();

            if (hasChanged)
            {
                EditorUtility.SetDirty(preset);
            }

            EditorGUILayout.Space(8f);

            if (GUILayout.Button("Force Apply to Shader", GUILayout.Height(30f)) || hasChanged)
            {
                preset.Apply();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(16f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Global Testing", EditorStyles.boldLabel);

            B2UShaderProfile activeProfile = B2URuntimeUtility.ActiveProfile;
            if (activeProfile != null && activeProfile.propertyBlocks != null &&
                activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.StylizeActive, out var stylizeBlock))
            {
                if (stylizeBlock.value is bool isActive && isActive)
                {
                    if (activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.StylizeBlend, out var stylizeBlendBlock))
                    {
                        EditorGUI.BeginChangeCheck();
                        float newBlend = EditorGUILayout.Slider(stylizeBlendLabel, (float)stylizeBlendBlock.value, 0f, 1f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.StylizeBlend, newBlend);
                            SceneView.RepaintAll();
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("The active profile does not have fullscreen stylizing enabled. Changing values will not be visible.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No active B2UShaderProfile is bound. Bind a profile to test fullscreen stylizing in the viewport.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private static void InitializeData()
        {
            if (descriptions != null) return;

            offsetModes = new string[] { "Rotated Offset", "Translated Offset" };
            styleNames = new string[]
            {
                "0 - Cloud",
                "1 - Gouache",
                "2 - Splotchy",
                "3 - Nebula",
                "4 - Swirled",
                "5 - Sweeping",
                "6 - Hatched",
                "7 - Cross Hatched",
                "8 - Cloth",
                "9 - Watercolor",
                "10 - Sparkles",
                "11 - Circuits",
                "12 - Star Grid",
                "13 - Diamond Grid",
                "14 - Dot Grid",
                "15 - Ring Grid"
            };
            mattingNames = new string[]
            {
                "No Matting",
                "Circular Mat",
                "Rectangular Mat",
                "Noisy Rectangular Mat"
            };

            stylizeBlendLabel = new GUIContent(
                "Stylize Blend",
                "Controls the global blend factor of the active fullscreen stylize pass (0 = off, 1 = full effect)."
            );

            descriptions = new GUIContent[]
            {
                new GUIContent("Style Texture Array", "The 16-slice Texture2DArray containing the procedural/hand-crafted pattern slices for the stylizer."), // 0
                new GUIContent("Base Style Index", "The pattern slice from the Style Texture Array applied across the entire screen."), // 1
                new GUIContent("Base Style Scale", "The UV tiling scale of the base fullscreen pattern overlay."), // 2
                new GUIContent("Base Style Intensity", "How strongly the base pattern texture modulates the scene color."), // 3
                new GUIContent("Shadow Bands", "The number of stepped luminance bands used to layer shadow hatching/patterns."), // 4
                new GUIContent("Shadow Offset Mode", "Determines whether successive shadow bands are rotated or UV-translated relative to one another."), // 5
                new GUIContent("Shadow Style Index", "The pattern slice from the Style Texture Array used inside shadowed areas."), // 6
                new GUIContent("Shadow Style Scale", "The UV tiling scale of the shadow pattern texture."), // 7
                new GUIContent("Shadow Style Intensity", "The opacity/strength of the patterned shadow bands."), // 8
                new GUIContent("Shadow Min/Max", "The luminance threshold range (X = upper shadow start, Y = deepest shadow) where shadow bands are applied."), // 9
                new GUIContent("Shadow Tint", "The color tint applied to the patterned shadow bands."), // 10
                new GUIContent("Matting Style", "The frame/vignette border shape drawn around the edges of the screen."), // 11
                new GUIContent("Matting Size", "How far inward the matting frame extends from the screen edges."), // 12
                new GUIContent("Matting Falloff", "The softness of the inner edge of the matting frame."), // 13
                new GUIContent("Matting Color", "The color of the fullscreen matting border.") // 14
            };
        }
    }
}
#endif