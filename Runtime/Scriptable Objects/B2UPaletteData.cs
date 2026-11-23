using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static B2U.B2UDebug;

#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
#endif

namespace B2U
{
    /// <summary>
    /// ScriptableObject container that holds a B2U palette's LUT and texture references, serialized runtime material/schema maps,
    /// and Editor-only JSON/HLSL synchronization utilities.
    /// </summary>
    [CreateAssetMenu(fileName = "New B2U Palette Data", menuName = "B2U/Palette Data")]
    public class B2UPaletteData : ScriptableObject
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Runtime;
        /// <summary>
        /// The name of the JSON file.
        /// </summary>
        /// <remarks>This is not the scriptable object's asset name.</remarks>
        public string paletteName;
        /// <summary>
        /// The path to the JSON file.
        /// </summary>
        public string palettePath;
        /// <summary>
        /// The path where the LUT images are saved.
        /// </summary>
        public string folderPath;

        /// <summary>
        /// Holds the material and schema data for the palette.
        /// </summary>
        [NonSerialized]
        public B2UMaterialContainer materials = new B2UMaterialContainer();

        /// <summary>
        /// A serialized array of the materials so they persist across domain reloads.
        /// </summary>
        [SerializeField, HideInInspector]
        private B2UMaterial[] serializedMaterials = new B2UMaterial[256];

        /// <summary>
        /// A serialized array of the solid schema data.
        /// </summary>
        [SerializeField, HideInInspector]
        private SerializedSlotMap[] serializedSolidSchema;

        /// <summary>
        /// A serialized array of the crystal schema data.
        /// </summary>
        [SerializeField, HideInInspector]
        private SerializedSlotMap[] serializedCrystalSchema;

        /// <summary>
        /// A serialized array of the liquid schema data.
        /// </summary>
        [SerializeField, HideInInspector]
        private SerializedSlotMap[] serializedLiquidSchema;

        /// <summary>
        /// The Color/Emission LUT for the palette.
        /// </summary>
        public Texture2D colorLUT;
        /// <summary>
        /// The Property LUT for the palette.
        /// </summary>
        public Texture2D propertyLUT;
        /// <summary>
        /// The Iridescence LUT for the palette.
        /// </summary>
        public Texture2D iridescenceLUT;
        /// <summary>
        /// The Emission Patterns texture for the palette.
        /// </summary>
        public Texture2D emissionPatterns;
        /// <summary>
        /// The detail map Texture2DArray for the palette.
        /// </summary>
        public Texture2DArray detailAtlas;
        /// <summary>
        /// The 3D noise texture for the palette.
        /// </summary>
        public Texture3D noise3D;
        /// <summary>
        /// The paint palette Texture2DArray for the palette.
        /// </summary>
        public Texture2DArray paintPalette;

        /// <summary>
        /// The number of subbiomes the palette has.
        /// </summary>
        [SerializeField] public int numSubbiomes;
        /// <summary>
        /// The main biome names for the palette.
        /// </summary>
        [SerializeField] public string[] biomeNames = new string[8];
        /// <summary>
        /// The subtype names for the palette.
        /// </summary>
        [SerializeField] public string[] subtypeNames;
        /// <summary>
        /// The detail image name (used to automatically pull it from assets).
        /// </summary>
        [SerializeField] private string detailImageName;
        /// <summary>
        /// The emission pattern image name (used to automatically pull it from assets).
        /// </summary>
        [SerializeField] private string emissionPatternName;
        /// <summary>
        /// The iridescence LUT image name (used to automatically pull it from assets).
        /// </summary>
        [SerializeField] private string iridescenceLUTName;

#if UNITY_EDITOR
        // Editor-only schema and material authoring data persisted on the asset to survive domain reloads
        /// <summary>
        /// EDITOR-ONLY: The full opaque schema data.
        /// </summary>
        [SerializeField, HideInInspector] public SchemaSlot[] opaqueSchema = new SchemaSlot[17];
        /// <summary>
        /// EDITOR-ONLY: The full transparent schema data.
        /// </summary>
        [SerializeField, HideInInspector] public SchemaSlot[] transparentSchema = new SchemaSlot[17];
        /// <summary>
        /// EDITOR-ONLY: The full liquid schema data.
        /// </summary>
        [SerializeField, HideInInspector] public SchemaSlot[] liquidSchema = new SchemaSlot[35];
        /// <summary>
        /// EDITOR-ONLY: The full crystal schema data.
        /// </summary>
        [SerializeField, HideInInspector] public SchemaSlot[] crystalSchema = new SchemaSlot[17];
        /// <summary>
        /// EDITOR-ONLY: Contains the materials in the palette. Holds data that is not needed at runtime but used by editor scripts.
        /// </summary>
        [SerializeField, HideInInspector] public MaterialSlot[] editorOnlyMaterials = new MaterialSlot[256];
        /// <summary>
        /// EDITOR-ONLY: Whether or not the textures assigned to the palette follow valid B2U naming conventions.
        /// </summary>
        [NonSerialized] private readonly bool[] validTextureNames = new bool[16];
#endif

        private void Awake()
        {
#if !UNITY_EDITOR
            if (string.IsNullOrEmpty(name))
            {
                B2ULogError(msgSrc, "Blocked runtime instantiation of B2UPaletteData. This object's initialization relies on Editor AssetDatabase functions and must be authored in the editor.");
                Destroy(this);
            }
#endif
        }

