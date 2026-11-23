using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static B2U.B2UDebug;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace B2U
{
    /// <summary>
    /// Serializable key-value container used to persist B2UShaderPropertyBlock values across domain reloads and builds.
    /// </summary>
    [Serializable]
    public class B2USerializedData
    {
        /// <summary>
        /// The key name.
        /// </summary>
        public string keyName;
        /// <summary>
        /// The float value.
        /// </summary>
        public float fValue;
        /// <summary>
        /// The int value.
        /// </summary>
        public int iValue;
        /// <summary>
        /// The bool value.
        /// </summary>
        public bool bValue;
        /// <summary>
        /// The Color value.
        /// </summary>
        public Color cValue;
        /// <summary>
        /// The Vector2 value.
        /// </summary>
        public Vector2 vValue;
        /// <summary>
        /// The LayerMask value.
        /// </summary>
        public LayerMask lValue;
        /// <summary>
        /// The UnityEngine.Object value.
        /// </summary>
        public UnityEngine.Object oValue;
    }

    /// <summary>
    /// ScriptableObject profile that stores all global runtime and prebuild shader settings, master material references,
    /// and active B2UPaletteData bindings for the B2U rendering pipeline.
    /// </summary>
    [CreateAssetMenu(fileName = "New B2U Shader Profile", menuName = "B2U/Shader Profile")]
    public class B2UShaderProfile : ScriptableObject, ISerializationCallbackReceiver
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Runtime;

        /// <summary>
        /// The name of the profile
        /// </summary>
        public string profileName;
        /// <summary>
        /// The associated palette data for the profile
        /// </summary>
        public B2UPaletteData paletteData;

        /// <summary>
        /// The serialized data for the profile so it can persist over domain reloads.
        /// </summary>
        [SerializeField, HideInInspector]
        private List<B2USerializedData> savedData = new List<B2USerializedData>();

        /// <summary>
        /// A list of the master ubershader materials.
        /// </summary>
        [SerializeField, HideInInspector]
        public Material[] masterMaterials = new Material[7];

        /// <summary>
        /// A dictionary of the property blocks that store the data for processing all of the B2UShaderProperties.
        /// </summary>
        [NonSerialized]
        public Dictionary<B2UShaderProperty, B2UShaderPropertyBlock> propertyBlocks;

        private void Awake()
        {
#if !UNITY_EDITOR
            if (string.IsNullOrEmpty(name))
            {
                B2ULogError(msgSrc, "Blocked runtime instantiation of B2UShaderProfile. This object relies on Editor AssetDatabase functions to initialize and must be authored in the editor.", this);
                Destroy(this);
            }
#endif
        }

        private void OnEnable()
        {
            InitializeMasterMaterialsEditorOnly();
            InitializeDictionaries();

            if (paletteData != null)
            {
                GetPaletteTextures();
            }
        }

        /// <summary>
        /// Clears all serialized overrides and resets both runtime and editor property dictionaries to their default values.
        /// </summary>
        public void RestoreDefaults()
        {
            propertyBlocks = null;
            savedData.Clear();

            InitializeDictionaries();
            if (paletteData != null)
            {
                GetPaletteTextures();
            }

            SyncSavedData();

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Pulls all texture references and subtype counts from the assigned B2UPaletteData asset into this profile's runtime property blocks.
        /// </summary>
        public void GetPaletteTextures()
        {
            if (paletteData == null) return;
            InitializeDictionaries();

            if (paletteData.colorLUT != null)
                propertyBlocks[B2UShaderProperty.ColorEmissionLUT].SetValue(paletteData.colorLUT);

            if (paletteData.propertyLUT != null)
                propertyBlocks[B2UShaderProperty.PropertyLUT].SetValue(paletteData.propertyLUT);

            if (paletteData.iridescenceLUT != null)
                propertyBlocks[B2UShaderProperty.IridescenceLUT].SetValue(paletteData.iridescenceLUT);

            if (paletteData.emissionPatterns != null)
                propertyBlocks[B2UShaderProperty.EmissionPatterns].SetValue(paletteData.emissionPatterns);

            if (paletteData.detailAtlas != null)
                propertyBlocks[B2UShaderProperty.DetailMapAtlas].SetValue(paletteData.detailAtlas);

            if (paletteData.noise3D != null)
                propertyBlocks[B2UShaderProperty.NoiseTexture].SetValue(paletteData.noise3D);

            if (paletteData.paintPalette != null)
               propertyBlocks[B2UShaderProperty.PaintPalette].SetValue(paletteData.paintPalette);

            if (paletteData.numSubbiomes != 0)
               propertyBlocks[B2UShaderProperty.NumberOfSubbiomes].SetValue(paletteData.numSubbiomes);
        }

        /// <summary>
        /// Writes the texture references currently stored in this profile's runtime blocks back to the assigned B2UPaletteData asset.
        /// </summary>
        /// <param name="tex">Optional B2UShaderPropertyRuntime index to update a single texture, or -1 to update all palette textures.</param>
        public void SetPaletteTextures(int tex = -1)
        {
            if (paletteData == null) return;
            InitializeDictionaries();

            if (propertyBlocks.TryGetValue(B2UShaderProperty.ColorEmissionLUT, out var colorBlock) && (tex == (int)B2UShaderProperty.ColorEmissionLUT || tex == -1))
                paletteData.colorLUT = colorBlock.value as Texture2D;

            if (propertyBlocks.TryGetValue(B2UShaderProperty.PropertyLUT, out var propBlock) && (tex == (int)B2UShaderProperty.PropertyLUT || tex == -1))
                paletteData.propertyLUT = propBlock.value as Texture2D;

            if (propertyBlocks.TryGetValue(B2UShaderProperty.IridescenceLUT, out var iridBlock) && (tex == (int)B2UShaderProperty.IridescenceLUT || tex == -1))
                paletteData.iridescenceLUT = iridBlock.value as Texture2D;

            if (propertyBlocks.TryGetValue(B2UShaderProperty.EmissionPatterns, out var emisBlock) && (tex == (int)B2UShaderProperty.EmissionPatterns || tex == -1))
                paletteData.emissionPatterns = emisBlock.value as Texture2D;

            if (propertyBlocks.TryGetValue(B2UShaderProperty.DetailMapAtlas, out var detailBlock) && (tex == (int)B2UShaderProperty.DetailMapAtlas || tex == -1))
                paletteData.detailAtlas = detailBlock.value as Texture2DArray;

            if (propertyBlocks.TryGetValue(B2UShaderProperty.NoiseTexture, out var noiseBlock) && (tex == (int)B2UShaderProperty.NoiseTexture || tex == -1))
                paletteData.noise3D = noiseBlock.value as Texture3D;

            if (propertyBlocks.TryGetValue(B2UShaderProperty.PaintPalette, out var paintBlock) && (tex == (int)B2UShaderProperty.PaintPalette || tex == -1))
                paletteData.paintPalette = paintBlock.value as Texture2DArray;

#if UNITY_EDITOR
            EditorUtility.SetDirty(paletteData);
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Packs the current runtime propertyBlocks dictionary into the serialized savedData list immediately.
        /// </summary>
        public void SyncSavedData()
        {
            if (propertyBlocks == null) return;

            savedData.Clear();
            foreach (var kvp in propertyBlocks)
            {
                savedData.Add(PackData(kvp.Key.ToString(), kvp.Value));
            }
        }

        public void OnBeforeSerialize()
        {
            SyncSavedData();
        }

        public void OnAfterDeserialize()
        {
            // Unpacking is deferred to OnEnable() so Unity API calls (such as Shader.PropertyToID) execute safely on the main thread.
        }

        /// <summary>
        /// Automatically locates and assigns the 7 B2U master materials in the project if any slots are unassigned (Editor Only).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public void InitializeMasterMaterialsEditorOnly()
        {
#if UNITY_EDITOR
            string[] names = { "Opaque_U", "Opaque_DS_U", "Transparent_U", "Crystal_U", "Liquid_U", "Liquid_V_U", "Tess_Liquid_U" };
            bool wasModified = false;

            if (masterMaterials == null || masterMaterials.Length != names.Length)
            {
                Array.Resize(ref masterMaterials, names.Length);
                wasModified = true;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (masterMaterials[i] != null) continue;

                string[] guids = AssetDatabase.FindAssets($"{names[i]} t:Material");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    masterMaterials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                    wasModified = true;
                }
                else
                {
                    B2ULogWarning(B2UMessageSource.Editor, $"Could not find Master Material: {names[i]}", this);
                }
            }

            if (wasModified) EditorUtility.SetDirty(this);
#endif
        }

        private B2USerializedData PackData(string key, B2UShaderPropertyBlock block)
        {
            B2USerializedData data = new B2USerializedData { keyName = key };
            if (block == null || block.value == null) return data;

            switch (block.value)
            {
                case float f: data.fValue = f; break;
                case int i: data.iValue = i; break;
                case uint ui: data.iValue = (int)ui; break;
                case bool b: data.bValue = b; break;
                case Color c: data.cValue = c; break;
                case Vector2 v: data.vValue = v; break;
                case LayerMask l: data.lValue = l; break;
                case UnityEngine.Object o: data.oValue = o; break;
            }

            return data;
        }

        /// <summary>
        /// Restores serialized property values into the active runtime and editor dictionaries.
        /// </summary>
        public void RestoreSavedValues()
        {
            if (propertyBlocks == null)
            {
                InitializeDictionaries();
                return;
            }

            ApplySavedDataToBlocks();
        }

        private void ApplySavedDataToBlocks()
        {
            if (savedData == null || propertyBlocks == null) return;

            for (int i = 0; i < savedData.Count; i++)
            {
                B2USerializedData data = savedData[i];
                if (data != null && Enum.TryParse(data.keyName, out B2UShaderProperty key))
                {
                    if (propertyBlocks.TryGetValue(key, out B2UShaderPropertyBlock block))
                    {
                        UnpackData(data, block);
                    }
                }
            }
        }

        private void UnpackData(B2USerializedData data, B2UShaderPropertyBlock block)
        {
            if (block.type == typeof(float)) block.SetValue(data.fValue);
            else if (block.type == typeof(int)) block.SetValue(data.iValue);
            else if (block.type == typeof(uint)) block.SetValue((uint)Mathf.Max(0, data.iValue));
            else if (block.type == typeof(bool)) block.SetValue(data.bValue);
            else if (block.type == typeof(Color)) block.SetValue(data.cValue);
            else if (block.type == typeof(Vector2)) block.SetValue(data.vValue);
            else if (block.type == typeof(LayerMask)) block.SetValue(data.lValue);
            else if (typeof(UnityEngine.Object).IsAssignableFrom(block.type))
            {
                // Normalize Unity fake-null wrappers to true C# null before passing to SetValue(object)
                UnityEngine.Object objVal = data.oValue != null ? data.oValue : null;
                block.SetValue(objVal);
            }
        }

        /// <summary>
        /// Allocates and populates the default runtime and editor shader property block dictionaries.
        /// </summary>
        public void InitializeDictionaries()
        {
            if (propertyBlocks != null /*&& editorPropertyBlocks != null*/) return;

            propertyBlocks = new Dictionary<B2UShaderProperty, B2UShaderPropertyBlock>
            {
                [B2UShaderProperty.BiomeTexture1] = new B2UShaderPropertyBlock("_B2U_Biome_Texture_1", typeof(Texture2D)),
                [B2UShaderProperty.BiomeTexture2] = new B2UShaderPropertyBlock("_B2U_Biome_Texture_2", typeof(Texture2D)),
                [B2UShaderProperty.ColorEmissionLUT] = new B2UShaderPropertyBlock("_B2U_Color_Emission_LUT", typeof(Texture2D)),
                [B2UShaderProperty.PropertyLUT] = new B2UShaderPropertyBlock("_B2U_Property_LUT", typeof(Texture2D)),
                [B2UShaderProperty.IridescenceLUT] = new B2UShaderPropertyBlock("_B2U_Iridescence_LUT", typeof(Texture2D)),
                [B2UShaderProperty.EmissionPatterns] = new B2UShaderPropertyBlock("_B2U_Emission_Patterns", typeof(Texture2D)),
                [B2UShaderProperty.DetailMapAtlas] = new B2UShaderPropertyBlock("_B2U_Detail_Map_Atlas", typeof(Texture2DArray)),
                [B2UShaderProperty.NoiseTexture] = new B2UShaderPropertyBlock("_B2U_3D_Noise_Texture", typeof(Texture3D)),
                [B2UShaderProperty.PaintPalette] = new B2UShaderPropertyBlock("_B2U_Paint_Palette", typeof(Texture2DArray)),
                [B2UShaderProperty.NumberOfSubbiomes] = new B2UShaderPropertyBlock("_B2U_Number_of_Biome_Subtypes", typeof(int), 3, new Vector2(1, 7)),
                [B2UShaderProperty.WorldSize] = new B2UShaderPropertyBlock("_B2U_World_Size", typeof(Vector2), new Vector2(2000f, 2000f)),
                [B2UShaderProperty.GlobalEmissionStrength] = new B2UShaderPropertyBlock("_B2U_Global_Emission_Strength", typeof(float), 1f, new Vector2(0f, 10f)),
                [B2UShaderProperty.GlobalEmissionSpeed] = new B2UShaderPropertyBlock("_B2U_Global_Emission_Speed", typeof(float), 1f, new Vector2(0f, 10f)),
                [B2UShaderProperty.GlobalCracksUseEmissionColor] = new B2UShaderPropertyBlock("_B2U_CRACKS_USE_EMISSION_COLOR", typeof(bool), true),
                [B2UShaderProperty.GlobalCrackColor] = new B2UShaderPropertyBlock("_B2U_Global_Crack_Color", typeof(Color), new Color(1f, 0f, 0f)),
                [B2UShaderProperty.GlobalCrackBrightness] = new B2UShaderPropertyBlock("_B2U_Global_Crack_Brightness", typeof(float), 1f),
                [B2UShaderProperty.TriplanarBlendPower] = new B2UShaderPropertyBlock("_B2U_Triplanar_Blend_Power", typeof(float), 10f, new Vector2(0.1f, 20f)),
                [B2UShaderProperty.SolidNoiseScale] = new B2UShaderPropertyBlock("_B2U_Global_Noise_Scale", typeof(float), 1f, new Vector2(0.1f, 10f)),
                [B2UShaderProperty.SolidNoiseStrength] = new B2UShaderPropertyBlock("_B2U_Global_Noise_Strength", typeof(float), 0.5f, new Vector2(0f, 2f)),
                [B2UShaderProperty.SolidHardEdgeColor] = new B2UShaderPropertyBlock("_B2U_Hard_Edge_Color", typeof(Color), new Color(1f, 1f, 1f)),
                [B2UShaderProperty.SolidEdgeStrength] = new B2UShaderPropertyBlock("_B2U_Global_Edge_Strength", typeof(float), 1f, new Vector2(0f, 1f)),
                [B2UShaderProperty.SolidEdgeThickness] = new B2UShaderPropertyBlock("_B2U_Global_Edge_Thickness", typeof(float), 1f, new Vector2(0f, 5f)),
                [B2UShaderProperty.SolidEdgePower] = new B2UShaderPropertyBlock("_B2U_Global_Edge_Power", typeof(float), 1f, new Vector2(0.1f, 5f)),
                [B2UShaderProperty.SolidCrackScale] = new B2UShaderPropertyBlock("_B2U_Global_Crack_Scale", typeof(float), 10f, new Vector2(0f, 30f)),
                [B2UShaderProperty.LiquidBubbleMultiplier] = new B2UShaderPropertyBlock("_B2U_Global_Bubble_Multiplier", typeof(float), 0.1f, new Vector2(0f, 0.5f)),
                [B2UShaderProperty.LiquidAngleOffsetSpeed] = new B2UShaderPropertyBlock("_B2U_Global_Angle_Offset_Speed", typeof(float), 1f, new Vector2(0f, 5f)),
                [B2UShaderProperty.LiquidRefractionDistortionStrength] = new B2UShaderPropertyBlock("_B2U_Global_Distortion_Strength", typeof(float), 1f, new Vector2(0f, 5f)),
                [B2UShaderProperty.LiquidShoreDistance] = new B2UShaderPropertyBlock("_B2U_Global_Edge_Distance", typeof(float), 1f, new Vector2(0f, 2f)),
                [B2UShaderProperty.LiquidNormalStrength] = new B2UShaderPropertyBlock("_B2U_Global_Liquid_Normal_Strength", typeof(float), 0.1f, new Vector2(0f, 0.5f)),
                [B2UShaderProperty.LiquidCausticStrength] = new B2UShaderPropertyBlock("_B2U_Global_Caustic_Strength", typeof(float), 1f, new Vector2(0f, 2f)),
                [B2UShaderProperty.TessFactor] = new B2UShaderPropertyBlock("_B2U_Tess", typeof(int), 5, new Vector2(1, 20)),
                [B2UShaderProperty.MinTessDistance] = new B2UShaderPropertyBlock("_B2U_Min_Tessellation_Distance", typeof(float), 5f),
                [B2UShaderProperty.MaxTessDistance] = new B2UShaderPropertyBlock("_B2U_Max_Tessellation_Distance", typeof(float), 25f),
                [B2UShaderProperty.CrystalCrackScale] = new B2UShaderPropertyBlock("_B2U_Global_Crack_Scale_Crystal", typeof(float), 20f, new Vector2(0f, 30f)),
                [B2UShaderProperty.UseNoiseForSolids] = new B2UShaderPropertyBlock("_B2U_USE_WORLD_SPACE_NOISE", typeof(bool), true),
                [B2UShaderProperty.UseProceduralNoiseForSolids] = new B2UShaderPropertyBlock("_B2U_USE_PROCEDURAL_NOISE_SOLID", typeof(bool), false),
                [B2UShaderProperty.UseProceduralNoiseForLiquids] = new B2UShaderPropertyBlock("_B2U_USE_PROCEDURAL_NOISE_LIQUID", typeof(bool), true),
                [B2UShaderProperty.UseBubbleAdvection] = new B2UShaderPropertyBlock("_B2U_USE_BUBBLE_ADVECTION", typeof(bool), false),
                [B2UShaderProperty.UseAngleOffset] = new B2UShaderPropertyBlock("_B2U_ANGLE_OFFSET", typeof(bool), false),
                [B2UShaderProperty.ChromaticAbberationForLiquids] = new B2UShaderPropertyBlock("_B2U_LIQUID_CHROMATIC_ABBERATION", typeof(bool), true),
                [B2UShaderProperty.LiquidsHaveVertexDisplacement] = new B2UShaderPropertyBlock("_B2U_USE_DISPLACEMENT", typeof(bool), true),
                [B2UShaderProperty.TessellatedLiquidsResampleInFragment] = new B2UShaderPropertyBlock("_B2U_TESS_RESAMPLE_FRAGMENT", typeof(bool), true),
                [B2UShaderProperty.IridescenceMode] = new B2UShaderPropertyBlock("_B2U_IRIDESCENCE", typeof(uint), 0u, new Vector2(0, 2), new string[] { "_B2U_IRIDESCENCE_AMBIENT", "_B2U_IRIDESCENCE_MAIN_LIGHT", "_B2U_IRIDESCENCE_ALL_LIGHTS" }),
                [B2UShaderProperty.CrystalFlareMode] = new B2UShaderPropertyBlock("_B2U_CRYSTAL_FLARES", typeof(uint), 1u, new Vector2(0, 2), new string[] { "_B2U_CRYSTAL_FLARES_AMBIENT", "_B2U_CRYSTAL_FLARES_MAIN_LIGHT", "_B2U_CRYSTAL_FLARES_ALL_LIGHTS" }),
                [B2UShaderProperty.CausticMode] = new B2UShaderPropertyBlock("_B2U_CAUSTICS", typeof(uint), 1u, new Vector2(0, 2), new string[] { "_B2U_CAUSTICS_OFF", "_B2U_CAUSTICS_MAIN_LIGHT", "_B2U_CAUSTICS_ALL_LIGHTS" }),
                [B2UShaderProperty.RefractionQualityMode] = new B2UShaderPropertyBlock("_B2U_REFRACTION_QUALITY", typeof(uint), 1u, new Vector2(0, 3), new string[] { "_B2U_REFRACTION_QUALITY_LOW", "_B2U_REFRACTION_QUALITY_MEDIUM", "_B2U_REFRACTION_QUALITY_HIGH" }),
                [B2UShaderProperty.EdgeDetectionBufferResolution] = new B2UShaderPropertyBlock("EdgeDetectionBufferResolution", typeof(uint), 1u, new Vector2(0, 3)),
                [B2UShaderProperty.EdgeDetectionSize] = new B2UShaderPropertyBlock("_B2U_Edge_Detection_Size", typeof(float), 0.25f),
                [B2UShaderProperty.EdgeDetectionSensitivity] = new B2UShaderPropertyBlock("_B2U_Edge_Detection_Sensitivity", typeof(float), 0.1f, new Vector2(0f, 5f)),
                [B2UShaderProperty.EdgeDetectionColor] = new B2UShaderPropertyBlock("_B2U_Edge_Detection_Color", typeof(Color), new Color(0f, 0f, 0f)),
                [B2UShaderProperty.EdgeDetectionLayermask] = new B2UShaderPropertyBlock("EdgeDetectionLayermask", typeof(LayerMask), (LayerMask)(-1)),
                [B2UShaderProperty.BackfaceBufferResolution] = new B2UShaderPropertyBlock("BackfaceBufferResolution", typeof(uint), 1u, new Vector2(0, 3)),
                [B2UShaderProperty.ColorCopyResolution] = new B2UShaderPropertyBlock("ColorCopyResolution", typeof(uint), 1u, new Vector2(0, 3)),
                [B2UShaderProperty.LightModel] = new B2UShaderPropertyBlock("_B2U_LIGHT_MODEL", typeof(uint), 0u, new Vector2(0, 7), new string[] { "_B2U_LIGHT_MODEL_URP", "_B2U_LIGHT_MODEL_LIGHTWEIGHT", "_B2U_LIGHT_MODEL_TOON", "_B2U_LIGHT_MODEL_ANTILIGHT", "_B2U_LIGHT_MODEL_RETRO", "_B2U_LIGHT_MODEL_GOOCH", "_B2U_LIGHT_MODEL_FACETED", "_B2U_LIGHT_MODEL_POPART" }),
                [B2UShaderProperty.LightControlFloat] = new B2UShaderPropertyBlock("_B2U_Light_Control_Float", typeof(float), 0.5f),
                [B2UShaderProperty.ImmersionActive] = new B2UShaderPropertyBlock("ImmersionActive", typeof(bool), false),
                [B2UShaderProperty.ImmersionTransitionEnabled] = new B2UShaderPropertyBlock("_B2U_IMMERSION_USE_TRANSITION", typeof(bool), true),
                [B2UShaderProperty.ImmersionTransitionDistance] = new B2UShaderPropertyBlock("_B2U_Immersion_Transition_Height", typeof(float), 1f),
                [B2UShaderProperty.ImmersionClarity] = new B2UShaderPropertyBlock("_B2U_Immersion_Clarity_Multiplier", typeof(float), 5f),
                [B2UShaderProperty.ImmersionDistortion] = new B2UShaderPropertyBlock("_B2U_Immersion_Distortion_Strength", typeof(float), 0.005f, new Vector2(0f, 0.02f)),
                [B2UShaderProperty.ImmersionCausticStrength] = new B2UShaderPropertyBlock("_B2U_Immersion_Caustic_Multiplier", typeof(float), 0.2f),
                [B2UShaderProperty.RecolorActive] = new B2UShaderPropertyBlock("RecolorActive", typeof(bool), false),
                [B2UShaderProperty.RecolorBlend] = new B2UShaderPropertyBlock("_B2U_Recolor_Blend", typeof(float), 0f, new Vector2(0f, 1f)),
                [B2UShaderProperty.RecolorPresets] = new B2UShaderPropertyBlock("RecolorPresets", typeof(B2URecolorPresetCollection), null),
                [B2UShaderProperty.StylizeActive] = new B2UShaderPropertyBlock("StylizeActive", typeof(bool), false),
                [B2UShaderProperty.StylizeBlend] = new B2UShaderPropertyBlock("_B2U_Stylize_Blend", typeof(float), 0f, new Vector2(0f, 1f)),
                [B2UShaderProperty.StylizePresets] = new B2UShaderPropertyBlock("StylizePresets", typeof(B2UStylizePresetCollection), null),
                [B2UShaderProperty.StatusActive] = new B2UShaderPropertyBlock("StatusActive", typeof(bool), false),
                [B2UShaderProperty.StatusPresets] = new B2UShaderPropertyBlock("StatusPresets", typeof(B2UStatusEffectPresetCollection), null),
                [B2UShaderProperty.StatusStrength] = new B2UShaderPropertyBlock("_B2U_Status_Effect_Strength", typeof(float), 0f, new Vector2(0f, 1f)),
                [B2UShaderProperty.InjuryStrength] = new B2UShaderPropertyBlock("_B2U_Status_Effect_Injury_Strength", typeof(float), 0f, new Vector2(0f, 1f)),
                [B2UShaderProperty.InjuryColor] = new B2UShaderPropertyBlock("_B2U_Status_Effect_Injury_Color", typeof(Color), new Color(1f, 0f, 0f)),
                [B2UShaderProperty.InjuryPulseSpeed] = new B2UShaderPropertyBlock("_B2U_Status_Effect_Injury_Pulse_Speed", typeof(float), 1f),
                [B2UShaderProperty.InjuryVignetteStrength] = new B2UShaderPropertyBlock("_B2U_Status_Effect_Injury_Vignette_Strength", typeof(float), 0.5f, new Vector2(0f, 1f)),
                [B2UShaderProperty.InjuryDarkenAmount] = new B2UShaderPropertyBlock("_B2U_Status_Effect_Injury_Darken_Amount", typeof(float), 0.5f, new Vector2(0f, 1f)),
                [B2UShaderProperty.EdgeDetectionEnabled] = new B2UShaderPropertyBlock("EdgeDetectionEnabled", typeof(bool), false),
                [B2UShaderProperty.VolumetricRefractionEnabled] = new B2UShaderPropertyBlock("VolumetricRefractionEnabled", typeof(bool), true),
                [B2UShaderProperty.FullscreenEffectsEnabled] = new B2UShaderPropertyBlock("FullscreenEffectsEnabled", typeof(bool), true),
                [B2UShaderProperty.EdgeDetectionMode] = new B2UShaderPropertyBlock("_B2U_EDGE_DETECTION_DEPTH", typeof(bool), true),
                [B2UShaderProperty.EdgeDetectionSpace] = new B2UShaderPropertyBlock("_B2U_EDGE_DETECTION_WORLD_SPACE", typeof(bool), true),
                [B2UShaderProperty.VolumetricLayer] = new B2UShaderPropertyBlock("VolumetricLayer", typeof(int), 0, new Vector2(0, 31)),
                [B2UShaderProperty.VertexRGamma] = new B2UShaderPropertyBlock("_B2U_GRADIENT_GAMMA", typeof(bool), true),
                [B2UShaderProperty.VertexGGamma] = new B2UShaderPropertyBlock("_B2U_EMISSION_GAMMA", typeof(bool), true),
                [B2UShaderProperty.VertexBGamma] = new B2UShaderPropertyBlock("_B2U_PAINT_GAMMA", typeof(bool), true),
                [B2UShaderProperty.SolidsHaveSmoothEdges] = new B2UShaderPropertyBlock("_B2U_SMOOTH_EDGES", typeof(bool), true),
                [B2UShaderProperty.DetailMapAffectsHardEdges] = new B2UShaderPropertyBlock("_B2U_HARD_EDGE_DETAIL", typeof(bool), false),
                [B2UShaderProperty.LiquidEmissionHasFlow] = new B2UShaderPropertyBlock("_B2U_EMISSION_HAS_FLOW", typeof(bool), true),
                [B2UShaderProperty.EdgeMode] = new B2UShaderPropertyBlock("_B2U_EDGE_MODE", typeof(uint), 0u, new Vector2(0, 2), new string[] { "_B2U_EDGE_MODE_BOTH", "_B2U_EDGE_MODE_CONVEX_ONLY", "_B2U_EDGE_MODE_CONCAVE_ONLY" }),
                [B2UShaderProperty.EdgeStyle] = new B2UShaderPropertyBlock("_B2U_EDGE_STYLE", typeof(uint), 0u, new Vector2(0, 2), new string[] { "_B2U_EDGE_STYLE_SOFT_EDGE", "_B2U_EDGE_STYLE_HARD_EDGE", "_B2U_EDGE_STYLE_COLORED_HARD_EDGE" })
            };
            ApplySavedDataToBlocks();
        }
    }
}