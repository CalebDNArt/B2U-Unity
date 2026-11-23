using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static B2U.B2UDebug;
using static B2U.B2URuntimeUtility;

namespace B2U
{
    /// <summary>
    /// Per-object component that manages and encodes Renderer Shader User Values (RSUV) for B2U Solid, Liquid, and Crystal meshes,
    /// provides optical LUT sampling for fullscreen liquid immersion, and evaluates 3D liquid flow physics.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class B2URSUVMono : MonoBehaviour
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Runtime;

        /// <summary>
        /// Global shader ID for liquid surface Y.
        /// </summary>
        public static readonly int liquidSurfaceYID = Shader.PropertyToID("_B2U_Immersion_Liquid_Surface_Y");
        /// <summary>
        /// Global shader ID for the immersion effect's optical properties Vector4.
        /// </summary>
        public static readonly int opticalPropertiesID = Shader.PropertyToID("_B2U_Immersion_Optical_Properties");
        /// <summary>
        /// Global shader ID for the immersion effect's surface and scale Vector4.
        /// </summary>
        public static readonly int surfaceAndScaleID = Shader.PropertyToID("_B2U_Immersion_Surface_and_Scale");
        /// <summary>
        /// Global shader ID for the immersion effect's depth and is transmissive Vector4.
        /// </summary>
        public static readonly int depthAndisTransmissiveID = Shader.PropertyToID("_B2U_Immersion_Depth_and_isTransmissive");

        /// <summary>
        /// An enum for storing which class of B2URSUV the object has.
        /// </summary>
        public enum RSUVType { Unassigned, Solid, Liquid, Crystal }
        /// <summary>
        /// The RSUV type on this object
        /// </summary>
        public RSUVType activeType = RSUVType.Unassigned;
        /// <summary>
        /// The 0-255 value that represents which row in the LUTs the material starts on. Mostly used for liquids, is not accurate for solids with multiple materials painted on them.
        /// </summary>
        public int materialID = int.MaxValue;
        /// <summary>
        /// The B2URSUV object that stores the data and pushes it to the renderer.
        /// </summary>
        public B2URSUV rsuvObject = null;

        /// <summary>
        /// The settings for a B2USolidRSUV.
        /// </summary>
        [Serializable]
        public class SolidSettings
        {
            /// <summary>
            /// Which palette slice to use from the active B2UPaletteData's Paint Palette Texture2DArray
            /// </summary>
            [Range(0, 15)] public int paletteIndex = 0;
            /// <summary>
            /// A flattened index of the pixel coordinate in the palette slice.
            /// </summary>
            [Range(0, 127)] public int paintIndex = 0;
            /// <summary>
            /// The emission strength for the solid.
            /// </summary>
            [Range(0f, 1f)] public float emissionStrength = 1f;
            /// <summary>
            /// Offsets the global noise texture in object space. Setting this lets you instantiate many of the same object without them looking perfectly identical.
            /// </summary>
            [Range(0, 63)] public int noiseOffset = 0;
            /// <summary>
            /// Whether or not the mesh is painted.
            /// </summary>
            /// <remarks>Even with this on, the paint will only be applied to areas where the paint mask was set in Blender.</remarks>
            public bool isPainted = false;
            /// <summary>
            /// The intensity of the procedural cracks on the solid.
            /// </summary>
            [Range(0f, 1f)] public float crackIntensity = 0f;
        }

        /// <summary>
        /// The settings for a B2ULiquidRSUV.
        /// </summary>
        [Serializable]
        public class LiquidSettings
        {
            /// <summary>
            /// The emission strength for the liquid.
            /// </summary>
            [Range(0f, 1f)] public float emissionStrength = 1f;
            /// <summary>
            /// The flow strength for the liquid. Can be set to negative to make a liquid flow backwards.
            /// </summary>
            [Range(-1f, 1f)] public float flowStrength = 1f;
            /// <summary>
            /// The wave height for the liquid. Can be set to negative to make the waves inverted.
            /// </summary>
            [Range(-1f, 1f)] public float waveMultiplier = 1f;
        }