        private void OnEnable()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the non-serialized runtime material and schema lookup dictionaries from the serialized arrays.
        /// </summary>
        public void Initialize()
        {
            if (materials == null) materials = new B2UMaterialContainer();
            materials.InitializeRuntimeDictionaries(paletteName, serializedMaterials, serializedSolidSchema, serializedCrystalSchema, serializedLiquidSchema);
        }
        /// <summary>
        /// Gets the schema-defined (Min, Max) range for the specified slot key and workflow.
        /// </summary>
        /// <param name="key">The pixel and channel to pull.</param>
        /// <param name="workflow">The workflow schema to pull the min/max from.</param>
        /// <returns>The Vector2 min/max value.</returns>
        public Vector2 TryGetMinMax(B2USlotKey key, B2UWorkflow workflow)
        {
            // Fast O(1) lookup via the initialized runtime dictionary
            if (materials != null && materials.IsInitialized)
            {
                B2USlotData slotData = materials.GetPixelID(key, workflow);
                if (slotData != null) return slotData.minMax;
            }

            // Fallback scan over the serialized schema array
            SerializedSlotMap[] slotMap = workflow switch
            {
                B2UWorkflow.Crystal => serializedCrystalSchema,
                B2UWorkflow.Liquid => serializedLiquidSchema,
                _ => serializedSolidSchema
            };

            if (slotMap != null)
            {
                for (int i = 0; i < slotMap.Length; i++)
                {
                    if (slotMap[i].key == key && slotMap[i].data != null)
                    {
                        return slotMap[i].data.minMax;
                    }
                }
            }

            return Vector2.zero;
        }

        /// <summary>
        /// Loads the palette data from a JSON file on disk (Editor Only).
        /// </summary>
        /// <param name="JSONPath">The path of the JSON file.</param>
        [Conditional("UNITY_EDITOR")]
        public void LoadPaletteFromJSONEditorOnly(string JSONPath)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(JSONPath) || !File.Exists(JSONPath))
            {
                B2ULogError(B2UMessageSource.Editor, $"JSON file not found at path {JSONPath}", this);
                return;
            }

            try
            {
                string jsonText = File.ReadAllText(JSONPath);
                B2UPaletteJSONRoot root = JsonConvert.DeserializeObject<B2UPaletteJSONRoot>(jsonText);

                if (root == null || root.settings == null) return;

                paletteName = Path.GetFileNameWithoutExtension(JSONPath);
                numSubbiomes = Mathf.Max(1, root.settings.num_subtypes);
                detailImageName = root.settings.detail_atlas_image_name;
                emissionPatternName = root.settings.emission_pattern_image_name;
                iridescenceLUTName = root.settings.iridescence_lut_image_name;

                if (biomeNames == null || biomeNames.Length != 8) biomeNames = new string[8];
                subtypeNames = new string[8 * numSubbiomes];

                for (int i = 0; i < 8; i++)
                {
                    if (root.settings.biome_names != null && i < root.settings.biome_names.Length && !string.IsNullOrEmpty(root.settings.biome_names[i]))
                        biomeNames[i] = root.settings.biome_names[i];
                    else
                        biomeNames[i] = $"Biome {i}";

                    for (int j = 0; j < numSubbiomes; j++)
                    {
                        subtypeNames[(i * numSubbiomes) + j] = $"Subtype {j} ({biomeNames[i]})";
                    }
                }

                ParseSchemas(root.schema_definitions);
                ParseEditorMaterials(root.materials);

                Initialize();
                EditorUtility.SetDirty(this);

                B2ULog(B2UMessageSource.Editor, $"Successfully loaded palette data from {JSONPath}.", B2UMessageType.None, this);
            }
            catch (Exception e)
            {
                B2ULogError(B2UMessageSource.Editor, $"Failed to parse JSON. Error: {e.Message}.", this);
            }
