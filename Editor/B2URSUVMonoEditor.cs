#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Custom Inspector for authoring per-object B2URSUVMono properties across Solid, Liquid, and Crystal workflows.
    /// </summary>
    [CustomEditor(typeof(B2URSUVMono))]
    [CanEditMultipleObjects]
    public class B2URSUVMonoEditor : Editor
    {
        private SerializedProperty activeTypeProp;
        private SerializedProperty materialIDProp;

        private SerializedProperty solidIsPaintedProp;
        private SerializedProperty solidPaletteIndexProp;
        private SerializedProperty solidPaintIndexProp;
        private SerializedProperty solidEmissionStrengthProp;
        private SerializedProperty solidNoiseOffsetProp;
        private SerializedProperty solidCrackIntensityProp;

        private SerializedProperty liquidEmissionStrengthProp;
        private SerializedProperty liquidFlowStrengthProp;
        private SerializedProperty liquidWaveMultiplierProp;

        private SerializedProperty crystalEmissionStrengthProp;
        private SerializedProperty crystalTransmissionProp;
        private SerializedProperty crystalAbsorptionProp;
        private SerializedProperty crystalCrackIntensityProp;

        private static readonly GUIContent ActiveTypeLabel = new GUIContent(
            "Active Workflow Type",
            "The B2U master shader workflow used to bit-pack this renderer's 32-bit Shader User Value (RSUV)."
        );
        private static readonly GUIContent AutoDetectBtnContent = new GUIContent(
            "Auto-Detect",
            "Reads the renderer's assigned material shader and automatically configures the RSUV workflow type."
        );

        // Solid Labels
        private static readonly GUIContent IsPaintedLabel = new GUIContent(
            "Is Painted",
            "Enables per-object paint tinting using the selected Palette Index and Paint Index (requires a paint mask baked into Vertex Color Blue in Blender)."
        );
        private static readonly GUIContent PaletteIndexLabel = new GUIContent(
            "Palette Index",
            "A 0-15 index selecting which slice of the Paint Palette Texture2DArray to sample for this object."
        );
        private static readonly GUIContent PaintIndexLabel = new GUIContent(
            "Paint Index",
            "A 0-127 index selecting which pixel in the active Paint Palette slice to apply as the paint color."
        );
        private static readonly GUIContent SolidEmissionLabel = new GUIContent(
            "Emission Strength",
            "Per-object 7-bit emission brightness multiplier (0 = off, 1 = full palette emission)."
        );
        private static readonly GUIContent NoiseOffsetLabel = new GUIContent(
            "Noise Offset",
            "A 0-63 spatial offset applied to break up world-space 3D noise patterns across identical meshes."
        );
        private static readonly GUIContent CrackIntensityLabel = new GUIContent(
            "Crack Intensity",
            "Controls the visibility of procedural surface damage/cracks on this object (0 = undamaged, 1 = fully cracked)."
        );

        // Liquid Labels
        private static readonly GUIContent LiquidEmissionLabel = new GUIContent(
            "Emission Strength",
            "Per-object 8-bit emission brightness multiplier (0 = off, 1 = full palette emission)."
        );
        private static readonly GUIContent FlowStrengthLabel = new GUIContent(
            "Flow Strength",
            "Per-object 10-bit multiplier (-1 to 1) scaling the baked UV0 fluid flow speed and direction."
        );
        private static readonly GUIContent WaveHeightLabel = new GUIContent(
            "Wave Multiplier",
            "Per-object 8-bit multiplier (-1 to 1) scaling procedural surface wave displacement height."
        );
        private static readonly GUIContent ApplyImmersionBtnContent = new GUIContent(
            "Apply to Immersion Shader",
            "Samples this liquid's optical and color properties from the active LUTs and binds it as the active fullscreen underwater immersion liquid."
        );
        private static readonly GUIContent RefreshLiquidDataBtnContent = new GUIContent(
            "Refresh Material ID & Flow Cache",
            "Re-reads the packed Material ID from UV1.y and rebuilds the 3D flow physics cache."
        );

        // Crystal Labels
        private static readonly GUIContent CrystalEmissionLabel = new GUIContent(
            "Emission Strength",
            "Per-object 8-bit emission brightness multiplier (0 = off, 1 = full palette emission)."
        );
        private static readonly GUIContent CrystalTransmissionLabel = new GUIContent(
            "Transmission Multiplier",
            "Per-object 8-bit multiplier scaling how much background light refracts through the crystal (0 = opaque, 1 = full LUT transmission)."
        );
        private static readonly GUIContent CrystalAbsorptionLabel = new GUIContent(
            "Absorption Multiplier",
            "Per-object 8-bit multiplier controlling how strongly crystal thickness tints refracted background light."
        );

        private void OnEnable()
        {
            activeTypeProp = serializedObject.FindProperty("activeType");
            materialIDProp = serializedObject.FindProperty("materialID");

            solidIsPaintedProp = serializedObject.FindProperty("solid.isPainted");
            solidPaletteIndexProp = serializedObject.FindProperty("solid.paletteIndex");
            solidPaintIndexProp = serializedObject.FindProperty("solid.paintIndex");
            solidEmissionStrengthProp = serializedObject.FindProperty("solid.emissionStrength");
            solidNoiseOffsetProp = serializedObject.FindProperty("solid.noiseOffset");
            solidCrackIntensityProp = serializedObject.FindProperty("solid.crackIntensity");

            liquidEmissionStrengthProp = serializedObject.FindProperty("liquid.emissionStrength");
            liquidFlowStrengthProp = serializedObject.FindProperty("liquid.flowStrength");
            liquidWaveMultiplierProp = serializedObject.FindProperty("liquid.waveMultiplier");

            crystalEmissionStrengthProp = serializedObject.FindProperty("crystal.emissionStrength");
            crystalTransmissionProp = serializedObject.FindProperty("crystal.transmissionMultiplier");
            crystalAbsorptionProp = serializedObject.FindProperty("crystal.absorptionMultiplier");
            crystalCrackIntensityProp = serializedObject.FindProperty("crystal.crackIntensity");
        }

        public override void OnInspectorGUI()
        {
            B2URSUVMono rsuvMono = (B2URSUVMono)target;
            if (rsuvMono.rsuvObject == null && rsuvMono.activeType != B2URSUVMono.RSUVType.Unassigned)
            {
                rsuvMono.ApplyRSUV();
            }

            serializedObject.Update();

            uint packedVal = rsuvMono.rsuvObject != null ? rsuvMono.rsuvObject.rsuv : 0u;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Packed RSUV", $"{packedVal} (0x{packedVal:X8})", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(activeTypeProp, ActiveTypeLabel);
            if (GUILayout.Button(AutoDetectBtnContent, GUILayout.Width(90f)))
            {
                foreach (Object obj in targets)
                {
                    if (obj is B2URSUVMono t)
                    {
                        Undo.RecordObject(t, "Auto-Detect RSUV Type");
                        t.AutoDetectType();
                        t.ApplyRSUV();
                        EditorUtility.SetDirty(t);
                    }
                }
                serializedObject.Update();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            switch ((B2URSUVMono.RSUVType)activeTypeProp.enumValueIndex)
            {
                case B2URSUVMono.RSUVType.Unassigned:
                    EditorGUILayout.HelpBox("No valid B2U Master shader detected on this object. Ensure a B2U material is assigned and click 'Auto-Detect'.", MessageType.Warning);
                    break;

                case B2URSUVMono.RSUVType.Solid:
                    EditorGUILayout.LabelField("Solid Properties", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(solidIsPaintedProp, IsPaintedLabel);
                    EditorGUILayout.PropertyField(solidPaletteIndexProp, PaletteIndexLabel);
                    EditorGUILayout.PropertyField(solidPaintIndexProp, PaintIndexLabel);
                    EditorGUILayout.PropertyField(solidEmissionStrengthProp, SolidEmissionLabel);
                    EditorGUILayout.PropertyField(solidNoiseOffsetProp, NoiseOffsetLabel);
                    EditorGUILayout.PropertyField(solidCrackIntensityProp, CrackIntensityLabel);
                    break;

                case B2URSUVMono.RSUVType.Liquid:
                    int matId = materialIDProp.intValue;
                    string matLabel = matId == int.MaxValue
                        ? "Unresolved (Click Refresh)"
                        : $"{Mathf.Abs(matId)} ({(matId < 0 ? "Biome Dynamic" : "Independent")})";
                    EditorGUILayout.LabelField("Resolved Material ID", matLabel);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(ApplyImmersionBtnContent))
                    {
                        rsuvMono.PushLiquidToImmersionEffect();
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button(RefreshLiquidDataBtnContent))
                    {
                        foreach (Object obj in targets)
                        {
                            if (obj is B2URSUVMono t)
                            {
                                Undo.RecordObject(t, "Refresh Liquid Material ID");
                                t.UpdateMaterialID();
                                if (Application.isPlaying) t.CacheLiquidMeshData();
                                EditorUtility.SetDirty(t);
                            }
                        }
                        serializedObject.Update();
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Liquid Properties", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(liquidEmissionStrengthProp, LiquidEmissionLabel);
                    EditorGUILayout.PropertyField(liquidFlowStrengthProp, FlowStrengthLabel);
                    EditorGUILayout.PropertyField(liquidWaveMultiplierProp, WaveHeightLabel);
                    break;

                case B2URSUVMono.RSUVType.Crystal:
                    EditorGUILayout.LabelField("Crystal Properties", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(crystalEmissionStrengthProp, CrystalEmissionLabel);
                    EditorGUILayout.PropertyField(crystalTransmissionProp, CrystalTransmissionLabel);
                    EditorGUILayout.PropertyField(crystalAbsorptionProp, CrystalAbsorptionLabel);
                    EditorGUILayout.PropertyField(crystalCrackIntensityProp, CrackIntensityLabel);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                foreach (Object obj in targets)
                {
                    if (obj is B2URSUVMono t)
                    {
                        Undo.RecordObject(t, "Update RSUV");
                        t.ApplyRSUV();
                        EditorUtility.SetDirty(t);
                    }
                }
                SceneView.RepaintAll();
            }
        }
    }
}
#endif