        /// <summary>
        /// The settings for a B2UCrystalRSUV.
        /// </summary>
        [Serializable]
        public class CrystalSettings
        {
            /// <summary>
            /// The emission strength for the crystal.
            /// </summary>
            [Range(0f, 1f)] public float emissionStrength = 1f;
            /// <summary>
            /// A multiplier for the transmission of the crystal (transparency).
            /// </summary>
            [Range(0f, 1f)] public float transmissionMultiplier = 1f;
            /// <summary>
            /// A multiplier for the absorption of the crystal (how strongly the crystal tints what is seen through it).
            /// </summary>
            [Range(0f, 1f)] public float absorptionMultiplier = 1f;
            /// <summary>
            /// The intensity of the procedural cracks on the crystal.
            /// </summary>
            [Range(0f, 1f)] public float crackIntensity = 0f;
        }

        /// <summary>
        /// The solid settings for this component.
        /// </summary>
        public SolidSettings solid = new SolidSettings();
        /// <summary>
        /// The liquid settings for this component.
        /// </summary>
        public LiquidSettings liquid = new LiquidSettings();
        /// <summary>
        /// The crystal settings for this component.
        /// </summary>
        public CrystalSettings crystal = new CrystalSettings();

        /// <summary>
        /// The active renderer on the object.
        /// </summary>
        [NonSerialized] private Renderer cachedRenderer;
        /// <summary>
        /// The mesh renderer (if applicable) on the object.
        /// </summary>
        [NonSerialized] private MeshRenderer cachedMeshRenderer;
        /// <summary>
        /// The skinned mesh renderer (if applicable) on the object.
        /// </summary>
        [NonSerialized] private SkinnedMeshRenderer cachedSkinnedRenderer;

        #region Flow Data
        // These are static so when multiple liquids cache data, they don't allocate multiple vertex data buffers on the heap.
        /// <summary>
        /// The weights of the 8 biomes. Static and overwritten to prevent excessive heap allocation and garbage collection.
        /// </summary>
        private static readonly float[] s_BiomeWeights = new float[8];
        /// <summary>
        /// A copy of the vertex colors on the mesh asset. Static and overwritten to prevent excessive heap allocation and garbage collection.
        /// </summary>
        private static readonly List<Color32> s_TempColors = new List<Color32>();
        /// <summary>
        /// A copy of the UVs on the mesh asset. Static and overwritten to prevent excessive heap allocation and garbage collection.
        /// </summary>
        private static readonly List<Vector2> s_TempUVs = new List<Vector2>();

        // Native buffers for Burst-accelerated liquid flow queries
        /// <summary>
        /// A cached Burst-Compatible array that stores the 3D flow vectors in world space.
        /// </summary>
        private NativeArray<float3> liquidFlow3D;
        /// <summary>
        /// A cache Burst-Compatible array that stores the vertex positions in world space.
        /// </summary>
        private NativeArray<float3> worldPositions;
        /// <summary>
        /// Whether the liquid's mesh data is cached or not.
        /// </summary>
        private bool liquidMeshCached = false;
        /// <summary>
        /// The min/max value for flow on the current B2UPaletteData's schema.
        /// </summary>
        private static Vector2 flowMinMax = Vector2.zero;
        /// <summary>
        /// The min/max value fof viscosity on the current B2UPaletteData's schema. 
        /// </summary>
        private Vector2 viscosityMinMax = Vector2.zero;
        #endregion

        private void OnEnable()
        {
            ApplyRSUV();
        }

        private void OnDisable()
        {
            DisposeLiquidFlowBuffers();
        }

        private void OnDestroy()
        {
            DisposeLiquidFlowBuffers();
        }

        private void Reset()
        {
            AutoDetectType();
            ApplyRSUV();
        }

