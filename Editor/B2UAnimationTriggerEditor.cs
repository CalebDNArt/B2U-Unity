#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static B2U.B2UAnimationTrigger;
using static B2U.B2URuntimeAnimator;

namespace B2U
{
    /// <summary>
    /// Custom Inspector for configuring and live-testing B2UAnimationTrigger event lists.
    /// </summary>
    [CustomEditor(typeof(B2UAnimationTrigger))]
    public class B2UAnimationTriggerEditor : Editor
    {
        private static readonly List<B2UShaderProperty> validFloatPropertyEnums = new List<B2UShaderProperty>();
        private static string[] validFloatPropertyNames;

        private static readonly string[] paletteChangeType = { "Material Property", "Material Color" };
        private static readonly string[] colorChangeType = { "Base Color", "Emission Color" };
        private static readonly string[] liquidChangeType = { "Surface Row", "Depth Row" };
        private static readonly string[] biomeResponseType = { "Change Independent Row", "Change Dynamic Row" };

        #region GUIContent Labels
        private static readonly GUIContent GroupLabel = new GUIContent("Animation Group", "Selects which B2U subsystem this animation event targets.");
        private static readonly GUIContent FullscreenTypeLabel = new GUIContent("Fullscreen Effect Type", "Selects which fullscreen effect pass to trigger.");
        private static readonly GUIContent EffectIndexLabel = new GUIContent("Effect Index in Collection", "The index of the preset inside the active profile's preset collection.");
        private static readonly GUIContent TargetStrengthLabel = new GUIContent("Target Strength", "The target blend or intensity value (0 to 1) for the fullscreen effect.");
        private static readonly GUIContent TransitionTimeLabel = new GUIContent("Transition Time (s)", "The time in seconds required to blend from the current strength to the Target Strength.");
        private static readonly GUIContent StatusDurationLabel = new GUIContent("Hold Duration (s)", "How long the status effect stays at full strength before fading out. Values <= 0 last indefinitely.");

        private static readonly GUIContent DataSwapTypeLabel = new GUIContent("Data Swap Type", "The type of B2U data asset to bind when this event is triggered.");
        private static readonly GUIContent DataSwapTargetLabel = new GUIContent("Data Swap Target", "The target B2U asset (Profile, Palette, or Preset Collection) to swap in.");

        private static readonly GUIContent AnimModeLabel = new GUIContent("Animation Mode", "The interpolation curve or periodic wave function used to evaluate the animation.");
        private static readonly GUIContent CustomCurveLabel = new GUIContent("Custom Curve", "Custom AnimationCurve evaluated over the task's duration or frequency.");
        private static readonly GUIContent SpeedLabel = new GUIContent("Speed", "Multiplier for the animation playback rate.");
        private static readonly GUIContent FrequencyLabel = new GUIContent("Frequency", "Cycles per second for periodic wave animation modes.");
        private static readonly GUIContent DurationLabel = new GUIContent("Duration (s)", "Total time in seconds for aperiodic animations to reach the Target Value.");
        private static readonly GUIContent DecayLabel = new GUIContent("Decay Time (s)", "Time in seconds for periodic waves to decay to 0. Set to 0 for an infinite loop.");
        private static readonly string[] decayDirectionOptions = { "Decay to Start", "Decay to Target" };
        private static readonly GUIContent DecayDirectionLabel = new GUIContent("Decay Direction", "Controls whether a finite periodic wave settles back to the Start Value or settles onto the Target Value.");

        private static readonly GUIContent TargetPropertyLabel = new GUIContent("Target Property", "The global float shader property on the active B2UShaderProfile to animate.");
        private static readonly GUIContent UseCustomStartLabel = new GUIContent("Use Custom Start", "If enabled, the animation begins from a fixed Start Value instead of the property's current live value.");
        private static readonly GUIContent StartValueLabel = new GUIContent("Start Value", "The initial value at the start of the animation.");
        private static readonly GUIContent TargetValueLabel = new GUIContent("Target Value", "The final value at the end of the animation (or peak amplitude for periodic waves).");

