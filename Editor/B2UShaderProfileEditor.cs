#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Custom Inspector for authoring B2UShaderProfile assets, estimating relative GPU shader cost,
    /// managing URP Renderer Feature bindings, and configuring global shader properties.
    /// </summary>
    [CustomEditor(typeof(B2UShaderProfile))]
    public class B2UShaderProfileEditor : Editor
    {
        private bool showTextureBindings = false;
        private bool showGlobalProperties = false;
        private bool showSolidProperties = false;
        private bool showCrystalProperties = false;
        private bool showLiquidProperties = false;
        private bool showFullscreenProperties = false;
        private bool showRecolorList = false;
        private bool showStylizeList = false;
        private bool showStatusEffectList = false;

        private int presetSelectedIndex = -1;
        private int listCurrentlyEditing = -1;
        private int maxCost = 1;
        private float currentCost = 0f;

        private ReorderableList effectOrderList;
        private readonly List<EffectOrderInfo> activeEffectsOrder = new List<EffectOrderInfo>(4);
        private B2UFullscreenEffects currentFsFeat;
        private int lastEffectMask = -1;

        private static GUIStyle s_RichLabelStyle;
        private static string[] s_CachedLayerNames;
        private static int[] s_CachedLayerIndices;

        private static readonly GUIContent PaletteFieldLabel = new GUIContent(
            "Palette for Profile",
            "The B2UPaletteData asset providing the Color LUT, Property LUT, Detail Atlas, and auxiliary textures for this profile."
        );
        private static readonly GUIContent SetActiveProfileBtnContent = new GUIContent(
            "Set as Active Profile",
            "Binds this B2UShaderProfile as the active global profile and pushes all properties to the master shaders."
        );

        private class EffectOrderInfo
        {
            public string id;
            public string displayName;
            public int order;
        }

        private void OnEnable()
        {
            showTextureBindings = EditorPrefs.GetBool("B2U_Fold_Textures", false);
            showGlobalProperties = EditorPrefs.GetBool("B2U_Fold_Globals", false);
            showSolidProperties = EditorPrefs.GetBool("B2U_Fold_Solids", false);
            showCrystalProperties = EditorPrefs.GetBool("B2U_Fold_Crystals", false);
            showLiquidProperties = EditorPrefs.GetBool("B2U_Fold_Liquids", false);
            showFullscreenProperties = EditorPrefs.GetBool("B2U_Fold_Fullscreen", false);
            showRecolorList = EditorPrefs.GetBool("B2U_Fold_Recolor", false);
            showStylizeList = EditorPrefs.GetBool("B2U_Fold_Stylize", false);
            showStatusEffectList = EditorPrefs.GetBool("B2U_Fold_Status", false);

            CacheLayerMaskData();
            maxCost = GetMaxCost();

            if (target is B2UShaderProfile preset)
            {
                preset.InitializeDictionaries();
                currentCost = GetCurrentCost(preset);
            }

            Undo.undoRedoEvent += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoEvent -= OnUndoRedo;

            EditorPrefs.SetBool("B2U_Fold_Textures", showTextureBindings);
            EditorPrefs.SetBool("B2U_Fold_Globals", showGlobalProperties);
            EditorPrefs.SetBool("B2U_Fold_Solids", showSolidProperties);
            EditorPrefs.SetBool("B2U_Fold_Crystals", showCrystalProperties);
            EditorPrefs.SetBool("B2U_Fold_Liquids", showLiquidProperties);
            EditorPrefs.SetBool("B2U_Fold_Fullscreen", showFullscreenProperties);
            EditorPrefs.SetBool("B2U_Fold_Recolor", showRecolorList);
            EditorPrefs.SetBool("B2U_Fold_Stylize", showStylizeList);
            EditorPrefs.SetBool("B2U_Fold_Status", showStatusEffectList);
        }

        private void OnUndoRedo(in UndoRedoInfo info)
        {
            if (target is B2UShaderProfile preset)
            {
                preset.RestoreSavedValues();
                currentCost = GetCurrentCost(preset);
                if (B2URuntimeUtility.ActiveProfile == preset)
                {
                    B2URuntimeUtility.PushAllGlobals();
                    SyncRenderFeatureStates(preset);
                    SceneView.RepaintAll();
                }
                Repaint();
            }
        }

        private static void CacheLayerMaskData()
        {
            List<string> names = new List<string>(32);
            List<int> indices = new List<int>(32);

            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    names.Add(layerName);
                    indices.Add(i);
                }
            }

            s_CachedLayerNames = names.ToArray();
            s_CachedLayerIndices = indices.ToArray();
        }

        public override void OnInspectorGUI()
        {
            B2UShaderProfile preset = (B2UShaderProfile)target;

            if (preset.propertyBlocks == null)
            {
                preset.InitializeDictionaries();
            }

            currentCost = GetCurrentCost(preset);
            EditorGUIUtility.labelWidth = Mathf.Max(220f, EditorGUIUtility.currentViewWidth * 0.45f);

            EditorGUILayout.Space(5f);

            if (B2URuntimeUtility.ActiveProfile != preset)
            {
                EditorGUILayout.HelpBox("The profile you are editing is not the current active profile. Changes will not be visible in the scene until this profile is activated.", MessageType.Warning);
                if (GUILayout.Button(SetActiveProfileBtnContent, GUILayout.Height(26f)))
                {
                    B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(preset);
                    SyncRenderFeatureStates(preset);
                    SceneView.RepaintAll();
                }
                EditorGUILayout.Space(5f);
            }

            if (B2URuntimeManager.Instance == null)
            {
                B2URuntimeUtility.TryAutoFindRuntimeManager();
                if (B2URuntimeManager.Instance == null)
                {
                    EditorGUILayout.HelpBox("There is no B2URuntimeManager in the active scene. Runtime animations, liquid waves, and fullscreen triggers will not function.", MessageType.Error);
                    if (GUILayout.Button("Auto Setup Runtime Manager", GUILayout.Height(25f)))
                    {
                        B2URuntimeUtility.AutoSetupRuntimeManager();
                    }
                    EditorGUILayout.Space(5f);
                }
            }

            EditorGUILayout.Space(5f);

            // Palette Binding
            EditorGUI.BeginChangeCheck();
            B2UPaletteData newPalette = (B2UPaletteData)EditorGUILayout.ObjectField(PaletteFieldLabel, preset.paletteData, typeof(B2UPaletteData), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(preset, "Change B2U Palette");
                preset.paletteData = newPalette;
                preset.GetPaletteTextures();
                preset.SyncSavedData();

                if (B2URuntimeUtility.ActiveProfile == preset)
                {
                    B2URuntimeUtility.PushAllGlobals();
                    SceneView.RepaintAll();
                }
                EditorUtility.SetDirty(preset);
            }

            if (preset.paletteData != null)
            {
                string missingTextures = "";
                bool missing = false;
                if (preset.paletteData.colorLUT == null) { missingTextures += "- Color Emission LUT\n"; missing = true; }
                if (preset.paletteData.propertyLUT == null) { missingTextures += "- Property LUT\n"; missing = true; }
                if (preset.paletteData.iridescenceLUT == null) { missingTextures += "- Iridescence LUT\n"; missing = true; }
                if (preset.paletteData.emissionPatterns == null) { missingTextures += "- Emission Patterns\n"; missing = true; }
                if (preset.paletteData.detailAtlas == null) { missingTextures += "- Detail Map Atlas\n"; missing = true; }
                if (preset.paletteData.noise3D == null) { missingTextures += "- 3D Noise Texture\n"; missing = true; }
                if (preset.paletteData.paintPalette == null) { missingTextures += "- Paint Palette\n"; missing = true; }

                if (missing)
                {
                    EditorGUILayout.HelpBox($"WARNING: Palette '{preset.paletteData.paletteName}' is missing texture references:\n{missingTextures.TrimEnd()}", MessageType.Error);
                }
            }

            EditorGUILayout.Space(5f);

            // Relative Shader Cost Indicator
            float costRatio = maxCost > 0 ? (currentCost / maxCost) : 0f;
            float costPercentage = Mathf.Clamp(costRatio * 100f, 0f, 100f);
            string color = "green";
            if (costPercentage > 50f) color = "yellow";
            if (costPercentage > 75f) color = "orange";
            if (costPercentage > 90f) color = "red";

            if (s_RichLabelStyle == null)
            {
                s_RichLabelStyle = new GUIStyle(EditorStyles.label) { richText = true };
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(new GUIContent(
                $"Current Profile Cost: <b><color={color}>{costPercentage:F0}%</color></b>",
                "An approximation of the relative GPU cost for this profile (0% = lowest B2U shader overhead, 100% = all high-cost features active)."
            ), s_RichLabelStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5f);

            // 1. Biome Textures
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showTextureBindings = EditorGUILayout.Foldout(showTextureBindings, "Biome Textures", true, EditorStyles.foldoutHeader);
            if (showTextureBindings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.BiomeTexture1);
                DrawProperty(preset, B2UShaderProperty.BiomeTexture2);
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 2. Global Properties
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showGlobalProperties = EditorGUILayout.Foldout(showGlobalProperties, "Global Properties", true, EditorStyles.foldoutHeader);
            if (showGlobalProperties)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.LightModel);
                if (preset.propertyBlocks.TryGetValue(B2UShaderProperty.LightModel, out var lightModelBlock))
                {
                    uint lightModel = (uint)lightModelBlock.value;
                    if (lightModel == 2u || lightModel == 5u || lightModel == 7u)
                    {
                        DrawProperty(preset, B2UShaderProperty.LightControlFloat);
                    }
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.WorldSize);
                DrawProperty(preset, B2UShaderProperty.GlobalEmissionStrength);
                DrawProperty(preset, B2UShaderProperty.GlobalEmissionSpeed);
                DrawProperty(preset, B2UShaderProperty.GlobalCracksUseEmissionColor);
                if (preset.propertyBlocks.TryGetValue(B2UShaderProperty.GlobalCracksUseEmissionColor, out var cracksUseEmissionColorBlock))
                {
                    if (!(bool)cracksUseEmissionColorBlock.value)
                    {
                        DrawProperty(preset, B2UShaderProperty.GlobalCrackColor);
                    }
                }
                DrawProperty(preset, B2UShaderProperty.GlobalCrackBrightness);
                DrawProperty(preset, B2UShaderProperty.IridescenceMode);
                DrawProperty(preset, B2UShaderProperty.VertexRGamma);
                DrawProperty(preset, B2UShaderProperty.VertexGGamma);
                DrawProperty(preset, B2UShaderProperty.VertexBGamma);
                EditorGUILayout.EndVertical();

                // Volumetric Refraction Section
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool prevVrEnabled = GetEditorBool(preset, B2UShaderProperty.VolumetricRefractionEnabled);
                DrawProperty(preset, B2UShaderProperty.VolumetricRefractionEnabled);
                bool isVrEnabled = GetEditorBool(preset, B2UShaderProperty.VolumetricRefractionEnabled);

                if (prevVrEnabled != isVrEnabled && preset == B2URuntimeUtility.ActiveProfile)
                {
                    SyncRenderFeatureStates(preset);
                }

                if (isVrEnabled)
                {
                    var vrFeat = B2URuntimeUtility.RefractionFeature ?? B2URuntimeUtility.TryAutoFindRefractionFeature(true);
                    if (vrFeat != null)
                    {
                        DrawProperty(preset, B2UShaderProperty.VolumetricLayer);
                        DrawProperty(preset, B2UShaderProperty.RefractionQualityMode);

                        uint quality = 0u;
                        if (preset.propertyBlocks.TryGetValue(B2UShaderProperty.RefractionQualityMode, out var qBlock) && qBlock?.value is uint qval)
                        {
                            quality = qval;
                        }

                        if (quality > 0u)
                        {
                            DrawProperty(preset, B2UShaderProperty.BackfaceBufferResolution);
                            DrawProperty(preset, B2UShaderProperty.ColorCopyResolution);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("This profile does not have an active B2UVolumetricRefraction feature on the URP renderer.", MessageType.Warning);
                        if (GUILayout.Button("Auto Setup Refraction Feature", GUILayout.Height(22f)))
                        {
                            B2URuntimeUtility.AutoSetupRefractionFeature();
                            SyncRenderFeatureStates(preset);
                            SceneView.RepaintAll();
                        }
                    }
                }
                EditorGUILayout.EndVertical();

                // Edge Detection Section
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool prevEdEnabled = GetEditorBool(preset, B2UShaderProperty.EdgeDetectionEnabled);
                DrawProperty(preset, B2UShaderProperty.EdgeDetectionEnabled);
                bool isEdEnabled = GetEditorBool(preset, B2UShaderProperty.EdgeDetectionEnabled);

                if (prevEdEnabled != isEdEnabled && preset == B2URuntimeUtility.ActiveProfile)
                {
                    SyncRenderFeatureStates(preset);
                }

                if (isEdEnabled)
                {
                    var edFeat = B2URuntimeUtility.EdgeFeature ?? B2URuntimeUtility.TryAutoFindEdgeFeature(true);
                    if (edFeat != null)
                    {
                        DrawProperty(preset, B2UShaderProperty.EdgeDetectionMode);
                        DrawProperty(preset, B2UShaderProperty.EdgeDetectionSpace);
                        DrawProperty(preset, B2UShaderProperty.EdgeDetectionBufferResolution);
                        DrawProperty(preset, B2UShaderProperty.EdgeDetectionSize);

                        if (GetEditorBool(preset, B2UShaderProperty.EdgeDetectionMode))
                        {
                            DrawProperty(preset, B2UShaderProperty.EdgeDetectionSensitivity);
                        }

                        DrawProperty(preset, B2UShaderProperty.EdgeDetectionColor);
                        DrawProperty(preset, B2UShaderProperty.EdgeDetectionLayermask);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("This profile does not have an active B2UEdgeDetection feature on the URP renderer.", MessageType.Warning);
                        if (GUILayout.Button("Auto Setup Edge Feature", GUILayout.Height(22f)))
                        {
                            B2URuntimeUtility.AutoSetupEdgeFeature();
                            SyncRenderFeatureStates(preset);
                            SceneView.RepaintAll();
                        }
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 3. Solid Properties
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showSolidProperties = EditorGUILayout.Foldout(showSolidProperties, "Solid Properties", true, EditorStyles.foldoutHeader);
            if (showSolidProperties)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.TriplanarBlendPower);
                DrawProperty(preset, B2UShaderProperty.UseNoiseForSolids);

                if ((bool)preset.propertyBlocks[B2UShaderProperty.UseNoiseForSolids].value)
                {
                    DrawProperty(preset, B2UShaderProperty.UseProceduralNoiseForSolids);
                    DrawProperty(preset, B2UShaderProperty.SolidNoiseScale);
                    DrawProperty(preset, B2UShaderProperty.SolidNoiseStrength);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.SolidsHaveSmoothEdges);
                DrawProperty(preset, B2UShaderProperty.EdgeMode);
                DrawProperty(preset, B2UShaderProperty.EdgeStyle);

                uint edgeStyle = (uint)preset.propertyBlocks[B2UShaderProperty.EdgeStyle].value;
                if (edgeStyle == 2u) // Colored Hard Edge
                {
                    DrawProperty(preset, B2UShaderProperty.DetailMapAffectsHardEdges);
                    DrawProperty(preset, B2UShaderProperty.SolidHardEdgeColor);
                    DrawProperty(preset, B2UShaderProperty.SolidEdgeStrength);
                }
                else if (edgeStyle == 0u) // Soft Edge
                {
                    DrawProperty(preset, B2UShaderProperty.SolidEdgeStrength);
                }
                else // Hard Edge
                {
                    DrawProperty(preset, B2UShaderProperty.DetailMapAffectsHardEdges);
                    DrawProperty(preset, B2UShaderProperty.SolidEdgeStrength);
                }

                DrawProperty(preset, B2UShaderProperty.SolidEdgeThickness);
                DrawProperty(preset, B2UShaderProperty.SolidEdgePower);
                DrawProperty(preset, B2UShaderProperty.SolidCrackScale);
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 4. Crystal Properties
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showCrystalProperties = EditorGUILayout.Foldout(showCrystalProperties, "Crystal Properties", true, EditorStyles.foldoutHeader);
            if (showCrystalProperties)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.CrystalFlareMode);
                DrawProperty(preset, B2UShaderProperty.CrystalCrackScale);
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 5. Liquid Properties
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showLiquidProperties = EditorGUILayout.Foldout(showLiquidProperties, "Liquid Properties", true, EditorStyles.foldoutHeader);
            if (showLiquidProperties)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.UseProceduralNoiseForLiquids);
                DrawProperty(preset, B2UShaderProperty.UseBubbleAdvection);

                if ((bool)preset.propertyBlocks[B2UShaderProperty.UseBubbleAdvection].value)
                {
                    DrawProperty(preset, B2UShaderProperty.LiquidBubbleMultiplier);
                }

                if ((bool)preset.propertyBlocks[B2UShaderProperty.UseProceduralNoiseForLiquids].value)
                {
                    DrawProperty(preset, B2UShaderProperty.UseAngleOffset);
                    if ((bool)preset.propertyBlocks[B2UShaderProperty.UseAngleOffset].value)
                    {
                        DrawProperty(preset, B2UShaderProperty.LiquidAngleOffsetSpeed);
                    }
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.ChromaticAbberationForLiquids);
                DrawProperty(preset, B2UShaderProperty.LiquidRefractionDistortionStrength);
                DrawProperty(preset, B2UShaderProperty.CausticMode);
                if ((uint)preset.propertyBlocks[B2UShaderProperty.CausticMode].value != 0u)
                {
                    DrawProperty(preset, B2UShaderProperty.LiquidCausticStrength);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.LiquidShoreDistance);
                bool procNoise = (bool)preset.propertyBlocks[B2UShaderProperty.UseProceduralNoiseForLiquids].value;
                bool displacement = (bool)preset.propertyBlocks[B2UShaderProperty.LiquidsHaveVertexDisplacement].value;
                if (!procNoise || !displacement)
                {
                    DrawProperty(preset, B2UShaderProperty.LiquidNormalStrength);
                }
                DrawProperty(preset, B2UShaderProperty.LiquidEmissionHasFlow);
                DrawProperty(preset, B2UShaderProperty.LiquidsHaveVertexDisplacement);
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawProperty(preset, B2UShaderProperty.TessellatedLiquidsResampleInFragment);
                DrawProperty(preset, B2UShaderProperty.TessFactor);
                DrawProperty(preset, B2UShaderProperty.MinTessDistance);
                DrawProperty(preset, B2UShaderProperty.MaxTessDistance);
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 6. Fullscreen Properties
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showFullscreenProperties = EditorGUILayout.Foldout(showFullscreenProperties, "Fullscreen Properties", true, EditorStyles.foldoutHeader);
            if (showFullscreenProperties)
            {
                EditorGUI.indentLevel++;
                bool prevFsEnabled = GetEditorBool(preset, B2UShaderProperty.FullscreenEffectsEnabled);
                DrawProperty(preset, B2UShaderProperty.FullscreenEffectsEnabled);
                bool isFsEnabled = GetEditorBool(preset, B2UShaderProperty.FullscreenEffectsEnabled);

                if (prevFsEnabled != isFsEnabled && preset == B2URuntimeUtility.ActiveProfile)
                {
                    SyncRenderFeatureStates(preset);
                }

                if (isFsEnabled)
                {
                    var fsFeat = B2URuntimeUtility.FullscreenFeature ?? B2URuntimeUtility.TryAutoFindFullscreenFeature(true);
                    if (fsFeat != null)
                    {
                        currentFsFeat = fsFeat;

                        bool currentImmersion = (bool)preset.propertyBlocks[B2UShaderProperty.ImmersionActive].value;
                        bool currentRecolor = (bool)preset.propertyBlocks[B2UShaderProperty.RecolorActive].value;
                        bool currentStylize = (bool)preset.propertyBlocks[B2UShaderProperty.StylizeActive].value;
                        bool currentStatus = (bool)preset.propertyBlocks[B2UShaderProperty.StatusActive].value;

                        int currentMask = (currentImmersion ? 1 : 0) | (currentRecolor ? 2 : 0) | (currentStylize ? 4 : 0) | (currentStatus ? 8 : 0);

                        if (effectOrderList == null || currentMask != lastEffectMask)
                        {
                            lastEffectMask = currentMask;
                            activeEffectsOrder.Clear();

                            if (currentImmersion) activeEffectsOrder.Add(new EffectOrderInfo { id = "Immersion", displayName = "Liquid Immersion", order = fsFeat.settings.immersionDrawOrder });
                            if (currentRecolor) activeEffectsOrder.Add(new EffectOrderInfo { id = "Recolor", displayName = "Recolor", order = fsFeat.settings.recolorDrawOrder });
                            if (currentStylize) activeEffectsOrder.Add(new EffectOrderInfo { id = "Stylize", displayName = "Stylize", order = fsFeat.settings.stylizeDrawOrder });
                            if (currentStatus) activeEffectsOrder.Add(new EffectOrderInfo { id = "Status", displayName = "Status Effects", order = fsFeat.settings.statusDrawOrder });

                            activeEffectsOrder.Sort((a, b) => a.order.CompareTo(b.order));

                            if (effectOrderList == null) SetupEffectOrderList();
                        }

                        if (activeEffectsOrder.Count > 1)
                        {
                            EditorGUILayout.Space(5f);
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Space(EditorGUI.indentLevel * 15f);
                            EditorGUILayout.BeginVertical();
                            effectOrderList.DoLayoutList();
                            EditorGUILayout.EndVertical();
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.Space(5f);
                        }

                        // Liquid Immersion
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        DrawProperty(preset, B2UShaderProperty.ImmersionActive);
                        bool immersionEnabled = (bool)preset.propertyBlocks[B2UShaderProperty.ImmersionActive].value;
                        if (preset == B2URuntimeUtility.ActiveProfile) fsFeat.settings.immersionEnabled = immersionEnabled;
                        if (immersionEnabled)
                        {
                            EditorGUI.indentLevel++;
                            DrawProperty(preset, B2UShaderProperty.ImmersionTransitionEnabled);
                            if ((bool)preset.propertyBlocks[B2UShaderProperty.ImmersionTransitionEnabled].value)
                            {
                                DrawProperty(preset, B2UShaderProperty.ImmersionTransitionDistance);
                            }
                            DrawProperty(preset, B2UShaderProperty.ImmersionClarity);
                            DrawProperty(preset, B2UShaderProperty.ImmersionDistortion);
                            DrawProperty(preset, B2UShaderProperty.ImmersionCausticStrength);
                            EditorGUI.indentLevel--;
                        }
                        EditorGUILayout.EndVertical();

                        // Recolor
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        DrawProperty(preset, B2UShaderProperty.RecolorActive);
                        bool recolorEnabled = (bool)preset.propertyBlocks[B2UShaderProperty.RecolorActive].value;
                        if (preset == B2URuntimeUtility.ActiveProfile) fsFeat.settings.recolorEnabled = recolorEnabled;
                        if (recolorEnabled)
                        {
                            EditorGUI.indentLevel++;
                            DrawProperty(preset, B2UShaderProperty.RecolorBlend);
                            DrawProperty(preset, B2UShaderProperty.RecolorPresets);
                            B2URecolorPresetCollection recolorCollection = preset.propertyBlocks[B2UShaderProperty.RecolorPresets].value as B2URecolorPresetCollection;
                            if (recolorCollection != null && recolorCollection.presets != null)
                            {
                                if (B2URuntimeUtility.ActiveProfile == preset)
                                {
                                    showRecolorList = EditorGUILayout.Foldout(showRecolorList, "Recolor List", true, EditorStyles.foldoutHeader);
                                    if (showRecolorList)
                                    {
                                        string[] recolorNames = new string[recolorCollection.presets.Count];
                                        for (int i = 0; i < recolorCollection.presets.Count; i++)
                                        {
                                            var p = recolorCollection.presets[i];
                                            recolorNames[i] = p != null ? $"{p.name} (Source: {p.source})" : "";
                                        }
                                        int newIndex = DrawPresetList(recolorNames, recolorCollection.activePreset, 0);
                                        if (newIndex != recolorCollection.activePreset)
                                        {
                                            Undo.RecordObject(recolorCollection, "Change Active Recolor Preset");
                                            B2URuntimeUtility.SetRecolorPreset(newIndex);
                                            EditorUtility.SetDirty(recolorCollection);
                                            SceneView.RepaintAll();
                                        }
                                    }
                                }
                                else
                                {
                                    EditorGUILayout.HelpBox("This shader profile is not the active profile. Activate it to preview and switch recolor presets.", MessageType.Warning);
                                }
                            }
                            EditorGUI.indentLevel--;
                        }
                        EditorGUILayout.EndVertical();

                        // Stylize
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        DrawProperty(preset, B2UShaderProperty.StylizeActive);
                        bool stylizeEnabled = (bool)preset.propertyBlocks[B2UShaderProperty.StylizeActive].value;
                        if (preset == B2URuntimeUtility.ActiveProfile) fsFeat.settings.stylizeEnabled = stylizeEnabled;
                        if (stylizeEnabled)
                        {
                            EditorGUI.indentLevel++;
                            DrawProperty(preset, B2UShaderProperty.StylizeBlend);
                            DrawProperty(preset, B2UShaderProperty.StylizePresets);
                            B2UStylizePresetCollection stylizeCollection = preset.propertyBlocks[B2UShaderProperty.StylizePresets].value as B2UStylizePresetCollection;
                            if (stylizeCollection != null && stylizeCollection.presets != null)
                            {
                                if (B2URuntimeUtility.ActiveProfile == preset)
                                {
                                    showStylizeList = EditorGUILayout.Foldout(showStylizeList, "Stylize List", true, EditorStyles.foldoutHeader);
                                    if (showStylizeList)
                                    {
                                        string[] stylizeNames = new string[stylizeCollection.presets.Count];
                                        for (int i = 0; i < stylizeCollection.presets.Count; i++)
                                        {
                                            var p = stylizeCollection.presets[i];
                                            stylizeNames[i] = p != null ? p.name : "";
                                        }
                                        int newIndex = DrawPresetList(stylizeNames, stylizeCollection.activePreset, 1);
                                        if (newIndex != stylizeCollection.activePreset)
                                        {
                                            Undo.RecordObject(stylizeCollection, "Change Active Stylize Preset");
                                            B2URuntimeUtility.SetStylizePreset(newIndex);
                                            EditorUtility.SetDirty(stylizeCollection);
                                            SceneView.RepaintAll();
                                        }
                                    }
                                }
                                else
                                {
                                    EditorGUILayout.HelpBox("This shader profile is not the active profile. Activate it to preview and switch stylize presets.", MessageType.Warning);
                                }
                            }
                            EditorGUI.indentLevel--;
                        }
                        EditorGUILayout.EndVertical();

                        // Status Effects & Injury
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        DrawProperty(preset, B2UShaderProperty.StatusActive);
                        bool statusEnabled = (bool)preset.propertyBlocks[B2UShaderProperty.StatusActive].value;
                        if (preset == B2URuntimeUtility.ActiveProfile) fsFeat.settings.statusEnabled = statusEnabled;
                        if (statusEnabled)
                        {
                            EditorGUI.indentLevel++;

                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            DrawProperty(preset, B2UShaderProperty.InjuryStrength);
                            DrawProperty(preset, B2UShaderProperty.InjuryColor);
                            DrawProperty(preset, B2UShaderProperty.InjuryVignetteStrength);
                            DrawProperty(preset, B2UShaderProperty.InjuryDarkenAmount);
                            DrawProperty(preset, B2UShaderProperty.InjuryPulseSpeed);
                            EditorGUILayout.EndVertical();

                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            DrawProperty(preset, B2UShaderProperty.StatusStrength);
                            DrawProperty(preset, B2UShaderProperty.StatusPresets);
                            B2UStatusEffectPresetCollection statusCollection = preset.propertyBlocks[B2UShaderProperty.StatusPresets].value as B2UStatusEffectPresetCollection;
                            if (statusCollection != null && statusCollection.presets != null)
                            {
                                if (B2URuntimeUtility.ActiveProfile == preset)
                                {
                                    showStatusEffectList = EditorGUILayout.Foldout(showStatusEffectList, "Status Effect List", true, EditorStyles.foldoutHeader);
                                    if (showStatusEffectList)
                                    {
                                        string[] statusNames = new string[statusCollection.presets.Count];
                                        for (int i = 0; i < statusCollection.presets.Count; i++)
                                        {
                                            var p = statusCollection.presets[i];
                                            statusNames[i] = p != null ? $"{p.name} (Type: {p.statusEffectType})" : "";
                                        }
                                        int newIndex = DrawPresetList(statusNames, statusCollection.activePreset, 2);
                                        if (newIndex != statusCollection.activePreset)
                                        {
                                            Undo.RecordObject(statusCollection, "Change Active Status Effect Preset");
                                            B2URuntimeUtility.SetStatusEffectPreset(newIndex);
                                            EditorUtility.SetDirty(statusCollection);
                                            SceneView.RepaintAll();
                                        }
                                    }
                                }
                                else
                                {
                                    EditorGUILayout.HelpBox("This shader profile is not the active profile. Activate it to preview and switch status effect presets.", MessageType.Warning);
                                }
                            }
                            EditorGUILayout.EndVertical();

                            EditorGUI.indentLevel--;
                        }
                        EditorGUILayout.EndVertical();
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("This profile does not have an active B2UFullscreenEffects feature on the URP renderer.", MessageType.Warning);
                        if (GUILayout.Button("Auto Setup Fullscreen Feature", GUILayout.Height(22f)))
                        {
                            B2URuntimeUtility.AutoSetupFullscreenFeature();
                            SyncRenderFeatureStates(preset);
                            SceneView.RepaintAll();
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10f);
        }

        private static bool GetEditorBool(B2UShaderProfile preset, B2UShaderProperty key)
        {
            return preset.propertyBlocks.TryGetValue(key, out var block) && block?.value is bool b && b;
        }

        /// <summary>
        /// Synchronizes URP ScriptableRendererFeature active states and transparent layer masks when toggled on the active profile.
        /// </summary>
        public static void SyncRenderFeatureStates(B2UShaderProfile preset)
        {
            if (preset == null || preset != B2URuntimeUtility.ActiveProfile) return;

            bool isVrEnabled = GetEditorBool(preset, B2UShaderProperty.VolumetricRefractionEnabled);
            var vrFeat = B2URuntimeUtility.RefractionFeature ?? B2URuntimeUtility.TryAutoFindRefractionFeature(true);
            if (vrFeat != null)
            {
                B2URuntimeUtility.SetFeatureActiveState(vrFeat, isVrEnabled);
            }
            if (isVrEnabled && vrFeat != null)
            {
                Shader.EnableKeyword("_B2U_VREF_ENABLED");
                B2URuntimeUtility.SetRendererTransparentLayerMask(0);
            }
            else
            {
                Shader.DisableKeyword("_B2U_VREF_ENABLED");
                B2URuntimeUtility.SetRendererTransparentLayerMask(-1);
            }

            bool isEdEnabled = GetEditorBool(preset, B2UShaderProperty.EdgeDetectionEnabled);
            var edFeat = B2URuntimeUtility.EdgeFeature ?? B2URuntimeUtility.TryAutoFindEdgeFeature(true);
            if (edFeat != null)
            {
                B2URuntimeUtility.SetFeatureActiveState(edFeat, isEdEnabled);
            }

            bool isFsEnabled = GetEditorBool(preset, B2UShaderProperty.FullscreenEffectsEnabled);
            var fsFeat = B2URuntimeUtility.FullscreenFeature ?? B2URuntimeUtility.TryAutoFindFullscreenFeature(true);
            if (fsFeat != null)
            {
                B2URuntimeUtility.SetFeatureActiveState(fsFeat, isFsEnabled);
            }
        }

        private int DrawPresetList(string[] names, int activeIndex, int listIndex)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i])) continue;

                bool isApplied = (i == activeIndex);
                bool isSelected = (i == presetSelectedIndex);

                if (isApplied)
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                else if (isSelected && listCurrentlyEditing == listIndex)
                    GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);
                else
                    GUI.backgroundColor = Color.white;

                if (GUILayout.Button(names[i], EditorStyles.miniButton))
                {
                    listCurrentlyEditing = listIndex;
                    presetSelectedIndex = i;
                    GUI.FocusControl(null);
                }

                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);

            bool canActivate = presetSelectedIndex != activeIndex &&
                               presetSelectedIndex >= 0 &&
                               presetSelectedIndex < names.Length &&
                               !string.IsNullOrEmpty(names[presetSelectedIndex]) &&
                               listCurrentlyEditing == listIndex;

            using (new EditorGUI.DisabledScope(!canActivate))
            {
                if (GUILayout.Button("Set Active", GUILayout.Height(25f)))
                {
                    activeIndex = presetSelectedIndex;
                }
            }

            EditorGUILayout.EndHorizontal();

            return activeIndex;
        }

        private void SetupEffectOrderList()
        {
            effectOrderList = new ReorderableList(activeEffectsOrder, typeof(EffectOrderInfo), true, true, false, false)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Effect Draw Order (Top renders first)");
                },
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2f;
                    EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), activeEffectsOrder[index].displayName);
                },
                onReorderCallback = list =>
                {
                    if (currentFsFeat != null)
                    {
                        Undo.RecordObject(currentFsFeat, "Change Effect Draw Order");

                        for (int i = 0; i < activeEffectsOrder.Count; i++)
                        {
                            activeEffectsOrder[i].order = i;

                            string id = activeEffectsOrder[i].id;
                            if (id == "Immersion") currentFsFeat.settings.immersionDrawOrder = i;
                            else if (id == "Recolor") currentFsFeat.settings.recolorDrawOrder = i;
                            else if (id == "Stylize") currentFsFeat.settings.stylizeDrawOrder = i;
                            else if (id == "Status") currentFsFeat.settings.statusDrawOrder = i;
                        }

                        EditorUtility.SetDirty(currentFsFeat);
                        AssetDatabase.SaveAssetIfDirty(currentFsFeat);

                        SceneView.RepaintAll();
                        InternalEditorUtility.RepaintAllViews();
                    }
                }
            };
        }


        // Need to refactor to combine this with the DrawProperty. Used to be two separate dictionaries but merged them as the separation wasn't needed
        private void DrawProperty(B2UShaderProfile preset, B2UShaderProperty key)
        {
            if (!preset.propertyBlocks.TryGetValue(key, out var block)) return;
            if (!propertyDescriptions.TryGetValue(key, out B2UShaderPropertyBlockDescription description))
            {
                description = new B2UShaderPropertyBlockDescription(new int[] { 0 }, block.name, "No description available for this property.", new string[] { "None" });
            }

            float cost = EvaluateBlockCost(block, description);
            string label = description.uiName;
            string tooltip = description.description;
            if (cost != 0f) label = $"{description.uiName} (+{cost:F0}%)";

            if (key == B2UShaderProperty.LightControlFloat &&
                preset.propertyBlocks.TryGetValue(B2UShaderProperty.LightModel, out var lightModelBlock))
            {
                switch ((uint)lightModelBlock.value)
                {
                    case 2u:
                        label = "Posterize Steps";
                        tooltip = "The number of discrete lighting steps used when posterizing in Toon mode. Fewer steps create wider flat cel-shaded bands; more steps create smoother transitions.";
                        block.minMax = new Vector2(2f, 10f);
                        break;
                    case 5u:
                        label = "Shadow Darkness";
                        tooltip = "Controls how dark the complementary colored shadows are in Gooch lighting mode.";
                        block.minMax = new Vector2(-1f, 1f);
                        break;
                    case 7u:
                        label = "Dot Size";
                        tooltip = "Controls the screen-space size of the Ben-Day halftone dots in Pop-Art lighting mode.";
                        block.minMax = new Vector2(0.001f, 0.2f);
                        break;
                }

                float curVal = (float)block.value;
                if (curVal > block.minMax.y || curVal < block.minMax.x)
                {
                    block.SetValue(Mathf.Clamp(curVal, block.minMax.x, block.minMax.y));
                    if (B2URuntimeUtility.ActiveProfile == preset)
                    {
                        B2URuntimeUtility.PushGlobalToMasters(key);
                    }
                }
            }

            EditorGUI.BeginChangeCheck();
            object newVal = RenderDynamicField(block, label, tooltip, description.enumOptions);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(preset, $"Modify {description.uiName}");
                block.SetValue(newVal);
                preset.SyncSavedData();
                EditorUtility.SetDirty(preset);

                if (B2URuntimeUtility.ActiveProfile == preset)
                {
                    B2URuntimeUtility.PushGlobalToMasters(key);
                    SceneView.RepaintAll();
                }
            }
        }

        /**private void DrawProperty(B2UShaderProfile preset, B2UShaderProperty key)
        {
            if (!preset.propertyBlocks.TryGetValue(key, out var block)) return;
            if (!propertyDescriptions.TryGetValue(key, out B2UShaderPropertyBlockDescription description))
            {
                description = new B2UShaderPropertyBlockDescription(new int[] { 0 }, block.name, "No description available for this property.", new string[] { "None" });
            }

            float cost = EvaluateBlockCost(block, description);
            string label = description.uiName;
            if (cost != 0f) label = $"{description.uiName} (+{cost:F0}%)";

            EditorGUI.BeginChangeCheck();
            object newVal = RenderDynamicField(block, label, description.description, description.enumOptions);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(preset, $"Modify {description.uiName}");
                block.SetValue(newVal);
                EditorUtility.SetDirty(preset);

                if (B2URuntimeUtility.ActiveProfile == preset)
                {
                    B2URuntimeUtility.PushGlobalToMasters(key);
                    SceneView.RepaintAll();
                }
            }
        }**/

        private object RenderDynamicField(B2UShaderPropertyBlock block, string label, string tooltip, string[] enumOptions)
        {
            GUIContent content = new GUIContent(label, tooltip);
            switch (block.value)
            {
                case float fVal:
                    if (block.minMax != Vector2.zero) return EditorGUILayout.Slider(content, fVal, block.minMax.x, block.minMax.y);
                    return EditorGUILayout.FloatField(content, fVal);

                case int iVal:
                    if (block.name == "VolumetricLayer") return EditorGUILayout.LayerField(content, iVal);
                    if (block.minMax != Vector2.zero) return EditorGUILayout.IntSlider(content, iVal, (int)block.minMax.x, (int)block.minMax.y);
                    return EditorGUILayout.IntField(content, iVal);

                case bool bVal:
                    return EditorGUILayout.Popup(content, bVal ? 1 : 0, enumOptions ?? disableEnable) == 1;

                case Color colorVal:
                    return EditorGUILayout.ColorField(content, colorVal);

                case Vector2 v2Val:
                    return EditorGUILayout.Vector2Field(content, v2Val);

                case uint uVal:
                    if (enumOptions != null && enumOptions.Length > 0)
                    {
                        return (uint)Mathf.Max(0, EditorGUILayout.Popup(content, (int)uVal, enumOptions));
                    }
                    return (uint)Mathf.Max(0, EditorGUILayout.IntField(content, (int)uVal));

                case LayerMask lVal:
                    if (s_CachedLayerNames == null || s_CachedLayerIndices == null)
                    {
                        CacheLayerMaskData();
                    }

                    int maskVal = lVal.value;
                    int compressedMask = 0;

                    for (int i = 0; i < s_CachedLayerIndices.Length; i++)
                    {
                        if ((maskVal & (1 << s_CachedLayerIndices[i])) != 0)
                        {
                            compressedMask |= (1 << i);
                        }
                    }

                    int newCompressedMask = EditorGUILayout.MaskField(content, compressedMask, s_CachedLayerNames);

                    int newMaskVal = 0;
                    if (newCompressedMask == -1)
                    {
                        newMaskVal = -1;
                    }
                    else
                    {
                        for (int i = 0; i < s_CachedLayerIndices.Length; i++)
                        {
                            if ((newCompressedMask & (1 << i)) != 0)
                            {
                                newMaskVal |= (1 << s_CachedLayerIndices[i]);
                            }
                        }
                    }

                    return (LayerMask)newMaskVal;

                case Texture2D t2dVal:
                    return EditorGUILayout.ObjectField(content, t2dVal, typeof(Texture2D), false);

                case Texture2DArray t2dArrayVal:
                    return EditorGUILayout.ObjectField(content, t2dArrayVal, typeof(Texture2DArray), false);

                case Texture3D t3dVal:
                    return EditorGUILayout.ObjectField(content, t3dVal, typeof(Texture3D), false);
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(block.type))
            {
                return EditorGUILayout.ObjectField(content, (UnityEngine.Object)block.value, block.type, false);
            }

            EditorGUILayout.LabelField(label, $"Unsupported Type: {block.type}");
            return block.value;
        }

        private int GetMaxCost()
        {
            int totalMax = 0;

            foreach (var kvp in propertyDescriptions)
            {
                int maxValue = 0;
                for (int i = 0; i < kvp.Value.shaderCost.Length; i++)
                {
                    if (kvp.Value.shaderCost[i] > maxValue) maxValue = kvp.Value.shaderCost[i];
                }
                totalMax += maxValue;
            }

            return Mathf.Max(1, totalMax);
        }

        private float GetCurrentCost(B2UShaderProfile preset)
        {
            float totalCost = 0f;

            if (preset.propertyBlocks != null)
            {
                foreach (var kvp in propertyDescriptions)
                {
                    if (preset.propertyBlocks.TryGetValue(kvp.Key, out B2UShaderPropertyBlock block) && block?.value != null)
                    {
                        totalCost += EvaluateBlockCost(block, kvp.Value);
                    }
                }
            }

            return totalCost;
        }

        private float EvaluateBlockCost(B2UShaderPropertyBlock block, B2UShaderPropertyBlockDescription desc)
        {
            if (desc == null || desc.shaderCost == null || desc.shaderCost.Length == 0) return 0f;
            if (block == null || block.value == null) return 0f;

            switch (block.value)
            {
                case int intVal:
                    if (desc.shaderCost.Length > 1 && block.minMax != Vector2.zero)
                    {
                        float minCost = desc.shaderCost[0];
                        float maxCostVal = desc.shaderCost[desc.shaderCost.Length - 1];
                        float t = Mathf.InverseLerp(block.minMax.x, block.minMax.y, intVal);
                        return Mathf.Lerp(minCost, maxCostVal, t);
                    }
                    return desc.shaderCost[0];

                case bool boolVal:
                    int bIndex = boolVal ? 1 : 0;
                    if (bIndex < desc.shaderCost.Length) return desc.shaderCost[bIndex];
                    return desc.shaderCost[0];

                case uint uintVal:
                    int uIndex = (int)uintVal;
                    if (uIndex >= 0 && uIndex < desc.shaderCost.Length) return desc.shaderCost[uIndex];
                    return desc.shaderCost[0];

                default:
                    return desc.shaderCost[0];
            }
        }

        private class B2UShaderPropertyBlockDescription
        {
            public readonly int[] shaderCost;
            public readonly string uiName;
            public readonly string description;
            public readonly string[] enumOptions;

            public B2UShaderPropertyBlockDescription(int[] cost, string name, string description, string[] enumOptions = null)
            {
                shaderCost = cost;
                uiName = name;
                this.description = description;
                this.enumOptions = enumOptions;
            }
        }

        private static readonly string[] disableEnable = { "Disabled", "Enabled" };

        public static string GetBlockUiName(B2UShaderProperty property)
        {
            if (propertyDescriptions.TryGetValue(property, out var desc)) return desc.uiName;
            return null;
        }

        private static readonly Dictionary<B2UShaderProperty, B2UShaderPropertyBlockDescription> propertyDescriptions = new Dictionary<B2UShaderProperty, B2UShaderPropertyBlockDescription>
        {
            [B2UShaderProperty.BiomeTexture1] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Biome Texture 1", "Holds the world-space weights of Biomes 1-4 (mapped to R, G, B, A) projected across the XZ plane, centered at the world origin.\n\nBiome 8 is represented by an absence of weight across both Biome Texture 1 and Biome Texture 2."),
            [B2UShaderProperty.BiomeTexture2] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Biome Texture 2", "Holds the world-space weights of Biomes 5-7 (mapped to R, G, B) and the Biome Subtype index (mapped to Alpha) projected across the XZ plane.\n\nBiome 8 is represented by an absence of weight across both Biome Texture 1 and Biome Texture 2 (Subtype Alpha does not count as a biome weight)."),
            [B2UShaderProperty.ColorEmissionLUT] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Color Emission LUT", "A Texture2D generated by B2U in Blender storing all surface/depth RGB colors, emission RGB colors, metallic (base alpha), and smoothness (emission alpha)."),
            [B2UShaderProperty.PropertyLUT] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Property LUT", "A 4x256 Texture2D generated by B2U in Blender storing normalized material properties, bitmasks, enum indices, and workflow IDs."),
            [B2UShaderProperty.IridescenceLUT] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Iridescence LUT", "A Texture2D where each row corresponds to an iridescence mode in Blender and each column maps to the dot product between the surface normal and the view/light vector."),
            [B2UShaderProperty.EmissionPatterns] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Emission Patterns", "A channel-packed RGBA Texture2D containing 4 grayscale emission patterns animated continuously over time."),
            [B2UShaderProperty.DetailMapAtlas] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Detail Map Atlas", "A 64-slice Texture2DArray containing surface detail maps (RG = normal, B = curvature, A = height)."),
            [B2UShaderProperty.NoiseTexture] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "3D Noise Texture", "A baked Texture3D volume where RGB store 3D Simplex noise offsets and Alpha stores 3D Voronoi noise."),
            [B2UShaderProperty.PaintPalette] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Paint Palette", "A 16-slice Texture2DArray (16x8 pixels per slice) providing per-object paint colors for meshes with a Vertex Color Blue paint mask."),
            [B2UShaderProperty.NumberOfSubbiomes] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Number of Subbiomes", "The number of subtype columns per biome in the active Color LUT, equal to (ColorLUTWidth - 8) / 8."),
            [B2UShaderProperty.WorldSize] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "World Size", "The total XZ dimensions of the world in units, centered at the world origin (e.g., a world spanning -1000 to +1000 on X and Z should use (2000, 2000))."),
            [B2UShaderProperty.GlobalEmissionStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Global Emission Strength", "Global brightness multiplier applied to all B2U emissive materials."),
            [B2UShaderProperty.GlobalEmissionSpeed] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Global Emission Speed", "Global speed multiplier applied to all animated B2U emission modes (Breathing, Flickering, and Patterns)."),
            [B2UShaderProperty.GlobalCracksUseEmissionColor] = new B2UShaderPropertyBlockDescription( new int[] { 0 }, "Crack Color Source", "Where to get the color for cracks from.\n\n---Global Color---\nUses the global color set below.\n\n---Emission Color---\nUses the material's emission color from the LUT.", new string[] {"Global Color", "Emission Color"}),
            [B2UShaderProperty.GlobalCrackColor] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Global Crack Color", "The color to use for all procedural cracks"),
            [B2UShaderProperty.GlobalCrackBrightness] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Global Crack Brightness", "The brightness to use for the crack glow, set this to 0 if you don't want them to glow"),
            [B2UShaderProperty.TriplanarBlendPower] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Triplanar Blend Power", "Controls the sharpness of transitions between projection axes in triplanar detail mapping. Higher values sharpen transitions; lower values soften blends."),
            [B2UShaderProperty.SolidNoiseScale] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Solid Noise Scale", "The world-space scale of the 3D noise applied to solid materials."),
            [B2UShaderProperty.SolidNoiseStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Solid Noise Strength", "How strongly the 3D noise modulates the base color and surface normals on solid materials."),
            [B2UShaderProperty.SolidHardEdgeColor] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Solid Hard Edge Color", "The color applied to hard edges when Solid Edge Style is set to Colored Hard Edges."),
            [B2UShaderProperty.SolidEdgeStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Solid Edge Strength/Step", "---IF USING SOFT EDGES---\nHow strongly the edge mask tints the surface color and softens normals.\n\n---IF USING HARD EDGES---\nThe step threshold that determines where the hard toon edge begins."),
            [B2UShaderProperty.SolidEdgeThickness] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Solid Edge Thickness", "A global multiplier for the baked physical edge wear thickness on solid meshes."),
            [B2UShaderProperty.SolidEdgePower] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Solid Edge Power", "The exponent applied to the solid edge wear mask, controlling how rapidly soft edges fall off."),
            [B2UShaderProperty.SolidCrackScale] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Solid Crack Scale", "The world-space scale of the procedural damage cracks on solid materials."),
            [B2UShaderProperty.LiquidBubbleMultiplier] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Bubble Speed", "A global multiplier for the upward bubble advection speed of liquids, multiplied by the per-material Bubble Speed property in the Property LUT."),
            [B2UShaderProperty.LiquidAngleOffsetSpeed] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Angle Offset Speed", "A global multiplier for the Voronoi cell angle offset speed of liquids, multiplied by the per-material Bubble Speed property in the Property LUT."),
            [B2UShaderProperty.LiquidRefractionDistortionStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Liquid Distortion Strength", "A global multiplier for refractive screen distortion through liquids."),
            [B2UShaderProperty.LiquidShoreDistance] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Shore Distance", "A global multiplier for the shore/intersection foam distance where liquids meet other geometry."),
            [B2UShaderProperty.LiquidNormalStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Liquid Normal Strength", "The strength of the generated surface normals for liquids."),
            [B2UShaderProperty.LiquidCausticStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Liquid Caustic Strength", "The brightness multiplier for projected liquid caustics."),
            [B2UShaderProperty.TessFactor] = new B2UShaderPropertyBlockDescription(new int[] { 0, 10 }, "Tessellation Factor", "Defines the maximum triangle subdivision factor for tessellated liquids. Vertex count scales quadratically with this value."),
            [B2UShaderProperty.MinTessDistance] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Min. Tessellation Distance", "The camera distance within which liquid triangles receive maximum tessellation. Should be smaller than Max. Tessellation Distance.\n\n---NOT FACTORED INTO COST PROFILE---\nLarger distances increase GPU cost."),
            [B2UShaderProperty.MaxTessDistance] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Max. Tessellation Distance", "The camera distance beyond which liquid triangles receive no tessellation. Keep around 50 units or less to avoid subpixel triangles.\n\n---NOT FACTORED INTO COST PROFILE---\nLarger distances increase GPU cost."),
            [B2UShaderProperty.CrystalCrackScale] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Crystal Crack Scale", "The world-space scale of the procedural internal/surface cracks on crystal materials."),
            [B2UShaderProperty.UseNoiseForSolids] = new B2UShaderPropertyBlockDescription(new int[] { 0, 2 }, "Noise on Solids", "Controls whether solid shaders sample 3D noise to break up flat colors and surface normals.", disableEnable),
            [B2UShaderProperty.UseProceduralNoiseForSolids] = new B2UShaderPropertyBlockDescription(new int[] { 0, 4 }, "Solid Noise Mode", "The noise evaluation mode for solids.\n\n---Texture3D---\nSamples the baked 3D Noise Texture. Fast, though tiling may be noticeable across very large flat surfaces.\n\n---Procedural---\nEvaluates 3D Simplex noise analytically. Eliminates tiling repetition at a higher ALU cost.", new string[] { "Texture3D", "Procedural" }),
            [B2UShaderProperty.UseProceduralNoiseForLiquids] = new B2UShaderPropertyBlockDescription(new int[] { 0, 8 }, "Liquid Noise Mode", "The noise evaluation mode for liquids.\n\n---Texture3D---\nSamples the baked 3D Noise Texture and computes normals via screen-space derivatives. Fast and ideal for smaller water bodies.\n\n---Procedural---\nEvaluates analytical 3D Voronoi and Simplex noise for non-repeating waves and smooth analytical normals at a high fragment ALU cost.", new string[] { "Texture3D", "Procedural" }),
            [B2UShaderProperty.UseBubbleAdvection] = new B2UShaderPropertyBlockDescription(new int[] { 0, 3 }, "Liquid Bubble Advection", "Advects the 3D liquid noise upward over time to simulate internal churning and bubbling.", disableEnable),
            [B2UShaderProperty.UseAngleOffset] = new B2UShaderPropertyBlockDescription(new int[] { 0, 4 }, "Liquid Angle Offset", "Continuously rotates procedural Voronoi cells over time so liquid cells appear to swim and morph.", disableEnable),
            [B2UShaderProperty.ChromaticAbberationForLiquids] = new B2UShaderPropertyBlockDescription(new int[] { 0, 3 }, "Liquid Chromatic Aberration", "Splits refracted RGB channels based on the liquid's Index of Refraction (IOR) to produce prismatic dispersion.", disableEnable),
            [B2UShaderProperty.LiquidsHaveVertexDisplacement] = new B2UShaderPropertyBlockDescription(new int[] { 0, 4 }, "Liquid Displacement Mode", "Controls whether liquids displace vertex positions in addition to perturbing fragment normals.\n\n---Normal Only---\nPerturbs normals in the fragment stage only.\n\n---Normal and Height---\nSamples wave noise in both vertex and fragment stages to physically displace the mesh surface.", new string[] { "Normal Only", "Normal and Height" }),
            [B2UShaderProperty.TessellatedLiquidsResampleInFragment] = new B2UShaderPropertyBlockDescription(new int[] { 0, 6 }, "Tessellation Surface Mode", "Controls how surface normals and height are evaluated on tessellated liquid meshes.\n\n---Vertex Passthrough---\nPasses domain-stage displacement data directly to the fragment stage.\n\n---Resample in Fragment---\nRe-evaluates 3D noise per fragment for crisp normals even at lower tessellation factors.", new string[] { "Vertex Passthrough", "Resample in Fragment" }),
            [B2UShaderProperty.IridescenceMode] = new B2UShaderPropertyBlockDescription(new int[] { 0, 2, 6 }, "Iridescence Mode", "The lighting response mode for iridescence.\n\n---Ambient---\nEvaluated from view direction and surface normal only.\n\n---Main Light---\nCombines ambient view-angle iridescence with directional main-light response.\n\n---All Lights---\nAccumulates iridescence from the main light plus up to 4 additional lights.", new string[] { "Ambient", "Main Light", "All Lights" }),
            [B2UShaderProperty.CrystalFlareMode] = new B2UShaderPropertyBlockDescription(new int[] { 0, 2, 6 }, "Crystal Flare Mode", "The lighting response mode for internal crystal flares.\n\n---Ambient---\nEvaluated from the view vector and entry/exit normals.\n\n---Main Light---\nEvaluated using the main directional light and entry/exit normals.\n\n---All Lights---\nAccumulates internal flares from the main light plus up to 4 additional lights.", new string[] { "Ambient", "Main Light", "All Lights" }),
            [B2UShaderProperty.CausticMode] = new B2UShaderPropertyBlockDescription(new int[] { 0, 3, 7 }, "Liquid Caustic Mode", "The lighting response mode for projected liquid caustics.\n\n---Disabled---\nDisables caustics.\n\n---Main Light---\nProjects caustics using the scene's main directional light.\n\n---All Lights---\nProjects caustics from the main light plus up to 4 additional lights.", new string[] { "Disabled", "Main Light", "All Lights" }),
            [B2UShaderProperty.RefractionQualityMode] = new B2UShaderPropertyBlockDescription(new int[] { 0, 4, 8 }, "Refraction Quality Mode", "Controls the number of screen-color copy passes and volumetric backface passes.\n\n---Low---\nSingle-pass refraction using entry normals against the opaque color buffer.\n\n---Medium---\nRenders crystal backfaces and performs a screen copy after crystals so crystals are visible inside liquids.\n\n---High---\nRenders crystal and volumetric liquid backfaces and performs two screen copies so crystals, standard liquids, and volumetric liquids composite through one another.", new string[] { "Low", "Medium", "High" }),
            [B2UShaderProperty.EdgeDetectionBufferResolution] = new B2UShaderPropertyBlockDescription(new int[] { 6, 4, 2, 0 }, "Edge Buffer Resolution", "Resolution of the Object ID edge buffer relative to screen resolution.", new string[] { "Full", "Half", "Quarter", "Eighth" }),
            [B2UShaderProperty.EdgeDetectionSize] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Edge Detection Size", "Width of the outlines generated by the fullscreen edge detection pass."),
            [B2UShaderProperty.EdgeDetectionSensitivity] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Edge Detection Sensitivity", "Minimum depth discontinuity required to draw an outline when Edge Detection Source is set to Depth Buffer."),
            [B2UShaderProperty.EdgeDetectionColor] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Edge Detection Color", "The color of the outlines drawn by the fullscreen edge detection pass."),
            [B2UShaderProperty.EdgeDetectionLayermask] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Edge Detection Layermask", "Selects which Unity layers are included in the Object ID edge detection pass."),
            [B2UShaderProperty.BackfaceBufferResolution] = new B2UShaderPropertyBlockDescription(new int[] { 6, 4, 2, 0 }, "Backface Buffer Resolution", "Resolution of the volumetric backface depth-normal buffer relative to screen resolution.", new string[] { "Full", "Half", "Quarter", "Eighth" }),
            [B2UShaderProperty.ColorCopyResolution] = new B2UShaderPropertyBlockDescription(new int[] { 6, 4, 2, 0 }, "Color Copy Resolution", "Resolution of the intermediate refraction color copy buffer relative to screen resolution.", new string[] { "Full", "Half", "Quarter", "Eighth" }),
            [B2UShaderProperty.LightModel] = new B2UShaderPropertyBlockDescription(new int[] { 5, 2, 3, 0, 1, 3, 4, 5 }, "Light Model", "The global shading model used by B2U materials.\n\n---URP---\nStandard PBR shading matching URP Lit.\n\n---Lightweight---\nSimplified Blinn-Phong lighting.\n\n---Toon---\nPosterized cel shading.\n\n---Anti-Light---\nInverts illumination so lit surfaces darken while shadows remain bright.\n\n---Retro---\nOrdered Bayer dithered shading.\n\n---Gooch---\nWarm-to-cool complementary shadow tinting.\n\n---Faceted---\nPer-triangle flat shading using screen-space derivatives.\n\n---Pop-Art---\nHalftone Ben-Day dot shading.", new string[] { "URP", "Lightweight", "Toon", "Anti-Light", "Retro", "Gooch", "Faceted", "Pop-Art" }),
            [B2UShaderProperty.LightControlFloat] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Light Control Float", "Context-sensitive parameter for stylized lighting models (Posterize Steps in Toon mode, Shadow Darkness in Gooch mode, and Dot Size in Pop-Art mode)."),
            [B2UShaderProperty.ImmersionActive] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Liquid Immersion Active", "Enables the fullscreen underwater immersion pass when the camera dips below the active liquid surface height.", disableEnable),
            [B2UShaderProperty.ImmersionTransitionEnabled] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Immersion Transition", "Enables a transition which applies an opaque mask to the screen when the camera moves in or out of the liquid. Use this to hide the near clip plane of the camera intersecting with the liquid mesh.", disableEnable),
            [B2UShaderProperty.ImmersionTransitionDistance] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Transition Distance", "The distance in units above or below the liquid GameObject's Y value for the transition effect to appear"),
            [B2UShaderProperty.ImmersionClarity] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Immersion Clarity", "Controls underwater visibility distance. Higher values allow the camera to see further through the liquid."),
            [B2UShaderProperty.ImmersionDistortion] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Immersion Distortion Strength", "Controls the intensity of wavy screen-space distortion while submerged."),
            [B2UShaderProperty.ImmersionCausticStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Immersion Caustic Strength", "Multiplier for caustics projected onto geometry while submerged. If you want the caustics to appear the same as the above surface caustics, set this to 1/Immersion Clarity"),
            [B2UShaderProperty.RecolorActive] = new B2UShaderPropertyBlockDescription(new int[] { 0, 0 }, "Recolor Effect Active", "Enables the fullscreen gradient recolor pass.", disableEnable),
            [B2UShaderProperty.RecolorBlend] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Recolor Blend", "Master blend weight (0 to 1) between the original scene color and the active Recolor Preset."),
            [B2UShaderProperty.RecolorPresets] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Recolor Preset Collection", "The B2URecolorPresetCollection asset containing the recolor presets available to this profile."),
            [B2UShaderProperty.StylizeActive] = new B2UShaderPropertyBlockDescription(new int[] { 0, 0 }, "Stylize Effect Active", "Enables the fullscreen artistic pattern/hatching stylize pass.", disableEnable),
            [B2UShaderProperty.StylizeBlend] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Stylize Blend", "Master opacity/blend weight (0 to 1) of the active Stylize Preset."),
            [B2UShaderProperty.StylizePresets] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Stylize Preset Collection", "The B2UStylizePresetCollection asset containing the stylize presets available to this profile."),
            [B2UShaderProperty.StatusActive] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Status Effects Active", "Enables the fullscreen status effect and injury vignette pass.", disableEnable),
            [B2UShaderProperty.StatusPresets] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Status Effect Preset Collection", "The B2UStatusEffectPresetCollection asset containing the status effect presets available to this profile."),
            [B2UShaderProperty.StatusStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Status Effect Strength", "Master intensity (0 to 1) of the active status effect overlay."),
            [B2UShaderProperty.InjuryStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Injury Strength", "Master intensity (0 = uninjured, 1 = gravely injured) of the pulsing injury vignette and scene darkening."),
            [B2UShaderProperty.InjuryColor] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Injury Vignette Color", "The color of the pulsing injury border vignette."),
            [B2UShaderProperty.InjuryPulseSpeed] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Injury Pulse Speed", "The heartbeat pulse frequency of the injury vignette. Set to discrete values rather than animating continuously."),
            [B2UShaderProperty.InjuryVignetteStrength] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Injury Vignette Strength", "How far inward the injury vignette extends across the screen (0 = invisible, 1 = maximum coverage)."),
            [B2UShaderProperty.InjuryDarkenAmount] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Injury Darken Amount", "How strongly the injury effect darkens the scene."),
            [B2UShaderProperty.EdgeDetectionEnabled] = new B2UShaderPropertyBlockDescription(new int[] { 0, 5 }, "Edge Detection", "Enables the fullscreen outline detection pass. Requires the B2UEdgeDetection renderer feature on the active URP renderer.", disableEnable),
            [B2UShaderProperty.VolumetricRefractionEnabled] = new B2UShaderPropertyBlockDescription(new int[] { 0, 2 }, "Volumetric Refraction", "Enables custom multi-queue refraction and backface thickness rendering for crystals and liquids. Requires the B2UVolumetricRefraction renderer feature.", disableEnable),
            [B2UShaderProperty.FullscreenEffectsEnabled] = new B2UShaderPropertyBlockDescription(new int[] { 0, 0 }, "Fullscreen Effects", "Enables the B2U fullscreen effects pass (Liquid Immersion, Recolor, Stylize, and Status Effects). Requires the B2UFullscreenEffects renderer feature.", disableEnable),
            [B2UShaderProperty.EdgeDetectionMode] = new B2UShaderPropertyBlockDescription(new int[] { 4, 0 }, "Edge Detection Source", "Selects which buffer is used to detect outlines.\n\n---Edge Buffer---\nRenders solid objects into a dedicated per-object color buffer for clean silhouette and object-boundary outlines.\n\n---Depth Buffer---\nDetects outlines directly from the URP camera depth buffer.", new string[] { "Edge Buffer", "Depth Buffer" }),
            [B2UShaderProperty.EdgeDetectionSpace] = new B2UShaderPropertyBlockDescription(new int[] { 0, 2 }, "Edge Detection Space", "Controls how outline width scales with camera distance.\n\n---Screen Space---\nOutlines maintain a constant pixel width regardless of distance.\n\n---World Space---\nOutline width scales with depth so distant objects have thinner outlines.", new string[] { "Screen Space", "World Space" }),
            [B2UShaderProperty.VolumetricLayer] = new B2UShaderPropertyBlockDescription(new int[] { 0 }, "Volumetric Layer", "The Unity layer assigned to crystals and volumetric liquids so the backface pass only renders volumetric geometry."),
            [B2UShaderProperty.VertexRGamma] = new B2UShaderPropertyBlockDescription(new int[] { 0, 0 }, "Vertex Red Colorspace", "Controls whether Vertex Color Red (Gradient mask on solids/crystals, Transparency mask on transparents) is evaluated in sRGB or Linear space.", new string[] { "sRGB", "Linear" }),
            [B2UShaderProperty.VertexGGamma] = new B2UShaderPropertyBlockDescription(new int[] { 0, 0 }, "Vertex Green Colorspace", "Controls whether Vertex Color Green (Emission mask) is evaluated in sRGB or Linear space.", new string[] { "sRGB", "Linear" }),
            [B2UShaderProperty.VertexBGamma] = new B2UShaderPropertyBlockDescription(new int[] { 0, 0 }, "Vertex Blue Colorspace", "Controls whether Vertex Color Blue (Paint mask on solids) is evaluated in sRGB or Linear space.", new string[] { "sRGB", "Linear" }),
            [B2UShaderProperty.SolidsHaveSmoothEdges] = new B2UShaderPropertyBlockDescription(new int[] { 0, 1 }, "Edge Normals", "Controls whether the baked edge wear mask blends surface normals into smooth bevel normals along sharp edges.", new string[] { "Hard Normals", "Smooth Normals" }),
            [B2UShaderProperty.DetailMapAffectsHardEdges] = new B2UShaderPropertyBlockDescription(new int[] { 0, 2 }, "Detail Map Hard Edges", "Includes detail map curvature in the hard-edge step calculation when Solid Edge Style is set to Hard Edges or Colored Hard Edges.", disableEnable),
            [B2UShaderProperty.LiquidEmissionHasFlow] = new B2UShaderPropertyBlockDescription(new int[] { 0, 2 }, "Liquid Emission Flows", "Advects patterned liquid emission along the mesh's baked UV0 flow vectors.", disableEnable),
            [B2UShaderProperty.EdgeMode] = new B2UShaderPropertyBlockDescription(new int[] { 0, 0, 0 }, "Solid Edge Mode", "Selects which surface curvature types contribute to the solid edge wear mask.\n\n---Both---\nApplies to both convex and concave edges.\n\n---Convex Only---\nApplies only to outward-pointing ridges.\n\n---Concave Only---\nApplies only to inward-pointing cavities.", new string[] { "Both", "Convex Only", "Concave Only" }),
            [B2UShaderProperty.EdgeStyle] = new B2UShaderPropertyBlockDescription(new int[] { 1, 0, 0 }, "Solid Edge Style", "Selects the visual style of the edge wear mask on solid materials.\n\n---Soft Edges---\nSmoothly highlights/shades and bevels edges.\n\n---Hard Edges---\nSteps the edge mask into a crisp black comic/toon ink line.\n\n---Colored Hard Edges---\nSteps the edge mask into a crisp line tinted by Solid Hard Edge Color.", new string[] { "Soft Edges", "Hard Edges", "Colored Hard Edges" })
        };
    }
}
#endif