        /// <summary>
        /// Disposes allocated flow buffers to prevent memory leaks.
        /// </summary>
        private void DisposeLiquidFlowBuffers()
        {
            if (liquidFlow3D.IsCreated) liquidFlow3D.Dispose();
            if (worldPositions.IsCreated) worldPositions.Dispose();
            liquidMeshCached = false;
        }

        /// <summary>
        /// Ensures that a renderer exists on the object or tries to find it.
        /// </summary>
        private void EnsureCachedRenderer()
        {
            if (cachedRenderer != null) return;
            if (TryGetComponent(out cachedMeshRenderer))
            {
                cachedRenderer = cachedMeshRenderer;
            }
            else if (TryGetComponent(out cachedSkinnedRenderer))
            {
                cachedRenderer = cachedSkinnedRenderer;
            }
        }

        /// <summary>
        /// Gets the shared mesh from the object's renderer.
        /// </summary>
        /// <returns>The shared mesh asset.</returns>
        private Mesh GetTargetSharedMesh()
        {
            if (TryGetComponent<MeshFilter>(out MeshFilter filter) && filter.sharedMesh != null)
            {
                return filter.sharedMesh;
            }
            EnsureCachedRenderer();
            if (cachedSkinnedRenderer != null && cachedSkinnedRenderer.sharedMesh != null)
            {
                return cachedSkinnedRenderer.sharedMesh;
            }
            return null;
        }

        /// <summary>
        /// Samples the active Color LUT (and world-space biome maps if biome-responsive) to resolve the liquid's surface color, depth color, and viscosity.
        /// </summary>
        /// <returns>True if the liquid color can be retrieved.</returns>
        private bool GetLiquidColors(out Color32 surfaceColor, out Color32 depthColor, out float viscosity)
        {
            surfaceColor = Color.white;
            depthColor = Color.black;
            viscosity = 0f;

            if (!CheckActivePalette(this)) return false;

            B2UShaderProfile profile = ActiveProfile;
            if (profile.propertyBlocks == null) return false;

            if (!profile.propertyBlocks.TryGetValue(B2UShaderProperty.ColorEmissionLUT, out var colorBlock))
            {
                B2ULogError(msgSrc, "Could not retrieve the color LUT from the active profile.", this);
                return false;
            }

            Texture2D colorLUT = colorBlock.value as Texture2D;
            if (colorLUT == null)
            {
                B2ULogError(msgSrc, "The color LUT on the active profile is null.", this);
                return false;
            }

            int numSubtypes = Mathf.Max(1, profile.paletteData.numSubbiomes);
            int lutWidth = (numSubtypes * 8) + 8;

            if (materialID < 0) // Biome-Responsive Liquid
            {
                if (!profile.propertyBlocks.TryGetValue(B2UShaderProperty.BiomeTexture1, out var biome1Block) || !(biome1Block.value is Texture2D biomeTex1))
                {
                    B2ULogError(msgSrc, "Biome texture 1 on the active profile is null or missing.", this);
                    return false;
                }
                if (!profile.propertyBlocks.TryGetValue(B2UShaderProperty.BiomeTexture2, out var biome2Block) || !(biome2Block.value is Texture2D biomeTex2))
                {
                    B2ULogError(msgSrc, "Biome texture 2 on the active profile is null or missing.", this);
                    return false;
                }

                Camera mainCam = Camera.main;
                Vector3 samplePos = mainCam != null ? mainCam.transform.position : transform.position;
                Vector2 worldSize = (Vector2)profile.propertyBlocks[B2UShaderProperty.WorldSize].value;
                if (Mathf.Abs(worldSize.x) < 0.0001f || Mathf.Abs(worldSize.y) < 0.0001f)
                {
                    worldSize = Vector2.one;
                }

                int matID = Mathf.Abs(materialID);
                Vector2 biomeUV = (new Vector2(samplePos.x, samplePos.z) / worldSize) + new Vector2(0.5f, 0.5f);

                Color t1 = biomeTex1.GetPixelBilinear(biomeUV.x, biomeUV.y);
                Color t2 = biomeTex2.GetPixelBilinear(biomeUV.x, biomeUV.y);

                float colorV = (matID + 0.5f) / 256f;

                s_BiomeWeights[0] = t1.r;
                s_BiomeWeights[1] = t1.g;
                s_BiomeWeights[2] = t1.b;
                s_BiomeWeights[3] = t1.a;
                s_BiomeWeights[4] = t2.r;
                s_BiomeWeights[5] = t2.g;
                s_BiomeWeights[6] = t2.b;

                float totalNatWeight = t1.r + t1.g + t1.b + t1.a + t2.r + t2.g + t2.b;
                s_BiomeWeights[7] = Mathf.Clamp01(1f - totalNatWeight);

                float maxSubtype = Mathf.Max(1f, numSubtypes) - 1f;
                float subtypeIndex = Mathf.Clamp01(t2.a) * maxSubtype;

                Color accumSurface = Color.clear;
                Color accumDepth = Color.clear;

                for (int i = 0; i < 8; i++)
                {
                    float w = s_BiomeWeights[i];
                    if (w > 0.01f)
                    {
                        float xColorIndex = (i * numSubtypes) + subtypeIndex;
                        float xColorU = (xColorIndex + 0.5f) / lutWidth;

                        accumSurface += colorLUT.GetPixelBilinear(xColorU, colorV) * w;
                        accumDepth += colorLUT.GetPixelBilinear(xColorU, colorV + (1f / 256f)) * w;
                    }
                }

                accumSurface.a = 1f;
                accumDepth.a = 1f;
                surfaceColor = accumSurface;
                depthColor = accumDepth;
                viscosity = colorLUT.GetPixel(lutWidth - 1, matID + 1).a;
                return true;
            }
            else // Biome-Independent Liquid
            {
                surfaceColor = colorLUT.GetPixel(0, materialID);
                depthColor = colorLUT.GetPixel(0, materialID + 1);
                viscosity = colorLUT.GetPixel(lutWidth - 1, materialID + 1).a;
                return true;
            }
        }