#endif
        }

        /// <summary>
        /// Reverse-engineers baked LUT textures to recover all Material Data, Workflows, and Colors (Editor Only).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public void LoadPaletteFromLUTsEditorOnly()
        {
#if UNITY_EDITOR
            if (colorLUT == null || propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Editor, "Cannot recover from LUTs because the Color LUT or Property LUT is missing from the asset references.", this);
                return;
            }

            Color32[] colPix = colorLUT.GetPixels32();
            Color32[] propPix = propertyLUT.GetPixels32();

            int colW = (numSubbiomes * 8) + 8;
            int propW = 4;

            List<MaterialSlot> recoveredMats = new List<MaterialSlot>();

            bool IsEmpty(int r)
            {
                if (r >= 256) return true;
                for (int i = 0; i < propW; i++)
                {
                    Color32 c = propPix[(r * propW) + i];
                    if (c.r != 0 || c.g != 0 || c.b != 0 || c.a != 0) return false;
                }
                for (int i = 0; i < colW; i++)
                {
                    Color32 c = colPix[(r * colW) + i];
                    if (c.r != 0 || c.g != 0 || c.b != 0 || c.a != 0) return false;
                }
                return true;
            }

            int y = 0;
            while (y < 256)
            {
                if (IsEmpty(y)) { y++; continue; }

                // Decode Workflow & Biome Type from r1_p4_r
                byte rawWf = propPix[(y * propW) + 3].r;
                int wfInt = Mathf.RoundToInt((rawWf / 255f) * 10f);

                B2UWorkflow wf = B2UWorkflow.Opaque;
                SchemaClass schemaClass = SchemaClass.OPAQUE;
                SchemaSlot[] activeSchema = opaqueSchema;

                int structWf = wfInt % 5;
                if (structWf == 0) { wf = B2UWorkflow.Opaque; schemaClass = SchemaClass.OPAQUE; activeSchema = opaqueSchema; }
                else if (structWf == 1) { wf = B2UWorkflow.OpaqueDoubleSided; schemaClass = SchemaClass.OPAQUE; activeSchema = opaqueSchema; }
                else if (structWf == 2) { wf = B2UWorkflow.Transparent; schemaClass = SchemaClass.TRANSPARENT; activeSchema = transparentSchema; }
                else if (structWf == 3) { wf = B2UWorkflow.Crystal; schemaClass = SchemaClass.CRYSTAL; activeSchema = crystalSchema; }
                else if (structWf == 4) { wf = B2UWorkflow.Liquid; schemaClass = SchemaClass.LIQUID; activeSchema = liquidSchema; }

                bool isDyn1 = wfInt >= 5;
                bool isLiquid = structWf == 4;

                string bType = isDyn1 ? "DYNAMIC" : "INDEPENDENT";
                int rowsNeeded = isLiquid ? 2 : 1;
                int indepY = isDyn1 ? -1 : y;
                int dynY = isDyn1 ? y : -1;

                if (!isDyn1)
                {
                    int nextY = y + rowsNeeded;
                    if (nextY < 256 && !IsEmpty(nextY))
                    {
                        byte nextWfRaw = propPix[(nextY * propW) + 3].r;
                        int nextWfInt = Mathf.RoundToInt((nextWfRaw / 255f) * 10f);
                        if (nextWfInt == wfInt + 5)
                        {
                            bType = "INDEP_DYN";
                            dynY = nextY;
                            rowsNeeded = isLiquid ? 4 : 2;
                        }
                    }
                }

                MaterialSlot mat = new MaterialSlot
                {
                    materialName = $"Recovered {wf} {y}",
                    workflow = wf,
                    biomeType = bType,
                    startRowIndex = y,
                    allocatedRows = rowsNeeded,
                    isLocked = false,
                    isBiomeResponsive = bType != "INDEPENDENT"
                };

                for (int i = 0; i < 8; i++)
                {
                    mat.variantColorBlocks[i] = new B2UColorBlock { bases = new Color32[numSubbiomes], depths = new Color32[numSubbiomes] };
                    mat.biomeColorBlocks[i] = new B2UColorBlock { bases = new Color32[numSubbiomes], depths = new Color32[numSubbiomes] };
                }

                // Decode Properties
                List<B2UPropertyBlock> pBlocks = new List<B2UPropertyBlock>();
                if (activeSchema != null)
                {
                    foreach (var sDef in activeSchema)
                    {
                        if (sDef == null || !sDef.isActive || sDef.slotID == "r1_p4_r") continue;

                        float norm = 0f;

                        if (sDef.slotID.EndsWith("_ca") || sDef.slotID.EndsWith("_ea"))
                        {
                            bool isRow2 = sDef.slotID.StartsWith("r2");
                            int pY = isRow2 ? y + 1 : y;
                            int cX = sDef.slotID.EndsWith("_ca") ? 0 : (8 * numSubbiomes);
                            norm = colPix[(pY * colW) + cX].a / 255f;
                        }
                        else
                        {
                            string[] parts = sDef.slotID.Split('_');
                            if (parts.Length < 3) continue;

                            bool isRow2 = parts[0] == "r2";
                            int pY = isRow2 ? y + 1 : y;

                            int col = parts[1] switch
                            {
                                "p1" => 0,
                                "p2" => 1,
                                "p3" => 2,
                                "p4" => 3,
                                _ => -1
                            };
                            if (col == -1) continue;

                            Color32 pixel = propPix[(pY * propW) + col];
                            byte bVal = parts[2] switch
                            {
                                "r" => pixel.r,
                                "g" => pixel.g,
                                "b" => pixel.b,
                                "a" => pixel.a,
                                _ => (byte)0
                            };

                            norm = bVal / 255f;
                        }

                        B2UPropertyBlock pb = new B2UPropertyBlock
                        {
                            slotID = sDef.slotID,
                            schema = schemaClass,
                            fVal = sDef.defaultVal,
                            iVal = (int)sDef.defaultVal,
                            bVal = sDef.defaultBVal,
                            eVal = (int)sDef.defaultVal
                        };

                        if (sDef.type == B2USlotType.Float) pb.fVal = (norm * (sDef.maxVal - sDef.minVal)) + sDef.minVal;
                        else if (sDef.type == B2USlotType.Int) pb.iVal = Mathf.RoundToInt((norm * (sDef.maxVal - sDef.minVal)) + sDef.minVal);
                        else if (sDef.type == B2USlotType.Bool) pb.bVal = norm > 0.5f;
                        else if (sDef.type == B2USlotType.Bitmask) pb.iVal = Mathf.RoundToInt(norm * 255f);
                        else if (sDef.type == B2USlotType.Enum) pb.eVal = Mathf.RoundToInt(norm * Mathf.Max(1f, sDef.enumItems != null ? sDef.enumItems.Length - 1 : 1));

                        pBlocks.Add(pb);
                    }
                }
                mat.propertyBlocks = pBlocks.ToArray();

                // Decode Colors
                if (indepY != -1)
                {
                    for (int v = 0; v < 8; v++)
                    {
                        int bx = v * numSubbiomes;
                        Color32 bc = colPix[(indepY * colW) + bx];
                        bc.a = 255;
                        mat.variantColorBlocks[v].bases[0] = bc;

                        int ex = (8 * numSubbiomes) + v;
                        Color32 ec = colPix[(indepY * colW) + ex];
                        ec.a = 255;
                        mat.variantColorBlocks[v].emission = ec;

                        if (isLiquid)
                        {
                            Color32 dc = colPix[((indepY + 1) * colW) + bx];
                            dc.a = 255;
                            mat.variantColorBlocks[v].depths[0] = dc;

                            Color32 sec = colPix[((indepY + 1) * colW) + ex];
                            sec.a = 255;
                            mat.variantColorBlocks[v].secEmission = sec;
                        }
                    }
                }

                if (dynY != -1)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        for (int s = 0; s < numSubbiomes; s++)
                        {
                            int bx = (b * numSubbiomes) + s;
                            Color32 bc = colPix[(dynY * colW) + bx];
                            bc.a = 255;
                            mat.biomeColorBlocks[b].bases[s] = bc;

                            if (isLiquid)
                            {
                                Color32 dc = colPix[((dynY + 1) * colW) + bx];
                                dc.a = 255;
                                mat.biomeColorBlocks[b].depths[s] = dc;
                            }
                        }

                        int ex = (8 * numSubbiomes) + b;
                        Color32 ec = colPix[(dynY * colW) + ex];
                        ec.a = 255;
                        mat.biomeColorBlocks[b].emission = ec;

                        if (isLiquid)
                        {
                            Color32 sec = colPix[((dynY + 1) * colW) + ex];
                            sec.a = 255;
                            mat.biomeColorBlocks[b].secEmission = sec;
                        }
                    }
                }

                recoveredMats.Add(mat);
                y += rowsNeeded;
            }

            MaterialSlot[] finalArray = new MaterialSlot[256];
            for (int i = 0; i < recoveredMats.Count && i < 256; i++) finalArray[i] = recoveredMats[i];
            editorOnlyMaterials = finalArray;

            SyncSerializedMaterialsFromEditorSlots();
            Initialize();
            EditorUtility.SetDirty(this);

            B2ULog(B2UMessageSource.Editor, $"Successfully recovered {recoveredMats.Count} materials from LUTs. Please assign names in the Tweaker, then hit Save JSON.", B2UMessageType.None, this);
