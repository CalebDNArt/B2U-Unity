#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Destructive mesh utility window for inspecting and modifying baked B2U Material IDs,
    /// Solid Color Variants (encoded in the float sign bits of UV1.xyz edge distances), and Liquid Mesh Flags (packed in UV1.y).
    /// </summary>
    public class B2UMaterialChanger : EditorWindow
    {
        private Mesh targetMesh;
        private bool isLiquid = false;
        private Vector2 scrollPos;

        // Solid / Crystal Data
        private readonly List<DetectedSolidSlot> detectedSolidSlots = new List<DetectedSolidSlot>();
        private readonly Dictionary<DetectedSolidSlot, bool> replaceMask = new Dictionary<DetectedSolidSlot, bool>();
        private readonly Dictionary<DetectedSolidSlot, int> slotVertexCounts = new Dictionary<DetectedSolidSlot, int>();

        private readonly List<SolidTargetOption> validSolidTargets = new List<SolidTargetOption>();
        private string[] solidTargetPopupLabels = Array.Empty<string>();
        private int selectedSolidTargetIndex = 0;

        // Liquid Data
        private byte currentLiquidID;
        private int selectedLiquidTargetIndex = 0;
        private readonly List<LiquidTargetOption> validLiquidTargets = new List<LiquidTargetOption>();
        private string[] liquidTargetPopupLabels = Array.Empty<string>();

        // Liquid Bitmask Flags (UV1.y bits 8..16)
        private bool isTessellated;
        private bool useVolumetricRefraction;
        private bool useDisplacement;
        private bool isTransmissive;
        private bool ignoreGravity;
        private bool useCaustics;
        private bool invertHeight;
        private bool hbTrans;
        private bool hbEmis;

        // Reusable mesh attribute buffers
        private static readonly List<Color32> s_TempColors = new List<Color32>();
        private static readonly List<Vector4> s_TempUV1_4D = new List<Vector4>();
        private static readonly List<Vector2> s_TempUV1_2D = new List<Vector2>();

        private struct DetectedSolidSlot : IEquatable<DetectedSolidSlot>, IComparable<DetectedSolidSlot>
        {
            public byte materialID;
            public int variantIndex; // 0..7 for Solid Independent rows, -1 for Dynamic/Crystal rows

            public bool Equals(DetectedSolidSlot other)
            {
                return materialID == other.materialID && variantIndex == other.variantIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is DetectedSolidSlot other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (materialID << 8) ^ (variantIndex + 2);
            }

            public int CompareTo(DetectedSolidSlot other)
            {
                int idCmp = materialID.CompareTo(other.materialID);
                return idCmp != 0 ? idCmp : variantIndex.CompareTo(other.variantIndex);
            }
        }

        private struct SolidTargetOption
        {
            public B2UMaterial baseMaterial;
            public byte targetRow;
            public int targetVariant; // 0..7 = set specific variant, -1 = keep/no variant
            public bool isDynamicRow;
            public bool supportsVariants;
            public string popupPath;
            public string displaySummary;
        }

        private struct LiquidTargetOption
        {
            public B2UMaterial baseMaterial;
            public byte targetRow;
            public bool isDynamicRow;
            public string popupLabel;
        }

        [MenuItem("B2U/Material Changer")]
        public static void ShowWindow()
        {
            B2UMaterialChanger window = GetWindow<B2UMaterialChanger>("B2U Material Changer");
            window.minSize = new Vector2(420f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += AnalyzeSelection;
            Undo.undoRedoPerformed += AnalyzeSelection;
            AnalyzeSelection();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= AnalyzeSelection;
            Undo.undoRedoPerformed -= AnalyzeSelection;
        }

        #region UV1.xyz Edge-Distance Sign Variant Helpers
        /// <summary>
        /// Unpacks the 3-bit variant index (0-7) from the IEEE-754 sign bits of UV1.xyz
        /// (merged from B2U_EdgeDistances_XY and B2U_EdgeDistances_ZW.x by B2UMeshPostprocessor).
        /// Matches B2UGetVariantIndexInternal in HLSL.
        /// </summary>
        private static int UnpackVariantIndex(Vector4 edgeDistancesUV1)
        {
            uint bit0 = math.asuint(edgeDistancesUV1.x) >> 31;
            uint bit1 = math.asuint(edgeDistancesUV1.y) >> 31;
            uint bit2 = math.asuint(edgeDistancesUV1.z) >> 31;

            return (int)(bit0 | (bit1 << 1) | (bit2 << 2));
        }

        /// <summary>
        /// Encodes a 3-bit variant index (0-7) into the signs of UV1.xyz while preserving the edge distance magnitudes
        /// and clamping to >= 0.0001f (matching Blender's B2U_OT_assign_material_id operator).
        /// </summary>
        private static Vector4 PackVariantIndex(Vector4 edgeDistancesUV1, int variantIndex)
        {
            float distX = Mathf.Max(Mathf.Abs(edgeDistancesUV1.x), 0.0001f);
            float distY = Mathf.Max(Mathf.Abs(edgeDistancesUV1.y), 0.0001f);
            float distZ = Mathf.Max(Mathf.Abs(edgeDistancesUV1.z), 0.0001f);

            edgeDistancesUV1.x = ((variantIndex & 1) != 0) ? -distX : distX;
            edgeDistancesUV1.y = ((variantIndex & 2) != 0) ? -distY : distY;
            edgeDistancesUV1.z = ((variantIndex & 4) != 0) ? -distZ : distZ;

            return edgeDistancesUV1;
        }
        #endregion

        #region Selection & Palette Analysis
        private static bool IsSolidWorkflow(B2UWorkflow wf)
        {
            return wf == B2UWorkflow.Opaque ||
                   wf == B2UWorkflow.OpaqueDoubleSided ||
                   wf == B2UWorkflow.Transparent;
        }

        /// <summary>
        /// Resolves the B2UMaterial owning a specific LUT row index, accounting for secondary Dynamic rows on INDEP_DYN materials.
        /// </summary>
        private static B2UMaterial ResolveMaterialAtRow(B2UPaletteData palette, byte rowId, out bool isDynamicRow)
        {
            isDynamicRow = false;
            if (palette?.materials == null) return null;

            B2UMaterial directMat = palette.materials.GetMaterialByIndex(rowId);
            if (directMat != null)
            {
                isDynamicRow = directMat.biomeType == "DYNAMIC";
                return directMat;
            }

            if (rowId >= 1)
            {
                B2UMaterial prevMat1 = palette.materials.GetMaterialByIndex((byte)(rowId - 1));
                if (prevMat1 != null && prevMat1.workflow != B2UWorkflow.Liquid &&
                    (prevMat1.biomeType == "INDEP_DYN" || prevMat1.biomeType == "INDEP-DYN"))
                {
                    isDynamicRow = true;
                    return prevMat1;
                }
            }

            if (rowId >= 2)
            {
                B2UMaterial prevMat2 = palette.materials.GetMaterialByIndex((byte)(rowId - 2));
                if (prevMat2 != null && prevMat2.workflow == B2UWorkflow.Liquid &&
                    (prevMat2.biomeType == "INDEP_DYN" || prevMat2.biomeType == "INDEP-DYN"))
                {
                    isDynamicRow = true;
                    return prevMat2;
                }
            }

            return null;
        }

        private void AnalyzeSelection()
        {
            targetMesh = null;
            detectedSolidSlots.Clear();
            replaceMask.Clear();
            slotVertexCounts.Clear();
            validSolidTargets.Clear();
            validLiquidTargets.Clear();
            solidTargetPopupLabels = Array.Empty<string>();
            liquidTargetPopupLabels = Array.Empty<string>();

            GameObject obj = Selection.activeGameObject;
            if (obj == null)
            {
                Repaint();
                return;
            }

            if (obj.TryGetComponent<MeshFilter>(out var mf)) targetMesh = mf.sharedMesh;
            else if (obj.TryGetComponent<SkinnedMeshRenderer>(out var smr)) targetMesh = smr.sharedMesh;

            if (targetMesh == null || targetMesh.vertexCount == 0)
            {
                targetMesh = null;
                Repaint();
                return;
            }

            if (!B2URuntimeUtility.CheckPaletteMaterials(silent: true))
            {
                Repaint();
                return;
            }

            B2UPaletteData palette = B2URuntimeUtility.ActiveProfile.paletteData;

            s_TempColors.Clear();
            if (targetMesh.HasVertexAttribute(VertexAttribute.Color))
            {
                targetMesh.GetColors(s_TempColors);
            }

            if (s_TempColors.Count > 0)
            {
                isLiquid = false;

                // Postprocessed solid meshes store merged EdgeDistances_XY and EdgeDistances_ZW in UV1 (channel 1)
                s_TempUV1_4D.Clear();
                targetMesh.GetUVs(1, s_TempUV1_4D);
                bool hasEdgeDistanceUVs = s_TempUV1_4D.Count == s_TempColors.Count;

                B2UMaterial firstValidMat = null;

                for (int i = 0; i < s_TempColors.Count; i++)
                {
                    byte rowId = s_TempColors[i].a;
                    B2UMaterial mat = ResolveMaterialAtRow(palette, rowId, out bool isDynRow);
                    if (firstValidMat == null && mat != null)
                    {
                        firstValidMat = mat;
                    }

                    bool usesVariants = mat != null && IsSolidWorkflow(mat.workflow) && !isDynRow;
                    int variantIdx = (usesVariants && hasEdgeDistanceUVs) ? UnpackVariantIndex(s_TempUV1_4D[i]) : -1;

                    DetectedSolidSlot slot = new DetectedSolidSlot
                    {
                        materialID = rowId,
                        variantIndex = variantIdx
                    };

                    if (slotVertexCounts.TryGetValue(slot, out int count))
                    {
                        slotVertexCounts[slot] = count + 1;
                    }
                    else
                    {
                        slotVertexCounts[slot] = 1;
                        detectedSolidSlots.Add(slot);
                    }
                }

                detectedSolidSlots.Sort();
                for (int i = 0; i < detectedSolidSlots.Count; i++)
                {
                    replaceMask[detectedSolidSlots[i]] = (detectedSolidSlots.Count == 1);
                }

                PopulatePaletteTargets(firstValidMat != null ? firstValidMat.workflow : B2UWorkflow.Opaque);
            }
            else
            {
                s_TempUV1_2D.Clear();
                targetMesh.GetUVs(1, s_TempUV1_2D);

                if (s_TempUV1_2D.Count > 0)
                {
                    isLiquid = true;
                    UnpackLiquidData(s_TempUV1_2D[0].y);
                    PopulatePaletteTargets(B2UWorkflow.Liquid);
                }
                else
                {
                    targetMesh = null;
                }
            }

            Repaint();
        }

        private void UnpackLiquidData(float rawV)
        {
            uint packedV = (uint)Mathf.RoundToInt(rawV);

            currentLiquidID = (byte)(packedV & 0xFFu);
            isTessellated = ((packedV >> 8) & 1u) == 1u;
            useVolumetricRefraction = ((packedV >> 9) & 1u) == 1u;
            useDisplacement = ((packedV >> 10) & 1u) == 1u;
            isTransmissive = ((packedV >> 11) & 1u) == 1u;
            ignoreGravity = ((packedV >> 12) & 1u) == 1u;
            useCaustics = ((packedV >> 13) & 1u) == 1u;
            invertHeight = ((packedV >> 14) & 1u) == 1u;
            hbTrans = ((packedV >> 15) & 1u) == 1u;
            hbEmis = ((packedV >> 16) & 1u) == 1u;
        }

        private uint PackLiquidData(byte matID)
        {
            uint packed = (uint)matID & 0xFFu;

            if (isTessellated) packed |= (1u << 8);
            if (useVolumetricRefraction) packed |= (1u << 9);
            if (useDisplacement) packed |= (1u << 10);
            if (isTransmissive) packed |= (1u << 11);
            if (ignoreGravity) packed |= (1u << 12);
            if (useCaustics) packed |= (1u << 13);
            if (invertHeight) packed |= (1u << 14);
            if (hbTrans) packed |= (1u << 15);
            if (hbEmis) packed |= (1u << 16);

            return packed;
        }

        private void PopulatePaletteTargets(B2UWorkflow existingWorkflow)
        {
            if (!B2URuntimeUtility.CheckPaletteMaterials(silent: true)) return;

            B2UPaletteData palette = B2URuntimeUtility.ActiveProfile.paletteData;

            foreach (var mat in palette.materials.matArray)
            {
                if (mat == null || string.IsNullOrEmpty(mat.slotName)) continue;
                if (mat.workflow != existingWorkflow) continue;

                string cleanName = mat.slotName.Replace('/', '-');
                bool isDual = mat.biomeType == "INDEP_DYN" || mat.biomeType == "INDEP-DYN";

                if (existingWorkflow == B2UWorkflow.Liquid)
                {
                    if (mat.biomeType == "INDEPENDENT" || isDual)
                    {
                        string suffix = isDual ? " (Independent)" : "";
                        validLiquidTargets.Add(new LiquidTargetOption
                        {
                            baseMaterial = mat,
                            targetRow = mat.alphaValue,
                            isDynamicRow = false,
                            popupLabel = $"ID: {mat.alphaValue} - {cleanName}{suffix}"
                        });
                    }

                    if (mat.biomeType == "DYNAMIC" || isDual)
                    {
                        byte dynRow = (byte)(isDual ? mat.alphaValue + 2 : mat.alphaValue);
                        validLiquidTargets.Add(new LiquidTargetOption
                        {
                            baseMaterial = mat,
                            targetRow = dynRow,
                            isDynamicRow = true,
                            popupLabel = $"ID: {dynRow} - {cleanName} (Dynamic)"
                        });
                    }
                }
                else
                {
                    bool solidSupportsVariants = IsSolidWorkflow(mat.workflow);

                    if (mat.biomeType == "INDEPENDENT" || isDual)
                    {
                        string indepPrefix = isDual
                            ? $"ID: {mat.alphaValue} - {cleanName} (Independent)"
                            : $"ID: {mat.alphaValue} - {cleanName}";

                        if (solidSupportsVariants)
                        {
                            for (int v = 0; v < 8; v++)
                            {
                                validSolidTargets.Add(new SolidTargetOption
                                {
                                    baseMaterial = mat,
                                    targetRow = mat.alphaValue,
                                    targetVariant = v,
                                    isDynamicRow = false,
                                    supportsVariants = true,
                                    popupPath = $"{indepPrefix}/Variant {v + 1}",
                                    displaySummary = $"ID {mat.alphaValue}: {mat.slotName} — Variant {v + 1}"
                                });
                            }

                            validSolidTargets.Add(new SolidTargetOption
                            {
                                baseMaterial = mat,
                                targetRow = mat.alphaValue,
                                targetVariant = -1,
                                isDynamicRow = false,
                                supportsVariants = true,
                                popupPath = $"{indepPrefix}/Keep Existing Variant",
                                displaySummary = $"ID {mat.alphaValue}: {mat.slotName} — Keep Existing Variant"
                            });
                        }
                        else
                        {
                            validSolidTargets.Add(new SolidTargetOption
                            {
                                baseMaterial = mat,
                                targetRow = mat.alphaValue,
                                targetVariant = -1,
                                isDynamicRow = false,
                                supportsVariants = false,
                                popupPath = indepPrefix,
                                displaySummary = $"ID {mat.alphaValue}: {mat.slotName}"
                            });
                        }
                    }

                    if (mat.biomeType == "DYNAMIC" || isDual)
                    {
                        byte dynRow = (byte)(isDual ? mat.alphaValue + 1 : mat.alphaValue);
                        validSolidTargets.Add(new SolidTargetOption
                        {
                            baseMaterial = mat,
                            targetRow = dynRow,
                            targetVariant = -1,
                            isDynamicRow = true,
                            supportsVariants = false,
                            popupPath = $"ID: {dynRow} - {cleanName} (Dynamic)",
                            displaySummary = $"ID {dynRow}: {mat.slotName} (Biome Dynamic)"
                        });
                    }
                }
            }

            solidTargetPopupLabels = new string[validSolidTargets.Count];
            for (int i = 0; i < validSolidTargets.Count; i++)
            {
                solidTargetPopupLabels[i] = validSolidTargets[i].popupPath;
            }

            liquidTargetPopupLabels = new string[validLiquidTargets.Count];
            for (int i = 0; i < validLiquidTargets.Count; i++)
            {
                liquidTargetPopupLabels[i] = validLiquidTargets[i].popupLabel;
            }

            selectedSolidTargetIndex = Mathf.Clamp(selectedSolidTargetIndex, 0, Mathf.Max(0, validSolidTargets.Count - 1));
            selectedLiquidTargetIndex = Mathf.Clamp(selectedLiquidTargetIndex, 0, Mathf.Max(0, validLiquidTargets.Count - 1));
        }
        #endregion

        #region GUI Rendering
        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (B2URuntimeUtility.ActiveProfile == null || B2URuntimeUtility.ActiveProfile.paletteData == null)
            {
                EditorGUILayout.HelpBox("An active B2UShaderProfile with an assigned B2UPaletteData asset is required.", MessageType.Error);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                return;
            }

            if (targetMesh == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject in the scene with a B2U-compatible MeshFilter or SkinnedMeshRenderer.", MessageType.Info);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Selected Mesh: {targetMesh.name} ({targetMesh.vertexCount:N0} vertices)", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox("DESTRUCTIVE OPERATION\nApplying changes permanently overwrites the vertex colors / UVs on the mesh.", MessageType.Warning);
            EditorGUILayout.Space(6f);

            if (!isLiquid)
            {
                DrawSolidInterface();
            }
            else
            {
                DrawLiquidInterface();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSolidInterface()
        {
            B2UPaletteData palette = B2URuntimeUtility.ActiveProfile.paletteData;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Existing Materials & Variants on Mesh", EditorStyles.boldLabel);
            if (detectedSolidSlots.Count > 1)
            {
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(40f)))
                {
                    for (int i = 0; i < detectedSolidSlots.Count; i++) replaceMask[detectedSolidSlots[i]] = true;
                }
                if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(45f)))
                {
                    for (int i = 0; i < detectedSolidSlots.Count; i++) replaceMask[detectedSolidSlots[i]] = false;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool anySelected = false;

            for (int i = 0; i < detectedSolidSlots.Count; i++)
            {
                DetectedSolidSlot slot = detectedSolidSlots[i];
                B2UMaterial mat = ResolveMaterialAtRow(palette, slot.materialID, out bool isDynRow);

                string matName = (mat != null && !string.IsNullOrEmpty(mat.slotName))
                    ? $"{mat.slotName} ({(isDynRow ? "Dynamic" : mat.workflow.ToString())})"
                    : "Unknown Material";

                string variantText = slot.variantIndex >= 0 ? $" — Variant {slot.variantIndex + 1}" : "";
                int vCount = slotVertexCounts.TryGetValue(slot, out int vc) ? vc : 0;

                EditorGUILayout.BeginHorizontal();
                bool currentMask = replaceMask.TryGetValue(slot, out bool mVal) && mVal;
                bool newMask = EditorGUILayout.ToggleLeft($"ID {slot.materialID}: {matName}{variantText} [{vCount} verts]", currentMask);
                replaceMask[slot] = newMask;
                if (newMask) anySelected = true;

                if (mat != null && !isDynRow && palette.colorLUT != null)
                {
                    Color previewCol = B2URuntimeUtility.GetColorLUTPixel(palette, mat, false, slot.variantIndex >= 0 ? slot.variantIndex : 0, false);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ColorField(GUIContent.none, previewCol, false, false, true, GUILayout.Width(42f));
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Replacement Material & Variant", EditorStyles.boldLabel);

            if (validSolidTargets.Count == 0)
            {
                EditorGUILayout.HelpBox("No compatible solid/crystal materials found in the active palette.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            selectedSolidTargetIndex = Mathf.Clamp(selectedSolidTargetIndex, 0, validSolidTargets.Count - 1);
            selectedSolidTargetIndex = EditorGUILayout.Popup(
                new GUIContent("Target Material", "Select a replacement material. Solid Biome-Independent materials expand into a submenu to pick Variant 1-8."),
                selectedSolidTargetIndex,
                solidTargetPopupLabels
            );

            SolidTargetOption chosen = validSolidTargets[selectedSolidTargetIndex];

            if (chosen.supportsVariants && chosen.targetVariant >= 0)
            {
                EditorGUI.BeginChangeCheck();
                int newVariant = EditorGUILayout.IntSlider(
                    new GUIContent("Variant (1-8)", "Quick-selects Variant 1-8 for the chosen material."),
                    chosen.targetVariant + 1,
                    1,
                    8
                ) - 1;

                if (EditorGUI.EndChangeCheck())
                {
                    int matchingIdx = validSolidTargets.FindIndex(t =>
                        t.targetRow == chosen.targetRow &&
                        t.isDynamicRow == chosen.isDynamicRow &&
                        t.targetVariant == newVariant);

                    if (matchingIdx >= 0)
                    {
                        selectedSolidTargetIndex = matchingIdx;
                        chosen = validSolidTargets[selectedSolidTargetIndex];
                    }
                }
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Selected Target:", chosen.displaySummary, EditorStyles.miniBoldLabel);

            if (chosen.baseMaterial != null && !chosen.isDynamicRow && palette.colorLUT != null)
            {
                int previewVar = chosen.targetVariant >= 0 ? chosen.targetVariant : 0;
                Color basePreview = B2URuntimeUtility.GetColorLUTPixel(palette, chosen.baseMaterial, false, previewVar, false);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ColorField(GUIContent.none, basePreview, false, false, true, GUILayout.Width(55f));
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!anySelected))
            {
                if (GUILayout.Button("Apply Destructive Changes to Mesh", GUILayout.Height(30f)))
                {
                    ApplySolidChanges();
                }
            }
        }

        private void DrawLiquidInterface()
        {
            B2UPaletteData palette = B2URuntimeUtility.ActiveProfile.paletteData;
            B2UMaterial currentMat = ResolveMaterialAtRow(palette, currentLiquidID, out bool isDyn);
            string matName = currentMat != null ? $"{currentMat.slotName}{(isDyn ? " - Dynamic" : "")}" : "Unknown Liquid";

            EditorGUILayout.LabelField($"Current Liquid: ID {currentLiquidID} ({matName})", EditorStyles.boldLabel);

            if (validLiquidTargets.Count > 0)
            {
                selectedLiquidTargetIndex = Mathf.Clamp(selectedLiquidTargetIndex, 0, validLiquidTargets.Count - 1);
                selectedLiquidTargetIndex = EditorGUILayout.Popup("Replace With", selectedLiquidTargetIndex, liquidTargetPopupLabels);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Mesh-Wide Liquid Properties (UV1.y)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            isTessellated = EditorGUILayout.ToggleLeft(new GUIContent("Is Tessellated (Bit 8)", "Marks the liquid mesh for dynamic GPU tessellation."), isTessellated);
            useVolumetricRefraction = EditorGUILayout.ToggleLeft(new GUIContent("Volumetric Refraction (Bit 9)", "Enables backface thickness rendering for volumetric liquids."), useVolumetricRefraction);
            useDisplacement = EditorGUILayout.ToggleLeft(new GUIContent("Use Displacement (Bit 10)", "Enables vertex wave height displacement on this liquid mesh."), useDisplacement);
            isTransmissive = EditorGUILayout.ToggleLeft(new GUIContent("Is Transmissive (Bit 11)", "Enables background refraction through the liquid."), isTransmissive);
            ignoreGravity = EditorGUILayout.ToggleLeft(new GUIContent("Ignore Gravity (Bit 12)", "Prevents gravity-based vertical flow overrides."), ignoreGravity);
            useCaustics = EditorGUILayout.ToggleLeft(new GUIContent("Use Caustics (Bit 13)", "Enables projected caustics on geometry beneath the liquid."), useCaustics);
            invertHeight = EditorGUILayout.ToggleLeft(new GUIContent("Invert Height (Bit 14)", "Inverts the procedural noise heightmap."), invertHeight);
            hbTrans = EditorGUILayout.ToggleLeft(new GUIContent("Height-Based Transmission (Bit 15)", "Modulates liquid transmission by wave height."), hbTrans);
            hbEmis = EditorGUILayout.ToggleLeft(new GUIContent("Height-Based Emission (Bit 16)", "Modulates liquid emission by wave height."), hbEmis);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);

            if (GUILayout.Button("Apply Destructive Changes to Mesh", GUILayout.Height(30f)))
            {
                ApplyLiquidChanges();
            }
        }
        #endregion

        #region Mesh Modification Execution
        private void ApplySolidChanges()
        {
            if (targetMesh == null || validSolidTargets.Count == 0) return;

            B2UPaletteData palette = B2URuntimeUtility.ActiveProfile.paletteData;
            SolidTargetOption targetOption = validSolidTargets[selectedSolidTargetIndex];

            s_TempColors.Clear();
            targetMesh.GetColors(s_TempColors);
            if (s_TempColors.Count == 0) return;

            // Read merged UV1 (B2U_EdgeDistances_XY in .xy, B2U_EdgeDistances_ZW in .zw)
            s_TempUV1_4D.Clear();
            targetMesh.GetUVs(1, s_TempUV1_4D);
            bool hasEdgeDistanceUVs = s_TempUV1_4D.Count == s_TempColors.Count;
            bool modifiedUV1 = false;

            for (int i = 0; i < s_TempColors.Count; i++)
            {
                Color32 c = s_TempColors[i];
                B2UMaterial existingMat = ResolveMaterialAtRow(palette, c.a, out bool isDynRow);
                bool existingUsesVariants = existingMat != null && IsSolidWorkflow(existingMat.workflow) && !isDynRow;
                int existingVariant = (existingUsesVariants && hasEdgeDistanceUVs) ? UnpackVariantIndex(s_TempUV1_4D[i]) : -1;

                DetectedSolidSlot vertexSlot = new DetectedSolidSlot
                {
                    materialID = c.a,
                    variantIndex = existingVariant
                };

                if (replaceMask.TryGetValue(vertexSlot, out bool shouldReplace) && shouldReplace)
                {
                    c.a = targetOption.targetRow;
                    s_TempColors[i] = c;

                    if (hasEdgeDistanceUVs && targetOption.supportsVariants && targetOption.targetVariant >= 0)
                    {
                        s_TempUV1_4D[i] = PackVariantIndex(s_TempUV1_4D[i], targetOption.targetVariant);
                        modifiedUV1 = true;
                    }
                }
            }

            Undo.RecordObject(targetMesh, "B2U Replace Solid Material & Variant");
            targetMesh.SetColors(s_TempColors);
            if (modifiedUV1)
            {
                targetMesh.SetUVs(1, s_TempUV1_4D);
            }

            SyncSelectedObjectRSUVMono();
            FinalizeMeshSave();
        }

        private void ApplyLiquidChanges()
        {
            if (targetMesh == null || validLiquidTargets.Count == 0) return;

            LiquidTargetOption targetOption = validLiquidTargets[selectedLiquidTargetIndex];
            uint packedV = PackLiquidData(targetOption.targetRow);
            float newV = packedV;

            s_TempUV1_2D.Clear();
            targetMesh.GetUVs(1, s_TempUV1_2D);

            for (int i = 0; i < s_TempUV1_2D.Count; i++)
            {
                s_TempUV1_2D[i] = new Vector2(s_TempUV1_2D[i].x, newV);
            }

            Undo.RecordObject(targetMesh, "B2U Replace Liquid Data");
            targetMesh.SetUVs(1, s_TempUV1_2D);

            currentLiquidID = targetOption.targetRow;
            SyncSelectedObjectRSUVMono();
            FinalizeMeshSave();
        }

        private void SyncSelectedObjectRSUVMono()
        {
            GameObject obj = Selection.activeGameObject;
            if (obj != null && obj.TryGetComponent<B2URSUVMono>(out var mono))
            {
                Undo.RecordObject(mono, "B2U Sync Material ID");
                mono.UpdateMaterialID();
                EditorUtility.SetDirty(mono);
            }
        }

        private void FinalizeMeshSave()
        {
            targetMesh.UploadMeshData(false);
            EditorUtility.SetDirty(targetMesh);

            string assetPath = AssetDatabase.GetAssetPath(targetMesh);
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.SaveAssets();
                B2ULog(B2UMessageSource.Editor, $"Successfully modified mesh: {assetPath}", B2UMessageType.None, this);
            }
            else
            {
                B2ULogWarning(B2UMessageSource.Editor, "Mesh was modified in memory, but it is not saved as an independent .asset file on disk.");
            }

            SceneView.RepaintAll();
            AnalyzeSelection();
        }
        #endregion
    }
}
#endif