        /// <summary>
        /// Reads this liquid's optical and color parameters from the active LUTs and pushes them to the fullscreen immersion shader globals.
        /// </summary>
        public void PushLiquidToImmersionEffect()
        {
            if (activeType != RSUVType.Liquid)
            {
                B2ULogError(msgSrc, "The RSUVMono object you are calling this on is not a liquid object.", this);
                return;
            }
            if (materialID == int.MaxValue)
            {
                UpdateMaterialID();
                if (materialID == int.MaxValue)
                {
                    B2ULogError(msgSrc, "The RSUVMono object you are calling this on has an invalid material ID.", this);
                    return;
                }
            }

            if (!CheckActiveProfile(this)) return;
            B2UShaderProfile profile = ActiveProfile;
            if (profile.propertyBlocks == null) return;

            if (profile.propertyBlocks.TryGetValue(B2UShaderProperty.PropertyLUT, out var propBlock))
            {
                Texture2D propLUT = propBlock.value as Texture2D;
                if (propLUT == null)
                {
                    B2ULogError(msgSrc, "The property LUT on the active profile is null.", this);
                    return;
                }

                int matID = Mathf.Abs(materialID);
                Color row1Pixel0 = propLUT.GetPixel(0, matID);
                Color opticalPixel = propLUT.GetPixel(0, matID + 1);

                float transmission = opticalPixel.r;
                float absorption = opticalPixel.g * 5f;
                float ior = opticalPixel.b + 1f;
                float noiseScale = row1Pixel0.r * 10f;
                bool isTransmissive = transmission > 0f;

                if (GetLiquidColors(out Color32 surfaceColor, out Color32 depthColor, out float viscosity))
                {
                    Shader.SetGlobalFloat(liquidSurfaceYID, transform.position.y);
                    Shader.SetGlobalVector(opticalPropertiesID, new Vector4(transmission, absorption, ior, viscosity));
                    Shader.SetGlobalVector(surfaceAndScaleID, new Vector4(surfaceColor.r / 255f, surfaceColor.g / 255f, surfaceColor.b / 255f, noiseScale));
                    Shader.SetGlobalVector(depthAndisTransmissiveID, new Vector4(depthColor.r / 255f, depthColor.g / 255f, depthColor.b / 255f, isTransmissive ? 1f : 0f));
                }
                else B2ULogError(msgSrc, "Could not retrieve liquid colors.", this);
            }
            else
            {
                B2ULogError(msgSrc, "Could not retrieve the property LUT from the active profile.", this);
            }
        }