#endif
        }

        /// <summary>
        /// Writes changes made to the palette back to the JSON file on disk to keep Blender and Unity synchronized (Editor Only).
        /// </summary>
        /// <param name="JSONPath">The path for the JSON file.</param>
        [Conditional("UNITY_EDITOR")]
        public void WritePaletteToJSONEditorOnly(string JSONPath)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(JSONPath) || !File.Exists(JSONPath))
            {
                B2ULogError(B2UMessageSource.Editor, $"JSON file not found at path {JSONPath}", this);
                return;
            }

            try
            {
                string jsonText = File.ReadAllText(JSONPath);
                B2UPaletteJSONRoot root = JsonConvert.DeserializeObject<B2UPaletteJSONRoot>(jsonText);

                if (root == null)
                {
                    B2ULogError(B2UMessageSource.Editor, "Could not parse existing JSON.", this);
                    return;
                }

                if (root.settings != null && biomeNames != null)
                {
                    root.settings.biome_names = (string[])biomeNames.Clone();
                }

                var newMaterials = new List<B2UPaletteJSONMaterial>();

                foreach (var mat in editorOnlyMaterials)
                {
                    if (mat == null || string.IsNullOrEmpty(mat.materialName)) continue;

                    B2UPaletteJSONMaterial jm = new B2UPaletteJSONMaterial
                    {
                        mat_name = mat.materialName,
                        workflow = mat.workflow == B2UWorkflow.OpaqueDoubleSided ? "OPAQUE_DS" : mat.workflow.ToString().ToUpper(),
                        biome_type = mat.biomeType,
                        start_row_index = mat.startRowIndex,
                        is_locked = mat.isLocked,
                        allocated_rows = mat.allocatedRows
                    };

                    if (mat.biomeColorBlocks != null)
                    {
                        var dynList = new List<B2UPaletteJSONColorBlock>(mat.biomeColorBlocks.Length);
                        foreach (var cb in mat.biomeColorBlocks) dynList.Add(ColorBlockToJSON(cb));
                        jm.dynamic_colors = dynList.ToArray();
                    }

                    if (mat.variantColorBlocks != null)
                    {
                        var varList = new List<B2UPaletteJSONColorBlock>(mat.variantColorBlocks.Length);
                        foreach (var cb in mat.variantColorBlocks) varList.Add(ColorBlockToJSON(cb));
                        jm.variant_colors = varList.ToArray();
                    }

                    if (mat.propertyBlocks != null)
                    {
                        var dbList = new List<B2UPaletteJSONDataBlock>(mat.propertyBlocks.Length);
                        foreach (var pb in mat.propertyBlocks)
                        {
                            dbList.Add(new B2UPaletteJSONDataBlock
                            {
                                name = pb.slotID,
                                schema_type = pb.schema.ToString().ToUpper(),
                                f_val = pb.fVal,
                                i_val = pb.iVal,
                                b_val = pb.bVal,
                                e_val = pb.eVal.ToString()
                            });
                        }
                        jm.data_blocks = dbList.ToArray();
                    }

                    newMaterials.Add(jm);
                }

                root.materials = newMaterials.ToArray();

                SyncSerializedMaterialsFromEditorSlots();
                Initialize();
                EditorUtility.SetDirty(this);

                string outputJson = JsonConvert.SerializeObject(root, Formatting.Indented);
                File.WriteAllText(JSONPath, outputJson);

                B2ULog(B2UMessageSource.Editor, $"Successfully wrote palette data back to {JSONPath}.", B2UMessageType.None, this);
            }
            catch (Exception e)
            {
                B2ULogError(B2UMessageSource.Editor, $"Failed to write JSON. Error: {e.Message}.", this);
            }
