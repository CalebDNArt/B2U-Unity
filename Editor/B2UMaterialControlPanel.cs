#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Primary B2U Editor control window for binding the active B2UShaderProfile,
    /// editing global shader settings, and auto-assigning workflow materials and RSUV components across the scene.
    /// </summary>
    public class B2UMaterialControlPanel : EditorWindow
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Editor;

        private Vector2 scrollPosition;
        private Editor currentPresetEditor;

        private bool showAutoApply = true;
        private bool showMaterialProperties = true;
        private bool useUbershader = true;
        private bool ignoreTess = false;

        private readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Color32> s_TempVertexColors = new List<Color32>();
        private static readonly List<Vector2> s_TempUVs = new List<Vector2>();

        // Cached GUIContent with tooltips
        private static readonly GUIContent ActiveProfileLabel = new GUIContent(
            "Active Profile",
            "The B2UShaderProfile currently driving global shader properties and URP renderer features."
        );
        private static readonly GUIContent UseUbershaderLabel = new GUIContent(
            "Use Ubershader",
            "Assigns universal B2U master materials (Opaque_U, Liquid_U, etc.) to maximize SRP Batcher draw call batching across the scene."
        );
        private static readonly GUIContent IgnoreTessLabel = new GUIContent(
            "Ignore Tessellation",
            "Forces liquid meshes marked as tessellated in Blender to use standard non-tessellated liquid materials."
        );
        private static readonly GUIContent AutoApplyBtnContent = new GUIContent(
            "Auto Apply Materials to Scene",
            "Scans all B2U-prefixed GameObject hierarchies in the active scene, assigns the appropriate B2U materials and layers, and configures B2URSUVMono components."
        );
        private static readonly GUIContent ForcePushBtnContent = new GUIContent(
            "Force Push Globals",
            "Re-initializes and pushes all runtime and editor shader properties from the active profile to the GPU immediately."
        );
        private static readonly GUIContent ResetProfileBtnContent = new GUIContent(
            "Reset Profile to Defaults",
            "Resets all runtime and editor shader properties in the active profile back to their default factory values."
        );
        private static readonly GUIContent OpenTweakerBtnContent = new GUIContent(
            "Open Palette Tweaker",
            "Opens the B2U Palette Tweaker window to edit individual material properties and colors."
        );

        [MenuItem("B2U/Control Panel")]
        public static void ShowWindow()
        {
            B2UMaterialControlPanel window = GetWindow<B2UMaterialControlPanel>("B2U Control Panel");
            window.minSize = new Vector2(450f, 600f);
            window.Show();
        }

        private void OnEnable()
        {
            string savedPath = EditorPrefs.GetString("B2U_ActiveProfilePath", "");
            if (!string.IsNullOrEmpty(savedPath) && B2URuntimeUtility.ActiveProfile == null)
            {
                B2UShaderProfile loadedProfile = AssetDatabase.LoadAssetAtPath<B2UShaderProfile>(savedPath);
                if (loadedProfile != null)
                {
                    B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(loadedProfile);
                    B2UShaderProfileEditor.SyncRenderFeatureStates(loadedProfile);
                }
            }

            showAutoApply = EditorPrefs.GetBool("B2U_Fold_AutoApply", true);
            showMaterialProperties = EditorPrefs.GetBool("B2U_Fold_MatProps", true);
            useUbershader = EditorPrefs.GetBool("B2U_Shader_Mode", true);
            ignoreTess = EditorPrefs.GetBool("B2U_Ignore_Tess", false);
        }

        private void OnDisable()
        {
            if (B2URuntimeUtility.ActiveProfile != null)
            {
                EditorPrefs.SetString("B2U_ActiveProfilePath", AssetDatabase.GetAssetPath(B2URuntimeUtility.ActiveProfile));
            }

            EditorPrefs.SetBool("B2U_Fold_AutoApply", showAutoApply);
            EditorPrefs.SetBool("B2U_Fold_MatProps", showMaterialProperties);
            EditorPrefs.SetBool("B2U_Shader_Mode", useUbershader);
            EditorPrefs.SetBool("B2U_Ignore_Tess", ignoreTess);

            if (currentPresetEditor != null)
            {
                DestroyImmediate(currentPresetEditor);
                currentPresetEditor = null;
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUIUtility.labelWidth = Mathf.Max(220f, position.width * 0.45f);

            EditorGUILayout.Space(8f);

            // 1. Active Profile Header
            B2UShaderProfile currentProfile = B2URuntimeUtility.ActiveProfile;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            B2UShaderProfile newProfile = (B2UShaderProfile)EditorGUILayout.ObjectField(
                ActiveProfileLabel,
                currentProfile,
                typeof(B2UShaderProfile),
                false
            );
            if (GUILayout.Button(OpenTweakerBtnContent, GUILayout.Width(145f)))
            {
                B2UPaletteTweaker.ShowWindow();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            if (newProfile != currentProfile)
            {
                if (currentPresetEditor != null)
                {
                    DestroyImmediate(currentPresetEditor);
                    currentPresetEditor = null;
                }

                if (newProfile != null)
                {
                    B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(newProfile);
                    B2UShaderProfileEditor.SyncRenderFeatureStates(newProfile);
                    EditorPrefs.SetString("B2U_ActiveProfilePath", AssetDatabase.GetAssetPath(newProfile));
                }
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(6f);

            if (B2URuntimeUtility.ActiveProfile == null)
            {
                EditorGUILayout.HelpBox("Please assign an active B2UShaderProfile above to configure scene materials and shader properties.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            // 2. Scene Material Auto-Applier
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showAutoApply = EditorGUILayout.Foldout(showAutoApply, "Scene Material Auto-Applier", true, EditorStyles.foldoutHeader);
            if (showAutoApply)
            {
                EditorGUILayout.Space(4f);
                EditorGUI.indentLevel++;
                useUbershader = EditorGUILayout.ToggleLeft(UseUbershaderLabel, useUbershader);
                ignoreTess = EditorGUILayout.ToggleLeft(IgnoreTessLabel, ignoreTess);
                EditorGUILayout.Space(4f);

                if (GUILayout.Button(AutoApplyBtnContent, GUILayout.Height(32f)))
                {
                    AutoApplyB2UMaterials(useUbershader);
                    SceneView.RepaintAll();
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4f);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // 3. Embedded Profile Inspector
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showMaterialProperties = EditorGUILayout.Foldout(showMaterialProperties, "Active Profile Properties", true, EditorStyles.foldoutHeader);
            if (showMaterialProperties)
            {
                if (currentPresetEditor == null || currentPresetEditor.target != B2URuntimeUtility.ActiveProfile)
                {
                    if (currentPresetEditor != null) DestroyImmediate(currentPresetEditor);
                    currentPresetEditor = Editor.CreateEditor(B2URuntimeUtility.ActiveProfile);
                }

                currentPresetEditor.OnInspectorGUI();

                EditorGUILayout.Space(6f);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(ForcePushBtnContent, GUILayout.Height(30f)))
                {
                    B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(B2URuntimeUtility.ActiveProfile);
                    B2UShaderProfileEditor.SyncRenderFeatureStates(B2URuntimeUtility.ActiveProfile);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button(ResetProfileBtnContent, GUILayout.Height(30f), GUILayout.Width(170f)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Reset Profile Values?",
                        $"Are you sure you want to reset all values in '{B2URuntimeUtility.ActiveProfile.name}' to their defaults?",
                        "Reset",
                        "Cancel"))
                    {
                        Undo.RecordObject(B2URuntimeUtility.ActiveProfile, "Reset B2U Shader Profile");
                        B2URuntimeUtility.ActiveProfile.RestoreDefaults();
                        B2URuntimeUtility.PushAllGlobals();
                        B2UShaderProfileEditor.SyncRenderFeatureStates(B2URuntimeUtility.ActiveProfile);
                        SceneView.RepaintAll();
                        B2ULog(msgSrc, "Profile successfully reset to defaults.", B2UMessageType.None, this);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4f);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10f);
            EditorGUILayout.EndScrollView();
        }

        #region Auto Apply Functions
        public enum Workflow
        {
            Opaque,
            Opaque_DS,
            Transparent,
            Crystal,
            Liquid
        }

        private struct MaterialDesignation
        {
            public Workflow wf;
            public bool isBiomeResponsive;
            public bool isDetailMapped;
            public bool isEmissive;
            public bool isIridescent;
            public bool tessellated;
            public bool volumetricRefraction;
        }

        private MaterialDesignation SolveB2UMaterialDesignation(List<byte> materialIDs, Color32[] propertyLUTPixels, string objName, uint packedLiquidData = 0)
        {
            MaterialDesignation md = new MaterialDesignation
            {
                tessellated = false,
                volumetricRefraction = false
            };

            for (int i = 0; i < materialIDs.Count; i++)
            {
                int pixelIndex = (materialIDs[i] * 4) + 3;
                if (pixelIndex >= propertyLUTPixels.Length) continue;

                Color32 p4 = propertyLUTPixels[pixelIndex];

                float p4_r = p4.r / 255.0f;
                float p4_g = p4.g / 255.0f;
                float p4_b = p4.b / 255.0f;
                float p4_a = p4.a / 255.0f;

                if (p4_r >= 0.45f) md.isBiomeResponsive = true;
                if (p4_g > 0.01f) md.isDetailMapped = true;
                if (p4_b > 0.01f) md.isEmissive = true;
                if (p4_a > 0.01f) md.isIridescent = true;

                float strippedR = md.isBiomeResponsive ? Mathf.Max(0f, p4_r - 0.5f) : p4_r;
                int workflowIndex = Mathf.RoundToInt(strippedR * 10.0f);

                Workflow tempWf = workflowIndex switch
                {
                    0 => Workflow.Opaque,
                    1 => Workflow.Opaque_DS,
                    2 => Workflow.Transparent,
                    3 => Workflow.Crystal,
                    4 => Workflow.Liquid,
                    _ => md.wf
                };

                if (i != 0 && tempWf != md.wf)
                {
                    B2ULogError(msgSrc, $"Mesh '{objName}' has multiple workflows painted across its faces. Defaulting to Opaque.", this);
                    md.wf = Workflow.Opaque;
                }
                else
                {
                    md.wf = tempWf;
                }

                if (md.wf == Workflow.Liquid)
                {
                    md.tessellated = ((packedLiquidData >> 8) & 1u) != 0;
                    md.volumetricRefraction = ((packedLiquidData >> 9) & 1u) != 0;
                    md.isDetailMapped = ((packedLiquidData >> 11) & 1u) != 0;
                }
                else if (md.wf == Workflow.Crystal)
                {
                    int crystalPixelIndex = materialIDs[i] * 4;
                    if (crystalPixelIndex < propertyLUTPixels.Length)
                    {
                        Color32 p1 = propertyLUTPixels[crystalPixelIndex];
                        md.isDetailMapped = p1.a > 0;
                    }
                    md.volumetricRefraction = true;
                }
            }

            return md;
        }

        private Material GetB2UMaterial(List<byte> materialIDs, Color32[] propertyLUTPixels, string objName, bool ubershader, out bool volumetricRef, out bool isBiomeResponsive, out Workflow resolvedWorkflow, uint packedLiquidData = 0)
        {
            MaterialDesignation md = SolveB2UMaterialDesignation(materialIDs, propertyLUTPixels, objName, packedLiquidData);
            string materialName = "";
            volumetricRef = md.volumetricRefraction;
            isBiomeResponsive = md.isBiomeResponsive;
            resolvedWorkflow = md.wf;

            if (ubershader)
            {
                switch (md.wf)
                {
                    case Workflow.Opaque: materialName = "Opaque_U"; break;
                    case Workflow.Opaque_DS: materialName = "Opaque_DS_U"; break;
                    case Workflow.Transparent: materialName = "Transparent_U"; break;
                    case Workflow.Crystal: materialName = "Crystal_U"; break;
                    case Workflow.Liquid:
                        if (md.tessellated && !ignoreTess)
                        {
                            if (md.volumetricRefraction)
                            {
                                B2ULogWarning(msgSrc, $"Object '{objName}' is marked as both tessellated and volumetric. These are mutually exclusive; applying tessellated material.", this);
                                volumetricRef = false;
                            }
                            materialName = "Tess_Liquid_U";
                        }
                        else
                        {
                            materialName = md.volumetricRefraction ? "Liquid_V_U" : "Liquid_U";
                        }
                        break;
                }
            }
            else
            {
                if ((int)md.wf < 3)
                {
                    if (md.isBiomeResponsive) materialName += "B";
                    if (md.isDetailMapped) materialName += "D";
                    if (md.isEmissive) materialName += "E";
                    if (md.isIridescent) materialName += "I";
                }
                else if (md.wf == Workflow.Crystal)
                {
                    volumetricRef = true;
                    if (md.isBiomeResponsive) materialName += "B";
                    if (md.isDetailMapped) materialName += "T";
                    if (md.isEmissive) materialName += "E";
                }
                else if (md.wf == Workflow.Liquid)
                {
                    if (md.tessellated && md.volumetricRefraction && !ignoreTess)
                    {
                        B2ULogWarning(msgSrc, $"Object '{objName}' is marked as both tessellated and volumetric. These are mutually exclusive; applying tessellated material.", this);
                        volumetricRef = false;
                        materialName += "Tess_";
                    }
                    else
                    {
                        volumetricRef = md.volumetricRefraction;
                        if (md.volumetricRefraction) materialName += "V";
                        if (md.tessellated && !ignoreTess) materialName += "Tess_";
                    }
                    if (md.isBiomeResponsive) materialName += "B";
                    if (md.isDetailMapped) materialName += "T";
                    if (md.isEmissive) materialName += "E";
                    if (md.isIridescent) materialName += "I";
                }

                materialName += "_" + md.wf;
            }

            if (materialCache.TryGetValue(materialName, out Material cachedMat) && cachedMat != null)
            {
                return cachedMat;
            }

            string[] guids = AssetDatabase.FindAssets($"{materialName} t:Material");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.Equals(materialName, StringComparison.OrdinalIgnoreCase) && IsValidB2UMaterial(path, fileName))
                {
                    Material loadedMat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (loadedMat != null)
                    {
                        materialCache[materialName] = loadedMat;
                        return loadedMat;
                    }
                }
            }

            B2ULogError(msgSrc, $"Cannot locate material '{materialName}' for '{objName}'. Ensure materials exist in your B2U folder.", this);
            return null;
        }

        private void AutoApplyB2UMaterials(bool ubershader)
        {
            if (B2URuntimeUtility.ActiveProfile == null || B2URuntimeUtility.ActiveProfile.propertyBlocks == null)
            {
                return;
            }

            Texture2D propertyLUT = B2URuntimeUtility.ActiveProfile.propertyBlocks[B2UShaderProperty.PropertyLUT].value as Texture2D;
            if (propertyLUT == null)
            {
                EditorUtility.DisplayDialog("B2U Error", "No Property LUT is assigned in the active profile. Cannot determine material workflows.", "OK");
                return;
            }

            materialCache.Clear();
            Color32[] propertyLUTPixels = propertyLUT.GetPixels32();
            bool hasB2ULiquidTag = Array.IndexOf(InternalEditorUtility.tags, "B2ULiquid") >= 0;

            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = activeScene.GetRootGameObjects();

            int appliedCount = 0;
            foreach (GameObject root in rootObjects)
            {
                appliedCount += ProcessGameObjectHierarchy(root, propertyLUTPixels, ubershader, false, hasB2ULiquidTag);
            }

            B2ULog(msgSrc, $"Successfully applied B2U materials to {appliedCount} renderers.", B2UMessageType.None, this);
        }

        private int ProcessGameObjectHierarchy(GameObject obj, Color32[] propertyLUTPixels, bool ubershader, bool parentIsB2U, bool hasB2ULiquidTag)
        {
            int count = 0;
            bool isB2U = parentIsB2U || obj.name.StartsWith("B2U", StringComparison.OrdinalIgnoreCase);

            if (isB2U)
            {
                int materialID = int.MaxValue;
                bool hasMesh = false;
                bool appliedSuccessfully = false;
                Workflow resolvedWf = Workflow.Opaque;

                if (obj.TryGetComponent<MeshFilter>(out var mf) && obj.TryGetComponent<MeshRenderer>(out var mr))
                {
                    if (mf.sharedMesh != null && mf.sharedMesh.vertexCount > 3)
                    {
                        hasMesh = true;
                        if (ApplyToRenderer(mf.sharedMesh, mr, propertyLUTPixels, ubershader, out materialID, out resolvedWf))
                        {
                            appliedSuccessfully = true;
                            count++;
                        }
                    }
                }
                else if (obj.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                {
                    if (smr.sharedMesh != null && smr.sharedMesh.vertexCount > 3)
                    {
                        hasMesh = true;
                        if (ApplyToRenderer(smr.sharedMesh, smr, propertyLUTPixels, ubershader, out materialID, out resolvedWf))
                        {
                            appliedSuccessfully = true;
                            count++;
                        }
                    }
                }

                if (hasMesh && appliedSuccessfully)
                {
                    if (resolvedWf == Workflow.Liquid && hasB2ULiquidTag && !obj.CompareTag("B2ULiquid"))
                    {
                        Undo.RecordObject(obj, "B2U Set Liquid Tag");
                        obj.tag = "B2ULiquid";
                    }

                    if (!obj.TryGetComponent<B2URSUVMono>(out B2URSUVMono mono))
                    {
                        mono = Undo.AddComponent<B2URSUVMono>(obj);
                    }
                    else
                    {
                        Undo.RecordObject(mono, "B2U Update RSUVMono");
                    }

                    mono.materialID = materialID;
                    mono.AutoDetectType();
                    mono.ApplyRSUV();
                    EditorUtility.SetDirty(mono);
                }
                else if (!hasMesh && obj.TryGetComponent<B2URSUVMono>(out B2URSUVMono existing))
                {
                    Undo.DestroyObjectImmediate(existing);
                }
            }

            foreach (Transform child in obj.transform)
            {
                count += ProcessGameObjectHierarchy(child.gameObject, propertyLUTPixels, ubershader, isB2U, hasB2ULiquidTag);
            }

            return count;
        }

        private bool ApplyToRenderer(Mesh mesh, Renderer renderer, Color32[] propertyLUTPixels, bool ubershader, out int materialID, out Workflow resolvedWorkflow)
        {
            materialID = int.MaxValue;
            resolvedWorkflow = Workflow.Opaque;

            s_TempVertexColors.Clear();
            mesh.GetColors(s_TempVertexColors);

            s_TempUVs.Clear();
            mesh.GetUVs(1, s_TempUVs);
            if (s_TempUVs.Count == 0)
            {
                mesh.GetUVs(0, s_TempUVs);
            }

            if (s_TempVertexColors.Count == 0 && s_TempUVs.Count == 0)
            {
                return false;
            }

            List<byte> materialIDs = new List<byte>(8);
            uint packedLiquidData = 0;

            if (s_TempVertexColors.Count > 0)
            {
                materialID = s_TempVertexColors[0].a;
                for (int i = 0; i < s_TempVertexColors.Count; i++)
                {
                    byte a = s_TempVertexColors[i].a;
                    if (!materialIDs.Contains(a))
                    {
                        materialIDs.Add(a);
                    }
                }
            }
            else if (s_TempUVs.Count > 0)
            {
                packedLiquidData = (uint)Mathf.RoundToInt(s_TempUVs[0].y);

                for (int i = 0; i < s_TempUVs.Count; i++)
                {
                    uint pd = (uint)Mathf.RoundToInt(s_TempUVs[i].y);
                    byte matID = (byte)(pd & 0xFFu);

                    if (!materialIDs.Contains(matID))
                    {
                        materialIDs.Add(matID);
                    }
                    materialID = matID;
                }
            }

            Material targetMat = GetB2UMaterial(
                materialIDs,
                propertyLUTPixels,
                renderer.gameObject.name,
                ubershader,
                out bool volumetricRefraction,
                out bool isBiomeResponsive,
                out resolvedWorkflow,
                packedLiquidData
            );

            if (isBiomeResponsive && materialID != int.MaxValue)
            {
                materialID = -Mathf.Abs(materialID);
            }

            int volumetricRefractionLayer = (int)B2URuntimeUtility.ActiveProfile.propertyBlocks[B2UShaderProperty.VolumetricLayer].value;

            if (targetMat != null)
            {
                if (renderer.sharedMaterial != targetMat)
                {
                    Undo.RecordObject(renderer, "B2U Auto Apply Material");
                    renderer.sharedMaterial = targetMat;
                    EditorUtility.SetDirty(renderer);
                }

                GameObject obj = renderer.gameObject;
                if (volumetricRefraction)
                {
                    if (volumetricRefractionLayer >= 0 && volumetricRefractionLayer <= 31)
                    {
                        if (obj.layer != volumetricRefractionLayer)
                        {
                            Undo.RecordObject(obj, "B2U Auto Apply Layer");
                            obj.layer = volumetricRefractionLayer;
                            EditorUtility.SetDirty(obj);
                        }
                    }
                    else
                    {
                        B2ULogWarning(msgSrc, "The Volumetric Refraction Layer has not been set in the B2U settings. Please set it in the control panel and run the auto-applier again.", this);
                    }
                }
                else if (obj.layer == volumetricRefractionLayer && volumetricRefractionLayer > 0)
                {
                    Undo.RecordObject(obj, "B2U Reset Layer");
                    obj.layer = 0;
                    EditorUtility.SetDirty(obj);
                }

                return true;
            }

            return false;
        }

        private bool IsValidB2UMaterial(string path, string fileName)
        {
            bool isB2URoot = path.StartsWith("Packages/ca.dnart.b2umaterialmaster/", StringComparison.OrdinalIgnoreCase) ||
                             path.Contains("/B2U/", StringComparison.OrdinalIgnoreCase);

            if (!isB2URoot)
            {
                return false;
            }

            if (path.Contains("/Materials/Ubershaders/"))
            {
                return fileName == "Opaque_DS_U" ||
                       fileName == "Liquid_U" ||
                       fileName == "Liquid_V_U" ||
                       fileName == "Tess_Liquid_U" ||
                       fileName == "Opaque_U" ||
                       fileName == "Crystal_U" ||
                       fileName == "Transparent_U";
            }

            if (path.Contains("/Materials/Keyworded/"))
            {
                if (path.Contains("/Liquid/Tessellated/") && fileName.EndsWith("_Tess_Liquid")) return true;
                if (path.Contains("/Liquid/Volumetric/") && fileName.EndsWith("_Liquid")) return true;
                if (path.Contains("/Liquid/") && fileName.EndsWith("_Liquid")) return true;
                if (path.Contains("/Opaque/Double Sided/") && fileName.EndsWith("_Opaque_DS")) return true;
                if (path.Contains("/Opaque/Front Face/") && fileName.EndsWith("_Opaque")) return true;
                if (path.Contains("/Crystal/") && fileName.EndsWith("_Crystal")) return true;
                if (path.Contains("/Transparent/") && fileName.EndsWith("_Transparent")) return true;
            }

            return false;
        }
        #endregion
    }
}
#endif