        /// <summary>
        /// Gets a world-space Vector3 that represents the direction and strength the liquid is flowing in,
        /// plus any active vertical wave displacement at that position.
        /// </summary>
        /// <param name="position">The position of the object being pushed by the liquid.</param>
        /// <param name="interactionRange">The radius of the interaction sphere that blends different flow vectors together.</param>
        /// <param name="includeWaves">If true, adds vertical displacement from any active dynamic waves on this liquid mesh.</param>
        /// <returns>The raw liquid push vector. Multiply by Time.deltaTime or Time.fixedDeltaTime when applying as a translation (not needed if adding as a force).</returns>
        /// <example>
        /// <code>
        /// public Transform floatingObject;
        /// public B2URSUVMono liquidRSUVMono;
        /// public Rigidbody floatingRigidbody;
        /// 
        /// private void Update()
        /// {
        ///     // Calculates the net flow for a 5 unit radius around the given position, adds vertical displacement from instantiated waves.
        ///     Vector3 flow = liquidRSUVMono.GetLiquidPush(floatingObject.position, 5f, true);
        ///     
        ///     // For a non rigidbody
        ///     floatingObject.position += flow * Time.deltaTime;
        ///     
        ///     // For a rigidbody
        ///     floatingRigidbody.AddForce(flow);
        /// }
        /// </code>
        /// </example>
        public Vector3 GetLiquidPush(Vector3 position, float interactionRange = 5f, bool includeWaves = true)
        {
            if (!CheckPaletteMaterials(this)) return Vector3.zero;
            if (!liquidMeshCached) CacheLiquidMeshData();
            if (!liquidMeshCached) return Vector3.zero;

            if (materialID == int.MaxValue) UpdateMaterialID();
            if (materialID == int.MaxValue) return Vector3.zero;

            B2UPaletteData palette = ActiveProfile.paletteData;

            if (flowMinMax == Vector2.zero)
            {
                flowMinMax = palette.TryGetMinMax(B2USlotKey.r1_p2_g, B2UWorkflow.Liquid);
            }

            int absMatID = Mathf.Clamp(Mathf.Abs(materialID), 0, 255);
            B2UMaterial mat = palette.materials.GetMaterialByIndex((byte)absMatID);
            if (mat == null && absMatID >= 2)
            {
                mat = palette.materials.GetMaterialByIndex((byte)(absMatID - 2));
            }
            if (mat == null) return Vector3.zero;

            float normFlow = GetPropertyLUTValue(palette, mat, B2USlotKey.r1_p2_g);
            if (normFlow < 0f) return Vector3.zero;

            Vector3 avgFlowDir = GetNearestAverageFlowVector(position, interactionRange);
            float liquidFlowStrength = Mathf.Lerp(flowMinMax.x, flowMinMax.y, normFlow) * liquid.flowStrength;
            Vector3 totalPush = avgFlowDir * liquidFlowStrength;

            if (includeWaves && rsuvObject != null && B2URuntimeManager.Instance != null)
            {
                uint activeMeshID = (rsuvObject.rsuv >> 26) & 0x3Fu;
                if (activeMeshID != 0u)
                {
                    float viscosity = 0.5f;
                    if (palette.colorLUT != null)
                    {
                        if (viscosityMinMax == Vector2.zero)
                        {
                            viscosityMinMax = palette.TryGetMinMax(B2USlotKey.r2_ea, B2UWorkflow.Liquid);
                        }

                        int lutWidth = (Mathf.Max(1, palette.numSubbiomes) * 8) + 8;
                        float rawVisc = palette.colorLUT.GetPixel(lutWidth - 1, Mathf.Min(absMatID + 1, 255)).a;
                        viscosity = Mathf.Lerp(viscosityMinMax.x, viscosityMinMax.y, rawVisc);
                    }

                    Vector3 advectedFlowWS = avgFlowDir * liquid.flowStrength;
                    float waveHeight = B2URuntimeManager.Instance.EvaluateWaveHeight(activeMeshID, position, advectedFlowWS, viscosity);
                    totalPush.y += waveHeight;
                }
            }

            return totalPush;
        }

