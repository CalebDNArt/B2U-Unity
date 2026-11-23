#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Dedicated EditorWindow for inspecting, adding, removing, and live-tweaking B2U palette materials,
    /// schema properties, and variant/biome colors directly in Unity while keeping LUTs and Blender JSON files synchronized.
    /// </summary>
    public class B2UPaletteTweaker : EditorWindow
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Editor;

        [SerializeField] private B2UPaletteData activePalette;
        private readonly List<B2UPaletteData> allPalettes = new List<B2UPaletteData>();
        private string[] paletteNames = Array.Empty<string>();
        private int selectedPaletteIndex = 0;

        [SerializeField] private int activeMaterialIndex = 0;
        private int previewVariantIndex = 0;
        private int previewBiomeIndex = 0;

        private Vector2 matListScroll;
        private Vector2 propertiesScroll;

        private float leftPanelWidth = 260f;
        private bool isResizing = false;

        private static readonly string[] BiomeTypes = { "INDEPENDENT", "DYNAMIC", "INDEP_DYN" };
        private static readonly string[] BiomeTypeLabels =
        {
            "Independent (Variants 1-8)",
            "Dynamic (Biomes 1-8)",
            "Independent & Dynamic (Dual Row)"
        };

        // Cached GUIContent with tooltips
        private static readonly GUIContent RefreshBtnContent = new GUIContent(
            "Refresh List",
            "Scans the project for B2UPaletteData assets and updates Sister Palette compatibility tags."
        );
        private static readonly GUIContent BuildHlslBtnContent = new GUIContent(
            "Build HLSL",
            "Generates B2U_Constants.hlsl from the active palette's schema Min/Max ranges."
        );
        private static readonly GUIContent RecoverLutsBtnContent = new GUIContent(
            "Recover from LUTs",
            "Reconstructs material slots and values by reverse-engineering the Color and Property LUT textures."
        );
        private static readonly GUIContent AddMatBtnContent = new GUIContent(
            "+ Add Material",
            "Allocates a new material slot at the lowest available LUT row index."
        );
        private static readonly GUIContent RemoveMatBtnContent = new GUIContent(
            "- Remove",
            "Removes the currently selected material from the palette."
        );

        private static readonly GUIContent MatNameLabel = new GUIContent("Material Name", "Display name of the material slot.");
        private static readonly GUIContent LockedLabel = new GUIContent("Locked", "Prevents accidental edits to this material's workflow, properties, or colors.");
        private static readonly GUIContent WorkflowLabel = new GUIContent("Workflow", "The B2U master shader workflow for this material.");
        private static readonly GUIContent BiomeTypeLabel = new GUIContent("Biome Response", "Controls whether this material uses 8 independent variants, 8 dynamic biomes, or allocates rows for both.");
        private static readonly GUIContent RowIdLabel = new GUIContent("Starting Row ID", "The 0-255 starting row index in the Color and Property LUTs.");

        [SerializeField] private bool _isDirty = false;
        private bool isDirty
        {
            get => _isDirty;
            set
            {
                _isDirty = value;
                hasUnsavedChanges = value;
                saveChangesMessage = "You have unsaved changes to the active B2U Palette. Would you like to save them to disk and JSON?";
            }
        }

        [MenuItem("B2U/Palette Tweaker")]
        public static void ShowWindow()
        {
            B2UPaletteTweaker window = GetWindow<B2UPaletteTweaker>("B2U Palette Tweaker");
            window.minSize = new Vector2(680f, 450f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshPalettes();

            if (_isDirty && activePalette != null && activePalette.editorOnlyMaterials != null)
            {
                isDirty = true;
                for (int i = 0; i < activePalette.editorOnlyMaterials.Length; i++)
                {
                    MaterialSlot mat = activePalette.editorOnlyMaterials[i];
                    if (mat != null && !string.IsNullOrEmpty(mat.materialName))
                    {
                        RefreshMaterialInLUTs(mat);
                    }
                }
                SceneView.RepaintAll();
            }

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            if (activePalette != null && activePalette.editorOnlyMaterials != null)
            {
                if (activeMaterialIndex >= 0 && activeMaterialIndex < activePalette.editorOnlyMaterials.Length)
                {
                    MaterialSlot mat = activePalette.editorOnlyMaterials[activeMaterialIndex];
                    if (mat != null)
                    {
                        RefreshMaterialInLUTs(mat);
                    }
                }
                isDirty = true;
                SceneView.RepaintAll();
                Repaint();
            }
        }

        public void CommitChanges()
        {
            if (activePalette != null)
            {
                activePalette.WritePaletteToJSONEditorOnly(activePalette.palettePath);
                activePalette.WriteLUTsToAssetsEditorOnly();
                EditorUtility.SetDirty(activePalette);
                AssetDatabase.SaveAssets();
                isDirty = false;
            }
        }

        public override void SaveChanges()
        {
            CommitChanges();
            base.SaveChanges();
        }

        /// <summary>
        /// Creates a temporary B2UMaterial pointer for LUT read/write calls.
        /// For INDEP_DYN materials, passing forDynamicRow = true offsets alphaValue to the dynamic row block.
        /// </summary>
        private B2UMaterial GetTempMat(MaterialSlot mat, bool forDynamicRow = false)
        {
            int rowOffset = 0;
            if (forDynamicRow && mat.biomeType == "INDEP_DYN")
            {
                rowOffset = mat.workflow == B2UWorkflow.Liquid ? 2 : 1;
            }

            return new B2UMaterial
            {
                workflow = mat.workflow,
                alphaValue = (byte)Mathf.Clamp(mat.startRowIndex + rowOffset, 0, 255),
                isBiomeResponsive = forDynamicRow || mat.biomeType == "DYNAMIC",
                biomeType = mat.biomeType,
                slotName = mat.materialName
            };
        }

        private void PushStructuralWorkflowToLUT(MaterialSlot mat, bool apply = false)
        {
            float baseWfVal = mat.workflow switch
            {
                B2UWorkflow.OpaqueDoubleSided => 0.1f,
                B2UWorkflow.Transparent => 0.2f,
                B2UWorkflow.Crystal => 0.3f,
                B2UWorkflow.Liquid => 0.4f,
                _ => 0.0f
            };

            if (mat.biomeType == "INDEPENDENT")
            {
                byte wfByte = (byte)Mathf.Clamp(Mathf.RoundToInt(baseWfVal * 255f), 0, 255);
                B2URuntimeUtility.SetPropertyLUTValue(activePalette, GetTempMat(mat, false), B2USlotKey.r1_p4_r, wfByte, apply);
            }
            else if (mat.biomeType == "DYNAMIC")
            {
                byte wfByte = (byte)Mathf.Clamp(Mathf.RoundToInt((baseWfVal + 0.5f) * 255f), 0, 255);
                B2URuntimeUtility.SetPropertyLUTValue(activePalette, GetTempMat(mat, false), B2USlotKey.r1_p4_r, wfByte, apply);
            }
            else if (mat.biomeType == "INDEP_DYN")
            {
                byte indepByte = (byte)Mathf.Clamp(Mathf.RoundToInt(baseWfVal * 255f), 0, 255);
                byte dynByte = (byte)Mathf.Clamp(Mathf.RoundToInt((baseWfVal + 0.5f) * 255f), 0, 255);
                B2URuntimeUtility.SetPropertyLUTValue(activePalette, GetTempMat(mat, false), B2USlotKey.r1_p4_r, indepByte, false);
                B2URuntimeUtility.SetPropertyLUTValue(activePalette, GetTempMat(mat, true), B2USlotKey.r1_p4_r, dynByte, apply);
            }
        }

        private void PushPropertyToLUT(MaterialSlot mat, B2USlotKey slotKey, byte bVal, bool apply = false)
        {
            B2URuntimeUtility.SetPropertyLUTValue(activePalette, GetTempMat(mat, false), slotKey, bVal, false);
            if (mat.biomeType == "INDEP_DYN")
            {
                B2URuntimeUtility.SetPropertyLUTValue(activePalette, GetTempMat(mat, true), slotKey, bVal, false);
            }

            if (apply)
            {
                if (slotKey == B2USlotKey.r1_ca || slotKey == B2USlotKey.r1_ea || slotKey == B2USlotKey.r2_ca || slotKey == B2USlotKey.r2_ea)
                {
                    if (activePalette.colorLUT != null) activePalette.colorLUT.Apply();
                }
                else
                {
                    if (activePalette.propertyLUT != null) activePalette.propertyLUT.Apply();
                }
            }
        }

        private void RefreshMaterialInLUTs(MaterialSlot mat)
        {
            if (activePalette == null || activePalette.colorLUT == null || activePalette.propertyLUT == null) return;

            // 1. Push Structural Workflow
            PushStructuralWorkflowToLUT(mat, false);

            // 2. Push Schema Properties
            SchemaSlot[] activeSchema = mat.workflow switch
            {
                B2UWorkflow.Transparent => activePalette.transparentSchema,
                B2UWorkflow.Liquid => activePalette.liquidSchema,
                B2UWorkflow.Crystal => activePalette.crystalSchema,
                _ => activePalette.opaqueSchema
            };

            if (activeSchema != null && mat.propertyBlocks != null)
            {
                foreach (var pb in mat.propertyBlocks)
                {
                    if (Enum.TryParse(pb.slotID, out B2USlotKey slotKey) && slotKey != B2USlotKey.r1_p4_r)
                    {
                        SchemaSlot sDef = Array.Find(activeSchema, s => s != null && s.slotID == pb.slotID);
                        if (sDef != null && sDef.isActive)
                        {
                            float norm = EvaluateNormalizedProperty(pb, sDef);
                            byte bVal = (byte)Mathf.Clamp(Mathf.RoundToInt(norm * 255f), 0, 255);
                            PushPropertyToLUT(mat, slotKey, bVal, false);
                        }
                    }
                }
            }

            // 3. Push Colors
            if (mat.biomeType == "INDEPENDENT" || mat.biomeType == "INDEP_DYN")
            {
                B2UMaterial indepMat = GetTempMat(mat, false);

                if (mat.workflow == B2UWorkflow.Liquid || mat.workflow == B2UWorkflow.Crystal)
                {
                    if (mat.variantColorBlocks.Length > 0)
                    {
                        var vBlock = mat.variantColorBlocks[0];
                        if (vBlock.bases != null && vBlock.bases.Length > 0)
                        {
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vBlock.bases[0], indepMat, false, -1, false, false);
                        }
                        B2URuntimeUtility.SetColorLUTPixel(activePalette, vBlock.emission, indepMat, true, -1, false, false);

                        if (mat.workflow == B2UWorkflow.Liquid)
                        {
                            if (vBlock.depths != null && vBlock.depths.Length > 0)
                            {
                                B2URuntimeUtility.SetColorLUTPixel(activePalette, vBlock.depths[0], indepMat, false, -1, true, false);
                            }
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vBlock.secEmission, indepMat, true, -1, true, false);
                        }
                    }
                }
                else
                {
                    for (int v = 0; v < 8 && v < mat.variantColorBlocks.Length; v++)
                    {
                        var vBlock = mat.variantColorBlocks[v];
                        if (vBlock.bases != null && vBlock.bases.Length > 0)
                        {
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vBlock.bases[0], indepMat, false, v, false, false);
                        }
                        B2URuntimeUtility.SetColorLUTPixel(activePalette, vBlock.emission, indepMat, true, v, false, false);
                    }
                }
            }

            if (mat.biomeType == "DYNAMIC" || mat.biomeType == "INDEP_DYN")
            {
                B2UMaterial dynMat = GetTempMat(mat, true);
                for (int b = 0; b < 8 && b < mat.biomeColorBlocks.Length; b++)
                {
                    var bData = mat.biomeColorBlocks[b];
                    for (int s = 0; s < activePalette.numSubbiomes; s++)
                    {
                        if (bData.bases != null && bData.bases.Length > s)
                        {
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.bases[s], dynMat, b, s, false, false, false);
                        }

                        if (mat.workflow == B2UWorkflow.Liquid && bData.depths != null && bData.depths.Length > s)
                        {
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.depths[s], dynMat, b, s, false, true, false);
                        }
                    }
                    B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.emission, dynMat, b, 0, true, false, false);
                    if (mat.workflow == B2UWorkflow.Liquid)
                    {
                        B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.secEmission, dynMat, b, 0, true, true, false);
                    }
                }
            }

            activePalette.colorLUT.Apply();
            activePalette.propertyLUT.Apply();
        }

        private static float EvaluateNormalizedProperty(B2UPropertyBlock pb, SchemaSlot sDef)
        {
            float range = Mathf.Approximately(sDef.maxVal, sDef.minVal) ? 1f : (sDef.maxVal - sDef.minVal);
            return sDef.type switch
            {
                B2USlotType.Float => Mathf.Clamp01((pb.fVal - sDef.minVal) / range),
                B2USlotType.Int => Mathf.Clamp01((pb.iVal - sDef.minVal) / range),
                B2USlotType.Bool => pb.bVal ? 1f : 0f,
                B2USlotType.Bitmask => Mathf.Clamp01(pb.iVal / 255f),
                B2USlotType.Enum => Mathf.Clamp01(pb.eVal / Mathf.Max(1f, (sDef.enumItems != null ? sDef.enumItems.Length : 1) - 1)),
                _ => 0f
            };
        }

        private void RefreshPalettes()
        {
            allPalettes.Clear();
            string[] guids = AssetDatabase.FindAssets("t:B2UPaletteData");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                B2UPaletteData pal = AssetDatabase.LoadAssetAtPath<B2UPaletteData>(path);
                if (pal != null) allPalettes.Add(pal);
            }

            if (activePalette == null && allPalettes.Count > 0)
            {
                B2UPaletteData profilePal = B2URuntimeUtility.ActiveProfile?.paletteData;
                if (profilePal != null && allPalettes.Contains(profilePal))
                {
                    activePalette = profilePal;
                    selectedPaletteIndex = allPalettes.IndexOf(profilePal);
                }
                else
                {
                    activePalette = allPalettes[0];
                    selectedPaletteIndex = 0;
                }

                if (activePalette != null &&
                    (activePalette.editorOnlyMaterials == null || activePalette.editorOnlyMaterials.All(m => m == null)) &&
                    !string.IsNullOrEmpty(activePalette.palettePath) &&
                    File.Exists(activePalette.palettePath))
                {
                    activePalette.LoadPaletteFromJSONEditorOnly(activePalette.palettePath);
                }
                SortMaterials();
            }

            UpdatePaletteNames();
        }

        private void UpdatePaletteNames()
        {
            paletteNames = new string[allPalettes.Count];
            for (int i = 0; i < allPalettes.Count; i++)
            {
                string pName = allPalettes[i].paletteName;
                if (string.IsNullOrEmpty(pName)) pName = allPalettes[i].name;

                if (activePalette != null)
                {
                    if (allPalettes[i] == activePalette)
                    {
                        pName = $"[{pName}] (Editing)";
                    }
                    else if (IsSisterPalette(activePalette, allPalettes[i]))
                    {
                        pName = $"[{pName}] (Sister Palette)";
                    }
                }
                paletteNames[i] = pName;
            }
        }

        private bool IsSisterPalette(B2UPaletteData basePal, B2UPaletteData targetPal)
        {
            if (basePal == null || targetPal == null || basePal.editorOnlyMaterials == null || targetPal.editorOnlyMaterials == null)
            {
                return false;
            }

            foreach (var baseMat in basePal.editorOnlyMaterials)
            {
                if (baseMat == null || string.IsNullOrEmpty(baseMat.materialName)) continue;
                var targetMat = targetPal.editorOnlyMaterials.FirstOrDefault(m => m != null && m.startRowIndex == baseMat.startRowIndex);
                if (targetMat == null || targetMat.workflow != baseMat.workflow) return false;
            }
            return true;
        }

        private void OnGUI()
        {
            if (allPalettes.Count == 0)
            {
                EditorGUILayout.Space(10f);
                EditorGUILayout.HelpBox("No B2UPaletteData assets found in the project.", MessageType.Warning);
                if (GUILayout.Button("Refresh Palettes", GUILayout.Height(28f)))
                {
                    RefreshPalettes();
                }
                return;
            }

            // Top Toolbar: Palette Selection & File Actions
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (activePalette != null && allPalettes.Contains(activePalette))
            {
                selectedPaletteIndex = allPalettes.IndexOf(activePalette);
            }

            EditorGUI.BeginChangeCheck();
            int newSelectedIndex = EditorGUILayout.Popup(selectedPaletteIndex, paletteNames, EditorStyles.toolbarPopup, GUILayout.MinWidth(150f));

            if (EditorGUI.EndChangeCheck() && newSelectedIndex != selectedPaletteIndex)
            {
                if (isDirty && activePalette != null)
                {
                    int option = EditorUtility.DisplayDialogComplex(
                        "Unsaved Palette Changes",
                        $"Save changes to '{activePalette.paletteName}' before switching palettes?",
                        "Save",
                        "Discard",
                        "Cancel"
                    );

                    if (option == 0)
                    {
                        CommitChanges();
                    }
                    else if (option == 2)
                    {
                        EditorGUILayout.EndHorizontal();
                        return;
                    }
                    else
                    {
                        activePalette.LoadPaletteFromJSONEditorOnly(activePalette.palettePath);
                        isDirty = false;
                    }
                }

                selectedPaletteIndex = newSelectedIndex;
                activePalette = allPalettes[selectedPaletteIndex];
                activeMaterialIndex = 0;
                isDirty = false;
                UpdatePaletteNames();

                if (activePalette != null && !string.IsNullOrEmpty(activePalette.palettePath) && File.Exists(activePalette.palettePath))
                {
                    activePalette.LoadPaletteFromJSONEditorOnly(activePalette.palettePath);
                    SortMaterials();
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(RefreshBtnContent, EditorStyles.toolbarButton, GUILayout.MinWidth(90f)))
            {
                if (!isDirty || EditorUtility.DisplayDialog("Unsaved Changes", "Refreshing palettes will discard unsaved changes. Continue?", "Yes", "No"))
                {
                    isDirty = false;
                    RefreshPalettes();
                }
            }

            if (GUILayout.Button(BuildHlslBtnContent, EditorStyles.toolbarButton, GUILayout.MinWidth(70f)))
            {
                if (activePalette != null && EditorUtility.DisplayDialog("Build HLSL Constants?", "This will update B2U_Constants.hlsl with the active palette's schema ranges. Continue?", "Build", "Cancel"))
                {
                    activePalette.BuildHLSLConstantsEditorOnly();
                }
            }

            if (GUILayout.Button(RecoverLutsBtnContent, EditorStyles.toolbarButton, GUILayout.MinWidth(110f)))
            {
                if (activePalette != null && EditorUtility.DisplayDialog("Recover from LUTs?", "Recovering from LUTs will overwrite existing material slots in the palette. Continue?", "Recover", "Cancel"))
                {
                    Undo.RecordObject(activePalette, "Recover from LUTs");
                    activePalette.LoadPaletteFromLUTsEditorOnly();
                    activeMaterialIndex = 0;
                    isDirty = true;
                    SortMaterials();
                }
            }

            Color defaultBg = GUI.backgroundColor;
            if (isDirty) GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);

            if (GUILayout.Button(new GUIContent(isDirty ? "Save Changes *" : "Save Changes", "Writes all palette changes to the LUT PNG files and the Blender JSON file."), EditorStyles.toolbarButton, GUILayout.MinWidth(100f)))
            {
                CommitChanges();
                Focus();
                GUIUtility.ExitGUI();
            }

            GUI.backgroundColor = defaultBg;
            EditorGUILayout.EndHorizontal();

            if (activePalette == null) return;

            // JSON Path Validation
            if (string.IsNullOrEmpty(activePalette.palettePath) || !File.Exists(activePalette.palettePath))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox($"The JSON data path is missing or invalid for '{activePalette.name}'.\nPlease link the JSON file exported from Blender to continue.", MessageType.Error);

                if (GUILayout.Button("Select JSON Palette File", GUILayout.Height(30f)))
                {
                    string path = EditorUtility.OpenFilePanel("Select B2U JSON Palette", Application.dataPath, "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (path.StartsWith(Application.dataPath))
                        {
                            path = "Assets" + path.Substring(Application.dataPath.Length);
                        }

                        activePalette.palettePath = path;
                        activePalette.folderPath = Path.GetDirectoryName(path);
                        EditorUtility.SetDirty(activePalette);
                        AssetDatabase.SaveAssets();

                        activePalette.LoadPaletteFromJSONEditorOnly(activePalette.palettePath);
                        SortMaterials();
                    }
                }
                return;
            }

            if (activePalette.editorOnlyMaterials == null) return;
            if (activePalette.materials == null || !activePalette.materials.IsInitialized)
            {
                activePalette.Initialize();
            }

            // Main Split View
            EditorGUILayout.BeginHorizontal();

            // Left Panel: Material Roster
            EditorGUILayout.BeginVertical(GUILayout.Width(leftPanelWidth));
            DrawMaterialList();
            EditorGUILayout.EndVertical();

            // Draggable Vertical Splitter
            Rect splitterRect = GUILayoutUtility.GetRect(6f, 6f, GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(new Rect(splitterRect.x + 2f, splitterRect.y, 1f, splitterRect.height), new Color(0f, 0f, 0f, 0.35f));

            int splitterControlId = GUIUtility.GetControlID(FocusType.Passive);
            Event evt = Event.current;
            switch (evt.GetTypeForControl(splitterControlId))
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(evt.mousePosition) && evt.button == 0)
                    {
                        isResizing = true;
                        GUIUtility.hotControl = splitterControlId;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isResizing && GUIUtility.hotControl == splitterControlId)
                    {
                        leftPanelWidth = Mathf.Clamp(evt.mousePosition.x, 180f, Mathf.Max(220f, position.width - 280f));
                        Repaint();
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (isResizing && GUIUtility.hotControl == splitterControlId)
                    {
                        isResizing = false;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;
            }

            // Right Panel: Active Material Inspector
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            propertiesScroll = EditorGUILayout.BeginScrollView(propertiesScroll);
            DrawActiveMaterial();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void SortMaterials()
        {
            if (activePalette == null || activePalette.editorOnlyMaterials == null) return;

            var activeMat = (activeMaterialIndex >= 0 && activeMaterialIndex < activePalette.editorOnlyMaterials.Length)
                ? activePalette.editorOnlyMaterials[activeMaterialIndex]
                : null;

            activePalette.editorOnlyMaterials = activePalette.editorOnlyMaterials
                .Where(m => m != null && !string.IsNullOrEmpty(m.materialName))
                .OrderBy(m => m.startRowIndex)
                .ToArray();

            if (activeMat != null)
            {
                activeMaterialIndex = Array.IndexOf(activePalette.editorOnlyMaterials, activeMat);
                if (activeMaterialIndex == -1) activeMaterialIndex = 0;
            }
        }

        private void DrawMaterialList()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Material Roster", EditorStyles.boldLabel);

            int usedRows = activePalette.editorOnlyMaterials.Where(m => m != null && !string.IsNullOrEmpty(m.materialName)).Sum(m => m.allocatedRows);
            EditorGUILayout.LabelField($"LUT Memory: {usedRows} / 256 Slots", EditorStyles.miniLabel);

            matListScroll = EditorGUILayout.BeginScrollView(matListScroll, EditorStyles.helpBox);
            for (int i = 0; i < activePalette.editorOnlyMaterials.Length; i++)
            {
                var mat = activePalette.editorOnlyMaterials[i];
                if (mat == null || string.IsNullOrEmpty(mat.materialName)) continue;

                GUI.backgroundColor = (i == activeMaterialIndex) ? new Color(0.3f, 0.55f, 0.85f) : Color.white;

                string rowLabel = mat.allocatedRows > 1
                    ? $"{mat.startRowIndex}-{mat.startRowIndex + mat.allocatedRows - 1}"
                    : mat.startRowIndex.ToString();

                string lockPrefix = mat.isLocked ? "[L] " : "";
                if (GUILayout.Button($"{lockPrefix}ID {rowLabel}: {mat.materialName} ({GetShortWorkflow(mat.workflow)})", EditorStyles.toolbarButton))
                {
                    activeMaterialIndex = i;
                    GUI.FocusControl(null);
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(usedRows >= 256))
            {
                if (GUILayout.Button(AddMatBtnContent, GUILayout.Height(25f))) AddMaterial();
            }
            using (new EditorGUI.DisabledScope(activePalette.editorOnlyMaterials.Length == 0))
            {
                if (GUILayout.Button(RemoveMatBtnContent, GUILayout.Height(25f))) RemoveMaterial();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        private string GetShortWorkflow(B2UWorkflow workflow)
        {
            switch (workflow)
            {
                case B2UWorkflow.Opaque:
                    return "O";
                case B2UWorkflow.OpaqueDoubleSided:
                    return "ODS";
                case B2UWorkflow.Transparent:
                    return "T";
                case B2UWorkflow.Crystal:
                    return "C";
                case B2UWorkflow.Liquid:
                    return "L";
                default:
                    return "Invalid";
            }
        }

        private void AddMaterial()
        {
            List<MaterialSlot> mats = activePalette.editorOnlyMaterials.Where(m => m != null && !string.IsNullOrEmpty(m.materialName)).ToList();

            HashSet<int> occupied = new HashSet<int>();
            foreach (var m in mats)
            {
                for (int r = 0; r < m.allocatedRows; r++) occupied.Add(m.startRowIndex + r);
            }

            int newIndex = 0;
            while (occupied.Contains(newIndex) && newIndex < 256) newIndex++;

            if (newIndex >= 256)
            {
                B2ULogError(msgSrc, "LUT is full (256/256 rows). Cannot add more materials.", this);
                return;
            }

            Undo.RecordObject(activePalette, "Add Palette Material");

            MaterialSlot newMat = new MaterialSlot
            {
                materialName = $"New Material {newIndex}",
                workflow = B2UWorkflow.Opaque,
                biomeType = "INDEPENDENT",
                isBiomeResponsive = false,
                startRowIndex = newIndex,
                allocatedRows = 1,
                isLocked = false
            };

            Color32 defaultCol = new Color32(204, 204, 204, 255);
            Color32 defaultEmis = new Color32(0, 0, 0, 255);

            for (int i = 0; i < 8; i++)
            {
                Color32[] bBases = new Color32[7];
                Color32[] bDepths = new Color32[7];
                Color32[] vBases = new Color32[7];
                Color32[] vDepths = new Color32[7];
                for (int s = 0; s < 7; s++)
                {
                    bBases[s] = defaultCol;
                    bDepths[s] = defaultCol;
                    vBases[s] = defaultCol;
                    vDepths[s] = defaultCol;
                }

                newMat.biomeColorBlocks[i] = new B2UColorBlock { bases = bBases, depths = bDepths, emission = defaultEmis, secEmission = defaultEmis };
                newMat.variantColorBlocks[i] = new B2UColorBlock { bases = vBases, depths = vDepths, emission = defaultEmis, secEmission = defaultEmis };
            }

            newMat.propertyBlocks = Array.Empty<B2UPropertyBlock>();
            EnsurePropertyBlocks(newMat);

            mats.Add(newMat);
            activePalette.editorOnlyMaterials = mats.ToArray();
            activeMaterialIndex = mats.Count - 1;

            RefreshMaterialInLUTs(newMat);
            isDirty = true;
            SortMaterials();
            SceneView.RepaintAll();
        }

        private void RemoveMaterial()
        {
            if (activePalette.editorOnlyMaterials == null || activePalette.editorOnlyMaterials.Length == 0) return;

            List<MaterialSlot> mats = activePalette.editorOnlyMaterials.Where(m => m != null && !string.IsNullOrEmpty(m.materialName)).ToList();
            if (activeMaterialIndex >= 0 && activeMaterialIndex < mats.Count)
            {
                Undo.RecordObject(activePalette, "Remove Palette Material");
                mats.RemoveAt(activeMaterialIndex);
                activePalette.editorOnlyMaterials = mats.ToArray();
                activeMaterialIndex = Mathf.Clamp(activeMaterialIndex - 1, 0, Mathf.Max(0, mats.Count - 1));
                isDirty = true;
            }
        }

        private void EnsurePropertyBlocks(MaterialSlot mat)
        {
            if (mat == null || activePalette == null) return;

            SchemaSlot[] activeSchema = mat.workflow switch
            {
                B2UWorkflow.Transparent => activePalette.transparentSchema,
                B2UWorkflow.Liquid => activePalette.liquidSchema,
                B2UWorkflow.Crystal => activePalette.crystalSchema,
                _ => activePalette.opaqueSchema
            };

            if (activeSchema == null) return;

            List<B2UPropertyBlock> blocks = mat.propertyBlocks != null ? mat.propertyBlocks.ToList() : new List<B2UPropertyBlock>();
            bool addedAny = false;

            foreach (var sDef in activeSchema)
            {
                if (sDef == null || !sDef.isActive) continue;

                if (!blocks.Any(b => b.slotID == sDef.slotID))
                {
                    blocks.Add(new B2UPropertyBlock
                    {
                        slotID = sDef.slotID,
                        schema = sDef.schema,
                        fVal = sDef.defaultVal,
                        iVal = (int)sDef.defaultVal,
                        bVal = sDef.defaultBVal,
                        eVal = (int)sDef.defaultVal
                    });
                    addedAny = true;
                }
            }

            if (addedAny || mat.propertyBlocks == null)
            {
                mat.propertyBlocks = blocks.ToArray();
            }
        }

        /// <summary>
        /// Draws a ColorField with the HDR flag enabled so Unity's IMGUI gamma-corrects the linear LUT color in the swatch preview.
        /// Clamps the returned RGB channels to 0-1 so values never exceed byte range.
        /// </summary>
        private Color DrawPaletteColorField(string label, string tooltip, Color color)
        {
            Color c = EditorGUILayout.ColorField(new GUIContent(label, tooltip), color, showEyedropper: true, showAlpha: false, hdr: true);
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }

        private void DrawActiveMaterial()
        {
            if (activePalette.editorOnlyMaterials == null || activePalette.editorOnlyMaterials.Length == 0)
            {
                EditorGUILayout.HelpBox("No materials in palette. Click '+ Add Material' to create one.", MessageType.Info);
                return;
            }
            if (activeMaterialIndex < 0 || activeMaterialIndex >= activePalette.editorOnlyMaterials.Length) return;

            MaterialSlot mat = activePalette.editorOnlyMaterials[activeMaterialIndex];
            if (mat == null) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string tempName = EditorGUILayout.TextField(MatNameLabel, mat.materialName);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(tempName))
            {
                Undo.RecordObject(activePalette, "Tweak Palette Material Name");
                mat.materialName = tempName;
                isDirty = true;
            }

            EditorGUI.BeginChangeCheck();
            bool tempLocked = EditorGUILayout.ToggleLeft(LockedLabel, mat.isLocked, GUILayout.Width(70f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(activePalette, "Tweak Palette Material Lock");
                mat.isLocked = tempLocked;
                isDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);

            if (mat.isLocked)
            {
                EditorGUILayout.HelpBox("This material is locked. Unlock it above to edit its workflow, properties, or colors.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            B2UWorkflow tempWf = (B2UWorkflow)EditorGUILayout.EnumPopup(WorkflowLabel, mat.workflow);

            int bIdx = Array.IndexOf(BiomeTypes, mat.biomeType);
            if (bIdx == -1) bIdx = 0;
            string tempType = BiomeTypes[EditorGUILayout.Popup(BiomeTypeLabel, bIdx, BiomeTypeLabels)];

            int tempRow = EditorGUILayout.IntField(RowIdLabel, mat.startRowIndex);

            if (EditorGUI.EndChangeCheck())
            {
                int newRowsNeeded = 1;
                if (tempType == "INDEP_DYN") newRowsNeeded = 2;
                if (tempWf == B2UWorkflow.Liquid) newRowsNeeded = (tempType == "INDEP_DYN") ? 4 : 2;

                bool isOccupied = false;
                if (tempRow < 0 || tempRow + newRowsNeeded > 256)
                {
                    isOccupied = true;
                }
                else
                {
                    foreach (var otherMat in activePalette.editorOnlyMaterials)
                    {
                        if (otherMat == mat || otherMat == null) continue;
                        if (tempRow < otherMat.startRowIndex + otherMat.allocatedRows && tempRow + newRowsNeeded > otherMat.startRowIndex)
                        {
                            isOccupied = true;
                            break;
                        }
                    }
                }

                if (isOccupied)
                {
                    EditorUtility.DisplayDialog(
                        "LUT Slot Unavailable",
                        $"The requested Row ID ({tempRow}..{tempRow + newRowsNeeded - 1}) overlaps with an existing material or exceeds the 256-row LUT limit.",
                        "OK"
                    );
                }
                else
                {
                    Undo.RecordObject(activePalette, "Tweak Palette Workflow");

                    mat.workflow = tempWf;
                    mat.biomeType = tempType;
                    mat.startRowIndex = tempRow;
                    mat.isBiomeResponsive = (mat.biomeType != "INDEPENDENT");
                    mat.allocatedRows = newRowsNeeded;
                    isDirty = true;

                    EnsurePropertyBlocks(mat);
                    SortMaterials();
                    RefreshMaterialInLUTs(mat);
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(12f);
            EnsurePropertyBlocks(mat);
            DrawSchemaProperties(mat);
            EditorGUILayout.Space(12f);
            DrawColorProperties(mat);
            EditorGUILayout.Space(8f);
        }

        private B2UPropertyBlock GetBlock(MaterialSlot mat, string id)
        {
            if (mat == null || mat.propertyBlocks == null) return default;
            for (int i = 0; i < mat.propertyBlocks.Length; i++)
            {
                if (mat.propertyBlocks[i].slotID == id) return mat.propertyBlocks[i];
            }
            return default;
        }

        private void SetBlock(MaterialSlot mat, string id, B2UPropertyBlock updatedBlock)
        {
            if (mat == null || mat.propertyBlocks == null) return;
            for (int i = 0; i < mat.propertyBlocks.Length; i++)
            {
                if (mat.propertyBlocks[i].slotID == id)
                {
                    mat.propertyBlocks[i] = updatedBlock;
                    break;
                }
            }
        }

        private void DrawSchemaProperties(MaterialSlot mat)
        {
            EditorGUILayout.LabelField("Material Data (Grouped by LUT Pixel)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            SchemaSlot[] activeSchema = mat.workflow switch
            {
                B2UWorkflow.Transparent => activePalette.transparentSchema,
                B2UWorkflow.Liquid => activePalette.liquidSchema,
                B2UWorkflow.Crystal => activePalette.crystalSchema,
                _ => activePalette.opaqueSchema
            };

            if (activeSchema == null)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            var detBlock = GetBlock(mat, "r1_p4_g");
            var eBlock = GetBlock(mat, "r1_p4_b");
            var iBlock = GetBlock(mat, "r1_p4_a");

            bool isDetailMapped = detBlock.eVal > 0;
            bool isEmissiveOrIridescence = eBlock.eVal > 0 || iBlock.eVal > 0;

            string lastPixelGroup = "";

            foreach (var sDef in activeSchema)
            {
                if (sDef == null || !sDef.isActive || sDef.slotID == "r1_p4_r") continue;

                string[] idVals = sDef.slotID.Split('_');
                bool skip = false;

                if (mat.workflow == B2UWorkflow.Opaque || mat.workflow == B2UWorkflow.OpaqueDoubleSided)
                {
                    if (idVals[1] == "p1" && !isEmissiveOrIridescence) skip = true;
                    if (idVals[1] == "p2" && !isDetailMapped) skip = true;
                    if (idVals[1] == "p3" && mat.workflow == B2UWorkflow.Opaque) skip = true;
                }
                else if (mat.workflow == B2UWorkflow.Transparent)
                {
                    if (idVals[1] == "p1" && !isEmissiveOrIridescence) skip = true;
                    if (idVals[1] == "p2" && !isDetailMapped) skip = true;
                }
                else if (mat.workflow == B2UWorkflow.Liquid)
                {
                    if (idVals[0] == "r1" && idVals[1] == "p3" && !isEmissiveOrIridescence) skip = true;
                }

                if (skip) continue;

                var block = GetBlock(mat, sDef.slotID);
                if (string.IsNullOrEmpty(block.slotID)) continue;

                string pixelGroup = idVals.Length >= 3 ? $"{idVals[0]}_{idVals[1]}" : $"{idVals[0]}_alpha";
                if (!string.IsNullOrEmpty(lastPixelGroup) && lastPixelGroup != pixelGroup)
                {
                    EditorGUILayout.Space(4f);
                }
                lastPixelGroup = pixelGroup;

                EditorGUI.BeginChangeCheck();

                GUIContent label = new GUIContent(sDef.uiName, $"{sDef.uiDescription}\n(LUT Slot: {sDef.slotID})");

                if (sDef.type == B2USlotType.Float)
                {
                    block.fVal = EditorGUILayout.Slider(label, block.fVal, sDef.minVal, sDef.maxVal);
                }
                else if (sDef.type == B2USlotType.Int)
                {
                    block.iVal = EditorGUILayout.IntSlider(label, block.iVal, (int)sDef.minVal, (int)sDef.maxVal);
                }
                else if (sDef.type == B2USlotType.Bool)
                {
                    block.bVal = EditorGUILayout.Toggle(label, block.bVal);
                }
                else if (sDef.type == B2USlotType.Enum)
                {
                    if (sDef.enumItems != null && sDef.enumItems.Length > 0)
                    {
                        block.eVal = EditorGUILayout.Popup(label, Mathf.Clamp(block.eVal, 0, sDef.enumItems.Length - 1), sDef.enumItems);
                    }
                }
                else if (sDef.type == B2USlotType.Bitmask)
                {
                    if (sDef.bitmaskFlags != null && sDef.bitmaskFlags.Length > 0)
                        block.iVal = EditorGUILayout.MaskField(label, block.iVal, sDef.bitmaskFlags) & 0xFF;
                    else
                        block.iVal = Mathf.Clamp(EditorGUILayout.IntField(label, block.iVal), 0, 255);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(activePalette, "Tweak Palette Property");
                    SetBlock(mat, sDef.slotID, block);
                    isDirty = true;

                    if (Enum.TryParse(sDef.slotID, out B2USlotKey slotKey))
                    {
                        float norm = EvaluateNormalizedProperty(block, sDef);
                        byte bVal = (byte)Mathf.Clamp(Mathf.RoundToInt(norm * 255f), 0, 255);
                        PushPropertyToLUT(mat, slotKey, bVal, true);
                        SceneView.RepaintAll();
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private string GetSubtypeLabel(int biomeIdx, int subtypeIdx)
        {
            int numSub = Mathf.Max(1, activePalette.numSubbiomes);
            int flatIdx = (biomeIdx * numSub) + subtypeIdx;
            if (activePalette.subtypeNames != null && flatIdx >= 0 && flatIdx < activePalette.subtypeNames.Length && !string.IsNullOrEmpty(activePalette.subtypeNames[flatIdx]))
            {
                return activePalette.subtypeNames[flatIdx];
            }
            return $"Subtype {subtypeIdx}";
        }

        private void DrawColorProperties(MaterialSlot mat)
        {
            EditorGUILayout.LabelField("Material Colors", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (mat.variantColorBlocks == null || mat.biomeColorBlocks == null)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            string[] bNames = (activePalette.biomeNames != null && activePalette.biomeNames.Length == 8)
                ? activePalette.biomeNames
                : new string[] { "Biome 0", "Biome 1", "Biome 2", "Biome 3", "Biome 4", "Biome 5", "Biome 6", "Biome 7" };

            if (mat.workflow == B2UWorkflow.Opaque || mat.workflow == B2UWorkflow.OpaqueDoubleSided || mat.workflow == B2UWorkflow.Transparent)
            {
                if (mat.biomeType == "INDEPENDENT" || mat.biomeType == "INDEP_DYN")
                {
                    EditorGUILayout.LabelField("Color Variants (1-8)", EditorStyles.miniBoldLabel);
                    previewVariantIndex = EditorGUILayout.IntSlider("Edit Variant", previewVariantIndex + 1, 1, 8) - 1;

                    if (previewVariantIndex < mat.variantColorBlocks.Length && mat.variantColorBlocks[previewVariantIndex].bases != null)
                    {
                        var vData = mat.variantColorBlocks[previewVariantIndex];
                        B2UMaterial indepMat = GetTempMat(mat, false);

                        EditorGUI.BeginChangeCheck();
                        Color tempBase = DrawPaletteColorField($"Variant {previewVariantIndex + 1} Base", "Base surface color for this variant.", vData.bases[0]);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            vData.bases[0] = tempBase;
                            mat.variantColorBlocks[previewVariantIndex] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vData.bases[0], indepMat, false, previewVariantIndex, false, true);
                            SceneView.RepaintAll();
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempEmis = DrawPaletteColorField($"Variant {previewVariantIndex + 1} Emission", "Emission color for this variant.", vData.emission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            vData.emission = tempEmis;
                            mat.variantColorBlocks[previewVariantIndex] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vData.emission, indepMat, true, previewVariantIndex, false, true);
                            SceneView.RepaintAll();
                        }
                    }
                }

                if (mat.biomeType == "DYNAMIC" || mat.biomeType == "INDEP_DYN")
                {
                    if (mat.biomeType == "INDEP_DYN") EditorGUILayout.Space(10f);
                    EditorGUILayout.LabelField("Dynamic Biome Colors", EditorStyles.miniBoldLabel);

                    previewBiomeIndex = Mathf.Clamp(EditorGUILayout.Popup("Edit Biome", previewBiomeIndex, bNames), 0, 7);

                    if (previewBiomeIndex < mat.biomeColorBlocks.Length)
                    {
                        var bData = mat.biomeColorBlocks[previewBiomeIndex];
                        B2UMaterial dynMat = GetTempMat(mat, true);

                        for (int i = 0; i < activePalette.numSubbiomes; i++)
                        {
                            string subName = GetSubtypeLabel(previewBiomeIndex, i);

                            EditorGUI.BeginChangeCheck();
                            Color tempBase = (bData.bases != null && bData.bases.Length > i) ? (Color)bData.bases[i] : Color.black;
                            tempBase = DrawPaletteColorField(subName, $"Base surface color for {subName}.", tempBase);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(activePalette, "Tweak Palette Color");
                                if (bData.bases != null && bData.bases.Length > i) bData.bases[i] = tempBase;
                                mat.biomeColorBlocks[previewBiomeIndex] = bData;
                                isDirty = true;
                                B2URuntimeUtility.SetColorLUTPixel(activePalette, tempBase, dynMat, previewBiomeIndex, i, false, false, true);
                                SceneView.RepaintAll();
                            }
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempEmis = DrawPaletteColorField("Biome Emission", "Emission color for this biome.", bData.emission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            bData.emission = tempEmis;
                            mat.biomeColorBlocks[previewBiomeIndex] = bData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.emission, dynMat, previewBiomeIndex, 0, true, false, true);
                            SceneView.RepaintAll();
                        }
                    }
                }
            }
            else if (mat.workflow == B2UWorkflow.Crystal)
            {
                if (mat.biomeType == "INDEPENDENT" || mat.biomeType == "INDEP_DYN")
                {
                    EditorGUILayout.LabelField("Global Crystal Colors", EditorStyles.miniBoldLabel);

                    if (mat.variantColorBlocks.Length > 0)
                    {
                        var vData = mat.variantColorBlocks[0];
                        B2UMaterial indepMat = GetTempMat(mat, false);

                        EditorGUI.BeginChangeCheck();
                        Color tempBase = (vData.bases != null && vData.bases.Length > 0) ? (Color)vData.bases[0] : Color.black;
                        tempBase = DrawPaletteColorField("Base Color", "Primary transmission/surface color of the crystal.", tempBase);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            if (vData.bases != null && vData.bases.Length > 0) vData.bases[0] = tempBase;
                            mat.variantColorBlocks[0] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, tempBase, indepMat, false, -1, false, true);
                            SceneView.RepaintAll();
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempEmis = DrawPaletteColorField("Emission Color", "Internal emission/secondary color of the crystal.", vData.emission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            vData.emission = tempEmis;
                            mat.variantColorBlocks[0] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vData.emission, indepMat, true, -1, false, true);
                            SceneView.RepaintAll();
                        }
                    }
                }

                if (mat.biomeType == "DYNAMIC" || mat.biomeType == "INDEP_DYN")
                {
                    if (mat.biomeType == "INDEP_DYN") EditorGUILayout.Space(10f);
                    EditorGUILayout.LabelField("Dynamic Biome Colors", EditorStyles.miniBoldLabel);

                    previewBiomeIndex = Mathf.Clamp(EditorGUILayout.Popup("Edit Biome", previewBiomeIndex, bNames), 0, 7);

                    if (previewBiomeIndex < mat.biomeColorBlocks.Length)
                    {
                        var bData = mat.biomeColorBlocks[previewBiomeIndex];
                        B2UMaterial dynMat = GetTempMat(mat, true);

                        for (int i = 0; i < activePalette.numSubbiomes; i++)
                        {
                            string subName = GetSubtypeLabel(previewBiomeIndex, i);

                            EditorGUI.BeginChangeCheck();
                            Color tempBase = (bData.bases != null && bData.bases.Length > i) ? (Color)bData.bases[i] : Color.black;
                            tempBase = DrawPaletteColorField(subName, $"Crystal base color for {subName}.", tempBase);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(activePalette, "Tweak Palette Color");
                                if (bData.bases != null && bData.bases.Length > i) bData.bases[i] = tempBase;
                                mat.biomeColorBlocks[previewBiomeIndex] = bData;
                                isDirty = true;
                                B2URuntimeUtility.SetColorLUTPixel(activePalette, tempBase, dynMat, previewBiomeIndex, i, false, false, true);
                                SceneView.RepaintAll();
                            }
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempEmis = DrawPaletteColorField("Biome Emission", "Crystal emission color for this biome.", bData.emission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            bData.emission = tempEmis;
                            mat.biomeColorBlocks[previewBiomeIndex] = bData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.emission, dynMat, previewBiomeIndex, 0, true, false, true);
                            SceneView.RepaintAll();
                        }
                    }
                }
            }
            else if (mat.workflow == B2UWorkflow.Liquid)
            {
                if (mat.biomeType == "INDEPENDENT" || mat.biomeType == "INDEP_DYN")
                {
                    EditorGUILayout.LabelField("Global Liquid Colors", EditorStyles.miniBoldLabel);

                    if (mat.variantColorBlocks.Length > 0)
                    {
                        var vData = mat.variantColorBlocks[0];
                        B2UMaterial indepMat = GetTempMat(mat, false);

                        EditorGUI.BeginChangeCheck();
                        Color tempBase = (vData.bases != null && vData.bases.Length > 0) ? (Color)vData.bases[0] : Color.black;
                        tempBase = DrawPaletteColorField("Surface Color", "Shallow/surface color of the liquid (Row 1).", tempBase);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            if (vData.bases != null && vData.bases.Length > 0) vData.bases[0] = tempBase;
                            mat.variantColorBlocks[0] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, tempBase, indepMat, false, -1, false, true);
                            SceneView.RepaintAll();
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempEmis = DrawPaletteColorField("Primary Emission", "Surface/primary emission color of the liquid (Row 1).", vData.emission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            vData.emission = tempEmis;
                            mat.variantColorBlocks[0] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vData.emission, indepMat, true, -1, false, true);
                            SceneView.RepaintAll();
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempDepth = (vData.depths != null && vData.depths.Length > 0) ? (Color)vData.depths[0] : Color.black;
                        tempDepth = DrawPaletteColorField("Depth Color", "Deep/absorption color of the liquid (Row 2).", tempDepth);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            if (vData.depths != null && vData.depths.Length > 0) vData.depths[0] = tempDepth;
                            mat.variantColorBlocks[0] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, tempDepth, indepMat, false, -1, true, true);
                            SceneView.RepaintAll();
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempSec = DrawPaletteColorField("Secondary Emission", "Deep/secondary emission color of the liquid (Row 2).", vData.secEmission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            vData.secEmission = tempSec;
                            mat.variantColorBlocks[0] = vData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, vData.secEmission, indepMat, true, -1, true, true);
                            SceneView.RepaintAll();
                        }
                    }
                }

                if (mat.biomeType == "DYNAMIC" || mat.biomeType == "INDEP_DYN")
                {
                    if (mat.biomeType == "INDEP_DYN") EditorGUILayout.Space(10f);
                    EditorGUILayout.LabelField("Dynamic Biome Colors", EditorStyles.miniBoldLabel);

                    previewBiomeIndex = Mathf.Clamp(EditorGUILayout.Popup("Edit Biome", previewBiomeIndex, bNames), 0, 7);

                    if (previewBiomeIndex < mat.biomeColorBlocks.Length)
                    {
                        var bData = mat.biomeColorBlocks[previewBiomeIndex];
                        B2UMaterial dynMat = GetTempMat(mat, true);

                        EditorGUILayout.LabelField("Row 1: Surface Dynamics", EditorStyles.miniBoldLabel);
                        for (int i = 0; i < activePalette.numSubbiomes; i++)
                        {
                            string subName = GetSubtypeLabel(previewBiomeIndex, i);

                            EditorGUI.BeginChangeCheck();
                            Color tempBase = (bData.bases != null && bData.bases.Length > i) ? (Color)bData.bases[i] : Color.black;
                            tempBase = DrawPaletteColorField(subName, $"Liquid surface color for {subName}.", tempBase);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(activePalette, "Tweak Palette Color");
                                if (bData.bases != null && bData.bases.Length > i) bData.bases[i] = tempBase;
                                mat.biomeColorBlocks[previewBiomeIndex] = bData;
                                isDirty = true;
                                B2URuntimeUtility.SetColorLUTPixel(activePalette, tempBase, dynMat, previewBiomeIndex, i, false, false, true);
                                SceneView.RepaintAll();
                            }
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempEmis = DrawPaletteColorField("Primary Emission", "Primary surface emission color for this biome.", bData.emission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            bData.emission = tempEmis;
                            mat.biomeColorBlocks[previewBiomeIndex] = bData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.emission, dynMat, previewBiomeIndex, 0, true, false, true);
                            SceneView.RepaintAll();
                        }

                        EditorGUILayout.Space(6f);
                        EditorGUILayout.LabelField("Row 2: Depth Dynamics", EditorStyles.miniBoldLabel);
                        for (int i = 0; i < activePalette.numSubbiomes; i++)
                        {
                            string subName = GetSubtypeLabel(previewBiomeIndex, i);

                            EditorGUI.BeginChangeCheck();
                            Color tempDepth = (bData.depths != null && bData.depths.Length > i) ? (Color)bData.depths[i] : Color.black;
                            tempDepth = DrawPaletteColorField(subName, $"Liquid depth color for {subName}.", tempDepth);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(activePalette, "Tweak Palette Color");
                                if (bData.depths != null && bData.depths.Length > i) bData.depths[i] = tempDepth;
                                mat.biomeColorBlocks[previewBiomeIndex] = bData;
                                isDirty = true;
                                B2URuntimeUtility.SetColorLUTPixel(activePalette, tempDepth, dynMat, previewBiomeIndex, i, false, true, true);
                                SceneView.RepaintAll();
                            }
                        }

                        EditorGUI.BeginChangeCheck();
                        Color tempSec = DrawPaletteColorField("Secondary Emission", "Secondary depth emission color for this biome.", bData.secEmission);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(activePalette, "Tweak Palette Color");
                            bData.secEmission = tempSec;
                            mat.biomeColorBlocks[previewBiomeIndex] = bData;
                            isDirty = true;
                            B2URuntimeUtility.SetColorLUTPixel(activePalette, bData.secEmission, dynMat, previewBiomeIndex, 0, true, true, true);
                            SceneView.RepaintAll();
                        }
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif