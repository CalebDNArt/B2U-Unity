#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Custom Inspector for authoring and live-testing B2URecolorPreset assets.
    /// </summary>
    [CustomEditor(typeof(B2URecolorPreset))]
    public class B2URecolorPresetEditor : Editor
    {
        private static string[] invNormal;
        private static string[] disableEnable;
        private static string[] blendTypeNames;
        private static string[] recolorSourceNames;
        private static GUIContent[] descriptions;
        private static GUIContent recolorBlendLabel;

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
            if (target is B2URecolorPreset preset)
            {
                preset.RebuildGradientTexture();
                preset.Apply();
                SceneView.RepaintAll();
            }
        }

        public override void OnInspectorGUI()
        {
            B2URecolorPreset preset = (B2URecolorPreset)target;

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(preset, "Change Recolor Preset Property");

            preset.blendStyle = (B2URecolorBlendStyle)EditorGUILayout.Popup(descriptions[0], (int)preset.blendStyle, blendTypeNames);
            EditorGUILayout.Space(8f);

            preset.source = (B2URecolorSource)EditorGUILayout.Popup(descriptions[1], (int)preset.source, recolorSourceNames);
            EditorGUILayout.Space(8f);

            preset.gradient = EditorGUILayout.GradientField(descriptions[11], preset.gradient);
            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            preset.blendEdgeColor = EditorGUILayout.ColorField(descriptions[2], preset.blendEdgeColor);
            preset.invertBlendDirection = EditorGUILayout.Popup(descriptions[4], preset.invertBlendDirection ? 1 : 0, invNormal) == 1;
            if (preset.source == B2URecolorSource.Depth)
            {
                preset.depthMinMax = EditorGUILayout.Vector2Field(descriptions[3], preset.depthMinMax);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            preset.usePulse = EditorGUILayout.Popup(descriptions[5], preset.usePulse ? 1 : 0, disableEnable) == 1;
            if (preset.usePulse)
            {
                preset.pulseSpeed = EditorGUILayout.FloatField(descriptions[6], preset.pulseSpeed);
                preset.pulseFrequency = EditorGUILayout.FloatField(descriptions[7], preset.pulseFrequency);
                preset.pulseSize = EditorGUILayout.FloatField(descriptions[8], preset.pulseSize);
                preset.pulseAttenuation = EditorGUILayout.Slider(descriptions[9], preset.pulseAttenuation, 0f, 1f);
                preset.pulseIntensity = EditorGUILayout.FloatField(descriptions[10], preset.pulseIntensity);
            }
            EditorGUILayout.EndVertical();

            bool hasChanged = EditorGUI.EndChangeCheck();

            if (hasChanged)
            {
                EditorUtility.SetDirty(preset);
                preset.RebuildGradientTexture();
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
                activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.RecolorActive, out var recolorBlock))
            {
                if (recolorBlock.value is bool isActive && isActive)
                {
                    if (activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.RecolorBlend, out var recolorBlendBlock))
                    {
                        EditorGUI.BeginChangeCheck();
                        float newBlend = EditorGUILayout.Slider(recolorBlendLabel, (float)recolorBlendBlock.value, 0f, 1f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.RecolorBlend, newBlend);
                            SceneView.RepaintAll();
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("The active profile does not have fullscreen recolor enabled. Changing values will not be visible.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No active B2UShaderProfile is bound. Bind a profile to test fullscreen recolor in the viewport.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private static void InitializeData()
        {
            if (descriptions != null) return;

            invNormal = new string[] { "Normal", "Inverted" };
            disableEnable = new string[] { "Disabled", "Enabled" };
            blendTypeNames = new string[]
            {
                "Alpha",
                "Radial",
                "Distorted Radial",
                "Horizontal Scan",
                "Vertical Scan",
                "Horizontal Double Scan",
                "Vertical Double Scan"
            };
            recolorSourceNames = new string[]
            {
                "Scene Luminance",
                "Scene Red",
                "Scene Green",
                "Scene Blue",
                "Depth",
                "Normal"
            };

            recolorBlendLabel = new GUIContent(
                "Recolor Blend",
                "Controls the global blend factor of the active fullscreen recolor pass (0 = off, 1 = full effect)."
            );

            descriptions = new GUIContent[]
            {
                new GUIContent(
                    "Blend Style",
                    "The transition pattern used when blending the recolor effect in and out.\n\n" +
                    "---Alpha---\nBlends the entire screen uniformly.\n\n" +
                    "---Radial---\nExpands or contracts circularly from the screen center.\n\n" +
                    "---Distorted Radial---\nExpands radially with noise distortion along the transition edge.\n\n" +
                    "---Horizontal Scan---\nWipes horizontally across the screen.\n\n" +
                    "---Vertical Scan---\nWipes vertically across the screen.\n\n" +
                    "---Horizontal Double Scan---\nWipes inward/outward from the left and right edges.\n\n" +
                    "---Vertical Double Scan---\nWipes inward/outward from the top and bottom edges."
                ), // 0
                new GUIContent(
                    "Recolor Source",
                    "The scene buffer value used as the 0-1 coordinate to sample the Recolor Gradient.\n\n" +
                    "---Scene Luminance---\nUses the grayscale luminance of the rendered scene color.\n\n" +
                    "---Scene Red---\nUses the scene's red channel.\n\n" +
                    "---Scene Green---\nUses the scene's green channel.\n\n" +
                    "---Scene Blue---\nUses the scene's blue channel.\n\n" +
                    "---Depth---\nUses linear scene depth mapped between Depth Min/Max. Objects that do not write to depth will not appear.\n\n" +
                    "---Normal---\nUses the dot product between the scene normal buffer and the camera view vector."
                ), // 1
                new GUIContent("Blend Edge Color", "The border color rendered along the active wipe transition edge."), // 2
                new GUIContent("Depth Min/Max", "The near (X) and far (Y) world-space distance range mapped to 0-1 when Recolor Source is set to Depth."), // 3
                new GUIContent("Blend Direction", "Reverses the direction of the transition wipe."), // 4
                new GUIContent("Pulse", "Enables a sonar-like wave pulse that emanates outward from the camera in world space."), // 5
                new GUIContent("Pulse Speed", "How fast the pulse waves travel outward."), // 6
                new GUIContent("Pulse Spacing", "The frequency/spacing between consecutive pulse waves."), // 7
                new GUIContent("Pulse Width", "The thickness of each pulse wave."), // 8
                new GUIContent("Pulse Attenuation", "How rapidly the pulse intensity fades out over distance."), // 9
                new GUIContent("Pulse Strength", "How strongly the pulse wave modulates the recolor gradient sampling."), // 10
                new GUIContent("Recolor Gradient", "The color gradient mapped onto the selected Recolor Source.") // 11
            };
        }
    }
}
#endif