        /// <summary>
        /// Reads the assigned material's shader name to automatically configure the RSUV workflow type.
        /// </summary>
        public void AutoDetectType()
        {
            EnsureCachedRenderer();
            if (cachedRenderer == null || cachedRenderer.sharedMaterial == null || cachedRenderer.sharedMaterial.shader == null)
            {
                activeType = RSUVType.Unassigned;
                return;
            }

            string shaderName = cachedRenderer.sharedMaterial.shader.name;

            if (shaderName.Contains("B2U Master Liquid"))
            {
                activeType = RSUVType.Liquid;
                if (Application.isPlaying) CacheLiquidMeshData();
            }
            else if (shaderName.Contains("B2U Master Crystal"))
            {
                activeType = RSUVType.Crystal;
            }
            else if (shaderName.Contains("B2U Master Solid"))
            {
                activeType = RSUVType.Solid;
            }
            else
            {
                activeType = RSUVType.Unassigned;
            }
        }

        /// <summary>
        /// Updates the underlying B2URSUV instance in-place and pushes the packed 32-bit uint to the cached renderer.
        /// </summary>
        public B2URSUV ApplyRSUV()
        {
            EnsureCachedRenderer();

            switch (activeType)
            {
                case RSUVType.Solid:
                    if (!(rsuvObject is B2USolidRSUV solidRSUV))
                    {
                        solidRSUV = new B2USolidRSUV();
                        rsuvObject = solidRSUV;
                    }
                    solidRSUV.paletteIndex = (uint)Mathf.Clamp(solid.paletteIndex, 0, 15);
                    solidRSUV.paintIndex = (uint)Mathf.Clamp(solid.paintIndex, 0, 127);
                    solidRSUV.emissionStrength = Mathf.Clamp01(solid.emissionStrength);
                    solidRSUV.noiseOffset = (uint)Mathf.Clamp(solid.noiseOffset, 0, 63);
                    solidRSUV.isPainted = solid.isPainted;
                    solidRSUV.crackIntensity = Mathf.Clamp01(solid.crackIntensity);
                    solidRSUV.UpdateRSUV();
                    PushToCachedRenderer(solidRSUV);
                    return solidRSUV;

                case RSUVType.Liquid:
                    if (!(rsuvObject is B2ULiquidRSUV liquidRSUV))
                    {
                        liquidRSUV = new B2ULiquidRSUV();
                        rsuvObject = liquidRSUV;
                    }
                    liquidRSUV.emissionStrength = Mathf.Clamp01(liquid.emissionStrength);
                    liquidRSUV.flowStrength = Mathf.Clamp(liquid.flowStrength, -1f, 1f);
                    liquidRSUV.waveMultiplier = Mathf.Clamp(liquid.waveMultiplier, -1f, 1f);
                    liquidRSUV.UpdateRSUV();
                    PushToCachedRenderer(liquidRSUV);

                    if (materialID == int.MaxValue) UpdateMaterialID();
                    if (Application.isPlaying && !liquidMeshCached) CacheLiquidMeshData();
                    return liquidRSUV;

                case RSUVType.Crystal:
                    if (!(rsuvObject is B2UCrystalRSUV crystalRSUV))
                    {
                        crystalRSUV = new B2UCrystalRSUV();
                        rsuvObject = crystalRSUV;
                    }
                    crystalRSUV.emissionStrength = Mathf.Clamp01(crystal.emissionStrength);
                    crystalRSUV.transmissionMultiplier = Mathf.Clamp01(crystal.transmissionMultiplier);
                    crystalRSUV.absorptionMultiplier = Mathf.Clamp01(crystal.absorptionMultiplier);
                    crystalRSUV.crackIntensity = Mathf.Clamp01(crystal.crackIntensity);
                    crystalRSUV.UpdateRSUV();
                    PushToCachedRenderer(crystalRSUV);
                    return crystalRSUV;

                case RSUVType.Unassigned:
                default:
                    return null;
            }
        }

