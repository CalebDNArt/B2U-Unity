#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Custom Inspector for configuring B2UPaletteData assets, synchronizing JSON schemas/materials with Blender,
    /// validating LUT texture references, and generating B2U_Constants.hlsl.
    /// </summary>
    [CustomEditor(typeof(B2UPaletteData))]
    public class B2UPaletteDataEditor : Editor
    {
        private SerializedProperty paletteNameProp;
        private SerializedProperty palettePathProp;
        private SerializedProperty folderPathProp;

        private SerializedProperty colorLUTProp;
        private SerializedProperty propertyLUTProp;
        private SerializedProperty iridescenceLUTProp;
        private SerializedProperty emissionPatternsProp;
        private SerializedProperty detailAtlasProp;
        private SerializedProperty noise3DProp;
        private SerializedProperty paintPaletteProp;

        private SerializedProperty numSubbiomesProp;
        private SerializedProperty biomeNamesProp;
        private SerializedProperty subtypeNamesProp;

        private SerializedProperty detailImageNameProp;
        private SerializedProperty emissionPatternNameProp;
        private SerializedProperty iridescenceLUTNameProp;

        private bool showBiomeNames = true;

        private static readonly FieldInfo ValidTextureNamesField = typeof(B2UPaletteData).GetField(
            "validTextureNames",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        #region GUIContent Labels
        private static readonly GUIContent PaletteNameLabel = new GUIContent(
            "Palette Name",
            "The identifier of this palette, automatically derived from the JSON file name."
        );
        private static readonly GUIContent JsonPathLabel = new GUIContent(
            "JSON Path",
            "File path to the .json palette file authored in Blender."
        );
        private static readonly GUIContent LutFolderLabel = new GUIContent(
            "LUT Output Folder",
            "Project folder where the exported Color and Property LUT textures reside."
        );
        private static readonly GUIContent BrowseBtnContent = new GUIContent(
            "Browse",
            "Select a file or folder path from disk."
        );

        private static readonly GUIContent LoadJsonBtnContent = new GUIContent(
            "Load From JSON",
            "Parses schema definitions, materials, and biome names from the JSON file and locates matching LUT textures in the project."
        );
        private static readonly GUIContent WriteJsonBtnContent = new GUIContent(
            "Write To JSON",
            "Serializes any material, property, or color edits made in Unity back to the JSON file so Blender stays synchronized."
        );
        private static readonly GUIContent BuildHlslBtnContent = new GUIContent(
            "Build HLSL Constants",
            "Generates or updates B2U_Constants.hlsl with the active schema Min/Max ranges so B2U shaders compile with exact bounds."
        );
        private static readonly GUIContent RecoverLutsBtnContent = new GUIContent(
            "Recover Data From LUTs",
            "Reverse-engineers the assigned Color and Property LUT textures to reconstruct material slots if the JSON file was lost."
        );

        private static readonly GUIContent ColorLutLabel = new GUIContent(
            "Color & Emission LUT",
            "The 2D Lookup Texture storing base/depth RGB colors, emission RGB colors, metallic (base alpha), and smoothness (emission alpha)."
        );
        private static readonly GUIContent PropertyLutLabel = new GUIContent(
            "Property LUT",
            "The 4x256 Lookup Texture storing normalized material parameters, bitmasks, enums, and workflow IDs."
        );
        private static readonly GUIContent IridescenceLutLabel = new GUIContent(
            "Iridescence LUT",
            "The 1D/2D Lookup Texture sampled by view/light angle for thin-film and structural iridescence."
        );
        private static readonly GUIContent EmissionPatternsLabel = new GUIContent(
            "Emission Patterns",
            "Channel-packed RGBA texture containing 4 distinct grayscale emission patterns."
        );
        private static readonly GUIContent DetailAtlasLabel = new GUIContent(
            "Detail Map Atlas",
            "Texture2DArray containing surface detail normal (RG), curvature (B), and height (A) slices."
        );
        private static readonly GUIContent Noise3DLabel = new GUIContent(
            "3D Noise Texture",
            "Seamless 3D noise volume (RGB = Simplex offsets, A = Voronoi) used for world-space solid and liquid breakup."
        );
        private static readonly GUIContent PaintPaletteLabel = new GUIContent(
            "Paint Palette",
            "Texture2DArray (16 slices of 16x8 pixels) used for per-object RSUV paint tinting on solids."
        );

        private static readonly GUIContent FindTexturesBtnContent = new GUIContent(
            "Auto-Find Palette Textures",
            "Searches the AssetDatabase for textures matching this palette's naming conventions and assigns them."
        );
        private static readonly GUIContent WriteLutsBtnContent = new GUIContent(
            "Save Live LUT Changes to Disk",
            "Encodes the current in-memory Color and Property LUT textures to PNG files on disk and re-imports them."
        );

        private static readonly GUIContent NumSubtypesLabel = new GUIContent(
            "Number of Subtypes",
            "The number of subtype columns per biome (1 to 7), defined by the Blender palette."
        );
        private static readonly GUIContent DetailNameLabel = new GUIContent(
            "Detail Atlas Source Name",
            "The filename of the detail atlas image configured in the Blender palette."
        );
        private static readonly GUIContent EmissionNameLabel = new GUIContent(
            "Emission Pattern Source Name",
            "The filename of the emission pattern image configured in the Blender palette."
        );
        private static readonly GUIContent IridescenceNameLabel = new GUIContent(
            "Iridescence LUT Source Name",
            "The filename of the iridescence LUT image configured in the Blender palette."
        );

        private static readonly GUIContent[] BiomeLabels =
        {
            new GUIContent("Biome 0"), new GUIContent("Biome 1"), new GUIContent("Biome 2"), new GUIContent("Biome 3"),
            new GUIContent("Biome 4"), new GUIContent("Biome 5"), new GUIContent("Biome 6"), new GUIContent("Biome 7")
        };

        private static readonly GUIContent[] SubtypeLabels =
        {
            new GUIContent("Subtype 0"), new GUIContent("Subtype 1"), new GUIContent("Subtype 2"), new GUIContent("Subtype 3"),
            new GUIContent("Subtype 4"), new GUIContent("Subtype 5"), new GUIContent("Subtype 6")
        };
        #endregion

        private void OnEnable()
        {
            paletteNameProp = serializedObject.FindProperty("paletteName");
            palettePathProp = serializedObject.FindProperty("palettePath");
            folderPathProp = serializedObject.FindProperty("folderPath");

            colorLUTProp = serializedObject.FindProperty("colorLUT");
            propertyLUTProp = serializedObject.FindProperty("propertyLUT");
            iridescenceLUTProp = serializedObject.FindProperty("iridescenceLUT");
            emissionPatternsProp = serializedObject.FindProperty("emissionPatterns");
            detailAtlasProp = serializedObject.FindProperty("detailAtlas");
            noise3DProp = serializedObject.FindProperty("noise3D");
            paintPaletteProp = serializedObject.FindProperty("paintPalette");

            numSubbiomesProp = serializedObject.FindProperty("numSubbiomes");
            biomeNamesProp = serializedObject.FindProperty("biomeNames");
            subtypeNamesProp = serializedObject.FindProperty("subtypeNames");

            detailImageNameProp = serializedObject.FindProperty("detailImageName");
            emissionPatternNameProp = serializedObject.FindProperty("emissionPatternName");
            iridescenceLUTNameProp = serializedObject.FindProperty("iridescenceLUTName");
        }

        public override void OnInspectorGUI()
        {
            B2UPaletteData palette = (B2UPaletteData)target;
            serializedObject.Update();

            // 1. Path Configuration
            EditorGUILayout.LabelField("Configuration Paths", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(paletteNameProp, PaletteNameLabel);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(palettePathProp, JsonPathLabel);
            if (GUILayout.Button(BrowseBtnContent, GUILayout.Width(70f)))
            {
                string path = EditorUtility.OpenFilePanel("Select Palette JSON", Application.dataPath, "json");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    palettePathProp.stringValue = path;
                    serializedObject.ApplyModifiedProperties();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(folderPathProp, LutFolderLabel);
            if (GUILayout.Button(BrowseBtnContent, GUILayout.Width(70f)))
            {
                string path = EditorUtility.OpenFolderPanel("Select LUT Folder", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    folderPathProp.stringValue = path;
                    serializedObject.ApplyModifiedProperties();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);

            // 2. JSON & Shader Synchronization Controls
            EditorGUILayout.LabelField("JSON & Shader Synchronization", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(LoadJsonBtnContent, GUILayout.Height(25f)))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(palette, "Load Palette From JSON");
                palette.LoadPaletteFromJSONEditorOnly(palette.palettePath);
                palette.FindTexturesEditorOnly();
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssets();
                serializedObject.Update();
            }
            if (GUILayout.Button(WriteJsonBtnContent, GUILayout.Height(25f)))
            {
                serializedObject.ApplyModifiedProperties();
                palette.WritePaletteToJSONEditorOnly(palette.palettePath);
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssets();
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(BuildHlslBtnContent, GUILayout.Height(24f)))
            {
                if (EditorUtility.DisplayDialog(
                    "Build HLSL Constants file?", 
                    "This is only needed if your palette does not use the default B2U Schema. Running it will require all B2U Shaders to recompile.", 
                    "Build", 
                    "Cancel"))
                {
                    serializedObject.ApplyModifiedProperties();
                    palette.BuildHLSLConstantsEditorOnly();
                }
            }
            if (GUILayout.Button(RecoverLutsBtnContent, GUILayout.Height(24f)))
            {
                if (EditorUtility.DisplayDialog(
                    "Recover Palette From LUTs",
                    "This will overwrite the in-memory material slots by reverse-engineering the Color and Property LUT textures. Continue?",
                    "Recover",
                    "Cancel"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(palette, "Recover Palette From LUTs");
                    palette.LoadPaletteFromLUTsEditorOnly();
                    EditorUtility.SetDirty(palette);
                    AssetDatabase.SaveAssets();
                    serializedObject.Update();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);

            // 3. Texture Management
            EditorGUILayout.LabelField("Palette Textures", EditorStyles.boldLabel);

            DrawTextureField(palette, colorLUTProp, ColorLutLabel, B2UShaderProperty.ColorEmissionLUT);
            DrawTextureField(palette, propertyLUTProp, PropertyLutLabel, B2UShaderProperty.PropertyLUT);
            DrawTextureField(palette, iridescenceLUTProp, IridescenceLutLabel, B2UShaderProperty.IridescenceLUT);
            DrawTextureField(palette, emissionPatternsProp, EmissionPatternsLabel, B2UShaderProperty.EmissionPatterns);
            DrawTextureField(palette, detailAtlasProp, DetailAtlasLabel, B2UShaderProperty.DetailMapAtlas);
            DrawTextureField(palette, noise3DProp, Noise3DLabel, B2UShaderProperty.NoiseTexture);
            DrawTextureField(palette, paintPaletteProp, PaintPaletteLabel, B2UShaderProperty.PaintPalette);

            EditorGUILayout.Space(5f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(FindTexturesBtnContent, GUILayout.Height(28f)))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(palette, "Auto-Find Palette Textures");
                palette.FindTexturesEditorOnly();
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssets();
                serializedObject.Update();
            }
            if (GUILayout.Button(WriteLutsBtnContent, GUILayout.Height(28f)))
            {
                serializedObject.ApplyModifiedProperties();
                palette.WriteLUTsToAssetsEditorOnly();
                palette.WritePaletteToJSONEditorOnly(palette.palettePath);
                AssetDatabase.Refresh();
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(15f);

            // 4. Biomes & Subtypes
            EditorGUILayout.LabelField("Biome & Subtype Configuration", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(numSubbiomesProp, NumSubtypesLabel);
            }

            int subCount = numSubbiomesProp.intValue;

            if (biomeNamesProp != null && biomeNamesProp.isArray && subtypeNamesProp != null && subtypeNamesProp.isArray)
            {
                if (subCount > 0 && subtypeNamesProp.arraySize == biomeNamesProp.arraySize * subCount)
                {
                    showBiomeNames = EditorGUILayout.Foldout(showBiomeNames, "Biome & Subtype Names", true);
                    if (showBiomeNames)
                    {
                        for (int i = 0; i < biomeNamesProp.arraySize; i++)
                        {
                            SerializedProperty biomeProp = biomeNamesProp.GetArrayElementAtIndex(i);
                            GUIContent bLabel = i < BiomeLabels.Length ? BiomeLabels[i] : new GUIContent($"Biome {i}");
                            EditorGUILayout.PropertyField(biomeProp, bLabel);

                            EditorGUI.indentLevel++;
                            for (int j = 0; j < subCount; j++)
                            {
                                int subIndex = (i * subCount) + j;
                                SerializedProperty subProp = subtypeNamesProp.GetArrayElementAtIndex(subIndex);
                                GUIContent sLabel = j < SubtypeLabels.Length ? SubtypeLabels[j] : new GUIContent($"Subtype {j}");
                                EditorGUILayout.PropertyField(subProp, sLabel);
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Load palette from JSON to initialize biome and subtype name arrays.", MessageType.Info);
                }
            }

            EditorGUILayout.Space(15f);

            // 5. Internal Source Image Names (Read-Only Reference)
            EditorGUILayout.LabelField("Internal Palette Image Names", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                if (detailImageNameProp != null) EditorGUILayout.PropertyField(detailImageNameProp, DetailNameLabel);
                if (emissionPatternNameProp != null) EditorGUILayout.PropertyField(emissionPatternNameProp, EmissionNameLabel);
                if (iridescenceLUTNameProp != null) EditorGUILayout.PropertyField(iridescenceLUTNameProp, IridescenceNameLabel);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTextureField(B2UPaletteData palette, SerializedProperty prop, GUIContent label, B2UShaderProperty texEnum)
        {
            if (prop == null) return;
            EditorGUILayout.PropertyField(prop, label);

            if (prop.objectReferenceValue != null)
            {
                string texName = prop.objectReferenceValue.name;
                palette.CheckTextureName(texName, (int)texEnum);

                if (ValidTextureNamesField != null)
                {
                    bool[] validArray = ValidTextureNamesField.GetValue(palette) as bool[];
                    if (validArray != null && (int)texEnum < validArray.Length && !validArray[(int)texEnum])
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.HelpBox($"The texture '{texName}' does not match the B2U naming convention for {texEnum}.", MessageType.Warning);

                        if (GUILayout.Button("Fix Texture Name", GUILayout.Height(20f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            palette.FixTextureName(texName, (int)texEnum);
                            AssetDatabase.SaveAssets();
                            AssetDatabase.Refresh();
                            serializedObject.Update();
                        }
                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(5f);
                    }
                }
            }
        }
    }
}
#endif