#endif
        }

        /// <summary>
        /// Writes the active in-memory Color and Property LUT textures to disk as PNG files (Editor Only).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public void WriteLUTsToAssetsEditorOnly()
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(folderPath) || colorLUT == null || propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Editor, "Cannot write LUTs. Missing folder path or texture references.", this);
                return;
            }

            string colorPath = AssetDatabase.GetAssetPath(colorLUT);
            if (!string.IsNullOrEmpty(colorPath))
            {
                File.WriteAllBytes(colorPath, colorLUT.EncodeToPNG());
                AssetDatabase.ImportAsset(colorPath, ImportAssetOptions.ForceUpdate);
            }

            string propPath = AssetDatabase.GetAssetPath(propertyLUT);
            if (!string.IsNullOrEmpty(propPath))
            {
                File.WriteAllBytes(propPath, propertyLUT.EncodeToPNG());
                AssetDatabase.ImportAsset(propPath, ImportAssetOptions.ForceUpdate);
            }

            B2ULog(B2UMessageSource.Editor, "Successfully saved live LUT textures to disk.", B2UMessageType.None, this);
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public void CheckTextureName(string texName, int tex)
        {
#if UNITY_EDITOR
            if (tex < 0 || tex >= validTextureNames.Length) return;

            if (string.IsNullOrEmpty(texName) || !texName.StartsWith("B2U"))
            {
                validTextureNames[tex] = false;
                return;
            }

            switch ((B2UShaderProperty)tex)
            {
                case B2UShaderProperty.ColorEmissionLUT: validTextureNames[tex] = texName.Contains("ColorEmission"); break;
                case B2UShaderProperty.PropertyLUT: validTextureNames[tex] = texName.Contains("Property"); break;
                case B2UShaderProperty.IridescenceLUT: validTextureNames[tex] = texName.Contains("Iridescence"); break;
                case B2UShaderProperty.EmissionPatterns: validTextureNames[tex] = texName.Contains("Patterns"); break;
                case B2UShaderProperty.DetailMapAtlas: validTextureNames[tex] = texName.Contains("Detail"); break;
                case B2UShaderProperty.NoiseTexture: validTextureNames[tex] = texName.Contains("3DNoise"); break;
                case B2UShaderProperty.PaintPalette: validTextureNames[tex] = texName.Contains("PaintPalette"); break;
                default: validTextureNames[tex] = false; break;
            }
#endif
        }

        /// <summary>
        /// Automatically fixes an image's name to meet B2U naming conventions.
        /// </summary>
        /// <param name="texName">The image's name.</param>
        /// <param name="tex">An int matching the textures index in the B2UShaderProperty enum.</param>
        [Conditional("UNITY_EDITOR")]
        public void FixTextureName(string texName, int tex)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(texName)) return;
            string[] guids = AssetDatabase.FindAssets($"{texName} t:Texture");
            if (guids.Length == 0) return;

            string suffix = ((B2UShaderProperty)tex) switch
            {
                B2UShaderProperty.ColorEmissionLUT => "ColorEmission",
                B2UShaderProperty.PropertyLUT => "Property",
                B2UShaderProperty.IridescenceLUT => "Iridescence",
                B2UShaderProperty.EmissionPatterns => "Patterns",
                B2UShaderProperty.DetailMapAtlas => "Detail",
                B2UShaderProperty.NoiseTexture => "3DNoise",
                B2UShaderProperty.PaintPalette => "PaintPalette",
                _ => ""
            };
            if (string.IsNullOrEmpty(suffix)) return;

            string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            string cleanName = texName.StartsWith("B2U_") ? texName.Substring(4) : texName;

            AssetDatabase.RenameAsset(assetPath, $"B2U_{cleanName}_{suffix}");