        /// <summary>
        /// Updates the Material ID baked into the RSUV component from the mesh's vertex color alpha or UV1.y.
        /// </summary>
        public void UpdateMaterialID()
        {
            Mesh mesh = GetTargetSharedMesh();
            if (mesh == null || mesh.vertexCount == 0) return;

            if (mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                s_TempColors.Clear();
                mesh.GetColors(s_TempColors);
                if (s_TempColors.Count > 0)
                {
                    materialID = s_TempColors[0].a;
                    return;
                }
            }

            s_TempUVs.Clear();
            mesh.GetUVs(1, s_TempUVs);
            if (s_TempUVs.Count > 0)
            {
                uint pd = (uint)Mathf.RoundToInt(s_TempUVs[0].y);
                int matID = (int)(pd & 0xFFu);

                if (CheckPaletteMaterials(this, silent: true))
                {
                    B2UMaterial mat = ActiveProfile.paletteData.materials.GetMaterialByIndex((byte)matID);
                    if (mat != null)
                    {
                        if (GetPropertyLUTValue(ActiveProfile.paletteData, mat, B2USlotKey.r1_p4_r) > 0.5f)
                        {
                            matID = -matID;
                        }
                    }
                    else if (ActiveProfile.paletteData.propertyLUT != null && ActiveProfile.paletteData.propertyLUT.GetPixel(3, matID).r > 0.5f)
                    {
                        matID = -matID;
                    }
                }
                materialID = matID;
                return;
            }

            materialID = int.MaxValue;
        }

        /// <summary>
        /// Pushes the RSUV to the renderer.
        /// </summary>
        /// <param name="rsuv">The B2URSUV to push</param>
        private void PushToCachedRenderer(B2URSUV rsuv)
        {
            if (cachedMeshRenderer != null)
            {
                cachedMeshRenderer.SetRSUV(rsuv);
            }
            else if (cachedSkinnedRenderer != null)
            {
                cachedSkinnedRenderer.SetRSUV(rsuv);
            }
        }

        /// <summary>
        /// Gets the average flow vector.
        /// </summary>
        /// <param name="position">The position to sample.</param>
        /// <param name="interactionRange">The interaction range.</param>
        /// <returns></returns>
        private Vector3 GetNearestAverageFlowVector(Vector3 position, float interactionRange = 5f)
        {
            if (!liquidMeshCached || !worldPositions.IsCreated || !liquidFlow3D.IsCreated)
            {
                return Vector3.zero;
            }

            NativeArray<float3> finalResult = new NativeArray<float3>(1, Allocator.TempJob);

            CalculateAverageFlowJob job = new CalculateAverageFlowJob
            {
                objectPosition = position,
                sqrRange = Mathf.Max(0.0001f, interactionRange * interactionRange),
                positions = worldPositions,
                flowVectors = liquidFlow3D,
                outAverageForce = finalResult
            };

            job.Run();

            Vector3 averagePushForce = finalResult[0];
            finalResult.Dispose();

            return averagePushForce;
        }