        private static readonly GUIContent TargetMaterialLabel = new GUIContent("Target Material", "The palette material slot to animate.");
        private static readonly GUIContent TargetDataLabel = new GUIContent("Target Data", "Choose whether to animate a LUT property channel or a color pixel.");
        private static readonly GUIContent LiquidRowLabel = new GUIContent("Liquid Row", "Selects whether to animate the Surface row (Row 1) or Depth row (Row 2) of the liquid material.");
        private static readonly GUIContent ColorChannelLabel = new GUIContent("Color Channel", "Selects whether to animate the Base/Depth color or the Emission color.");
        private static readonly GUIContent VariantLabel = new GUIContent("Variant", "The color variant index (0-7) to animate on this independent material.");
        private static readonly GUIContent BiomeLabel = new GUIContent("Biome", "The biome index (0-7) to animate on this dynamic material.");
        private static readonly GUIContent SubtypeLabel = new GUIContent("Subtype", "The biome subtype column to animate.");
        private static readonly GUIContent BiomeRowModeLabel = new GUIContent("Biome Row Mode", "For dual Independent & Dynamic materials, selects which LUT row to animate.");
        private static readonly GUIContent CurrentColorLabel = new GUIContent("Current Color", "The live color currently stored in the Color LUT for the selected slot.");
        private static readonly GUIContent StartColorLabel = new GUIContent("Start Color", "The initial color at the start of the animation.");
        private static readonly GUIContent TargetColorLabel = new GUIContent("Target Color", "The target color to animate toward.");
        private static readonly GUIContent TargetSlotLabel = new GUIContent("Target Slot", "The active schema property channel on this material to animate.");

        private static readonly GUIContent TargetRSUVMonoLabel = new GUIContent("Target RSUV Mono", "The B2URSUVMono component whose per-object shader user value will be animated.");
        private static readonly GUIContent RSUVParamLabel = new GUIContent("RSUV Parameter", "The specific per-object RSUV property to animate.");
        #endregion

        private void OnEnable()
        {
            if (B2URuntimeUtility.ActiveProfile != null)
            {
                InitializeData();
            }
        }