#endif
        }

        /// <summary>
        /// Searches the AssetDatabase for the textures associated with this palette and binds their references (Editor Only).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public void FindTexturesEditorOnly()
        {
#if UNITY_EDITOR
            string colorFolderPath = "";
            string[] colorGuids = AssetDatabase.FindAssets($"B2U_{paletteName}_ColorEmissionLUT t:Texture2D");

            if (colorGuids.Length > 0)
            {
                string colorPath = AssetDatabase.GUIDToAssetPath(colorGuids[0]);
                colorLUT = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
                colorFolderPath = Path.GetDirectoryName(colorPath);
                folderPath = colorFolderPath;

                string currentFileName = Path.GetFileNameWithoutExtension(colorPath);
                string expectedFileName = $"B2U_{paletteName}_ColorEmissionLUT_{(numSubbiomes * 8) + 8}x256";
                if (currentFileName != expectedFileName)
                {
                    AssetDatabase.RenameAsset(colorPath, expectedFileName);
                }
            }
            else
            {
                B2ULogWarning(B2UMessageSource.Editor, $"Could not find existing color LUT for palette: {paletteName}", this);
            }

            string propFolderPath = "";
            string[] propGuids = AssetDatabase.FindAssets($"B2U_{paletteName}_PropertyLUT_4x256 t:Texture2D");
            if (propGuids.Length > 0)
            {
                string propPath = AssetDatabase.GUIDToAssetPath(propGuids[0]);
                propertyLUT = AssetDatabase.LoadAssetAtPath<Texture2D>(propPath);
                propFolderPath = Path.GetDirectoryName(propPath);
            }
            else
            {
                B2ULogWarning(B2UMessageSource.Editor, $"Could not find existing property LUT for palette: {paletteName}", this);
            }

            if (!string.IsNullOrEmpty(colorFolderPath)) folderPath = colorFolderPath;
            if (!string.IsNullOrEmpty(colorFolderPath) && !string.IsNullOrEmpty(propFolderPath) && colorFolderPath != propFolderPath)
            {
                B2ULogWarning(B2UMessageSource.Editor, $"The color LUT and property LUT for the palette {paletteName} do not exist in the same folder. When calling BuildLUTs() the editor will create a new texture for the property LUT in {colorFolderPath}.", this);
            }

            if (!string.IsNullOrEmpty(iridescenceLUTName))
            {
                string[] iridGuids = AssetDatabase.FindAssets($"{iridescenceLUTName.Split('.')[0]} t:Texture2D");
                if (iridGuids.Length > 0)
                {
                    iridescenceLUT = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(iridGuids[0]));
                }
                else B2ULogWarning(B2UMessageSource.Editor, $"Could not find iridescence LUT for palette: {paletteName}", this);
            }

            if (!string.IsNullOrEmpty(emissionPatternName))
            {
                string[] emisGuids = AssetDatabase.FindAssets($"{emissionPatternName.Split('.')[0]} t:Texture2D");
                if (emisGuids.Length > 0)
                {
                    emissionPatterns = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(emisGuids[0]));
                }
                else B2ULogWarning(B2UMessageSource.Editor, $"Could not find emission patterns for palette: {paletteName}", this);
            }

            if (!string.IsNullOrEmpty(detailImageName))
            {
                string[] detGuids = AssetDatabase.FindAssets($"{detailImageName.Split('.')[0]} t:Texture2DArray");
                if (detGuids.Length > 0)
                {
                    detailAtlas = AssetDatabase.LoadAssetAtPath<Texture2DArray>(AssetDatabase.GUIDToAssetPath(detGuids[0]));
                }
                else B2ULogWarning(B2UMessageSource.Editor, $"Could not find detail atlas for palette: {paletteName}", this);
            }

            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Generates the B2U_Constants.hlsl file from the current schema min/max definitions (Editor Only).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public void BuildHLSLConstantsEditorOnly()
        {
#if UNITY_EDITOR
            string[] guids = AssetDatabase.FindAssets("B2U_Constants");
            string path = "";

            foreach (string guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
                {
                    path = p;
                    break;
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("Save B2U Constants HLSL", "B2U_Constants", "hlsl", "Choose where to save the B2U_Constants.hlsl file.");
                if (string.IsNullOrEmpty(path)) return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#ifndef B2U_CONSTANTS_INCLUDED");
            sb.AppendLine("#define B2U_CONSTANTS_INCLUDED\n");
            sb.AppendLine("// Schema defined min/max values for each workflow's properties. The editor can write to this file to modify the min/max if the schema changes.");
            sb.AppendLine("// It can ONLY be changed in editor as changing these values requires the shaders to recompile.");
            sb.AppendLine("// Structure:");
            sb.AppendLine("// ///<summary>");
            sb.AppendLine("// ///{uiName} ({Note if applicable})");
            sb.AppendLine("// ///</summary>");
            sb.AppendLine("// static const half2 {Workflow}_{slotID}_MinMax = half2(Min, Max);\n");

            void AppendSchema(string prefix, SchemaSlot[] schema)
            {
                if (prefix == "Opaque") sb.AppendLine("// --- OPAQUE/OPAQUE_DS RANGES ---");
                else sb.AppendLine($"// --- {prefix.ToUpperInvariant()} RANGES ---");

                if (schema != null)
                {
                    foreach (var slot in schema)
                    {
                        if (slot == null || slot.slotID == "r1_p4_r") continue;

                        string uiName = slot.isActive ? slot.uiName : "None (Not defined in schema)";
                        float min = slot.isActive ? slot.minVal : 0f;
                        float max = slot.isActive ? slot.maxVal : 0f;

                        string minStr = min.ToString("0.0", CultureInfo.InvariantCulture);
                        string maxStr = max.ToString("0.0", CultureInfo.InvariantCulture);

                        sb.AppendLine("/// <summary>");
                        sb.AppendLine($"/// {uiName}");
                        sb.AppendLine("/// </summary>");
                        sb.AppendLine($"static const half2 {prefix}_{slot.slotID}_MinMax = half2({minStr}, {maxStr});\n");
                    }
                }
                sb.AppendLine("\n");
            }

            AppendSchema("Opaque", opaqueSchema);
            AppendSchema("Transparent", transparentSchema);
            AppendSchema("Crystal", crystalSchema);
            AppendSchema("Liquid", liquidSchema);

            sb.AppendLine("#endif");

            File.WriteAllText(path, sb.ToString().TrimEnd() + "\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            B2ULog(B2UMessageSource.Editor, $"Successfully built HLSL Constants at {path}", B2UMessageType.None, this);
#endif
        }

#if UNITY_EDITOR

        private SerializedSlotMap[] BuildSerializedSchemaMap(List<SchemaSlot> schemaSlots)
        {
            List<SerializedSlotMap> maps = new List<SerializedSlotMap>(schemaSlots.Count);
            foreach (var slot in schemaSlots)
            {
                if (Enum.TryParse(slot.slotID, out B2USlotKey key))
                {
                    B2USlotData data = new B2USlotData();
                    data.InitializeFromKey(key, new Vector2(slot.minVal, slot.maxVal), slot.type);
                    maps.Add(new SerializedSlotMap { key = key, data = data });
                }
            }
            return maps.ToArray();
        }

        private void ParseSchemas(B2UPaletteJSONSchema[] jsonSchemas)
        {
            if (jsonSchemas == null) return;

            List<SchemaSlot> opaqueList = new List<SchemaSlot>();
            List<SchemaSlot> transparentList = new List<SchemaSlot>();
            List<SchemaSlot> liquidList = new List<SchemaSlot>();
            List<SchemaSlot> crystalList = new List<SchemaSlot>();

            foreach (var js in jsonSchemas)
            {
                SchemaSlot slot = new SchemaSlot
                {
                    slotID = js.slot_id,
                    uiName = js.ui_name,
                    uiDescription = js.ui_description,
                    minVal = js.min_val,
                    maxVal = js.max_val,
                    defaultVal = js.default_val,
                    defaultBVal = js.default_b_val,
                    isActive = js.is_active
                };

                slot.enumItems = string.IsNullOrEmpty(js.enum_items)
                    ? Array.Empty<string>()
                    : js.enum_items.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < slot.enumItems.Length; i++) slot.enumItems[i] = slot.enumItems[i].Trim();

                slot.bitmaskFlags = string.IsNullOrEmpty(js.bitmask_items)
                    ? Array.Empty<string>()
                    : js.bitmask_items.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < slot.bitmaskFlags.Length; i++) slot.bitmaskFlags[i] = slot.bitmaskFlags[i].Trim();

                if (js.data_type == "FLOAT") slot.type = B2USlotType.Float;
                else if (js.data_type == "INT") slot.type = B2USlotType.Int;
                else if (js.data_type == "BOOL") slot.type = B2USlotType.Bool;
                else if (js.data_type == "ENUM")
                {
                    slot.type = B2USlotType.Enum;
                    slot.minVal = 0f;
                    slot.maxVal = Mathf.Max(0f, slot.enumItems.Length - 1);
                }
                else if (js.data_type == "BITMASK") slot.type = B2USlotType.Bitmask;

                if (js.schema == "OPAQUE") { slot.schema = SchemaClass.OPAQUE; opaqueList.Add(slot); }
                else if (js.schema == "TRANSPARENT") { slot.schema = SchemaClass.TRANSPARENT; transparentList.Add(slot); }
                else if (js.schema == "LIQUID") { slot.schema = SchemaClass.LIQUID; liquidList.Add(slot); }
                else if (js.schema == "CRYSTAL") { slot.schema = SchemaClass.CRYSTAL; crystalList.Add(slot); }
            }

            opaqueSchema = opaqueList.ToArray();
            transparentSchema = transparentList.ToArray();
            liquidSchema = liquidList.ToArray();
            crystalSchema = crystalList.ToArray();

            serializedSolidSchema = BuildSerializedSchemaMap(opaqueList);
            serializedCrystalSchema = BuildSerializedSchemaMap(crystalList);
            serializedLiquidSchema = BuildSerializedSchemaMap(liquidList);
        }

        private void ParseEditorMaterials(B2UPaletteJSONMaterial[] jsonMaterials)
        {
            if (jsonMaterials == null) return;

            List<MaterialSlot> matList = new List<MaterialSlot>(jsonMaterials.Length);

            foreach (var jm in jsonMaterials)
            {
                MaterialSlot slot = new MaterialSlot
                {
                    materialName = jm.mat_name,
                    isBiomeResponsive = !string.Equals(jm.biome_type, "INDEPENDENT", StringComparison.OrdinalIgnoreCase),
                    biomeType = string.IsNullOrEmpty(jm.biome_type) ? "INDEPENDENT" : jm.biome_type.ToUpperInvariant(),
                    startRowIndex = jm.start_row_index,
                    isLocked = jm.is_locked,
                    allocatedRows = jm.allocated_rows
                };

                if (jm.workflow == "OPAQUE") slot.workflow = B2UWorkflow.Opaque;
                else if (jm.workflow == "OPAQUE_DS") slot.workflow = B2UWorkflow.OpaqueDoubleSided;
                else if (jm.workflow == "TRANSPARENT") slot.workflow = B2UWorkflow.Transparent;
                else if (jm.workflow == "LIQUID") slot.workflow = B2UWorkflow.Liquid;
                else if (jm.workflow == "CRYSTAL") slot.workflow = B2UWorkflow.Crystal;

                slot.biomeColorBlocks = ParseColorBlocks(jm.dynamic_colors, 8);
                slot.variantColorBlocks = ParseColorBlocks(jm.variant_colors, 8);

                if (jm.data_blocks != null)
                {
                    slot.propertyBlocks = new B2UPropertyBlock[jm.data_blocks.Length];
                    for (int i = 0; i < jm.data_blocks.Length; i++)
                    {
                        var db = jm.data_blocks[i];
                        B2UPropertyBlock pb = new B2UPropertyBlock
                        {
                            slotID = db.name,
                            fVal = db.f_val,
                            iVal = db.i_val,
                            bVal = db.b_val,
                            eVal = int.TryParse(db.e_val, out int ev) ? ev : 0
                        };

                        if (db.schema_type == "OPAQUE") pb.schema = SchemaClass.OPAQUE;
                        else if (db.schema_type == "TRANSPARENT") pb.schema = SchemaClass.TRANSPARENT;
                        else if (db.schema_type == "LIQUID") pb.schema = SchemaClass.LIQUID;
                        else if (db.schema_type == "CRYSTAL") pb.schema = SchemaClass.CRYSTAL;

                        slot.propertyBlocks[i] = pb;
                    }
                }
                matList.Add(slot);
            }
            editorOnlyMaterials = matList.ToArray();
            SyncSerializedMaterialsFromEditorSlots();
        }

        private void SyncSerializedMaterialsFromEditorSlots()
        {
            serializedMaterials = new B2UMaterial[256];
            if (editorOnlyMaterials == null) return;

            foreach (var slot in editorOnlyMaterials)
            {
                if (slot == null || string.IsNullOrEmpty(slot.materialName)) continue;
                if (slot.startRowIndex < 0 || slot.startRowIndex >= 256) continue;

                serializedMaterials[slot.startRowIndex] = new B2UMaterial
                {
                    slotName = slot.materialName,
                    workflow = slot.workflow,
                    isBiomeResponsive = slot.isBiomeResponsive,
                    biomeType = slot.biomeType,
                    alphaValue = (byte)slot.startRowIndex
                };
            }
        }

        private B2UColorBlock[] ParseColorBlocks(B2UPaletteJSONColorBlock[] jBlocks, int expectedLength)
        {
            B2UColorBlock[] blocks = new B2UColorBlock[expectedLength];
            if (jBlocks == null) return blocks;

            for (int i = 0; i < expectedLength && i < jBlocks.Length; i++)
            {
                var jb = jBlocks[i];
                B2UColorBlock cb = new B2UColorBlock
                {
                    emission = FloatArrayToColor32(jb.emission),
                    secEmission = FloatArrayToColor32(jb.sec_emission)
                };

                if (jb.bases != null)
                {
                    cb.bases = new Color32[jb.bases.Length];
                    for (int b = 0; b < jb.bases.Length; b++) cb.bases[b] = FloatArrayToColor32(jb.bases[b]);
                }
                if (jb.depths != null)
                {
                    cb.depths = new Color32[jb.depths.Length];
                    for (int d = 0; d < jb.depths.Length; d++) cb.depths[d] = FloatArrayToColor32(jb.depths[d]);
                }
                blocks[i] = cb;
            }
            return blocks;
        }

        private Color32 FloatArrayToColor32(float[] floats)
        {
            if (floats == null || floats.Length < 4) return new Color32(0, 0, 0, 0);
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(floats[0] * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(floats[1] * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(floats[2] * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(floats[3] * 255f), 0, 255)
            );
        }

        private B2UPaletteJSONColorBlock ColorBlockToJSON(B2UColorBlock cb)
        {
            var jcb = new B2UPaletteJSONColorBlock
            {
                emission = Color32ToFloatArray(cb.emission),
                sec_emission = Color32ToFloatArray(cb.secEmission)
            };

            if (cb.bases != null)
            {
                jcb.bases = new float[cb.bases.Length][];
                for (int i = 0; i < cb.bases.Length; i++) jcb.bases[i] = Color32ToFloatArray(cb.bases[i]);
            }

            if (cb.depths != null)
            {
                jcb.depths = new float[cb.depths.Length][];
                for (int i = 0; i < cb.depths.Length; i++) jcb.depths[i] = Color32ToFloatArray(cb.depths[i]);
            }

            return jcb;
        }

        private float[] Color32ToFloatArray(Color32 c)
        {
            return new float[] { c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f };
        }
#endif
    }

#if UNITY_EDITOR

    public class B2UPaletteJSONRoot
    {
        public B2UPaletteJSONSettings settings;
        public B2UPaletteJSONSchema[] schema_definitions;
        public B2UPaletteJSONMaterial[] materials;
    }

    public class B2UPaletteJSONSettings
    {
        public bool palette_uses_default_schema;
        public int num_subtypes;
        public string[] biome_names;
        public string[] subtype_names;
        public bool invert_r;
        public bool invert_g;
        public bool invert_b;
        public int detail_atlas_width;
        public int detail_atlas_height;
        public int noise_atlas_dimensions = 64;
        public string detail_atlas_image_name;
        public string detail_atlas_image_path;
        public string emission_pattern_image_name;
        public string emission_pattern_image_path;
        public string iridescence_lut_image_name;
        public string iridescence_lut_image_path;
    }

    public class B2UPaletteJSONSchema
    {
        public string schema;
        public string slot_id;
        public string ui_name;
        public string ui_description;
        public string data_type;
        public string enum_items;
        public string bitmask_items;
        public float min_val;
        public float max_val;
        public float default_val;
        public bool default_b_val;
        public bool is_active;
    }

    public class B2UPaletteJSONMaterial
    {
        public string mat_name;
        public string workflow;
        public string biome_type;
        public int start_row_index;
        public bool is_locked;
        public int allocated_rows;
        public B2UPaletteJSONColorBlock[] dynamic_colors;
        public B2UPaletteJSONColorBlock[] variant_colors;
        public B2UPaletteJSONDataBlock[] data_blocks;
    }

    public class B2UPaletteJSONColorBlock
    {
        public float[] emission;
        public float[] sec_emission;
        public float[][] bases;
        public float[][] depths;
    }

    public class B2UPaletteJSONDataBlock
    {
        public string name;
        public string schema_type;
        public float f_val;
        public int i_val;
        public bool b_val;
        public string e_val;
    }

    [Serializable]
    public class SchemaSlot
    {
        public SchemaClass schema;
        public string slotID;
        public string uiName;
        public string uiDescription;
        public B2USlotType type;
        public string[] enumItems;
        public string[] bitmaskFlags;
        public float minVal;
        public float maxVal;
        public float defaultVal;
        public bool defaultBVal;
        public bool isActive;
    }

    public enum SchemaClass
    {
        OPAQUE,
        TRANSPARENT,
        LIQUID,
        CRYSTAL
    }

    [Serializable]
    public class MaterialSlot
    {
        public string materialName;
        public B2UWorkflow workflow;
        public bool isBiomeResponsive;
        public string biomeType;
        public int startRowIndex;
        public bool isLocked;
        public int allocatedRows;
        public B2UColorBlock[] biomeColorBlocks = new B2UColorBlock[8];
        public B2UColorBlock[] variantColorBlocks = new B2UColorBlock[8];
        public B2UPropertyBlock[] propertyBlocks = new B2UPropertyBlock[36];
    }

    [Serializable]
    public struct B2UPropertyBlock
    {
        public string slotID;
        public SchemaClass schema;
        public float fVal;
        public int iVal;
        public bool bVal;
        public int eVal;
    }

    [Serializable]
    public struct B2UColorBlock
    {
        public Color32 emission;
        public Color32 secEmission;
        public Color32[] bases;
        public Color32[] depths;
    }
#endif
}