        /// <summary>
        /// A Burst job that dispatches a worker to solve the average flow vector on a mesh.
        /// </summary>
        [BurstCompile]
        private struct CalculateAverageFlowJob : IJob
        {
            [ReadOnly] public float3 objectPosition;
            [ReadOnly] public float sqrRange;
            [ReadOnly] public NativeArray<float3> positions;
            [ReadOnly] public NativeArray<float3> flowVectors;

            public NativeArray<float3> outAverageForce;

            public void Execute()
            {
                float3 accumulatedForce = float3.zero;
                int vertexCountInRange = 0;

                for (int i = 0; i < positions.Length; i++)
                {
                    float distSqr = math.distancesq(positions[i], objectPosition);
                    if (distSqr > sqrRange) continue;

                    float falloff = 1f - (distSqr / sqrRange);
                    accumulatedForce += flowVectors[i] * falloff;
                    vertexCountInRange++;
                }

                outAverageForce[0] = vertexCountInRange > 0
                    ? accumulatedForce / vertexCountInRange
                    : float3.zero;
            }
        }

        /// <summary>
        /// Reconstructs and caches the world-space vertex positions and 3D tangent-space flow vectors for this liquid mesh.
        /// </summary>
        public void CacheLiquidMeshData()
        {
            DisposeLiquidFlowBuffers();

            flowMinMax = Vector2.zero;
            viscosityMinMax = Vector2.zero;

            Mesh mesh = GetTargetSharedMesh();
            if (mesh == null) return;

            int vertexCount = mesh.vertexCount;
            if (vertexCount == 0) return;

            Vector3[] localPos = mesh.vertices;
            Vector3[] localNorm = mesh.normals;
            s_TempUVs.Clear();
            mesh.GetUVs(0, s_TempUVs);

            if (localNorm == null || localNorm.Length < vertexCount || s_TempUVs.Count < vertexCount)
            {
                B2ULogWarning(msgSrc, $"Liquid mesh on '{name}' is missing normals or UV0 flow data. Cannot cache 3D liquid flow.", this);
                return;
            }

            worldPositions = new NativeArray<float3>(vertexCount, Allocator.Persistent);
            liquidFlow3D = new NativeArray<float3>(vertexCount, Allocator.Persistent);

            Matrix4x4 localToWorld = transform.localToWorldMatrix;

            for (int i = 0; i < vertexCount; i++)
            {
                worldPositions[i] = localToWorld.MultiplyPoint3x4(localPos[i]);

                Vector3 nBlend = new Vector3(-localNorm[i].x, localNorm[i].y, localNorm[i].z).normalized;

                Vector3 tBlend;
                Vector3 bBlend;

                if (nBlend.z > -0.9999f)
                {
                    float a = 1f / (1f + nBlend.z);
                    float b = -a * nBlend.x * nBlend.y;

                    tBlend = new Vector3(1f - a * nBlend.x * nBlend.x, b, -nBlend.x);
                    bBlend = new Vector3(b, 1f - a * nBlend.y * nBlend.y, -nBlend.y);
                }
                else
                {
                    tBlend = new Vector3(0f, -1f, 0f);
                    bBlend = new Vector3(-1f, 0f, 0f);
                }

                Vector3 tUnity = new Vector3(-tBlend.x, tBlend.y, tBlend.z);
                Vector3 bUnity = new Vector3(-bBlend.x, bBlend.y, bBlend.z);

                Vector3 worldTan = localToWorld.MultiplyVector(tUnity).normalized;
                Vector3 worldBitan = localToWorld.MultiplyVector(bUnity).normalized;

                Vector2 uv = s_TempUVs[i];
                liquidFlow3D[i] = (worldTan * uv.x) + (worldBitan * uv.y);
            }

            liquidMeshCached = true;
        }
    }
}