        public override void OnInspectorGUI()
        {
            if (B2URuntimeUtility.ActiveProfile == null)
            {
                EditorGUILayout.HelpBox("No B2U Shader Profile is set. This is required to use the animation trigger. Set it in the B2U Control Panel.", MessageType.Error);
                return;
            }
            if (B2URuntimeUtility.ActiveProfile.paletteData == null)
            {
                EditorGUILayout.HelpBox("No B2U Palette Data is set for the active profile. Set it in the profile's settings.", MessageType.Error);
                return;
            }

            if (validFloatPropertyNames == null || validFloatPropertyNames.Length == 0)
            {
                InitializeData();
            }

            B2UAnimationTrigger trigger = (B2UAnimationTrigger)target;
            if (trigger.events == null)
            {
                trigger.events = new List<B2UAnimationEvent>();
            }

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(trigger, "Modify B2U Animation Trigger");

            EditorGUILayout.Space();

            for (int i = 0; i < trigger.events.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Animation Event {i}", EditorStyles.boldLabel);
                if (GUILayout.Button("X", GUILayout.Width(25f)))
                {
                    trigger.events[i]?.KillTask();
                    trigger.events.RemoveAt(i);
                    i--;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2f);

                DrawEvent(trigger.events[i]);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            if (GUILayout.Button("Add Animation Event", GUILayout.Height(25f)))
            {
                trigger.events.Add(new B2UAnimationEvent());
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(trigger);
            }
        }

        public void DrawEvent(B2UAnimationEvent animEvent)
        {
            animEvent.group = (B2UAnimationGroup)EditorGUILayout.EnumPopup(GroupLabel, animEvent.group);

            // 1. Fullscreen Effect Trigger Group
            if (animEvent.group == B2UAnimationGroup.FullscreenTrigger)
            {
                animEvent.fullscreenType = (B2UFullscreenType)EditorGUILayout.EnumPopup(FullscreenTypeLabel, animEvent.fullscreenType);

                if (B2URuntimeUtility.CheckActiveProfile(target, silent: true))
                {
                    var blocks = B2URuntimeUtility.ActiveProfile.propertyBlocks;
                    switch (animEvent.fullscreenType)
                    {
                        case B2UFullscreenType.Recolor:
                            if (blocks.TryGetValue(B2UShaderProperty.RecolorPresets, out var recolorBlock) &&
                                recolorBlock.value is B2URecolorPresetCollection recolorCol &&
                                recolorCol.presets != null && recolorCol.presets.Count > 0)
                            {
                                animEvent.fullscreenEffectIndex = EditorGUILayout.IntSlider(EffectIndexLabel, animEvent.fullscreenEffectIndex, 0, recolorCol.presets.Count - 1);
                                var targetPreset = recolorCol.presets[animEvent.fullscreenEffectIndex];
                                EditorGUILayout.LabelField("Target Preset:", targetPreset != null ? targetPreset.name : "None (Null Slot)");
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("No Recolor Preset Collection (or no presets inside it) is assigned on the active profile.", MessageType.Warning);
                            }
                            break;

                        case B2UFullscreenType.Stylize:
                            if (blocks.TryGetValue(B2UShaderProperty.StylizePresets, out var stylizeBlock) &&
                                stylizeBlock.value is B2UStylizePresetCollection stylizeCol &&
                                stylizeCol.presets != null && stylizeCol.presets.Count > 0)
                            {
                                animEvent.fullscreenEffectIndex = EditorGUILayout.IntSlider(EffectIndexLabel, animEvent.fullscreenEffectIndex, 0, stylizeCol.presets.Count - 1);
                                var targetPreset = stylizeCol.presets[animEvent.fullscreenEffectIndex];
                                EditorGUILayout.LabelField("Target Preset:", targetPreset != null ? targetPreset.name : "None (Null Slot)");
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("No Stylize Preset Collection (or no presets inside it) is assigned on the active profile.", MessageType.Warning);
                            }
                            break;

                        case B2UFullscreenType.StatusEffect:
                            if (blocks.TryGetValue(B2UShaderProperty.StatusPresets, out var statusBlock) &&
                                statusBlock.value is B2UStatusEffectPresetCollection statusCol &&
                                statusCol.presets != null && statusCol.presets.Count > 0)
                            {
                                animEvent.fullscreenEffectIndex = EditorGUILayout.IntSlider(EffectIndexLabel, animEvent.fullscreenEffectIndex, 0, statusCol.presets.Count - 1);
                                var targetPreset = statusCol.presets[animEvent.fullscreenEffectIndex];
                                EditorGUILayout.LabelField("Target Preset:", targetPreset != null ? targetPreset.name : "None (Null Slot)");
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("No Status Effect Preset Collection (or no presets inside it) is assigned on the active profile.", MessageType.Warning);
                            }
                            break;
                    }
                }

                animEvent.target.x = EditorGUILayout.Slider(TargetStrengthLabel, animEvent.target.x, 0f, 1f);
                animEvent.speed = Mathf.Max(0f, EditorGUILayout.FloatField(TransitionTimeLabel, animEvent.speed));

                if (animEvent.fullscreenType == B2UFullscreenType.StatusEffect)
                {
                    animEvent.duration = EditorGUILayout.FloatField(StatusDurationLabel, animEvent.duration);
                }

                EditorGUILayout.HelpBox("Fullscreen triggers are singletons. Triggering another fullscreen effect of the same type will override the previous one. Provided for Unity Animator & UnityEvent support.", MessageType.Info);
                DrawTriggerButtons(animEvent, allowKill: false);
                return;
            }

            // 2. Data Swap Group
            if (animEvent.group == B2UAnimationGroup.DataSwap)
            {
                animEvent.dataSwapType = (B2UDataSwapType)EditorGUILayout.EnumPopup(DataSwapTypeLabel, animEvent.dataSwapType);
                Type expectedType = animEvent.dataSwapType switch
                {
                    B2UDataSwapType.Palette => typeof(B2UPaletteData),
                    B2UDataSwapType.RecolorCollection => typeof(B2URecolorPresetCollection),
                    B2UDataSwapType.StylizeCollection => typeof(B2UStylizePresetCollection),
                    B2UDataSwapType.StatusEffectCollection => typeof(B2UStatusEffectPresetCollection),
                    _ => typeof(B2UShaderProfile)
                };

                if (animEvent.dataSwapTarget != null && !expectedType.IsAssignableFrom(animEvent.dataSwapTarget.GetType()))
                {
                    animEvent.dataSwapTarget = null;
                }

                animEvent.dataSwapTarget = EditorGUILayout.ObjectField(DataSwapTargetLabel, animEvent.dataSwapTarget, expectedType, false);
                EditorGUILayout.HelpBox("Data swaps are instantaneous and cannot be interpolated over time. Provided for Unity Animator & UnityEvent support.", MessageType.Info);
                DrawTriggerButtons(animEvent, allowKill: false);
                return;
            }
            else if (animEvent.dataSwapTarget != null)
            {
                animEvent.dataSwapTarget = null;
            }

            // 3. Standard Animated Groups (GlobalFloat, Palette, RSUV)
            animEvent.mode = (AnimMode)EditorGUILayout.EnumPopup(AnimModeLabel, animEvent.mode);

            if (animEvent.mode == AnimMode.AnimationCurve)
            {
                if (animEvent.curve == null) animEvent.curve = new AnimationCurve();
                animEvent.curve = EditorGUILayout.CurveField(CustomCurveLabel, animEvent.curve);
            }
            else
            {
                animEvent.curve = null;
            }

            bool isAperiodic = animEvent.mode == AnimMode.Linear ||
                               animEvent.mode == AnimMode.Sigmoidal ||
                               animEvent.mode == AnimMode.EaseIn ||
                               animEvent.mode == AnimMode.EaseOut;

            bool showFrequency = !isAperiodic && (animEvent.mode != AnimMode.AnimationCurve || Mathf.Approximately(animEvent.duration, 0f));
            if (showFrequency)
            {
                animEvent.speed = EditorGUILayout.FloatField(FrequencyLabel, animEvent.speed);
            }

            if (isAperiodic)
            {
                // Strip any leftover negative sign if the user switched from a periodic mode
                animEvent.duration = Mathf.Max(0f, EditorGUILayout.FloatField(DurationLabel, Mathf.Abs(animEvent.duration)));

                if (animEvent.duration <= 0f)
                {
                    EditorGUILayout.HelpBox("Duration = 0: Task will immediately snap to the target value on trigger.", MessageType.Info);
                }
            }
            else
            {
                bool decayToTarget = animEvent.duration < 0f;
                float absDuration = Mathf.Max(0f, EditorGUILayout.FloatField(DecayLabel, Mathf.Abs(animEvent.duration)));

                if (absDuration > 0f)
                {
                    decayToTarget = EditorGUILayout.Popup(DecayDirectionLabel, decayToTarget ? 1 : 0, decayDirectionOptions) == 1;
                    animEvent.duration = decayToTarget ? -absDuration : absDuration;
                }
                else
                {
                    animEvent.duration = 0f;
                    EditorGUILayout.HelpBox("Decay = 0: Task will oscillate indefinitely as a periodic wave until manually killed.", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(5f);

            switch (animEvent.group)
            {
                case B2UAnimationGroup.GlobalFloat:
                    {
                        if (validFloatPropertyEnums.Count == 0)
                        {
                            EditorGUILayout.HelpBox("No float properties found on the active profile.", MessageType.Warning);
                            break;
                        }

                        int currentPropIndex = validFloatPropertyEnums.IndexOf(animEvent.property);
                        if (currentPropIndex == -1) currentPropIndex = 0;

                        int newPropIndex = EditorGUILayout.Popup(TargetPropertyLabel, currentPropIndex, validFloatPropertyNames);
                        animEvent.property = validFloatPropertyEnums[newPropIndex];

                        var propBlock = B2URuntimeUtility.ActiveProfile.propertyBlocks[animEvent.property];
                        Vector2 minMax = propBlock.minMax;
                        float currentGlobalValue = (float)propBlock.value;

                        EditorGUILayout.LabelField("Current Value:", currentGlobalValue.ToString("F2"));
                        animEvent.useCustomStart = EditorGUILayout.Toggle(UseCustomStartLabel, animEvent.useCustomStart);

                        if (minMax != Vector2.zero)
                        {
                            if (animEvent.useCustomStart)
                                animEvent.customStart = new Vector4(EditorGUILayout.Slider(StartValueLabel, animEvent.customStart.x, minMax.x, minMax.y), 0f, 0f, 0f);
                            animEvent.target = new Vector4(EditorGUILayout.Slider(TargetValueLabel, animEvent.target.x, minMax.x, minMax.y), 0f, 0f, 0f);
                        }
                        else
                        {
                            if (animEvent.useCustomStart)
                                animEvent.customStart = new Vector4(EditorGUILayout.FloatField(StartValueLabel, animEvent.customStart.x), 0f, 0f, 0f);
                            animEvent.target = new Vector4(EditorGUILayout.FloatField(TargetValueLabel, animEvent.target.x), 0f, 0f, 0f);
                        }
                        break;
                    }

                case B2UAnimationGroup.Palette:
                    {
                        B2UPaletteData paletteData = B2URuntimeUtility.ActiveProfile.paletteData;
                        if (!B2URuntimeUtility.CheckPaletteMaterials(paletteData, silent: true))
                        {
                            EditorGUILayout.HelpBox("Palette materials are not initialized.", MessageType.Warning);
                            return;
                        }

                        List<string> matNames = new List<string>();
                        List<int> matRowIndices = new List<int>();

                        if (paletteData.editorOnlyMaterials != null)
                        {
                            for (int i = 0; i < paletteData.editorOnlyMaterials.Length; i++)
                            {
                                var editorMat = paletteData.editorOnlyMaterials[i];
                                if (editorMat != null && !string.IsNullOrEmpty(editorMat.materialName))
                                {
                                    matNames.Add($"[{editorMat.startRowIndex}] {editorMat.materialName}");
                                    matRowIndices.Add(editorMat.startRowIndex);
                                }
                            }
                        }

                        if (matNames.Count == 0)
                        {
                            EditorGUILayout.HelpBox("No materials exist in the active palette.", MessageType.Warning);
                            return;
                        }

                        int selectedDropdownIdx = matRowIndices.IndexOf(animEvent.materialIndex);
                        if (selectedDropdownIdx == -1) selectedDropdownIdx = 0;

                        selectedDropdownIdx = EditorGUILayout.Popup(TargetMaterialLabel, selectedDropdownIdx, matNames.ToArray());
                        animEvent.materialIndex = matRowIndices[selectedDropdownIdx];

                        B2UMaterial mat = paletteData.materials.GetMaterialByIndex((byte)animEvent.materialIndex);
                        if (mat == null)
                        {
                            EditorGUILayout.HelpBox("Selected material could not be retrieved from the runtime container.", MessageType.Error);
                            return;
                        }

                        animEvent.changeColorLUT = EditorGUILayout.Popup(TargetDataLabel, animEvent.changeColorLUT ? 1 : 0, paletteChangeType) == 1;

                        if (animEvent.changeColorLUT)
                        {
                            string[] biomeNames = (paletteData.biomeNames != null && paletteData.biomeNames.Length == 8)
                                ? paletteData.biomeNames
                                : new string[] { "Biome 0", "Biome 1", "Biome 2", "Biome 3", "Biome 4", "Biome 5", "Biome 6", "Biome 7" };

                            int numSub = Mathf.Max(1, paletteData.numSubbiomes);
                            string[] subtypeNames = new string[numSub];

                            animEvent.variantOrBiome = Mathf.Clamp(animEvent.variantOrBiome, 0, biomeNames.Length - 1);
                            animEvent.subtype = Mathf.Clamp(animEvent.subtype, 0, numSub - 1);

                            for (int i = 0; i < numSub; i++)
                            {
                                int flatIdx = (numSub * animEvent.variantOrBiome) + i;
                                if (paletteData.subtypeNames != null && flatIdx < paletteData.subtypeNames.Length && !string.IsNullOrEmpty(paletteData.subtypeNames[flatIdx]))
                                    subtypeNames[i] = paletteData.subtypeNames[flatIdx];
                                else
                                    subtypeNames[i] = $"Subtype {i}";
                            }

                            if (mat.workflow == B2UWorkflow.Liquid)
                            {
                                animEvent.changeDepth = EditorGUILayout.Popup(LiquidRowLabel, animEvent.changeDepth ? 1 : 0, liquidChangeType) == 1;
                            }

                            animEvent.changeEmission = EditorGUILayout.Popup(ColorChannelLabel, animEvent.changeEmission ? 1 : 0, colorChangeType) == 1;
                            bool isSecondRow = mat.workflow == B2UWorkflow.Liquid && animEvent.changeDepth;

                            Color currentColor = Color.black;
                            switch (mat.biomeType)
                            {
                                case "INDEPENDENT":
                                    if (mat.workflow != B2UWorkflow.Liquid && mat.workflow != B2UWorkflow.Crystal)
                                    {
                                        animEvent.variantOrBiome = EditorGUILayout.IntSlider(VariantLabel, animEvent.variantOrBiome, 0, 7);
                                    }
                                    else
                                    {
                                        animEvent.variantOrBiome = 0;
                                    }
                                    currentColor = B2URuntimeUtility.GetColorLUTPixel(paletteData, mat, animEvent.changeEmission, animEvent.variantOrBiome, isSecondRow);
                                    break;

                                case "DYNAMIC":
                                    animEvent.variantOrBiome = EditorGUILayout.Popup(BiomeLabel, animEvent.variantOrBiome, biomeNames);
                                    if (!animEvent.changeEmission)
                                    {
                                        animEvent.subtype = EditorGUILayout.Popup(SubtypeLabel, animEvent.subtype, subtypeNames);
                                    }
                                    currentColor = B2URuntimeUtility.GetColorLUTPixel(paletteData, mat, animEvent.changeEmission, animEvent.variantOrBiome, animEvent.subtype, isSecondRow);
                                    break;

                                case "INDEP_DYN":
                                case "INDEP-DYN":
                                    animEvent.changeDynamic = EditorGUILayout.Popup(BiomeRowModeLabel, animEvent.changeDynamic ? 1 : 0, biomeResponseType) == 1;
                                    if (animEvent.changeDynamic)
                                    {
                                        animEvent.variantOrBiome = EditorGUILayout.Popup(BiomeLabel, animEvent.variantOrBiome, biomeNames);
                                        if (!animEvent.changeEmission)
                                        {
                                            animEvent.subtype = EditorGUILayout.Popup(SubtypeLabel, animEvent.subtype, subtypeNames);
                                        }
                                        currentColor = B2URuntimeUtility.GetColorLUTPixel(paletteData, mat, animEvent.changeEmission, animEvent.variantOrBiome, animEvent.subtype, isSecondRow);
                                    }
                                    else
                                    {
                                        if (mat.workflow != B2UWorkflow.Liquid && mat.workflow != B2UWorkflow.Crystal)
                                        {
                                            animEvent.variantOrBiome = EditorGUILayout.IntSlider(VariantLabel, animEvent.variantOrBiome, 0, 7);
                                        }
                                        else
                                        {
                                            animEvent.variantOrBiome = 0;
                                        }
                                        currentColor = B2URuntimeUtility.GetColorLUTPixel(paletteData, mat, animEvent.changeEmission, animEvent.variantOrBiome, isSecondRow);
                                    }
                                    break;
                            }

                            using (new EditorGUI.DisabledScope(true))
                            {
                                EditorGUILayout.ColorField(CurrentColorLabel, currentColor, showEyedropper: true, showAlpha: false, hdr: true);
                            }

                            animEvent.useCustomStart = EditorGUILayout.Toggle(UseCustomStartLabel, animEvent.useCustomStart);
                            if (animEvent.useCustomStart)
                            {
                                Color startCol = EditorGUILayout.ColorField(StartColorLabel, animEvent.customStart, showEyedropper: true, showAlpha: false, hdr: true);
                                animEvent.customStart = new Color(Mathf.Clamp01(startCol.r), Mathf.Clamp01(startCol.g), Mathf.Clamp01(startCol.b), 1f);
                            }

                            Color targetCol = EditorGUILayout.ColorField(TargetColorLabel, animEvent.target, showEyedropper: true, showAlpha: false, hdr: true);
                            animEvent.target = new Color(Mathf.Clamp01(targetCol.r), Mathf.Clamp01(targetCol.g), Mathf.Clamp01(targetCol.b), 1f);
                        }
                        else
                        {
                            SchemaSlot[] slotMap = mat.workflow switch
                            {
                                B2UWorkflow.Transparent => paletteData.transparentSchema,
                                B2UWorkflow.Crystal => paletteData.crystalSchema,
                                B2UWorkflow.Liquid => paletteData.liquidSchema,
                                _ => paletteData.opaqueSchema
                            };

                            if (slotMap == null || slotMap.Length == 0)
                            {
                                EditorGUILayout.HelpBox("Schema definitions for this workflow are empty.", MessageType.Warning);
                                break;
                            }

                            List<B2USlotKey> validSlotKeys = new List<B2USlotKey>();
                            List<SchemaSlot> validSchemaSlots = new List<SchemaSlot>();
                            List<string> validSlotNames = new List<string>();

                            for (int i = 0; i < slotMap.Length; i++)
                            {
                                SchemaSlot s = slotMap[i];
                                if (s == null || !s.isActive || string.IsNullOrEmpty(s.slotID) || s.slotID == "r1_p4_r")
                                {
                                    continue;
                                }

                                // Hide backface properties (r1_p3_*) on single-sided Opaque materials
                                if (mat.workflow == B2UWorkflow.Opaque && s.slotID.StartsWith("r1_p3_"))
                                {
                                    continue;
                                }

                                if (Enum.TryParse(s.slotID, out B2USlotKey parsedKey))
                                {
                                    validSlotKeys.Add(parsedKey);
                                    validSchemaSlots.Add(s);
                                    validSlotNames.Add(string.IsNullOrEmpty(s.uiName) ? s.slotID : s.uiName);
                                }
                            }

                            if (validSlotKeys.Count == 0)
                            {
                                EditorGUILayout.HelpBox("No active animatable property slots found for this material workflow.", MessageType.Warning);
                                break;
                            }

                            int currentSlotIndex = validSlotKeys.IndexOf(animEvent.slotKey);
                            if (currentSlotIndex == -1) currentSlotIndex = 0;

                            int newSlotIndex = EditorGUILayout.Popup(TargetSlotLabel, currentSlotIndex, validSlotNames.ToArray());
                            animEvent.slotKey = validSlotKeys[newSlotIndex];
                            SchemaSlot activeSchemaSlot = validSchemaSlots[newSlotIndex];

                            Vector2 propMinMax = new Vector2(activeSchemaSlot.minVal, activeSchemaSlot.maxVal);

                            float currentNormalized = Mathf.Clamp01(B2URuntimeUtility.GetPropertyLUTValue(paletteData, mat, animEvent.slotKey));
                            EditorGUILayout.LabelField("Current Value:", Mathf.Lerp(propMinMax.x, propMinMax.y, currentNormalized).ToString("F2"));

                            animEvent.useCustomStart = EditorGUILayout.Toggle(UseCustomStartLabel, animEvent.useCustomStart);
                            if (animEvent.useCustomStart)
                            {
                                float displayStart = Mathf.Lerp(propMinMax.x, propMinMax.y, animEvent.customStart.x);
                                displayStart = EditorGUILayout.Slider(StartValueLabel, displayStart, propMinMax.x, propMinMax.y);
                                animEvent.customStart.x = Mathf.InverseLerp(propMinMax.x, propMinMax.y, displayStart);
                            }

                            float displayTarget = Mathf.Lerp(propMinMax.x, propMinMax.y, animEvent.target.x);
                            displayTarget = EditorGUILayout.Slider(TargetValueLabel, displayTarget, propMinMax.x, propMinMax.y);
                            animEvent.target.x = Mathf.InverseLerp(propMinMax.x, propMinMax.y, displayTarget);
                        }
                        break;
                    }

                case B2UAnimationGroup.RSUV:
                    {
                        EditorGUILayout.BeginHorizontal();
                        animEvent.rsuvMono = (B2URSUVMono)EditorGUILayout.ObjectField(TargetRSUVMonoLabel, animEvent.rsuvMono, typeof(B2URSUVMono), true);

                        if (GUILayout.Button("This", GUILayout.Width(45f)))
                        {
                            animEvent.rsuvMono = ((B2UAnimationTrigger)target).GetComponent<B2URSUVMono>();
                        }
                        EditorGUILayout.EndHorizontal();

                        animEvent.rsuvAnimType = (B2URSUVAnimType)EditorGUILayout.EnumPopup(RSUVParamLabel, animEvent.rsuvAnimType);

                        float minVal = (int)animEvent.rsuvAnimType > 3 ? -1f : 0f;
                        animEvent.useCustomStart = EditorGUILayout.Toggle(UseCustomStartLabel, animEvent.useCustomStart);
                        if (animEvent.useCustomStart)
                        {
                            animEvent.customStart = new Vector4(EditorGUILayout.Slider(StartValueLabel, animEvent.customStart.x, minVal, 1f), 0f, 0f, 0f);
                        }

                        animEvent.target = new Vector4(EditorGUILayout.Slider(TargetValueLabel, animEvent.target.x, minVal, 1f), 0f, 0f, 0f);

                        if (animEvent.rsuvMono != null)
                        {
                            var rType = animEvent.rsuvMono.activeType;

                            if (animEvent.rsuvAnimType == B2URSUVAnimType.CrackIntensity && rType == B2URSUVMono.RSUVType.Liquid)
                                EditorGUILayout.HelpBox("Crack Intensity only applies to Solid and Crystal RSUVs.", MessageType.Warning);
                            else if ((animEvent.rsuvAnimType == B2URSUVAnimType.Transmission || animEvent.rsuvAnimType == B2URSUVAnimType.Absorption) && rType != B2URSUVMono.RSUVType.Crystal)
                                EditorGUILayout.HelpBox("Transmission and Absorption only apply to Crystal RSUVs.", MessageType.Warning);
                            else if ((animEvent.rsuvAnimType == B2URSUVAnimType.FlowStrength || animEvent.rsuvAnimType == B2URSUVAnimType.WaveMult) && rType != B2URSUVMono.RSUVType.Liquid)
                                EditorGUILayout.HelpBox("Flow Strength and Wave Multiplier only apply to Liquid RSUVs.", MessageType.Warning);
                        }
                        break;
                    }
            }

            DrawTriggerButtons(animEvent, allowKill: true);
        }

        private void DrawTriggerButtons(B2UAnimationEvent animEvent, bool allowKill)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Trigger Event", GUILayout.Height(22f)))
            {
                animEvent.TriggerAnimation();
                if (!Application.isPlaying) SceneView.RepaintAll();
            }

            if (allowKill)
            {
                using (new EditorGUI.DisabledScope(animEvent.task == null))
                {
                    if (GUILayout.Button("Kill Animation", GUILayout.Height(22f)))
                    {
                        animEvent.KillTask();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            if (allowKill && animEvent.task != null)
            {
                EditorGUILayout.HelpBox("This event has dispatched a task to the runtime animator and is actively running.", MessageType.Warning);
            }
        }

        public void InitializeData()
        {
            validFloatPropertyEnums.Clear();
            List<string> tempPropNames = new List<string>();

            if (B2URuntimeUtility.ActiveProfile == null || B2URuntimeUtility.ActiveProfile.propertyBlocks == null)
            {
                validFloatPropertyNames = Array.Empty<string>();
                return;
            }

            foreach (KeyValuePair<B2UShaderProperty, B2UShaderPropertyBlock> kvp in B2URuntimeUtility.ActiveProfile.propertyBlocks)
            {
                if (kvp.Value.type == typeof(float))
                {
                    validFloatPropertyEnums.Add(kvp.Key);

                    string name = B2UShaderProfileEditor.GetBlockUiName(kvp.Key);
                    string safeName = name ?? kvp.Value.name ?? kvp.Key.ToString();
                    tempPropNames.Add(safeName.Replace('/', '-'));
                }
            }
            validFloatPropertyNames = tempPropNames.ToArray();
        }
    }
}
#endif