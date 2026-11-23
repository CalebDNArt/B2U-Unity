using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static B2U.B2UDebug;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace B2U
{
    /// <summary>
    /// Provides runtime and editor functionality for B2U systems, including RSUV bit-packing,
    /// global shader property management, fullscreen effect coroutines, runtime mesh material swapping, and LUT manipulation.
    /// </summary>
    public static class B2URuntimeUtility
    {
        /// <summary>
        /// The active B2UFullscreenEffects URP renderer feature registered for runtime control.
        /// </summary>
        public static B2UFullscreenEffects FullscreenFeature { get; private set; }
        /// <summary>
        /// The active B2UVolumetricRefraction URP renderer feature registered for runtime control.
        /// </summary>
        public static B2UVolumetricRefraction RefractionFeature { get; private set; }
        /// <summary>
        /// The active B2UEdgeDetection URP renderer feature registered for runtime control.
        /// </summary>
        public static B2UEdgeDetection EdgeFeature { get; private set; }

        /// <summary>
        /// Registers the active B2UFullscreenEffects renderer feature instance with B2URuntimeUtility.
        /// </summary>
        /// <param name="feature">The B2UFullscreenEffects feature to register.</param>
        public static void RegisterFeature(B2UFullscreenEffects feature) => FullscreenFeature = feature;
        /// <summary>
        /// Registers the active B2UVolumetricRefraction renderer feature instance with B2URuntimeUtility.
        /// </summary>
        /// <param name="feature">The B2UVolumetricRefraction feature to register.</param>
        public static void RegisterFeature(B2UVolumetricRefraction feature) => RefractionFeature = feature;
        /// <summary>
        /// Registers the active B2UEdgeDetection renderer feature instance with B2URuntimeUtility.
        /// </summary>
        /// <param name="feature">The B2UEdgeDetection feature to register.</param>
        public static void RegisterFeature(B2UEdgeDetection feature) => EdgeFeature = feature;

        private static B2UShaderProfile activeProfile = null;
        private static readonly List<Vector2> s_TempUVList = new List<Vector2>();

        /// <summary>
        /// The currently active B2UShaderProfile.
        /// </summary>
        public static B2UShaderProfile ActiveProfile => activeProfile;

        #region Null Checks
        /// <summary>
        /// Verifies that an active B2UShaderProfile is currently bound.
        /// </summary>
        /// <param name="obj">The object that called the check (used for logging).</param>
        /// <param name="silent">If the call is silent and ignores logging to the console.</param>
        /// <returns>True if the active B2UShaderProfile exists.</returns>
        public static bool CheckActiveProfile(UnityEngine.Object obj = null, bool silent = false)
        {
            if (activeProfile == null)
            {
                if (!silent) B2ULogError(B2UMessageSource.Runtime, "The active shader profile is null. Set it in the control panel.", obj);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Verifies that an active B2UShaderProfile and its B2UPaletteData asset are currently bound.
        /// </summary>
        /// <param name="obj">The object that called the check (used for logging).</param>
        /// <param name="silent">If the call is silent and ignores logging to the console.</param>
        /// <returns>True if a valid B2UPaletteData exists on the active B2UShaderProfile.</returns>
        public static bool CheckActivePalette(UnityEngine.Object obj = null, bool silent = false)
        {
            if (CheckActiveProfile(obj, silent))
            {
                if (activeProfile.paletteData != null)
                {
                    return true;
                }
                if (!silent) B2ULogError(B2UMessageSource.Runtime, "The palette data on the active shader profile is null. Set it in the profile or control panel.", obj);
            }
            return false;
        }

        /// <summary>
        /// Verifies that the active palette's material and schema dictionaries are initialized and ready for queries.
        /// </summary>
        /// <param name="obj">The object that called the check (used for logging).</param>
        /// <param name="silent">If the call is silent and ignores logging to the console.</param>
        /// <returns>True if the B2UMaterials exist and are initialized in the active profile's palette</returns>
        public static bool CheckPaletteMaterials(UnityEngine.Object obj = null, bool silent = false)
        {
            if (CheckActivePalette(obj, silent))
            {
                if (activeProfile.paletteData.materials == null || !activeProfile.paletteData.materials.IsInitialized)
                {
                    activeProfile.paletteData.Initialize();
                    if (activeProfile.paletteData.materials == null || !activeProfile.paletteData.materials.IsInitialized)
                    {
                        if (!silent) B2ULogError(B2UMessageSource.Runtime, "The active palette's materials failed to initialize.", obj);
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Verifies that the active B2UShaderProfile has valid, readable BiomeTexture1 and BiomeTexture2 assets assigned.
        /// </summary>
        /// <param name="obj">The object that called the check (used for logging).</param>
        /// <param name="silent">If the call is silent and ignores logging to the console.</param>
        /// <returns>True if both biome textures exist on the active B2UShaderProfile.</returns>
        public static bool CheckBiomeTextures(UnityEngine.Object obj = null, bool silent = false)
        {
            if (!CheckActiveProfile(obj, silent)) return false;

            if (ActiveProfile.propertyBlocks == null ||
                !ActiveProfile.propertyBlocks.TryGetValue(B2UShaderProperty.BiomeTexture1, out var b1Block) ||
                !(b1Block.value is Texture2D b1Tex) || b1Tex == null)
            {
                if (!silent) B2ULogError(B2UMessageSource.Runtime, "Biome Texture 1 on the active shader profile is null.", obj);
                return false;
            }

            if (!ActiveProfile.propertyBlocks.TryGetValue(B2UShaderProperty.BiomeTexture2, out var b2Block) ||
                !(b2Block.value is Texture2D b2Tex) || b2Tex == null)
            {
                if (!silent) B2ULogError(B2UMessageSource.Runtime, "Biome Texture 2 on the active shader profile is null.", obj);
                return false;
            }

            return true;
        }
        #endregion

        #region RSUV Functions
        /// <summary>
        /// Sets the given B2U RSUV for a GameObject.
        /// </summary>
        /// <typeparam name="T">A B2U RSUV Type (B2USolidRSUV, B2ULiquidRSUV, B2UCrystalRSUV)</typeparam>
        /// <param name="gameObject">The GameObject to apply the RSUV to</param>
        /// <param name="rsuv">The B2U RSUV object</param>
        /// <example>
        /// This method and it's overloads allows you to set an RSUV on an object without needing a B2URSUVMono component. They are extensions of GameObject so they can be called directly by the GameObject class.
        /// <code>
        /// public GameObject solidObject;
        /// public Slider emissionSlider;
        /// private B2USolidRSUV solidRSUV;
        /// 
        /// private void Update()
        /// {
        ///     if (solidRSUV == null) solidRSUV = new();
        ///     
        ///     solidRSUV.emissionStrength = emissionSlider.value;    
        /// 
        ///     // Call the GameObject extension
        ///     solidObject.SetRSUV(solidRSUV);
        ///     
        ///     // Or call the static method itself
        ///     B2URuntimeUtility.SetRSUV(solidObject, solidRSUV);
        /// }
        /// </code>
        /// </example>
        public static void SetRSUV<T>(this GameObject gameObject, T rsuv) where T : B2URSUV
        {
            if (gameObject == null || rsuv == null) return;

            if (gameObject.TryGetComponent<MeshRenderer>(out MeshRenderer mr)) SetRSUV(mr, rsuv);
            else if (gameObject.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer smr)) SetRSUV(smr, rsuv);
            else B2ULogError(B2UMessageSource.Runtime, $"Cannot locate renderer on object: {gameObject.name}", gameObject);
        }

        /// <summary>
        /// Sets the given B2U RSUV for all objects in a GameObject array.
        /// </summary>
        /// <typeparam name="T">A B2U RSUV Type (B2USolidRSUV, B2ULiquidRSUV, B2UCrystalRSUV)</typeparam>
        /// <param name="gameObjects">Array of GameObjects to apply the RSUV to.</param>
        /// <param name="rsuv">The B2U RSUV object</param>
        /// <example>
        /// This method is NOT an extension of GameObject. Use it to set many objects to the same RSUV value at once.
        /// <code>
        /// public GameObject[] crystalObjects;
        /// public Slider transmissionSlider;
        /// private B2UCrystalRSUV crystalRSUV;
        /// 
        /// private void Update()
        /// {
        ///     if (crystalRSUV == null) crystalRSUV = new();
        ///     
        ///     crystalRSUV.transmissionMultiplier = transmissionSlider.value;
        ///     
        ///     B2URuntimeUtility.SetRSUV(crystalObjects, crystalRSUV);
        /// }
        /// </code>
        /// </example>
        public static void SetRSUV<T>(GameObject[] gameObjects, T rsuv) where T : B2URSUV
        {
            if (gameObjects == null || rsuv == null) return;
            foreach (GameObject obj in gameObjects)
            {
                SetRSUV(obj, rsuv);
            }
        }

        /// <summary>
        /// Sets the given B2U RSUV for a MeshRenderer.
        /// </summary>
        /// <typeparam name="T">A B2U RSUV Type (B2USolidRSUV, B2ULiquidRSUV, B2UCrystalRSUV)</typeparam>
        /// <param name="renderer">The MeshRenderer to apply the RSUV to</param>
        /// <param name="rsuv">The B2U RSUV object</param>
        public static void SetRSUV<T>(this MeshRenderer renderer, T rsuv) where T : B2URSUV
        {
            if (renderer == null || rsuv == null) return;
            uint targetRSUV = rsuv.rsuv;
            if (rsuv.isLiquid) targetRSUV = rsuv.SetMeshIDInRSUV(renderer.GetShaderUserValue());
            renderer.SetShaderUserValue(targetRSUV);
        }

        /// <summary>
        /// Sets the given B2U RSUV for a SkinnedMeshRenderer.
        /// </summary>
        /// <typeparam name="T">A B2U RSUV Type (B2USolidRSUV, B2ULiquidRSUV, B2UCrystalRSUV)</typeparam>
        /// <param name="renderer">The SkinnedMeshRenderer to apply the RSUV to</param>
        /// <param name="rsuv">A B2URSUV object (B2USolidRSUV, B2ULiquidRSUV, B2UCrystalRSUV)</param>
        public static void SetRSUV<T>(this SkinnedMeshRenderer renderer, T rsuv) where T : B2URSUV
        {
            if (renderer == null || rsuv == null) return;
            uint targetRSUV = rsuv.rsuv;
            if (rsuv.isLiquid) targetRSUV = rsuv.SetMeshIDInRSUV(renderer.GetShaderUserValue());
            renderer.SetShaderUserValue(targetRSUV);
        }

        /// <summary>
        /// Tries to get a B2U RSUV object from a GameObject without reflection.
        /// </summary>
        /// <typeparam name="T">A B2U RSUV Type (B2USolidRSUV, B2ULiquidRSUV, B2UCrystalRSUV)</typeparam>
        /// <param name="gameObject">The GameObject to try and get the RSUV from.</param>
        /// <param name="rsuv">The retrieved B2URSUV.</param>
        /// <returns>True if the RSUV was retrieved.</returns>
        /// <example>
        /// <code>
        /// public GameObject liquidObject;
        /// private B2ULiquidRSUV liquidRSUV;
        /// private void foundRSUV = false;
        /// 
        /// private void Start()
        /// {
        ///     if (liquidObject.TryGetRSUV(out liquidRSUV)
        ///     {
        ///         foundRSUV = true;
        ///         // Do something with the rsuv
        ///     }
        /// }
        /// </code>
        /// </example>
        public static bool TryGetRSUV<T>(this GameObject gameObject, out T rsuv) where T : B2URSUV, new()
        {
            rsuv = null;
            if (gameObject == null || !gameObject.TryGetRSUV(out uint meshRSUV)) return false;

            rsuv = new T();
            rsuv.DecodeFromUint(meshRSUV);
            return true;
        }

        /// <summary>
        /// Zero-allocation overload: decodes the GameObject's current RSUV into an existing B2URSUV instance.
        /// </summary>
        /// <typeparam name="T">A B2U RSUV Type (B2USolidRSUV, B2ULiquidRSUV, B2UCrystalRSUV)</typeparam>
        /// <param name="gameObject">THe GameObject to try and get the RSUV from.</param>
        /// <param name="targetInstance">The B2URSUV instance to overwrite with the retrieved data.</param>
        public static bool TryGetRSUV<T>(this GameObject gameObject, T targetInstance) where T : B2URSUV
        {
            if (gameObject == null || targetInstance == null || !gameObject.TryGetRSUV(out uint meshRSUV)) return false;

            targetInstance.DecodeFromUint(meshRSUV);
            return true;
        }

        /// <summary>
        /// Tries to get the raw uint RSUV value from a GameObject.
        /// </summary>
        /// <param name="gameObject">The GameObject to try getting the RSUV from</param>
        /// <param name="rsuv">The 32-bit uint RSUV value</param>
        /// <returns>True if a valid renderer is located</returns>
        public static bool TryGetRSUV(this GameObject gameObject, out uint rsuv)
        {
            rsuv = 0;
            if (gameObject == null) return false;

            if (gameObject.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
            {
                rsuv = mr.GetShaderUserValue();
                return true;
            }
            if (gameObject.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer smr))
            {
                rsuv = smr.GetShaderUserValue();
                return true;
            }

            B2ULogError(B2UMessageSource.Runtime, $"Cannot locate renderer on object: {gameObject.name}", gameObject);
            return false;
        }

        /// <summary>
        /// Sets the 6-bit MeshID (0-63) in bits 26-31 of the liquid's RSUV so waves only affect their target liquid body.
        /// </summary>
        /// <param name="liquidObject">The liquid GameObject to apply the MeshID to</param>
        /// <param name="meshID">The 0-63 uint MeshID</param>
        public static void SetLiquidMeshID(this GameObject liquidObject, uint meshID)
        {
            if (liquidObject == null) return;
            if (!liquidObject.CompareTag("B2ULiquid"))
            {
                B2ULogError(B2UMessageSource.Runtime, $"Object {liquidObject.name} not tagged B2ULiquid.", liquidObject);
                return;
            }

            uint newRSUVValue = 0;
            bool rendererFound = false;

            if (liquidObject.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
            {
                uint existingRSUV = mr.GetShaderUserValue();
                newRSUVValue = (existingRSUV & 0x03FFFFFFu) | ((meshID & 0x3Fu) << 26);
                mr.SetShaderUserValue(newRSUVValue);
                rendererFound = true;
            }
            else if (liquidObject.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer smr))
            {
                uint existingRSUV = smr.GetShaderUserValue();
                newRSUVValue = (existingRSUV & 0x03FFFFFFu) | ((meshID & 0x3Fu) << 26);
                smr.SetShaderUserValue(newRSUVValue);
                rendererFound = true;
            }
            else
            {
                B2ULogError(B2UMessageSource.Runtime, $"Cannot locate renderer on object {liquidObject.name}", liquidObject);
            }

            // Keep the CPU-side RSUVMono cache fully synchronized with the renderer
            if (rendererFound && liquidObject.TryGetComponent<B2URSUVMono>(out var rsuvMono))
            {
                if (rsuvMono.rsuvObject != null)
                {
                    rsuvMono.rsuvObject.rsuv = newRSUVValue;
                }
            }
        }

        /// <summary>
        /// Packs wave parameters (MeshID, 10-bit normalized amplitude, and 16-bit half-float elapsed time) into a 32-bit uint.
        /// </summary>
        /// <param name="meshID">A 0-63 uint WaveID</param>
        /// <param name="amplitude">The amplitude of the wave (Literal height displacement between 0-2m)</param>
        /// <param name="time">The time elapsed since the wave was instantiated</param>
        /// <returns>The 32-bit packed uint for the wave array W channel</returns>
        public static uint PackWaveData(uint meshID, float amplitude, float time = 0f)
        {
            uint packedW = meshID & 0x3Fu;

            float clampedAmp = Mathf.Clamp(amplitude, 0.0f, 10.0f);
            uint ampInt = (uint)Mathf.Clamp(Mathf.RoundToInt((clampedAmp / 10.0f) * 1023.0f), 0, 1023);
            packedW |= (ampInt & 0x3FFu) << 6;

            ushort timeHalf = Mathf.FloatToHalf(time);
            packedW |= (uint)timeHalf << 16;

            return packedW;
        }

        /// <summary>
        /// Checks a liquid's RSUV for an existing MeshID. If it is 0, requests a new ID from B2URuntimeManager and assigns it.
        /// </summary>
        /// <param name="liquidObject">The liquid GameObject</param>
        /// <returns>The 0-63 MeshID, or 0 if allocation fails.</returns>
        public static uint GetOrAssignMeshID(GameObject liquidObject)
        {
            if (liquidObject == null) return 0;
            if (!liquidObject.CompareTag("B2ULiquid"))
            {
                B2ULogError(B2UMessageSource.Runtime, $"Object {liquidObject.name} is not tagged B2ULiquid.", liquidObject);
                return 0;
            }

            if (!liquidObject.TryGetRSUV(out uint existingRSUV))
            {
                B2ULogError(B2UMessageSource.Runtime, $"Could not read RSUV from object {liquidObject.name}.", liquidObject);
                return 0;
            }

            uint currentMeshID = (existingRSUV >> 26) & 0x3Fu;
            if (currentMeshID != 0 && B2URuntimeManager.Instance != null && B2URuntimeManager.Instance.IsMeshIDRegisteredTo(currentMeshID, liquidObject))
            {
                return currentMeshID;
            }

            if (B2URuntimeManager.Instance != null)
            {
                uint newMeshID = B2URuntimeManager.Instance.GetAvailableMeshID(liquidObject);
                if (newMeshID != 0)
                {
                    liquidObject.SetLiquidMeshID(newMeshID);
                    return newMeshID;
                }

                B2ULogWarning(B2UMessageSource.Runtime, "Manager returned 0. Maximum active liquid bodies reached.", liquidObject);
                return 0;
            }

            B2ULogError(B2UMessageSource.Runtime, "No B2URuntimeManager instance exists. Ensure that there is a single instance in the scene and the object is not being destroyed by another script.");
            return 0;
        }

        /// <summary>
        /// Instantiates a wave into the liquid GameObject.
        /// </summary>
        /// <param name="liquidObject">The liquid GameObject</param>
        /// <param name="origin">The world-space origin of the wave</param>
        /// <param name="amplitude">The amplitude of the wave (0f-10f)</param>
        /// <example>
        /// <code>
        /// public GameObject liquidObject;
        /// public Rigidbody impactingSphere; //A 1m sphere
        /// private bool impaced = false;
        ///
        /// private void Update()
        /// {
        ///     Vector3 velocity = impactingSphere.linearVelocity;
        ///
        ///     // Check if the sphere is going to hit something along it's velocity.
        ///     if (Physics.Raycast(impactingSphere.position, velocity.normalized, out RaycastHit hit, 1.1f) && !impacted)
        ///     {
        ///         // Ensure the hit object is the liquid.
        ///         if (hit.collider.gameObject == liquidObject)
        ///         {
        ///             liquidObject.InstantiateWave(hit.point, Mathf.Lerp(0f, 10f, velocity.magnitude / 10f));
        ///            
        ///             // Prevents it from instantiating a wave every frame.
        ///             impacted = true;
        ///         }
        ///     }
        /// }
        /// </code>
        /// </example>
        public static void InstantiateWave(this GameObject liquidObject, Vector3 origin, float amplitude)
        {
            if (liquidObject == null) return;
            if (!liquidObject.CompareTag("B2ULiquid"))
            {
                B2ULogError(B2UMessageSource.Runtime, $"Object {liquidObject.name} is not tagged with B2ULiquid.", liquidObject);
                return;
            }

            if (B2URuntimeManager.Instance != null)
            {
                uint meshID = GetOrAssignMeshID(liquidObject);
                if (meshID != 0 && amplitude != 0)
                {
                    B2URuntimeManager.Instance.AddWave(origin, meshID, amplitude);
                }
            }
            else
            {
                B2ULogError(B2UMessageSource.Runtime, "No B2URuntimeManager instance exists. Ensure that there is a single instance of it in the scene and the object is not being destroyed by another script.");
            }
        }
        #endregion

        #region Master Shader Functions
        /// <summary>
        /// Sets the active B2UShaderProfile and pushes all its properties to the master shaders.
        /// </summary>
        /// <param name="profile">The shader profile to apply.</param>
        /// <example>
        /// <code>
        /// public B2UShaderProfile targetProfile;
        /// 
        /// private void Start()
        /// {
        ///     if (targetProfile != null) B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(targetProfile);
        /// }
        /// </code>
        /// </example>
        public static void SetB2UShaderPropertyValuesFromProfile(B2UShaderProfile profile)
        {
            if (profile != null)
            {
                activeProfile = profile;
                activeProfile.InitializeDictionaries();
                activeProfile.RestoreSavedValues();
                PushAllGlobals();
            }
        }

        /// <summary>
        /// Sets a runtime property to the given value in the active profile and pushes it to the master shaders.
        /// </summary>
        /// <param name="property">The B2UShaderPropertyRuntime property to set</param>
        /// <param name="value">The value to set. Must match the expected type for the property</param>
        /// <example>
        /// <code>
        /// public Slider slider;
        /// 
        /// private void Update()
        /// {
        ///     B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.GlobalEmissionStrength, slider.value);
        /// }
        /// </code>
        /// </example>
        public static void SetB2UShaderPropertyValue(B2UShaderProperty property, object value)
        {
            if (activeProfile == null || activeProfile.propertyBlocks == null)
            {
                B2ULogError(B2UMessageSource.Runtime, "No active B2UShaderProfile is initialized.");
                return;
            }
            if (!activeProfile.propertyBlocks.TryGetValue(property, out B2UShaderPropertyBlock block))
            {
                B2ULogError(B2UMessageSource.Runtime, $"Property {property} not found in dictionary.");
                return;
            }
            block.SetValue(value);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                activeProfile.SyncSavedData();
                EditorUtility.SetDirty(activeProfile);
            }
#endif

            PushGlobalToMasters(property);
        }

        /// <summary>
        /// Pushes a given property block's value from the profile to the master shaders and active URP renderer features.
        /// </summary>
        /// <param name="property">The property to push.</param>
        public static void PushGlobalToMasters(B2UShaderProperty property)
        {
            if (activeProfile == null || activeProfile.propertyBlocks == null) return;

            if (!activeProfile.propertyBlocks.TryGetValue(property, out B2UShaderPropertyBlock block))
            {
                B2ULogError(B2UMessageSource.Runtime, $"Property {property} not found in dictionary.");
                return;
            }

            if (block.value == null && block.type != typeof(Texture2D) && block.type != typeof(Texture2DArray) && block.type != typeof(Texture3D))
                return;

            switch (block.value)
            {
                case Texture2D tex2D:
                    Shader.SetGlobalTexture(block.shaderID, tex2D);
                    break;
                case Texture2DArray tex2DArray:
                    Shader.SetGlobalTexture(block.shaderID, tex2DArray);
                    break;
                case Texture3D tex3D:
                    Shader.SetGlobalTexture(block.shaderID, tex3D);
                    break;
                case Color col:
                    Shader.SetGlobalColor(block.shaderID, col);
                    break;
                case Vector2 vec2:
                    Shader.SetGlobalVector(block.shaderID, vec2);
                    break;
                case int intVal:
                    int clampedInt = block.minMax != Vector2.zero
                        ? Mathf.Clamp(intVal, (int)block.minMax.x, (int)block.minMax.y)
                        : intVal;

                    if (property == B2UShaderProperty.VolumetricLayer)
                    {
                        if (RefractionFeature != null)
                        {
                            RefractionFeature.settings.volumetricLayer = 1 << clampedInt;
                        }
                    }
                    else if (block.shaderID != 0)
                    {
                        Shader.SetGlobalFloat(block.shaderID, clampedInt);
                    }
                    break;
                case float floatVal:
                    if (block.minMax != Vector2.zero)
                    {
                        if (property == B2UShaderProperty.InjuryStrength)
                        {
                            if (floatVal > 0f) Shader.EnableKeyword("_B2U_USE_INJURY_OVERLAY");
                            else Shader.DisableKeyword("_B2U_USE_INJURY_OVERLAY");
                        }
                        else if (property == B2UShaderProperty.StatusStrength)
                        {
                            if (floatVal > 0f) Shader.EnableKeyword("_B2U_STATUS_EFFECT_ACTIVE");
                            else Shader.DisableKeyword("_B2U_STATUS_EFFECT_ACTIVE");
                        }
                        Shader.SetGlobalFloat(block.shaderID, Mathf.Clamp(floatVal, block.minMax.x, block.minMax.y));
                    }
                    else
                    {
                        if (property == B2UShaderProperty.ImmersionTransitionDistance)
                        {
                            if (FullscreenFeature != null) FullscreenFeature.settings.liquidTransitionDistance = floatVal;
                        }
                        Shader.SetGlobalFloat(block.shaderID, floatVal);
                    }
                    break;
                case bool boolVal:
                    if (!string.IsNullOrEmpty(block.name) && block.name.StartsWith("_"))
                    {
                        if (boolVal) Shader.EnableKeyword(block.name);
                        else Shader.DisableKeyword(block.name);

                        if (property == B2UShaderProperty.ImmersionTransitionEnabled)
                        {
                            if (!boolVal)
                            {
                                if (FullscreenFeature != null) FullscreenFeature.settings.liquidTransitionDistance = 0f;
                            }
                            else
                            {
                                PushGlobalToMasters(B2UShaderProperty.ImmersionTransitionDistance);
                            }
                        }
                        else if (property == B2UShaderProperty.EdgeDetectionMode)
                        {
                            if (EdgeFeature != null) EdgeFeature.settings.depthMode = boolVal;
                        }
                    }
                    else
                    {
                        switch (property)
                        {
                            case B2UShaderProperty.ImmersionActive:
                                if (FullscreenFeature != null) FullscreenFeature.settings.immersionEnabled = boolVal;
                                break;
                            case B2UShaderProperty.RecolorActive:
                                if (FullscreenFeature != null) FullscreenFeature.settings.recolorEnabled = boolVal;
                                break;
                            case B2UShaderProperty.StylizeActive:
                                if (FullscreenFeature != null) FullscreenFeature.settings.stylizeEnabled = boolVal;
                                break;
                            case B2UShaderProperty.StatusActive:
                                if (FullscreenFeature != null) FullscreenFeature.settings.statusEnabled = boolVal;
                                break;
                            case B2UShaderProperty.FullscreenEffectsEnabled:
                                if (FullscreenFeature != null) FullscreenFeature.SetActive(boolVal);
                                break;
                            case B2UShaderProperty.EdgeDetectionEnabled:
                                if (boolVal) Shader.EnableKeyword("_B2U_EDGE_DETECTION");
                                else Shader.DisableKeyword("_B2U_EDGE_DETECTION");
                                if (EdgeFeature != null) EdgeFeature.settings.active = boolVal;
                                break;
                            case B2UShaderProperty.VolumetricRefractionEnabled:
                                if (RefractionFeature != null)
                                {
                                    uint refQuality = activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.RefractionQualityMode, out var rqBlock) && rqBlock.value is uint u ? u : 1u;
                                    RefractionFeature.settings.drawVolumetric = boolVal && (refQuality != 0u);
                                }
                                break;
                        }
                    }
                    break;
                case uint uintVal:
                    if (block.enumOptions != null)
                    {
                        for (int i = 0; i < block.enumOptions.Length; i++) Shader.DisableKeyword(block.enumOptions[i]);
                        if (uintVal < block.enumOptions.Length) Shader.EnableKeyword(block.enumOptions[uintVal]);

                        if (property == B2UShaderProperty.RefractionQualityMode && RefractionFeature != null)
                        {
                            bool vrEnabled = !activeProfile.propertyBlocks.TryGetValue(B2UShaderProperty.VolumetricRefractionEnabled, out var vrBlock) || !(vrBlock.value is bool b) || b;
                            RefractionFeature.settings.copyMode = (B2UVolumetricRefraction.CopyPassMode)uintVal;
                            RefractionFeature.settings.drawVolumetric = vrEnabled && (uintVal != 0u);
                        }
                    }
                    else
                    {
                        B2UVolumetricRefraction.TextureDownsample downsampleMode = uintVal switch
                        {
                            1u => B2UVolumetricRefraction.TextureDownsample.Half,
                            2u => B2UVolumetricRefraction.TextureDownsample.Quarter,
                            3u => B2UVolumetricRefraction.TextureDownsample.Eighth,
                            _ => B2UVolumetricRefraction.TextureDownsample.Full
                        };

                        switch (property)
                        {
                            case B2UShaderProperty.BackfaceBufferResolution:
                                if (RefractionFeature != null) RefractionFeature.settings.volumetricResolution = downsampleMode;
                                break;
                            case B2UShaderProperty.ColorCopyResolution:
                                if (RefractionFeature != null) RefractionFeature.settings.colorCopyResolution = downsampleMode;
                                break;
                            case B2UShaderProperty.EdgeDetectionBufferResolution:
                                if (EdgeFeature != null) EdgeFeature.settings.edgeBufferResolution = downsampleMode;
                                break;
                        }
                    }
                    break;
                case LayerMask layerMask:
                    if (property == B2UShaderProperty.EdgeDetectionLayermask && EdgeFeature != null)
                    {
                        EdgeFeature.settings.layerMask = layerMask;
                    }
                    break;
                case UnityEngine.Object obj:
                    if (block.type == typeof(B2URecolorPresetCollection) && obj is B2URecolorPresetCollection recolorCol)
                    {
                        SetRecolorPreset(recolorCol.activePreset);
                    }
                    else if (block.type == typeof(B2UStylizePresetCollection) && obj is B2UStylizePresetCollection stylizeCol)
                    {
                        SetStylizePreset(stylizeCol.activePreset);
                    }
                    else if (block.type == typeof(B2UStatusEffectPresetCollection) && obj is B2UStatusEffectPresetCollection statusCol)
                    {
                        SetStatusEffectPreset(statusCol.activePreset);
                    }
                    break;
                case null:
                    if (block.type == typeof(Texture2D) || block.type == typeof(Texture2DArray) || block.type == typeof(Texture3D))
                    {
                        Shader.SetGlobalTexture(block.shaderID, (Texture)null);
                    }
                    break;
                default:
                    B2ULogError(B2UMessageSource.Runtime, $"No overload exists for processing data of type: {block.value.GetType()}");
                    break;
            }
        }

        /// <summary>
        /// Safely retrieves the strongly-typed value of a shader property from the active B2UShaderProfile,
        /// validating the requested type against the property block's cached Type before unboxing.
        /// </summary>
        /// <typeparam name="T">The expected C# type for the shader property (e.g. float, int, uint, bool, Color, Vector2, Texture2D).</typeparam>
        /// <param name="property">The B2UShaderProperty to read.</param>
        /// <param name="value">Outputs the retrieved value if successful, or default(T) on failure.</param>
        /// <param name="silent">If true, suppresses error logging when the profile is missing or types do not match.</param>
        /// <returns>True if the property exists and matches the requested type (note: reference-type properties such as Texture2D may still output null if unassigned).</returns>
        /// <example>
        /// <code>
        /// private void Update()
        /// {
        ///     if (B2URuntimeUtility.TryGetB2UShaderPropertyValue&lt;float&gt;(B2UShaderProperty.GlobalEmissionStrength, out float strength))
        ///     {
        ///         Debug.Log($"Current emission strength: {strength}");
        ///     }
        /// }
        /// </code>
        /// </example>
        public static bool TryGetB2UShaderPropertyValue<T>(B2UShaderProperty property, out T value, bool silent = false)
        {
            value = default;

            if (activeProfile == null || activeProfile.propertyBlocks == null)
            {
                if (!silent) B2ULogError(B2UMessageSource.Runtime, "No active B2UShaderProfile is initialized.");
                return false;
            }

            if (!activeProfile.propertyBlocks.TryGetValue(property, out B2UShaderPropertyBlock block) || block == null)
            {
                if (!silent) B2ULogError(B2UMessageSource.Runtime, $"Property {property} not found in dictionary.");
                return false;
            }

            if (block.type != null && !typeof(T).IsAssignableFrom(block.type))
            {
                if (!silent) B2ULogError(B2UMessageSource.Runtime, $"Type mismatch when getting {property}. Expected: {block.type.Name} | Requested: {typeof(T).Name}");
                return false;
            }

            if (block.value is T typedValue)
            {
                value = typedValue;
                return true;
            }

            if (block.value == null)
            {
                // Valid unassigned reference or nullable value (e.g. an empty Texture2D or preset collection slot)
                if (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null)
                {
                    return true;
                }

                if (!silent) B2ULogError(B2UMessageSource.Runtime, $"Property {property} has a null value, which cannot be returned as {typeof(T).Name}.");
                return false;
            }

            if (!silent) B2ULogError(B2UMessageSource.Runtime, $"Stored value for {property} is of type {block.value.GetType().Name}, which cannot be cast to {typeof(T).Name}.");
            return false;
        }

        /// <summary>
        /// Safely retrieves the strongly-typed value of a shader property from the active B2UShaderProfile,
        /// returning a fallback value if the profile is uninitialized, the property is missing, or the requested type does not match.
        /// </summary>
        /// <typeparam name="T">The expected C# type for the shader property.</typeparam>
        /// <param name="property">The B2UShaderProperty to read.</param>
        /// <param name="fallback">The value to return if retrieval fails.</param>
        /// <param name="silent">If true, suppresses error logging on failure.</param>
        /// <returns>The property's current value, or the fallback value on failure.</returns>
        /// <example>
        /// <code>
        /// float emission = B2URuntimeUtility.GetB2UShaderPropertyValue&lt;float&gt;(B2UShaderProperty.GlobalEmissionStrength, 1f);
        /// Texture2D biomeTex = B2URuntimeUtility.GetB2UShaderPropertyValue&lt;Texture2D&gt;(B2UShaderProperty.BiomeTexture1);
        /// </code>
        /// </example>
        public static T GetB2UShaderPropertyValue<T>(B2UShaderProperty property, T fallback = default, bool silent = false)
        {
            return TryGetB2UShaderPropertyValue(property, out T value, silent) ? value : fallback;
        }

        /// <summary>
        /// Pushes all global values in the active profile to the master shaders.
        /// </summary>
        public static void PushAllGlobals()
        {
            if (activeProfile == null)
            {
                B2ULogError(B2UMessageSource.Runtime, "No active shader profile is given. Set one first with SetB2UShaderPropertyValuesFromProfile(profile)");
                return;
            }

            if (RefractionFeature == null) TryAutoFindRefractionFeature(true);
            if (EdgeFeature == null) TryAutoFindEdgeFeature(true);
            if (FullscreenFeature == null) TryAutoFindFullscreenFeature(true);

            foreach (var kvp in activeProfile.propertyBlocks)
            {
                PushGlobalToMasters(kvp.Key);
            }
        }
        #endregion

        #region URP Feature Auto-Setup
        /// <summary>
        /// Retrieves the active URP ScriptableRendererData asset via reflection. Safe in both Editor and Standalone Builds.
        /// </summary>
        private static ScriptableRendererData GetActiveRendererData(bool silent = false)
        {
            RenderPipelineAsset currentPipe = QualitySettings.renderPipeline != null
                ? QualitySettings.renderPipeline
                : GraphicsSettings.currentRenderPipeline;

            if (currentPipe is UniversalRenderPipelineAsset urpAsset)
            {
                FieldInfo fieldInfo = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fieldInfo != null)
                {
                    var rendererDataList = (ScriptableRendererData[])fieldInfo.GetValue(urpAsset);
                    if (rendererDataList != null && rendererDataList.Length > 0 && rendererDataList[0] != null)
                    {
                        return rendererDataList[0];
                    }
                }
            }

            if (!silent)
            {
                B2ULogError(
                    Application.isEditor ? B2UMessageSource.Editor : B2UMessageSource.Runtime,
                    "Could not locate the active URP Renderer Data."
                );
            }
            return null;
        }

        /// <summary>
        /// Finds the B2UVolumetricRefraction feature on the active renderer asset if it exists.
        /// </summary>
        public static B2UVolumetricRefraction TryAutoFindRefractionFeature(bool silent = false)
        {
            if (RefractionFeature != null) return RefractionFeature;
            var rendererData = GetActiveRendererData(silent);
            if (rendererData != null)
            {
                foreach (var feature in rendererData.rendererFeatures)
                {
                    if (feature is B2UVolumetricRefraction b2uFeature)
                    {
                        RefractionFeature = b2uFeature;
                        return b2uFeature;
                    }
                }
            }
            if (!silent) B2ULogError(B2UMessageSource.Runtime, "Could not find B2UVolumetricRefraction. Use the auto setup button or make sure you have added it to your active URP Forward Renderer.");
            return null;
        }

        /// <summary>
        /// Finds the B2UEdgeDetection feature on the active renderer asset if it exists.
        /// </summary>
        public static B2UEdgeDetection TryAutoFindEdgeFeature(bool silent = false)
        {
            if (EdgeFeature != null) return EdgeFeature;
            var rendererData = GetActiveRendererData(silent);
            if (rendererData != null)
            {
                foreach (var feature in rendererData.rendererFeatures)
                {
                    if (feature is B2UEdgeDetection b2uFeature)
                    {
                        EdgeFeature = b2uFeature;
                        return b2uFeature;
                    }
                }
            }
            if (!silent) B2ULogError(B2UMessageSource.Runtime, "Could not find B2UEdgeDetection. Use the auto setup button or make sure you have added it to your active URP Forward Renderer.");
            return null;
        }

        /// <summary>
        /// Finds the B2UFullscreenEffects feature on the active renderer asset if it exists.
        /// </summary>
        public static B2UFullscreenEffects TryAutoFindFullscreenFeature(bool silent = false)
        {
            if (FullscreenFeature != null) return FullscreenFeature;
            var rendererData = GetActiveRendererData(silent);
            if (rendererData != null)
            {
                foreach (var feature in rendererData.rendererFeatures)
                {
                    if (feature is B2UFullscreenEffects b2uFeature)
                    {
                        FullscreenFeature = b2uFeature;
                        return b2uFeature;
                    }
                }
            }
            if (!silent) B2ULogError(B2UMessageSource.Runtime, "Could not find B2UFullscreenEffects. Use the auto setup button or make sure you have added it to your active URP Forward Renderer.");
            return null;
        }

        /// <summary>
        /// Searches the active scene for a B2URuntimeManager component and binds it as the active singleton instance if found.
        /// </summary>
        /// <returns>The located B2URuntimeManager, or null if none exists in the scene.</returns>
        public static B2URuntimeManager TryAutoFindRuntimeManager()
        {
            B2URuntimeManager existingManager = UnityEngine.Object.FindAnyObjectByType<B2URuntimeManager>();
            if (existingManager != null)
            {
                existingManager.SetInstance();
                return existingManager;
            }
            return null;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void AutoSetupRefractionFeature()
        {
#if UNITY_EDITOR
            var rendererData = GetActiveRendererData();
            if (rendererData == null) return;

            B2UVolumetricRefraction feature = TryAutoFindRefractionFeature(true);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<B2UVolumetricRefraction>();
                feature.name = "B2U Volumetric Refraction";
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                RefractionFeature = feature;
            }

            feature.settings.overrideMaterial = FindB2UMaterial("B2UVolumetricRefractionMat");
            EnforceFeatureOrder(rendererData);
            PushAllGlobals();
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void AutoSetupEdgeFeature()
        {
#if UNITY_EDITOR
            var rendererData = GetActiveRendererData();
            if (rendererData == null) return;

            B2UEdgeDetection feature = TryAutoFindEdgeFeature(true);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<B2UEdgeDetection>();
                feature.name = "B2U Edge Detection";
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                EdgeFeature = feature;
            }

            feature.settings.overrideMaterial = FindB2UMaterial("B2UObjectIDMat");
            feature.settings.fullscreenMaterial = FindB2UMaterial("B2UEdgeDetectionMat");
            EnforceFeatureOrder(rendererData);
            PushAllGlobals();
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void AutoSetupFullscreenFeature()
        {
#if UNITY_EDITOR
            var rendererData = GetActiveRendererData();
            if (rendererData == null) return;

            B2UFullscreenEffects feature = TryAutoFindFullscreenFeature(true);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<B2UFullscreenEffects>();
                feature.name = "B2U Fullscreen Effects";
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                FullscreenFeature = feature;
            }

            feature.settings.immersionMat = FindB2UMaterial("B2UImmersionMat");
            feature.settings.recolorMat = FindB2UMaterial("B2URecolorMat");
            feature.settings.stylizeMat = FindB2UMaterial("B2UStylizeMat");
            feature.settings.statusMat = FindB2UMaterial("B2UStatusEffectMat");
            EnforceFeatureOrder(rendererData);
            PushAllGlobals();
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void AutoSetupRuntimeManager()
        {
#if UNITY_EDITOR
            B2URuntimeManager existingManager = UnityEngine.Object.FindAnyObjectByType<B2URuntimeManager>();
            if (existingManager != null)
            {
                existingManager.SetInstance();
                B2ULog(B2UMessageSource.Editor, "A B2URuntimeManager already exists in the scene.", B2UMessageType.None);
                return;
            }
            GameObject managerObj = new GameObject("B2U Runtime Manager");
            B2URuntimeManager manager = managerObj.AddComponent<B2URuntimeManager>();
            manager.SetInstance();
            Undo.RegisterCreatedObjectUndo(managerObj, "Create B2U Runtime Manager");

            Selection.activeGameObject = managerObj;
            B2ULog(B2UMessageSource.Editor, "Successfully created a new B2URuntimeManager in the scene.", B2UMessageType.None);
#endif
        }

        /// <summary>
        /// Toggles a URP ScriptableRendererFeature in the Editor and forces URP to rebuild its render graph immediately.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void SetFeatureActiveState(ScriptableRendererFeature feature, bool isActive)
        {
#if UNITY_EDITOR
            if (feature == null || feature.isActive == isActive) return;

            feature.SetActive(isActive);
            EditorUtility.SetDirty(feature);

            var rendererData = GetActiveRendererData();
            if (rendererData != null)
            {
                EditorUtility.SetDirty(rendererData);
                rendererData.SetDirty();
            }

            ForceURPRebuild();
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void SetRendererTransparentLayerMask(int maskValue)
        {
#if UNITY_EDITOR
            if (GetActiveRendererData(true) is UniversalRendererData uniData)
            {
                uniData.transparentLayerMask = maskValue;
                EditorUtility.SetDirty(uniData);
                uniData.SetDirty();
            }
#endif
        }

#if UNITY_EDITOR
        private static Material FindB2UMaterial(string matName)
        {
            string[] guids = AssetDatabase.FindAssets($"{matName} t:Material");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            B2ULogError(B2UMessageSource.Editor, $"Could not find material: {matName}");
            return null;
        }

        private static void EnforceFeatureOrder(ScriptableRendererData rendererData)
        {
            rendererData.rendererFeatures.RemoveAll(f => f == null);

            Type[] strictOrder = { typeof(B2UEdgeDetection), typeof(B2UVolumetricRefraction), typeof(B2UFullscreenEffects) };

            rendererData.rendererFeatures.Sort((a, b) =>
            {
                int indexA = Array.IndexOf(strictOrder, a.GetType());
                int indexB = Array.IndexOf(strictOrder, b.GetType());

                if (indexA == -1) indexA = 999;
                if (indexB == -1) indexB = 999;

                return indexA.CompareTo(indexB);
            });

            EditorUtility.SetDirty(rendererData);
            rendererData.SetDirty();
            AssetDatabase.SaveAssets();

            ForceURPRebuild();
        }

        private static void ForceURPRebuild()
        {
            // Re-assigning the active pipeline asset forces Unity to tear down and re-initialize the URP rendering chain
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                GraphicsSettings.defaultRenderPipeline = GraphicsSettings.defaultRenderPipeline;
            }
            if (QualitySettings.renderPipeline != null)
            {
                QualitySettings.renderPipeline = QualitySettings.renderPipeline;
            }
        }
#endif
        #endregion

        #region Fullscreen Functions
        /// <summary>
        /// Disables all shader keywords in the provided string array.
        /// </summary>
        /// <param name="keywordsToDisable">The array of string keywords to disable</param>
        public static void DisableKeywords(string[] keywordsToDisable)
        {
            if (keywordsToDisable == null) return;
            for (int i = 0; i < keywordsToDisable.Length; i++) Shader.DisableKeyword(keywordsToDisable[i]);
        }

        /// <summary>
        /// Bakes a Unity Gradient into a 256x1 clamped Texture2D.
        /// </summary>
        /// <param name="grad">The Gradient object</param>
        /// <param name="tex">The target texture to populate (or null to allocate a new 256x1 texture).</param>
        /// <returns>The 256x1 gradient-baked Texture2D</returns>
        public static Texture2D BuildGradientTexture(Gradient grad, Texture2D tex)
        {
            if (grad == null) return tex;
            if (tex == null) tex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[256];
            for (int i = 0; i < 256; i++)
            {
                pixels[i] = grad.Evaluate(i / 255f);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Shorthand extension for setting a liquid GameObject's material as the active fullscreen immersion liquid.
        /// </summary>
        /// <param name="gameObject">The liquid object to set as the immersed liquid.</param>
        /// <example>
        /// <code>
        /// public Transform player;
        /// private GameObject currentLiquid = null;
        /// 
        /// private void Update()
        /// {
        ///     // Constantly raycast downward from the player
        ///     if (Physics.Raycast(player.position, player.down, out RaycastHit hit, 10f)
        ///     {
        ///         // Ensure that the hit object is a B2U Liquid (tag is automatically set by the auto-applyer)
        ///         if (hit.collider.gameObject.tag == "B2ULiquid")
        ///         {
        ///             if (hit.collider.gameObject != currentLiquid)
        ///             {
        ///                 currentLiquid = hit.collider.gameObject;
        ///                 currentLiquid.SetAsImmersedLiquid();
        ///             }
        ///         }
        ///     }
        /// }
        /// </code>
        /// </example>
        public static void SetAsImmersedLiquid(this GameObject gameObject)
        {
            if (gameObject == null || activeProfile == null) return;

            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.ImmersionActive, out bool immersionActive, true))
            {
                if (!immersionActive)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.ImmersionActive, true);
                    B2ULogWarning(B2UMessageSource.Runtime, "The active profile did not have fullscreen immersion enabled. It has been force enabled.");
                }
            }

            if (gameObject.TryGetComponent<B2URSUVMono>(out B2URSUVMono mono))
            {
                mono.PushLiquidToImmersionEffect();
            }
            else
            {
                B2ULogError(B2UMessageSource.Runtime, "The object you are trying to set as the immersed liquid does not have a B2URSUVMono component on it. This is needed with a valid material ID to set the effect.", gameObject);
            }
        }

        /// <summary>
        /// Sets a recolor preset from the active recolor preset collection by index.
        /// </summary>
        /// <param name="index">The index to use.</param>
        /// <returns>The B2URecolorPreset that was set.</returns>
        public static B2URecolorPreset SetRecolorPreset(int index)
        {
            if (!CheckActiveProfile(null, false)) return null;
            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.RecolorPresets, out B2URecolorPresetCollection collection, true))
            {
                if (collection != null)
                {
                    B2URecolorPreset preset = collection.GetPreset(index);
                    if (preset != null)
                    {
                        preset.Apply();
                        collection.activePreset = index;
                        return preset;
                    }
                }
                else B2ULogError(B2UMessageSource.Runtime, "The recolor collection on the active profile is null.");
            }
            else B2ULogError(B2UMessageSource.Runtime, "Could not retrieve the recolor collection from the active profile.");
            return null;
        }

        /// <summary>
        /// Sets a recolor preset from the active recolor preset collection by name.
        /// </summary>
        /// <param name="presetName">The name of the recolor preset to use.</param>
        /// <returns>The B2URecolorPreset that was set.</returns>
        public static B2URecolorPreset SetRecolorPreset(string presetName)
        {
            if (!CheckActiveProfile(null, false)) return null;
            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.RecolorPresets, out B2URecolorPresetCollection collection, true))
            {
                if (collection != null)
                {
                    B2URecolorPreset preset = collection.GetPreset(presetName);
                    if (preset != null)
                    {
                        preset.Apply();
                        collection.activePreset = collection.presets.IndexOf(preset);
                        return preset;
                    }
                }
                else B2ULogError(B2UMessageSource.Runtime, "The recolor collection on the active profile is null.");
            }
            else B2ULogError(B2UMessageSource.Runtime, "Could not retrieve the recolor collection from the active profile.");
            return null;
        }

        /// <summary>
        /// Triggers the runtime manager to animate in a recolor effect by index.
        /// </summary>
        /// <param name="recolorEffectIndex">The index in the active recolor preset collection to use.</param>
        /// <param name="targetBlend">The target blend strength.</param>
        /// <param name="timeToBlend">The time it takes in seconds to go from the current blend strength to the target blend strength.</param>
        public static void TriggerRecolorEffect(int recolorEffectIndex, float targetBlend = 1f, float timeToBlend = 1f)
        {
            if (activeProfile == null || B2URuntimeManager.Instance == null) return;

            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.RecolorActive, out bool recolorEnabled, true))
            {
                if (!recolorEnabled)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.RecolorActive, true);
                    B2ULogWarning(B2UMessageSource.Runtime, "The active profile did not have fullscreen recolor enabled. It has been force enabled.");
                }
            }

            B2URecolorPreset preset = SetRecolorPreset(recolorEffectIndex);
            if (preset == null) return;

            if (B2URuntimeManager.Instance.recolorCoroutine != null) B2URuntimeManager.Instance.StopCoroutine(B2URuntimeManager.Instance.recolorCoroutine);
            B2URuntimeManager.Instance.recolorCoroutine = B2URuntimeManager.Instance.StartCoroutine(TriggerRecolorEffectEnumerator(targetBlend, timeToBlend));
        }

        /// <summary>
        /// Triggers the runtime manager to animate in a recolor effect by name.
        /// </summary>
        /// <param name="recolorEffectName">The name of the recolor preset in the active recolor preset collection to use.</param>
        /// <param name="targetBlend">The target blend strength.</param>
        /// <param name="timeToBlend">The time it takes in seconds to go from the current blend strength to the target blend strength.</param>
        public static void TriggerRecolorEffect(string recolorEffectName, float targetBlend = 1f, float timeToBlend = 1f)
        {
            if (activeProfile == null || B2URuntimeManager.Instance == null) return;

            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.RecolorActive, out bool recolorEnabled, true))
            {
                if (!recolorEnabled)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.RecolorActive, true);
                    B2ULogWarning(B2UMessageSource.Runtime, "The active profile did not have fullscreen recolor enabled. It has been force enabled.");
                }
            }

            B2URecolorPreset preset = SetRecolorPreset(recolorEffectName);
            if (preset == null) return;

            if (B2URuntimeManager.Instance.recolorCoroutine != null) B2URuntimeManager.Instance.StopCoroutine(B2URuntimeManager.Instance.recolorCoroutine);
            B2URuntimeManager.Instance.recolorCoroutine = B2URuntimeManager.Instance.StartCoroutine(TriggerRecolorEffectEnumerator(targetBlend, timeToBlend));
        }

        private static System.Collections.IEnumerator TriggerRecolorEffectEnumerator(float targetBlend, float timeToBlend)
        {
            float t = 0f;
            float startBlend = GetB2UShaderPropertyValue(B2UShaderProperty.RecolorBlend, 0f, true);
            if (timeToBlend > 0f)
            {
                while (t < timeToBlend)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.RecolorBlend, Mathf.Lerp(startBlend, targetBlend, t / timeToBlend));
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            SetB2UShaderPropertyValue(B2UShaderProperty.RecolorBlend, targetBlend);
            if (B2URuntimeManager.Instance != null) B2URuntimeManager.Instance.recolorCoroutine = null;
        }

        /// <summary>
        /// Sets a stylize preset from the active stylize preset collection by index.
        /// </summary>
        /// <param name="index">The index to use.</param>
        /// <returns>The B2UStylizePreset that was set.</returns>
        public static B2UStylizePreset SetStylizePreset(int index)
        {
            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StylizePresets, out B2UStylizePresetCollection collection, true))
            {
                if (collection != null)
                {
                    B2UStylizePreset preset = collection.GetPreset(index);
                    if (preset != null)
                    {
                        preset.Apply();
                        collection.activePreset = index;
                        return preset;
                    }
                }
                else B2ULogError(B2UMessageSource.Runtime, "The stylize collection on the active profile is null.");
            }
            else B2ULogError(B2UMessageSource.Runtime, "Could not retrieve the stylize collection from the active profile.");
            return null;
        }

        /// <summary>
        /// Sets a stylize preset from the active stylize preset collection by name.
        /// </summary>
        /// <param name="presetName">The name of the stylize preset to use.</param>
        /// <returns>The B2UStylizePreset that was set.</returns>
        public static B2UStylizePreset SetStylizePreset(string presetName)
        {
            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StylizePresets, out B2UStylizePresetCollection collection, true))
            {
                if (collection != null)
                {
                    B2UStylizePreset preset = collection.GetPreset(presetName);
                    if (preset != null)
                    {
                        preset.Apply();
                        collection.activePreset = collection.presets.IndexOf(preset);
                        return preset;
                    }
                }
                else B2ULogError(B2UMessageSource.Runtime, "The stylize collection on the active profile is null.");
            }
            else B2ULogError(B2UMessageSource.Runtime, "Could not retrieve the stylize collection from the active profile.");
            return null;
        }

        /// <summary>
        /// Triggers the runtime manager to animate in a stylize effect by index.
        /// </summary>
        /// <param name="stylizeEffectIndex">The index in the active stylize preset collection to use.</param>
        /// <param name="targetBlend">The target blend strength.</param>
        /// <param name="timeToBlend">The time it takes in seconds to go from the current blend strength to the target blend strength.</param>
        public static void TriggerStylizeEffect(int stylizeEffectIndex, float targetBlend = 1f, float timeToBlend = 1f)
        {
            if (activeProfile == null || B2URuntimeManager.Instance == null) return;

            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StylizeActive, out bool stylizeEnabled, true))
            {
                if (!stylizeEnabled)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.StylizeActive, true);
                    B2ULogWarning(B2UMessageSource.Runtime, "The active profile did not have fullscreen stylize enabled. It has been force enabled.");
                }
            }

            B2UStylizePreset preset = SetStylizePreset(stylizeEffectIndex);
            if (preset == null) return;

            if (B2URuntimeManager.Instance.stylizeCoroutine != null) B2URuntimeManager.Instance.StopCoroutine(B2URuntimeManager.Instance.stylizeCoroutine);
            B2URuntimeManager.Instance.stylizeCoroutine = B2URuntimeManager.Instance.StartCoroutine(TriggerStylizeEffectEnumerator(targetBlend, timeToBlend));
        }

        /// <summary>
        /// Triggers the runtime manager to animate in a stylize effect by name.
        /// </summary>
        /// <param name="stylizeEffectName">The name of the stylize preset in the active stylize preset collection to use.</param>
        /// <param name="targetBlend">The target blend strength.</param>
        /// <param name="timeToBlend">The time it takes in seconds to go from the current blend strength to the target blend strength.</param>
        public static void TriggerStylizeEffect(string stylizeEffectName, float targetBlend = 1f, float timeToBlend = 1f)
        {
            if (activeProfile == null || B2URuntimeManager.Instance == null) return;

            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StylizeActive, out bool stylizeEnabled, true))
            {
                if (!stylizeEnabled)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.StylizeActive, true);
                    B2ULogWarning(B2UMessageSource.Runtime, "The active profile did not have fullscreen stylize enabled. It has been force enabled.");
                }
            }

            B2UStylizePreset preset = SetStylizePreset(stylizeEffectName);
            if (preset == null) return;

            if (B2URuntimeManager.Instance.stylizeCoroutine != null) B2URuntimeManager.Instance.StopCoroutine(B2URuntimeManager.Instance.stylizeCoroutine);
            B2URuntimeManager.Instance.stylizeCoroutine = B2URuntimeManager.Instance.StartCoroutine(TriggerStylizeEffectEnumerator(targetBlend, timeToBlend));
        }

        private static System.Collections.IEnumerator TriggerStylizeEffectEnumerator(float targetBlend, float timeToBlend)
        {
            float t = 0f;
            float startBlend = GetB2UShaderPropertyValue(B2UShaderProperty.StylizeBlend, 0f, true);
            if (timeToBlend > 0f)
            {
                while (t < timeToBlend)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.StylizeBlend, Mathf.Lerp(startBlend, targetBlend, t / timeToBlend));
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            SetB2UShaderPropertyValue(B2UShaderProperty.StylizeBlend, targetBlend);
            if (B2URuntimeManager.Instance != null) B2URuntimeManager.Instance.stylizeCoroutine = null;
        }

        /// <summary>
        /// Sets a status effect preset from the active status effect preset collection by index.
        /// </summary>
        /// <param name="index">The index to use.</param>
        /// <returns>The B2UStatusEffectPreset that was set.</returns>
        public static B2UStatusEffectPreset SetStatusEffectPreset(int index)
        {
            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StatusPresets, out B2UStatusEffectPresetCollection collection, true))
            {
                if (collection != null)
                {
                    B2UStatusEffectPreset preset = collection.GetPreset(index);
                    if (preset != null)
                    {
                        collection.activePreset = index;
                        preset.Apply();
                        return preset;
                    }
                }
                else B2ULogError(B2UMessageSource.Runtime, "The status effect collection on the active profile is null.");
            }
            else B2ULogError(B2UMessageSource.Runtime, "Could not retrieve the status effect collection from the active profile.");
            return null;
        }

        /// <summary>
        /// Sets a status effect preset from the active status effect preset collection by name.
        /// </summary>
        /// <param name="presetName">The name of the status effect preset to use.</param>
        /// <returns>The B2UStatusEffectPreset that was set.</returns>
        public static B2UStatusEffectPreset SetStatusEffectPreset(string presetName)
        {
            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StatusPresets, out B2UStatusEffectPresetCollection collection, true))
            {
                if (collection != null)
                {
                    B2UStatusEffectPreset preset = collection.GetPreset(presetName);
                    if (preset != null)
                    {
                        preset.Apply();
                        collection.activePreset = collection.presets.IndexOf(preset);
                        return preset;
                    }
                }
                else B2ULogError(B2UMessageSource.Runtime, "The status effect collection on the active profile is null.");
            }
            else B2ULogError(B2UMessageSource.Runtime, "Could not retrieve the status effect collection from the active profile.");
            return null;
        }

        /// <summary>
        /// Triggers the runtime manager to animate in a status effect by index.
        /// </summary>
        /// <param name="statusEffectIndex">The index in the active status effect preset collection to use.</param>
        /// <param name="targetStrength">The target status effect strength.</param>
        /// <param name="timeToBlend">The time in seconds that it takes to go from the start strength to the target strength.</param>
        /// <param name="timeLimit">The time limit for the effect to last. 0 lasts indefinitely until manually stopped.</param>
        public static void TriggerStatusEffect(int statusEffectIndex, float targetStrength = 1f, float timeToBlend = 1f, float timeLimit = 0f)
        {
            if (activeProfile == null || B2URuntimeManager.Instance == null) return;

            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StatusActive, out bool statusEnabled, true))
            {
                if (!statusEnabled)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.StatusActive, true);
                    B2ULogWarning(B2UMessageSource.Runtime, "The active profile did not have fullscreen status effects enabled. It has been force enabled.");
                }
            }

            B2UStatusEffectPreset preset = SetStatusEffectPreset(statusEffectIndex);
            if (preset == null) return;

            B2URuntimeManager.Instance.activeStatusEffectType = preset.statusEffectType;
            if (B2URuntimeManager.Instance.statusEffectCoroutine != null) B2URuntimeManager.Instance.StopCoroutine(B2URuntimeManager.Instance.statusEffectCoroutine);
            B2URuntimeManager.Instance.statusEffectCoroutine = B2URuntimeManager.Instance.StartCoroutine(TriggerStatusEffectEnumerator(Mathf.Clamp01(targetStrength), timeToBlend, timeLimit));
        }

        /// <summary>
        /// Triggers the runtime manager to animate in a status effect by name.
        /// </summary>
        /// <param name="statusEffectName">The name of the status effect preset in the active status effect preset collection to use.</param>
        /// <param name="targetStrength">The target status effect strength.</param>
        /// <param name="timeToBlend">The time in seconds that it takes to go from the start strength to the target strength.</param>
        /// <param name="timeLimit">The time limit for the effect to last. 0 lasts indefinitely until manually stopped.</param>
        public static void TriggerStatusEffect(string statusEffectName, float targetStrength = 1f, float timeToBlend = 1f, float timeLimit = 0f)
        {
            if (activeProfile == null || B2URuntimeManager.Instance == null) return;

            if (TryGetB2UShaderPropertyValue(B2UShaderProperty.StatusActive, out bool statusEnabled, true))
            {
                if (!statusEnabled)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.StatusActive, true);
                    B2ULogWarning(B2UMessageSource.Runtime, "The active profile did not have fullscreen status effects enabled. It has been force enabled.");
                }
            }

            B2UStatusEffectPreset preset = SetStatusEffectPreset(statusEffectName);
            if (preset == null) return;

            B2URuntimeManager.Instance.activeStatusEffectType = preset.statusEffectType;
            if (B2URuntimeManager.Instance.statusEffectCoroutine != null) B2URuntimeManager.Instance.StopCoroutine(B2URuntimeManager.Instance.statusEffectCoroutine);
            B2URuntimeManager.Instance.statusEffectCoroutine = B2URuntimeManager.Instance.StartCoroutine(TriggerStatusEffectEnumerator(Mathf.Clamp01(targetStrength), timeToBlend, timeLimit));
        }

        private static System.Collections.IEnumerator TriggerStatusEffectEnumerator(float maxStrength, float timeToMax, float timeLimit)
        {
            float startStrength = GetB2UShaderPropertyValue(B2UShaderProperty.StatusStrength, 0f, true);
            float t = 0f;
            B2URuntimeManager.Instance.hasActiveStatusEffect = true;
            if (timeToMax > 0f)
            {
                while (t < timeToMax)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.StatusStrength, Mathf.Lerp(startStrength, maxStrength, t / timeToMax));
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            SetB2UShaderPropertyValue(B2UShaderProperty.StatusStrength, maxStrength);

            if (timeLimit <= 0f)
            {
                B2URuntimeManager.Instance.statusEffectCoroutine = null;
                yield break;
            }

            t = 0f;
            while (t < timeLimit)
            {
                t += Time.deltaTime;
                yield return null;
            }

            t = 0f;
            if (timeToMax > 0f)
            {
                while (t < timeToMax)
                {
                    SetB2UShaderPropertyValue(B2UShaderProperty.StatusStrength, Mathf.Lerp(maxStrength, 0f, t / timeToMax));
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            B2URuntimeManager.Instance.hasActiveStatusEffect = false;
            B2URuntimeManager.Instance.statusEffectCoroutine = null;
            SetB2UShaderPropertyValue(B2UShaderProperty.StatusStrength, 0f);
        }
        #endregion

        #region Material Functions
        [BurstCompile]
        private struct B2UMaterialJob : IJobParallelFor
        {
            public NativeArray<Color32> colors;
            public byte newAlpha;
            public byte oldAlpha;
            public bool replaceAll;

            public void Execute(int index)
            {
                if (replaceAll || colors[index].a == oldAlpha)
                {
                    Color32 c = colors[index];
                    c.a = newAlpha;
                    colors[index] = c;
                }
            }
        }

        private static bool IsSolidWorkflow(B2UWorkflow workflow)
        {
            return workflow == B2UWorkflow.Opaque ||
                   workflow == B2UWorkflow.OpaqueDoubleSided ||
                   workflow == B2UWorkflow.Transparent;
        }

        private static Material GetMasterMaterialForWorkflow(B2UWorkflow workflow, Mesh mesh)
        {
            if (activeProfile == null || activeProfile.masterMaterials == null || activeProfile.masterMaterials.Length < 7)
            {
                B2ULogError(B2UMessageSource.Runtime, "Master materials array is missing or improperly configured on the active B2UShaderProfile.");
                return null;
            }
            switch (workflow)
            {
                case B2UWorkflow.Opaque: return activeProfile.masterMaterials[0];
                case B2UWorkflow.OpaqueDoubleSided: return activeProfile.masterMaterials[1];
                case B2UWorkflow.Transparent: return activeProfile.masterMaterials[2];
                case B2UWorkflow.Crystal: return activeProfile.masterMaterials[3];
                case B2UWorkflow.Liquid:
                    if (mesh != null && mesh.vertexCount > 0)
                    {
                        // Liquid mesh data is packed into UV1.y (B2U_LiquidData)
                        s_TempUVList.Clear();
                        mesh.GetUVs(1, s_TempUVList);
                        if (s_TempUVList.Count > 0)
                        {
                            uint packedLiquidData = (uint)Mathf.RoundToInt(s_TempUVList[0].y);
                            bool tessellated = ((packedLiquidData >> 8) & 1u) != 0;
                            bool volumetricRefraction = ((packedLiquidData >> 9) & 1u) != 0;

                            if (tessellated) return activeProfile.masterMaterials[6];
                            if (volumetricRefraction) return activeProfile.masterMaterials[5];
                        }
                    }
                    return activeProfile.masterMaterials[4];
                default:
                    return activeProfile.masterMaterials[0];
            }
        }

        private static B2UWorkflow GetWorkflowFromShader(Shader shader)
        {
            if (shader == null) return B2UWorkflow.Opaque;
            string name = shader.name;

            if (name.Contains("Liquid")) return B2UWorkflow.Liquid;
            if (name.Contains("Crystal")) return B2UWorkflow.Crystal;
            if (name.Contains("Transparent")) return B2UWorkflow.Transparent;
            if (name.Contains("Double Sided") || name.Contains("DS")) return B2UWorkflow.OpaqueDoubleSided;

            return B2UWorkflow.Opaque;
        }

        private static bool IsValidWorkflowTransition(B2UWorkflow oldWorkflow, B2UWorkflow newWorkflow)
        {
            if (oldWorkflow == newWorkflow) return true;
            if (oldWorkflow == B2UWorkflow.Liquid || newWorkflow == B2UWorkflow.Liquid) return false;
            if (oldWorkflow == B2UWorkflow.Crystal && IsSolidWorkflow(newWorkflow)) return false;

            return true;
        }

        private static bool ValidateWorkflowChange(Renderer renderer, Mesh mesh, B2UMaterial newMat, B2UMaterial oldMat, bool isPartial)
        {
            B2UWorkflow currentWorkflow = oldMat != null ? oldMat.workflow : GetWorkflowFromShader(renderer.sharedMaterial?.shader);

            if (!IsValidWorkflowTransition(currentWorkflow, newMat.workflow))
            {
                B2ULogError(B2UMessageSource.Runtime, $"Material change blocked! Illegal workflow transition from {currentWorkflow} to {newMat.workflow} due to incompatible UV layouts.");
                return false;
            }

            if (isPartial)
            {
                if (currentWorkflow != newMat.workflow)
                {
                    B2ULogError(B2UMessageSource.Runtime, "Material change blocked! Cannot partially replace vertices with a different workflow. Submeshes must have a uniform workflow.");
                    return false;
                }
                return true;
            }

            Material expectedMat = GetMasterMaterialForWorkflow(newMat.workflow, mesh);
            if (expectedMat != null && renderer.sharedMaterial != expectedMat)
            {
                renderer.sharedMaterial = expectedMat;
                B2ULog(B2UMessageSource.Runtime, $"Safely updated {renderer.gameObject.name}'s material to '{expectedMat.name}' to support the '{newMat.workflow}' workflow change.");
            }
            return true;
        }

        private static void ApplyB2UMaterialJob(Mesh mesh, byte newAlpha, int oldAlpha, bool replaceAll)
        {
            if (mesh == null) return;
            Color32[] meshColors = mesh.colors32;
            if (meshColors == null || meshColors.Length == 0) return;

            NativeArray<Color32> nativeColors = new NativeArray<Color32>(meshColors, Allocator.TempJob);

            B2UMaterialJob job = new B2UMaterialJob
            {
                colors = nativeColors,
                newAlpha = newAlpha,
                oldAlpha = (byte)Mathf.Max(0, oldAlpha),
                replaceAll = replaceAll
            };

            JobHandle handle = job.Schedule(nativeColors.Length, 128);
            handle.Complete();

            mesh.SetColors(nativeColors);
            nativeColors.Dispose();
        }

        private static bool TryGetMeshAndRenderer(GameObject obj, out Mesh sharedMesh, out Renderer renderer)
        {
            sharedMesh = null;
            renderer = null;
            if (obj == null) return false;

            if (obj.TryGetComponent<MeshFilter>(out MeshFilter mf))
            {
                renderer = obj.GetComponent<MeshRenderer>();
                sharedMesh = mf.sharedMesh;
                if (sharedMesh == null) B2ULogError(B2UMessageSource.Runtime, $"Object '{obj.name}' has a MeshFilter but no mesh is assigned.", obj);
                return sharedMesh != null && renderer != null;
            }
            if (obj.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer smr))
            {
                renderer = smr;
                sharedMesh = smr.sharedMesh;
                if (sharedMesh == null) B2ULogError(B2UMessageSource.Runtime, $"Object '{obj.name}' has a SkinnedMeshRenderer but no mesh is assigned.", obj);
                return sharedMesh != null;
            }

            B2ULogError(B2UMessageSource.Runtime, $"Object '{obj.name}' does not have an active MeshFilter or SkinnedMeshRenderer.", obj);
            return false;
        }

        private static Mesh CloneMeshForModification(GameObject obj)
        {
            if (obj.TryGetComponent<MeshFilter>(out MeshFilter mf))
            {
                return mf.mesh;
            }
            if (obj.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer smr))
            {
                Mesh targetMesh = smr.sharedMesh;
                if (targetMesh != null && !targetMesh.name.EndsWith("Instance"))
                {
                    targetMesh = UnityEngine.Object.Instantiate(smr.sharedMesh);
                    targetMesh.name += " Instance";
                    smr.sharedMesh = targetMesh;
                }
                return targetMesh;
            }
            return null;
        }

        /// <summary>
        /// Sets the given material into the mesh alpha data using Parallel Jobs. This change is volatile and does not write to the underlying mesh asset.
        /// </summary>
        /// <param name="obj">The GameObject with the mesh you are trying to update the material on.</param>
        /// <param name="material">A B2UMaterial object which you want to set.</param>
        /// <param name="indexToReplace">An optional 0-255 parameter to limit the replacement to a specific existing material on the object. -1 means it will override the entire object.</param>
        /// <example>
        /// <code>
        /// public GameObject objectToSwap;
        /// 
        /// private void SetNewMaterial()
        /// {
        ///     B2UMaterial newMat = B2URuntimeUtility.ActiveProfile.paletteData.materials.GetMaterialByName("Iron");
        ///     
        ///     // By not setting the index to replace, it will overwrite all materials on the mesh to the new material.
        ///     objectToSwap.SetB2UMaterial(newMat);
        /// }
        /// </code>
        /// </example>
        /// <remarks>WARNING: This instantiates a unique clone of the mesh. You must call B2UDestroy() when destroying this GameObject to prevent memory leaks.</remarks>
        public static void SetB2UMaterial(this GameObject obj, B2UMaterial material, int indexToReplace = -1)
        {
            if (obj == null || material == null) return;

            B2UMaterial oldMat = null;
            if (indexToReplace != -1)
            {
                if (indexToReplace < 0 || indexToReplace > 255)
                {
                    B2ULogError(B2UMessageSource.Runtime, "The index to replace given is out of the 0-255 range. Cannot set material.");
                    return;
                }
                if (activeProfile?.paletteData?.materials != null)
                {
                    oldMat = activeProfile.paletteData.materials.GetMaterialByIndex((byte)indexToReplace);
                }
            }

            if (!TryGetMeshAndRenderer(obj, out Mesh sharedMesh, out Renderer renderer)) return;
            if (!ValidateWorkflowChange(renderer, sharedMesh, material, oldMat, indexToReplace != -1)) return;

            Mesh instancedMesh = CloneMeshForModification(obj);
            ApplyB2UMaterialJob(instancedMesh, material.alphaValue, indexToReplace, indexToReplace == -1);
        }

        /// <summary>
        /// Sets the given material into the mesh alpha data using Parallel Jobs. This change is volatile and does not write to the underlying mesh asset.
        /// </summary>
        /// <param name="obj">The GameObject with the mesh you are trying to update the material on.</param>
        /// <param name="newMaterial">The B2UMaterial you want to set.</param>
        /// <param name="oldMaterial">The B2UMaterial you want to replace.</param>
        /// <example>
        /// <code>
        /// public GameObject objectToSwap;
        /// 
        /// private void SwapSpecificMaterials()
        /// {
        ///     // Get the new and old materials by name.
        ///     B2UMaterial newMat = B2URuntimeUtility.ActiveProfile.paletteData.materials.GetMaterialByName("Wood");
        ///     B2UMaterial oldMat = B2URuntimeUtility.ActiveProfile.paletteData.materials.GetMaterialByName("Stone");
        ///     
        ///     // Replace the old material with the new one. This method will check if the change you are trying to perform is illegal like changing workflows and prevent execution.
        ///     objectToSwap.SetB2UMaterial(newMat, oldMat);
        /// }
        /// </code>
        /// </example>
        /// <remarks>WARNING: This instantiates a unique clone of the mesh. You must call B2UDestroy() when destroying this GameObject to prevent memory leaks.</remarks>
        public static void SetB2UMaterial(this GameObject obj, B2UMaterial newMaterial, B2UMaterial oldMaterial)
        {
            if (obj == null || newMaterial == null || oldMaterial == null) return;
            if (!TryGetMeshAndRenderer(obj, out Mesh sharedMesh, out Renderer renderer)) return;
            if (!ValidateWorkflowChange(renderer, sharedMesh, newMaterial, oldMaterial, true)) return;

            Mesh instancedMesh = CloneMeshForModification(obj);
            ApplyB2UMaterialJob(instancedMesh, newMaterial.alphaValue, oldMaterial.alphaValue, false);
        }

        /// <summary>
        /// Sets the given material into the mesh alpha data using Parallel Jobs. This change is volatile and does not write to the underlying mesh asset.
        /// </summary>
        /// <param name="obj">The GameObject with the mesh you are trying to update the material on.</param>
        /// <param name="newMaterial">The slot name of the B2UMaterial you want to set.</param>
        /// <param name="oldMaterial">An optional string for the slot name of the B2UMaterial you want to replace. Leaving it blank will override the whole mesh</param>
        /// <remarks>WARNING: This instantiates a unique clone of the mesh. You must call B2UDestroy() when destroying this GameObject to prevent memory leaks.</remarks>
        public static void SetB2UMaterial(this GameObject obj, string newMaterial, string oldMaterial = "")
        {
            if (obj == null) return;
            if (activeProfile?.paletteData?.materials == null)
            {
                B2ULogError(B2UMessageSource.Runtime, "The active palette data does not have the material container initialized.");
                return;
            }

            B2UMaterial newMat = activeProfile.paletteData.materials.GetMaterialByName(newMaterial);
            B2UMaterial oldMat = string.IsNullOrEmpty(oldMaterial) ? null : activeProfile.paletteData.materials.GetMaterialByName(oldMaterial);

            if (newMat == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Target material '{newMaterial}' not found in palette.");
                return;
            }

            if (!TryGetMeshAndRenderer(obj, out Mesh sharedMesh, out Renderer renderer)) return;

            bool isPartial = oldMat != null;
            if (!ValidateWorkflowChange(renderer, sharedMesh, newMat, oldMat, isPartial)) return;

            Mesh instancedMesh = CloneMeshForModification(obj);
            ApplyB2UMaterialJob(instancedMesh, newMat.alphaValue, oldMat != null ? oldMat.alphaValue : -1, !isPartial);
        }

        /// <summary>
        /// Destroys a B2U GameObject and cleans up any runtime-instanced mesh generated by SetB2UMaterial().
        /// </summary>
        /// <param name="obj">The GameObject to destroy.</param>
        public static void B2UDestroy(GameObject obj)
        {
            if (obj == null) return;

            if (obj.TryGetComponent<MeshFilter>(out MeshFilter mf) && mf.sharedMesh != null && mf.sharedMesh.name.EndsWith("Instance"))
            {
                UnityEngine.Object.Destroy(mf.sharedMesh);
            }
            else if (obj.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer smr) && smr.sharedMesh != null && smr.sharedMesh.name.EndsWith("Instance"))
            {
                UnityEngine.Object.Destroy(smr.sharedMesh);
            }

            UnityEngine.Object.Destroy(obj);
        }
        #endregion

        #region Palette Functions
        /// <summary>
        /// Sets the Color LUT value for a base or emission color for the given non-biome-responsive material. Volatile; does not write to disk.
        /// </summary>
        /// <param name="palette">The palette to apply the change to.</param>
        /// <param name="color">The color to set.</param>
        /// <param name="material">The material to apply the color to.</param>
        /// <param name="setEmissionColor">Whether or not to apply this to the base color or emission color.</param>
        /// <param name="variant">The variant to change. If set to -1 it will set all base or emission colors.</param>
        /// <param name="setSecondRowColor">Whether or not to apply this to the second row color. Only applies to liquids or Independent/Dynamic materials.</param>
        /// <param name="apply">Whether or not to apply the change to the GPU when the call ends.</param>
        public static void SetColorLUTPixel(B2UPaletteData palette, Color32 color, B2UMaterial material, bool setEmissionColor, int variant = -1, bool setSecondRowColor = false, bool apply = false)
        {
            if (material == null) return;
            if (palette == null)
            {
                if (activeProfile?.paletteData != null) palette = activeProfile.paletteData;
                else
                {
                    B2ULogError(B2UMessageSource.Runtime, "There is no palette data on the active profile.");
                    return;
                }
            }
            else if (palette.colorLUT == null || palette.propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Palette: {palette.name} is missing either the color or property LUT.");
                return;
            }

            if (material.workflow != B2UWorkflow.Liquid && setSecondRowColor)
            {
                B2ULogError(B2UMessageSource.Runtime, $"You are trying to set a depth color on the material: {material.slotName} which has a workflow of: {material.workflow}. Setting depth color is only valid for liquid workflows.");
                return;
            }
            if (material.biomeType == "DYNAMIC" && variant != -1)
            {
                B2ULogWarning(B2UMessageSource.Runtime, $"Warning, you are setting a variant color for material: {material.slotName} which is marked dynamic. This overload will not affect biome colors.");
            }

            Texture2D colorLUT = palette.colorLUT;
            if (colorLUT != null)
            {
                Color32[] colors = colorLUT.GetPixels32();
                SetColorLUTPixelNonAlloc(palette, color, material, setEmissionColor, variant, setSecondRowColor, colors);
                colorLUT.SetPixels32(colors);
                if (apply) colorLUT.Apply();
            }
            else
            {
                B2ULogError(B2UMessageSource.Runtime, "No Color Emission LUT texture is set on the active profile.");
            }
        }

        /// <summary>
        /// Writes a base or emission color for a non-biome-responsive material directly into a caller-provided Color32 LUT buffer without calling GetPixels32() or Apply().
        /// Ideal for batching multiple LUT edits into a single GPU upload.
        /// </summary>
        /// <param name="palette">The target B2UPaletteData, or null to use the active profile's palette.</param>
        /// <param name="color">The RGB color to write (preserves the existing alpha channel).</param>
        /// <param name="material">The B2UMaterial row to modify.</param>
        /// <param name="setEmissionColor">If true, modifies the emission color section instead of base albedo colors.</param>
        /// <param name="variant">The variant index (0-7) to update, or -1 to update all variants on the row.</param>
        /// <param name="setSecondRowColor">For Liquid workflows, if true writes to the second row (depth color) instead of the surface row.</param>
        /// <param name="buffer">The pre-fetched Color32 array matching the palette's colorLUT pixel buffer.</param>
        public static void SetColorLUTPixelNonAlloc(B2UPaletteData palette, Color32 color, B2UMaterial material, bool setEmissionColor, int variant, bool setSecondRowColor, Color32[] buffer)
        {
            if (material == null || buffer == null) return;
            if (palette == null)
            {
                if (activeProfile?.paletteData != null) palette = activeProfile.paletteData;
                else
                {
                    B2ULogError(B2UMessageSource.Runtime, "There is no palette data on the active profile.");
                    return;
                }
            }
            if (palette.colorLUT == null || palette.propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Palette: {palette.name} is missing either the color or property LUT.");
                return;
            }
            if (material.workflow != B2UWorkflow.Liquid && setSecondRowColor)
            {
                B2ULogError(B2UMessageSource.Runtime, $"You are trying to set a depth color on the material: {material.slotName} which has a workflow of: {material.workflow}. Setting depth color is only valid for liquid workflows.");
                return;
            }
            if (material.biomeType == "DYNAMIC" && variant != -1)
            {
                B2ULogWarning(B2UMessageSource.Runtime, $"Warning, you are setting a variant color for material: {material.slotName} which is entirely DYNAMIC. This overload will not affect biome colors.");
            }

            int width = palette.colorLUT.width;
            int rowStartIndex = (material.alphaValue + (setSecondRowColor ? 1 : 0)) * width;
            int numSubbiomes = (width - 8) / 8;

            if (variant != -1 && material.workflow != B2UWorkflow.Liquid && material.workflow != B2UWorkflow.Crystal)
            {
                if (setEmissionColor)
                {
                    int emissionIndex = rowStartIndex + (width - 8) + variant;
                    Color32 oldCol = buffer[emissionIndex];
                    buffer[emissionIndex] = new Color32(color.r, color.g, color.b, oldCol.a);
                }
                else
                {
                    int variantStartIndex = rowStartIndex + (variant * numSubbiomes);
                    for (int i = variantStartIndex; i < variantStartIndex + numSubbiomes; i++)
                    {
                        Color32 oldCol = buffer[i];
                        buffer[i] = new Color32(color.r, color.g, color.b, oldCol.a);
                    }
                }
            }
            else
            {
                if (setEmissionColor)
                {
                    for (int i = rowStartIndex + (width - 8); i < rowStartIndex + width; i++)
                    {
                        Color32 oldCol = buffer[i];
                        buffer[i] = new Color32(color.r, color.g, color.b, oldCol.a);
                    }
                }
                else
                {
                    for (int i = rowStartIndex; i < rowStartIndex + (width - 8); i++)
                    {
                        Color32 oldCol = buffer[i];
                        buffer[i] = new Color32(color.r, color.g, color.b, oldCol.a);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the Color LUT value for a base or emission color for the given biome-responsive material. Volatile; does not write to disk.
        /// </summary>
        /// <param name="palette">The palette to apply the change to.</param>
        /// <param name="color">The color to set.</param>
        /// <param name="material">The material to apply the color to.</param>
        /// <param name="setEmissionColor">Whether or not to apply this to the base color or emission color.</param>
        /// <param name="biome">The biome to change.</param>
        /// <param name="subtype">The biome subtype to change.</param>
        /// <param name="setSecondRowColor">Whether or not to apply this to the second row color. Only applies to liquids or Independent/Dynamic materials.</param>
        /// <param name="apply">Whether or not to apply the change to the GPU when the call ends.</param>
        public static void SetColorLUTPixel(B2UPaletteData palette, Color32 color, B2UMaterial material, int biome, int subtype, bool setEmissionColor, bool setSecondRowColor = false, bool apply = false)
        {
            if (material == null) return;
            if (palette == null)
            {
                if (activeProfile?.paletteData != null) palette = activeProfile.paletteData;
                else
                {
                    B2ULogError(B2UMessageSource.Runtime, "There is no palette data on the active profile.");
                    return;
                }
            }
            else if (palette.colorLUT == null || palette.propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Palette: {palette.name} is missing either the color or property LUT.");
                return;
            }

            if (material.workflow != B2UWorkflow.Liquid && setSecondRowColor)
            {
                B2ULogError(B2UMessageSource.Runtime, $"You are trying to set a depth color on the material: {material.slotName} which has a workflow of: {material.workflow}. Setting depth color is only valid for liquid workflows.");
                return;
            }
            if (material.biomeType == "INDEPENDENT")
            {
                B2ULogWarning(B2UMessageSource.Runtime, $"Warning, you are setting a biome color for material: {material.slotName} which is INDEPENDENT. This overload will not show color changes.");
            }

            Texture2D colorLUT = palette.colorLUT;
            if (colorLUT != null)
            {
                Color32[] colors = colorLUT.GetPixels32();
                SetColorLUTPixelNonAlloc(palette, color, material, biome, subtype, setEmissionColor, setSecondRowColor, colors);
                colorLUT.SetPixels32(colors);
                if (apply) colorLUT.Apply();
            }
            else
            {
                B2ULogError(B2UMessageSource.Runtime, "No Color Emission LUT texture is set on the active profile.");
            }
        }

        /// <summary>
        /// Writes a base or emission color for a biome-responsive material directly into a caller-provided Color32 LUT buffer without calling GetPixels32() or Apply().
        /// Ideal for batching multiple LUT edits into a single GPU upload.
        /// </summary>
        /// <param name="palette">The target B2UPaletteData, or null to use the active profile's palette.</param>
        /// <param name="color">The RGB color to write (preserves the existing alpha channel).</param>
        /// <param name="material">The B2UMaterial row to modify.</param>
        /// <param name="biome">The biome index (0-7) to update.</param>
        /// <param name="subtype">The biome subtype index to update (ignored when setting emission color).</param>
        /// <param name="setEmissionColor">If true, modifies the emission color section instead of base albedo colors.</param>
        /// <param name="setSecondRowColor">For Liquid workflows, if true writes to the second row (depth color) instead of the surface row.</param>
        /// <param name="buffer">The pre-fetched Color32 array matching the palette's colorLUT pixel buffer.</param>
        public static void SetColorLUTPixelNonAlloc(B2UPaletteData palette, Color32 color, B2UMaterial material, int biome, int subtype, bool setEmissionColor, bool setSecondRowColor, Color32[] buffer)
        {
            if (material == null) return;
            if (palette == null)
            {
                if (activeProfile?.paletteData != null) palette = activeProfile.paletteData;
                else
                {
                    B2ULogError(B2UMessageSource.Runtime, "There is no palette data on the active profile.");
                    return;
                }
            }
            if (palette.colorLUT == null || palette.propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Palette: {palette.name} is missing either the color or property LUT.");
                return;
            }
            if (material.workflow != B2UWorkflow.Liquid && setSecondRowColor)
            {
                B2ULogError(B2UMessageSource.Runtime, $"You are trying to set a depth color on the material: {material.slotName} which has a workflow of: {material.workflow}. Setting depth color is only valid for liquid workflows.");
                return;
            }
            if (material.biomeType == "INDEPENDENT")
            {
                B2ULogWarning(B2UMessageSource.Runtime, $"Warning, you are setting a biome color for material: {material.slotName} which is INDEPENDENT. This overload will not show color changes.");
            }

            if (buffer != null)
            {
                int width = palette.colorLUT.width;
                int rowStartIndex = (material.alphaValue + (setSecondRowColor ? 1 : 0)) * width;
                int numSubbiomes = (width - 8) / 8;

                if (setEmissionColor)
                {
                    int emissionIndex = rowStartIndex + (width - 8) + biome;
                    Color32 oldCol = buffer[emissionIndex];
                    buffer[emissionIndex] = new Color32(color.r, color.g, color.b, oldCol.a);
                }
                else
                {
                    int colorIndex = rowStartIndex + (biome * numSubbiomes) + subtype;
                    Color32 oldCol = buffer[colorIndex];
                    buffer[colorIndex] = new Color32(color.r, color.g, color.b, oldCol.a);
                }
            }
            else
            {
                B2ULogError(B2UMessageSource.Runtime, "No Color Emission LUT buffer was provided.");
            }
        }

        /// <summary>
        /// Gets a color from the Color LUT for the given non-biome-responsive material.
        /// </summary>
        /// <param name="palette">The palette to retrieve the color from.</param>
        /// <param name="material">The material to retrieve the color from.</param>
        /// <param name="getEmission">Whether or not to get the base or emission color.</param>
        /// <param name="variant">The variant to get.</param>
        /// <param name="getSecondRowColor">Whether or not to get the color from the second row. Only applies to liquids or Independent/Dynamic materials.</param>
        /// <returns>The retrieved color or magenta if failed.</returns>
        public static Color GetColorLUTPixel(B2UPaletteData palette, B2UMaterial material, bool getEmission, int variant = -1, bool getSecondRowColor = false)
        {
            if (material == null) return new Color32(255, 0, 255, 255);
            if (palette == null) palette = activeProfile?.paletteData;
            if (palette == null || palette.colorLUT == null) return new Color32(255, 0, 255, 255);

            int y = material.alphaValue + (getSecondRowColor && material.workflow == B2UWorkflow.Liquid ? 1 : 0);

            if (getEmission)
            {
                int x = variant == -1 ? palette.colorLUT.width - 1 : palette.colorLUT.width - (8 - variant);
                return palette.colorLUT.GetPixel(x, y);
            }
            else
            {
                int x = variant == -1 ? 0 : variant * palette.numSubbiomes;
                return palette.colorLUT.GetPixel(x, y);
            }
        }

        /// <summary>
        /// Gets a color from the Color LUT for the given biome-responsive material.
        /// </summary>
        /// <param name="palette">The palette to retrieve the color from.</param>
        /// <param name="material">The material to retrieve the color from.</param>
        /// <param name="getEmission">Whether or not to get the base or emission color.</param>
        /// <param name="biome">The biome to get.</param>
        /// <param name="subtype">The subtype to get.</param>
        /// <param name="getSecondRowColor">Whether or not to get the color from the second row. Only applies to liquids or Independent/Dynamic materials.</param>
        /// <returns>The retrieved color or magenta if failed.</returns>
        public static Color GetColorLUTPixel(B2UPaletteData palette, B2UMaterial material, bool getEmission, int biome, int subtype, bool getSecondRowColor = false)
        {
            if (material == null) return new Color32(255, 0, 255, 255);
            if (palette == null) palette = activeProfile?.paletteData;
            if (palette == null || palette.colorLUT == null) return new Color32(255, 0, 255, 255);

            int y = material.alphaValue + (getSecondRowColor ? 1 : 0);

            if (getEmission)
            {
                int x = palette.colorLUT.width - (8 - biome);
                return palette.colorLUT.GetPixel(x, y);
            }
            else
            {
                int x = (biome * palette.numSubbiomes) + subtype;
                return palette.colorLUT.GetPixel(x, y);
            }
        }

        /// <summary>
        /// Sets the Property LUT value for the given material. Volatile; does not write to disk.
        /// </summary>
        /// <param name="palette">The palette to set the value on.</param>
        /// <param name="material">The material to set the value on.</param>
        /// <param name="property">The slot key to set.</param>
        /// <param name="value">The byte value to set.</param>
        /// <param name="apply">Whether or not to apply the change to the GPU at the end of the call.</param>
        public static void SetPropertyLUTValue(B2UPaletteData palette, B2UMaterial material, B2USlotKey property, byte value, bool apply = false)
        {
            if (material == null) return;
            if (palette == null)
            {
                if (activeProfile?.paletteData != null) palette = activeProfile.paletteData;
                else
                {
                    B2ULogError(B2UMessageSource.Runtime, "There is no palette data on the active profile.");
                    return;
                }
            }
            else if (palette.colorLUT == null || palette.propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Palette: {palette.name} is missing either the color or property LUT.");
                return;
            }

            if ((int)property > 17 && material.workflow != B2UWorkflow.Liquid)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Warning, you are trying to set {property} on the material: {material.slotName} which has a workflow of: {material.workflow}. Setting r2 slots is only valid for liquid workflows.");
                return;
            }

            if (property == B2USlotKey.r1_p4_r)
            {
                if (!Application.isEditor)
                {
                    B2ULogError(B2UMessageSource.Runtime, "You cannot set r1_p4_r at runtime. This stores static workflow data for materials and cannot be changed outside Blender or the Unity Editor.");
                    return;
                }

                Texture2D pLUT = palette.propertyLUT;
                if (pLUT != null)
                {
                    int index = (material.alphaValue * 4) + 3;
                    Color32[] colors = pLUT.GetPixels32();
                    Color32 existing = colors[index];
                    colors[index] = new Color32(value, existing.g, existing.b, existing.a);
                    pLUT.SetPixels32(colors);
                    if (apply) pLUT.Apply();
                }
                else B2ULogError(B2UMessageSource.Runtime, "No Property LUT texture is set on the given palette.");

                return;
            }

            if (property == B2USlotKey.r1_ca || property == B2USlotKey.r1_ea || property == B2USlotKey.r2_ca || property == B2USlotKey.r2_ea)
            {
                Texture2D colorLUT = palette.colorLUT;
                if (colorLUT != null)
                {
                    Color32[] colors = colorLUT.GetPixels32();
                    SetPropertyLUTValueNonAlloc(palette, material, property, value, null, colors);
                    colorLUT.SetPixels32(colors);
                    if (apply) colorLUT.Apply();
                }
                else B2ULogError(B2UMessageSource.Runtime, "No Color Emission LUT texture is set on the given palette.");
            }
            else
            {
                Texture2D propLUT = palette.propertyLUT;
                if (propLUT != null)
                {
                    Color32[] colors = propLUT.GetPixels32();
                    SetPropertyLUTValueNonAlloc(palette, material, property, value, colors, null);
                    propLUT.SetPixels32(colors);
                    if (apply) propLUT.Apply();
                }
                else B2ULogError(B2UMessageSource.Runtime, "No Property LUT texture is set on the given palette.");
            }
        }

        /// <summary>
        /// Writes a material property byte directly into a caller-provided Property LUT or Color LUT buffer without calling GetPixels32() or Apply().
        /// Ideal for batching multiple LUT edits into a single GPU upload.
        /// </summary>
        /// <param name="palette">The target B2UPaletteData, or null to use the active profile's palette.</param>
        /// <param name="material">The B2UMaterial row to modify.</param>
        /// <param name="property">The B2USlotKey channel to write.</param>
        /// <param name="value">The raw 0-255 byte value to write.</param>
        /// <param name="propBuffer">The pre-fetched Property LUT Color32 buffer (required for standard property slots).</param>
        /// <param name="colorBuffer">The pre-fetched Color LUT Color32 buffer (required for ca/ea alpha slots).</param>
        public static void SetPropertyLUTValueNonAlloc(B2UPaletteData palette, B2UMaterial material, B2USlotKey property, byte value, Color32[] propBuffer, Color32[] colorBuffer)
        {
            if (material == null) return;
            if (palette == null)
            {
                if (activeProfile?.paletteData != null) palette = activeProfile.paletteData;
                else
                {
                    B2ULogError(B2UMessageSource.Runtime, "There is no palette data on the active profile.");
                    return;
                }
            }
            else if (palette.colorLUT == null || palette.propertyLUT == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Palette: {palette.name} is missing either the color or property LUT.");
                return;
            }

            if ((int)property > 17 && material.workflow != B2UWorkflow.Liquid)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Warning, you are trying to set {property} on the material: {material.slotName} which has a workflow of: {material.workflow}. Setting r2 slots is only valid for liquid workflows.");
                return;
            }

            if (property == B2USlotKey.r1_p4_r)
            {
                if (!Application.isEditor)
                {
                    B2ULogError(B2UMessageSource.Runtime, "You cannot set r1_p4_r at runtime. This stores static workflow data for materials and cannot be changed outside Blender or the Unity Editor.");
                    return;
                }

                if (propBuffer != null)
                {
                    int index = (material.alphaValue * 4) + 3;
                    Color32 existing = propBuffer[index];
                    propBuffer[index] = new Color32(value, existing.g, existing.b, existing.a);
                }
                else B2ULogError(B2UMessageSource.Runtime, "No Property LUT buffer was provided.");

                return;
            }

            if (property == B2USlotKey.r1_ca || property == B2USlotKey.r1_ea || property == B2USlotKey.r2_ca || property == B2USlotKey.r2_ea)
            {
                if (colorBuffer != null)
                {
                    B2USlotData data = palette.materials.GetPixelID(property, material.workflow);
                    if (data == null) return;
                    if (!palette.materials.TryProcessValue(property, material.workflow, value, out byte processedValue)) return;

                    int width = palette.colorLUT.width;
                    int rowStartIndex = (material.alphaValue + (data.secondRow ? 1 : 0)) * width;
                    bool emissionProperty = property == B2USlotKey.r1_ea || property == B2USlotKey.r2_ea;

                    if (emissionProperty)
                    {
                        for (int i = rowStartIndex + (width - 8); i < rowStartIndex + width; i++)
                        {
                            Color32 oldCol = colorBuffer[i];
                            colorBuffer[i] = new Color32(oldCol.r, oldCol.g, oldCol.b, processedValue);
                        }
                    }
                    else
                    {
                        for (int i = rowStartIndex; i < rowStartIndex + (width - 8); i++)
                        {
                            Color32 oldCol = colorBuffer[i];
                            colorBuffer[i] = new Color32(oldCol.r, oldCol.g, oldCol.b, processedValue);
                        }
                    }
                }
            }
            else
            {
                if (propBuffer != null)
                {
                    B2USlotData data = palette.materials.GetPixelID(property, material.workflow);
                    if (data == null) return;
                    if (!palette.materials.TryProcessValue(property, material.workflow, value, out byte processedValue)) return;

                    int index = ((material.alphaValue + (data.secondRow ? 1 : 0)) * 4) + data.pixelIndex;
                    Color32 existing = propBuffer[index];
                    switch (data.pixelChannel)
                    {
                        case 0:
                            propBuffer[index] = new Color32(processedValue, existing.g, existing.b, existing.a);
                            break;
                        case 1:
                            propBuffer[index] = new Color32(existing.r, processedValue, existing.b, existing.a);
                            break;
                        case 2:
                            propBuffer[index] = new Color32(existing.r, existing.g, processedValue, existing.a);
                            break;
                        case 3:
                            propBuffer[index] = new Color32(existing.r, existing.g, existing.b, processedValue);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Gets a normalized 0-1 property value for the given material from the LUTs.
        /// </summary>
        /// <param name="palette">The palette to retrieve the value from.</param>
        /// <param name="material">The material to retrieve the value from.</param>
        /// <param name="property">The slot key to retrieve the value from.</param>
        /// <example>
        /// <code>
        /// public B2UMaterial material;
        /// public B2UPaletteData palette;
        /// 
        /// private void Update()
        /// {
        ///     float metallicValue = B2URuntimeUtility.GetPropertyLUTValue(palette, material, B2USlotKey.r1_ea);
        ///     Debug.Log($"Metallic value for {material.slotName} is {metallicValue}");
        /// }
        /// </code>
        /// </example>
        public static float GetPropertyLUTValue(B2UPaletteData palette, B2UMaterial material, B2USlotKey property)
        {
            if (material == null) return -1f;
            if (material.workflow != B2UWorkflow.Liquid && (int)property > 17) return -1f;
            if (palette == null) palette = activeProfile?.paletteData;
            if (palette == null || palette.colorLUT == null || palette.propertyLUT == null) return -1f;

            int y = material.alphaValue + ((int)property > 17 ? 1 : 0);

            if (property == B2USlotKey.r1_ca || property == B2USlotKey.r1_ea || property == B2USlotKey.r2_ca || property == B2USlotKey.r2_ea)
            {
                int x = (property == B2USlotKey.r1_ca || property == B2USlotKey.r2_ca) ? 0 : palette.colorLUT.width - 1;
                return palette.colorLUT.GetPixel(x, y).a;
            }
            else
            {
                int[] id = B2USlotData.GetPixelID(property);
                Color col = palette.propertyLUT.GetPixel(id[1], y);
                return id[2] switch
                {
                    0 => col.r,
                    1 => col.g,
                    2 => col.b,
                    3 => col.a,
                    _ => -1f
                };
            }
        }

        /// <summary>
        /// Reads a property from the Property LUT and remaps it from [0..1] into its schema-defined [Min..Max] range.
        /// </summary>
        /// <param name="material">The material to retrieve the property from.</param>
        /// <param name="property">The slot key of the property.</param>
        /// <param name="paletteOverride">The palette to retrieve from, uses the active palette if null.</param>
        /// <example>
        /// <code>
        /// public B2UMaterial material;
        /// 
        /// private void Update()
        /// {
        ///     float scaledEmissionValue = B2URuntimeUtility.GetPropertyLUTScaledValue(material, B2USlotKey.r1_p1_r);
        ///     Debug.Log($"Scaled emission strength value for {material.slotName} is {scaledEmissionValue}");
        /// }
        /// </code>
        /// </example>
        public static float GetPropertyLUTScaledValue(B2UMaterial material, B2USlotKey property, B2UPaletteData paletteOverride = null)
        {
            B2UPaletteData data = paletteOverride != null ? paletteOverride : ActiveProfile?.paletteData;
            if (data == null || material == null) return 0f;

            float norm = GetPropertyLUTValue(data, material, property);
            if (norm < 0f) return 0f;

            Vector2 minMax = data.TryGetMinMax(property, material.workflow);
            return Mathf.Lerp(minMax.x, minMax.y, norm);
        }

        /// <summary>
        /// Validates and binds a new B2UPaletteData asset to the active profile.
        /// </summary>
        /// <param name="palette">The palette to set.</param>
        /// <example>
        /// <code>
        /// public B2UPaletteData dataToSet;
        /// 
        /// private void Start()
        /// {
        ///     if (CheckActiveProfile() && dataToSet != null)
        ///     {
        ///         B2URuntimeUtility.SetNewPalette(dataToSet);
        ///     }
        /// }
        /// </code>
        /// </example>
        public static void SetNewPalette(B2UPaletteData palette)
        {
            if (activeProfile == null)
            {
                B2ULogError(B2UMessageSource.Runtime, "No active profile is set. Call SetB2UShaderPropertyValuesFromProfile() first.");
                return;
            }

            if (palette == null)
            {
                B2ULogError(B2UMessageSource.Runtime, "The provided palette is null.");
                return;
            }

            if (palette.colorLUT == null ||
                palette.propertyLUT == null ||
                palette.iridescenceLUT == null ||
                palette.emissionPatterns == null ||
                palette.detailAtlas == null ||
                palette.noise3D == null ||
                palette.paintPalette == null)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Cannot bind palette '{palette.name}'. One or more texture references are missing.");
                return;
            }

            if (Application.isPlaying && activeProfile.paletteData != null)
            {
                B2UPaletteData oldPalette = activeProfile.paletteData;

                if (oldPalette.materials == null || palette.materials == null)
                {
                    B2ULogWarning(B2UMessageSource.Runtime, "Palette material containers are not initialized. Skipping Sister Palette validation.");
                }
                else
                {
                    for (int i = 0; i < 256; i++)
                    {
                        B2UMaterial oldMat = oldPalette.materials.GetMaterialByIndex((byte)i);
                        if (oldMat != null)
                        {
                            B2UMaterial newMat = palette.materials.GetMaterialByIndex((byte)i);
                            if (newMat == null)
                            {
                                B2ULogError(B2UMessageSource.Runtime, $"Sister palette validation failed! New palette '{palette.name}' is missing a material at alpha index {i} (which is required by the active palette).");
                                return;
                            }
                            if (oldMat.workflow != newMat.workflow)
                            {
                                B2ULogError(B2UMessageSource.Runtime, $"Sister palette validation failed! Material at alpha index {i} changed workflow from {oldMat.workflow} to {newMat.workflow}.");
                                return;
                            }
                        }
                    }
                }
            }

            activeProfile.paletteData = palette;
            activeProfile.GetPaletteTextures();
            PushAllGlobals();
        }

        /// <summary>
        /// Packs the 8 detail mapping booleans into a single bitmask byte for SetPropertyLUTValue().
        /// </summary>
        public static byte GetDetailBooleanBitmask(bool irregularTiling, bool invertedNormals, bool invertedCurvature, bool invertedHeightmap, bool normalsForIridescenceOnly, bool heightBasedEmission, bool curvatureBasedEmission, bool useEmisColorForHeightmap)
        {
            byte value = 0;
            if (irregularTiling) value |= 1;
            if (invertedNormals) value |= 2;
            if (invertedCurvature) value |= 4;
            if (invertedHeightmap) value |= 8;
            if (normalsForIridescenceOnly) value |= 16;
            if (heightBasedEmission) value |= 32;
            if (curvatureBasedEmission) value |= 64;
            if (useEmisColorForHeightmap) value |= 128;
            return value;
        }

        /// <summary>
        /// Packs the 3 backface rendering booleans into a single bitmask byte for SetPropertyLUTValue().
        /// </summary>
        public static byte GetBackfaceBooleanBitmask(bool invertBackfaceNormals, bool invertBackfaceCurvature, bool invertBackfaceHeight)
        {
            byte value = 0;
            if (invertBackfaceNormals) value |= 1;
            if (invertBackfaceCurvature) value |= 2;
            if (invertBackfaceHeight) value |= 4;
            return value;
        }

        /// <summary>
        /// Packs 8 generic boolean flags into a single bitmask byte for SetPropertyLUTValue().
        /// </summary>
        public static byte GetGenericBooleanBitmask(bool bit0, bool bit1, bool bit2, bool bit3, bool bit4, bool bit5, bool bit6, bool bit7)
        {
            byte value = 0;
            if (bit0) value |= 1;
            if (bit1) value |= 2;
            if (bit2) value |= 4;
            if (bit3) value |= 8;
            if (bit4) value |= 16;
            if (bit5) value |= 32;
            if (bit6) value |= 64;
            if (bit7) value |= 128;
            return value;
        }
        #endregion

        #region Miscellaneous Functions
        /// <summary>
        /// Samples the world-space biome weights and subtype at the given position. Has several helper functions in it to get important data.
        /// Safe to call in Update() with zero heap allocations.
        /// </summary>
        /// <param name="position">The world-space position to sample.</param>
        /// <returns>A stack-allocated B2UBiomeData struct storing the 8 biome weights and subtype.</returns>
        public static B2UBiomeData GetBiomeData(Vector3 position)
        {
            if (CheckBiomeTextures())
            {
                Texture2D b1Tex = GetB2UShaderPropertyValue<Texture2D>(B2UShaderProperty.BiomeTexture1, null, true);
                Texture2D b2Tex = GetB2UShaderPropertyValue<Texture2D>(B2UShaderProperty.BiomeTexture2, null, true);
                Vector2 worldSize = GetB2UShaderPropertyValue(B2UShaderProperty.WorldSize, Vector2.zero, true);

                if (Mathf.Abs(worldSize.x) < 0.0001f || Mathf.Abs(worldSize.y) < 0.0001f)
                {
                    worldSize = new Vector2(2000f, 2000f);
                }

                float u = (position.x / worldSize.x) + 0.5f;
                float v = (position.z / worldSize.y) + 0.5f;

                Color32 b1Sample = b1Tex.GetPixelBilinear(u, v);
                Color32 b2Sample = b2Tex.GetPixelBilinear(u, v);

                return new B2UBiomeData(
                    b1Sample.r, b1Sample.g, b1Sample.b, b1Sample.a,
                    b2Sample.r, b2Sample.g, b2Sample.b, b2Sample.a
                );
            }
            return B2UBiomeData.Invalid;
        }

        /// <summary>
        /// Gets the primary biome index (0-7) and subtype index (0 to numSubbiomes-1) from a B2UBiomeData sample as an int tuple.
        /// </summary>
        /// <param name="biomeData">The biome data to pull from.</param>
        /// <returns>An (int biomeIndex, int subtypeIndex) tuple containing the respective indices as defined in the active palette.</returns>
        public static (int biomeIndex, int subtypeIndex) GetBiomeAndSubtypeIndex(B2UBiomeData biomeData)
        {
            return (biomeData.GetPrimaryBiomeIndex(), biomeData.GetSubtypeIndex());
        }

        /// <summary>
        /// Gets the primary biome index (0-7) and subtype index (0 to numSubbiomes-1) at a world position as an int tuple.
        /// </summary>
        /// <param name="position">The world space position to sample.</param>
        /// <returns>An (int biomeIndex, int subtypeIndex) tuple containing the respective indices as defined in the active palette.</returns>
        /// <example>
        /// <code>
        /// private void Update()
        /// {
        ///     (int biomeIdx, int subtypeIdx) = B2URuntimeUtility.GetBiomeAndSubtypeIndex(transform.position);
        ///     Debug.Log($"Current Biome Index: {biomeIdx}, Current Subtype Index: {subtypeIdx}");
        /// }
        /// </code>
        /// </example>
        public static (int biomeIndex, int subtypeIndex) GetBiomeAndSubtypeIndex(Vector3 position)
        {
            return GetBiomeAndSubtypeIndex(GetBiomeData(position));
        }

        /// <summary>
        /// Gets the primary biome name and subtype name from the active palette without allocating as a string tuple.
        /// </summary>
        /// <param name="biomeData">The biome data to pull from.</param>
        /// <returns>A (string biomeName, string subtypeName) tuple containing the respective names as defined in the active palette.</returns>
        public static (string biomeName, string subtypeName) GetBiomeAndSubtypeNames(B2UBiomeData biomeData)
        {
            const string fallbackBiome = "COULD NOT RETRIEVE BIOME";
            const string fallbackSubtype = "COULD NOT RETRIEVE SUBTYPE";
            
            if (!biomeData.isValidData || !CheckActivePalette())
            {
                return (fallbackBiome, fallbackSubtype);
            }

            B2UPaletteData palette = ActiveProfile.paletteData;
            string[] biomeNames = palette.biomeNames;
            string[] subtypeNames = palette.subtypeNames;
            int numSubbiomes = Mathf.Max(1, palette.numSubbiomes);

            int biomeIdx = biomeData.GetPrimaryBiomeIndex();
            int subIdx = biomeData.GetSubtypeIndex();
            int flatSubtypeIdx = (biomeIdx * numSubbiomes) + subIdx;

            string bName = (biomeNames != null && biomeIdx >= 0 && biomeIdx < biomeNames.Length)
                ? biomeNames[biomeIdx]
                : fallbackBiome;

            string sName = (subtypeNames != null && biomeIdx >= 0 && subIdx >= 0 && flatSubtypeIdx >= 0 && flatSubtypeIdx < subtypeNames.Length)
                ? subtypeNames[flatSubtypeIdx]
                : fallbackSubtype;

            return (bName, sName);
        }

        /// <summary>
        /// Gets the primary biome name and subtype name at the given world position without allocating a new string tuple.
        /// </summary>
        /// <param name="position">THe world space position to sample.</param>
        /// <returns>A (string biomeName, string subtypeName) tuple containing the respective names as defined in the active palette.</returns>
        /// <example>
        /// <code>
        /// private void Update()
        /// {
        ///     (string biomeName, string subtypeName) = B2URuntimeUtility.GetBiomeAndSubtypeNames(transform.position);
        ///     Debug.Log($"Current Biome Name: {biomeName}, Current Subtype Name: {subtypeName}");
        /// }
        /// </code>
        /// </example>
        public static (string biomeName, string subtypeName) GetBiomeAndSubtypeNames(Vector3 position)
        {
            return GetBiomeAndSubtypeNames(GetBiomeData(position));
        }

        /// <summary>
        /// Evaluates the actual rendered color of a B2U material at a specific world-space position,
        /// automatically blending biome colors for Dynamic materials or sampling the specified variant for Independent materials.
        /// </summary>
        /// <param name="material">The material to evaluate the color from.</param>
        /// <param name="worldPosition">The position to sample the color at.</param>
        /// <param name="variantIndex">The variant to sample (Only applies to independent materials).</param>
        /// <param name="emissionColor">Whether or not to get the base or emission colors.</param>
        /// <param name="secondRow">Whether or not to get the second row.</param>
        /// <param name="forceDynamicRowOnDual">Whether or not to pull the independent or dynamic colors from a Independent/Dynamic material.</param>
        /// <returns>The retrieved color.</returns>
        public static Color GetEvaluatedMaterialColor(B2UMaterial material, Vector3 worldPosition, int variantIndex = 0, bool emissionColor = false, bool secondRow = false, bool forceDynamicRowOnDual = false)
        {
            if (!CheckActivePalette() || material == null) return Color.white;

            B2UPaletteData palette = ActiveProfile.paletteData;
            Texture2D colorLUT = palette.colorLUT;
            if (colorLUT == null) return Color.white;

            bool useDynamic = material.biomeType == "DYNAMIC" || ((material.biomeType == "INDEP_DYN" || material.biomeType == "INDEP_DYN") && forceDynamicRowOnDual);

            if (!useDynamic)
            {
                return GetColorLUTPixel(palette, material, emissionColor, variantIndex, secondRow);
            }

            B2UBiomeData biomeData = GetBiomeData(worldPosition);
            if (!biomeData.isValidData)
            {
                return GetColorLUTPixel(palette, material, emissionColor, 0, 0, secondRow);
            }

            int numSubtypes = Mathf.Max(1, palette.numSubbiomes);
            int lutWidth = (numSubtypes * 8) + 8;

            int baseRow = material.alphaValue;
            // Check both because at one point I accidentally had the hyphen and dash versions. I think it is removed but I will review later.
            if (material.biomeType == "INDEP_DYN" || material.biomeType == "INDEP-DYN")
            {
                baseRow += (material.workflow == B2UWorkflow.Liquid) ? 2 : 1;
            }
            if (material.workflow == B2UWorkflow.Liquid && secondRow)
            {
                baseRow += 1;
            }

            float v = (baseRow + 0.5f) / 256f;
            float subtypeFloat = biomeData.GetContinuousSubtype(numSubtypes);

            Color accumColor = Color.clear;
            for (int b = 0; b < 8; b++)
            {
                float w = biomeData.GetNormalizedWeight(b);
                if (w > 0.01f)
                {
                    if (emissionColor)
                    {
                        int emisX = (numSubtypes * 8) + b;
                        accumColor += colorLUT.GetPixel(emisX, baseRow) * w;
                    }
                    else
                    {
                        float xColorIndex = (b * numSubtypes) + subtypeFloat;
                        float u = (xColorIndex + 0.5f) / lutWidth;
                        accumColor += colorLUT.GetPixelBilinear(u, v) * w;
                    }
                }
            }

            accumColor.a = 1f;
            return accumColor;
        }

        private static readonly List<Color32> s_HitColors = new List<Color32>();
        private static readonly List<Vector4> s_HitUV1 = new List<Vector4>();
        private static readonly List<int> s_HitTriangles = new List<int>();

        /// <summary>
        /// Attempts to retrieve the B2URSUVMono component from a RaycastHit's collider or its parent hierarchy.
        /// </summary>
        /// <param name="hit">The raycast hit to try and get the B2URSUVMono from.</param>
        /// <param name="rsuvMono">The retrieved B2URSUVMono.</param>
        /// <returns>True if it was able to get the B2URSUVMono.</returns>
        public static bool TryGetRSUVMonoFromHit(RaycastHit hit, out B2URSUVMono rsuvMono)
        {
            rsuvMono = null;
            if (hit.collider == null) return false;
            return TryGetRSUVMonoFromObject(hit.collider.gameObject, out rsuvMono);
        }

        /// <summary>
        /// Attempts to retrieve the B2URSUVMono component from a GameObject or its parent hierarchy.
        /// </summary>
        /// <param name="obj">The object to try and get the B2URSUVMono from.</param>
        /// <param name="rsuvMono">The retrieved B2URSUVMono.</param>
        /// <returns>True if it was able to get the B2URSUVMono.</returns>
        public static bool TryGetRSUVMonoFromObject(GameObject obj, out B2URSUVMono rsuvMono)
        {
            rsuvMono = null;
            if (obj == null) return false;

            if (!obj.TryGetComponent(out rsuvMono))
            {
                rsuvMono = obj.GetComponentInParent<B2URSUVMono>();
            }

            return rsuvMono != null;
        }

        /// <summary>
        /// Resolves the B2UMaterial and Variant Index (0-7, or -1 if dynamic/not applicable) from a Physics.Raycast hit.
        /// Supports per-triangle lookup on MeshColliders and automatic fallback to B2URSUVMono on primitive colliders. 
        /// </summary>
        /// <remarks>Primitive colliders cannot solve the variant, it will always return -1</remarks>
        /// <param name="hit">The raycast hit to try and retrieve the B2UMaterial from.</param>
        /// <param name="material">The retrieved B2UMaterial.</param>
        /// <param name="variantIndex">The retrieved variant index.</param>
        /// <param name="isDynamicRow">Whether the retrieved material is the dynamic row of an Independent/Dynamic material.</param>
        /// <returns>True if the material could be retrieved.</returns>
        public static bool TryGetMaterialAtHit(RaycastHit hit, out B2UMaterial material, out int variantIndex, out bool isDynamicRow)
        {
            material = null;
            variantIndex = -1;
            isDynamicRow = false;

            if (hit.collider == null || !CheckPaletteMaterials(silent: true)) return false;

            B2UPaletteData palette = ActiveProfile.paletteData;

            // 1. Exact Triangle Lookup on Readable MeshColliders
            if (hit.collider is MeshCollider meshCol && meshCol.sharedMesh != null && !meshCol.convex)
            {
                Mesh mesh = meshCol.sharedMesh;
                if (mesh.isReadable && hit.triangleIndex >= 0)
                {
                    s_HitTriangles.Clear();
                    mesh.GetTriangles(s_HitTriangles, 0);
                    int triStart = hit.triangleIndex * 3;

                    if (triStart + 2 < s_HitTriangles.Count)
                    {
                        int vIdx = s_HitTriangles[triStart];

                        s_HitColors.Clear();
                        mesh.GetColors(s_HitColors);
                        if (vIdx < s_HitColors.Count)
                        {
                            byte rowId = s_HitColors[vIdx].a;
                            material = ResolveMaterialFromRowIndex(palette, rowId, out isDynamicRow);

                            if (material != null && !isDynamicRow &&
                                (material.workflow == B2UWorkflow.Opaque ||
                                 material.workflow == B2UWorkflow.OpaqueDoubleSided ||
                                 material.workflow == B2UWorkflow.Transparent))
                            {
                                s_HitUV1.Clear();
                                mesh.GetUVs(1, s_HitUV1);
                                if (vIdx < s_HitUV1.Count)
                                {
                                    Vector4 uv1 = s_HitUV1[vIdx];
                                    uint b0 = Unity.Mathematics.math.asuint(uv1.x) >> 31;
                                    uint b1 = Unity.Mathematics.math.asuint(uv1.y) >> 31;
                                    uint b2 = Unity.Mathematics.math.asuint(uv1.z) >> 31;
                                    variantIndex = (int)(b0 | (b1 << 1) | (b2 << 2));
                                }
                            }
                            return material != null;
                        }
                    }
                }
            }

            // 2. Fallback to B2URSUVMono on the Collider or its Parent/Children
            return TryGetMaterialFromObject(hit.collider.gameObject, out material, out isDynamicRow);
        }

        /// <summary>
        /// Resolves the primary B2UMaterial assigned to a GameObject via its B2URSUVMono component.
        /// </summary>
        /// <param name="obj">The object to retrieve the material from.</param>
        /// <param name="material">The retrieved material.</param>
        /// <param name="isDynamicRow">Whether the retrieved material is the dynamic row of an Independent/Dynamic material.</param>
        /// <returns>True if the material could be retrieved.</returns>
        public static bool TryGetMaterialFromObject(GameObject obj, out B2UMaterial material, out bool isDynamicRow)
        {
            material = null;
            isDynamicRow = false;
            if (obj == null || !CheckPaletteMaterials(silent: true)) return false;

            if (!obj.TryGetComponent<B2URSUVMono>(out var rsuvMono))
            {
                rsuvMono = obj.GetComponentInParent<B2URSUVMono>();
            }

            if (rsuvMono != null)
            {
                if (rsuvMono.materialID == int.MaxValue) rsuvMono.UpdateMaterialID();
                if (rsuvMono.materialID != int.MaxValue)
                {
                    byte rowId = (byte)Mathf.Clamp(Mathf.Abs(rsuvMono.materialID), 0, 255);
                    material = ResolveMaterialFromRowIndex(ActiveProfile.paletteData, rowId, out isDynamicRow);
                    if (rsuvMono.materialID < 0) isDynamicRow = true;
                    return material != null;
                }
            }

            return false;
        }

        private static B2UMaterial ResolveMaterialFromRowIndex(B2UPaletteData palette, byte rowId, out bool isDynamicRow)
        {
            isDynamicRow = false;
            if (palette?.materials == null) return null;

            B2UMaterial direct = palette.materials.GetMaterialByIndex(rowId);
            if (direct != null)
            {
                isDynamicRow = direct.biomeType == "DYNAMIC";
                return direct;
            }

            if (rowId >= 1)
            {
                B2UMaterial prev1 = palette.materials.GetMaterialByIndex((byte)(rowId - 1));
                if (prev1 != null && prev1.workflow != B2UWorkflow.Liquid &&
                    (prev1.biomeType == "INDEP_DYN" || prev1.biomeType == "INDEP-DYN"))
                {
                    isDynamicRow = true;
                    return prev1;
                }
            }

            if (rowId >= 2)
            {
                B2UMaterial prev2 = palette.materials.GetMaterialByIndex((byte)(rowId - 2));
                if (prev2 != null && prev2.workflow == B2UWorkflow.Liquid &&
                    (prev2.biomeType == "INDEP_DYN" || prev2.biomeType == "INDEP-DYN"))
                {
                    isDynamicRow = true;
                    return prev2;
                }
            }

            return null;
        }
        #endregion
    }

    #region RSUV Classes
    /// <summary>
    /// Base class for B2U Renderer Shader User Value (RSUV) bit-packed data containers.
    /// </summary>
    [Serializable]
    public abstract class B2URSUV
    {
        /// <summary>
        /// The encoded 32-bit uint RSUV value.
        /// </summary>
        public uint rsuv = 0;
        /// <summary>
        /// True if this RSUV container targets the Liquid workflow (which reserves bits 26-31 for the 6-bit MeshID).
        /// </summary>
        public bool isLiquid = false;

        /// <summary>
        /// Preserves the 6-bit MeshID (bits 26-31) from an existing RSUV when applying new liquid properties.
        /// </summary>
        /// <param name="existingRSUV">The renderer's current raw 32-bit RSUV value.</param>
        /// <returns>The combined 32-bit RSUV containing this container's properties and the existing MeshID.</returns>
        public uint SetMeshIDInRSUV(uint existingRSUV)
        {
            return (rsuv & 0x03FF_FFFFu) | (existingRSUV & 0xFC00_0000u);
        }

        /// <summary>
        /// Decodes a raw 32-bit uint directly into this instance's fields in-place.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value read from a renderer.</param>
        public abstract void DecodeFromUint(uint rawRSUV);

        /// <summary>
        /// Encodes this instance's fields into a 32-bit uint.
        /// </summary>
        /// <returns>The bit-packed 32-bit uint RSUV value.</returns>
        public abstract uint EncodeRSUV();

        /// <summary>
        /// Re-encodes the internal uint value from the current field values.
        /// </summary>
        public void UpdateRSUV()
        {
            rsuv = EncodeRSUV();
        }
    }

    /// <summary>
    /// Stores per-renderer RSUV properties for B2U Solid workflows (Opaque, OpaqueDS, Transparent).
    /// </summary>
    [Serializable]
    public class B2USolidRSUV : B2URSUV
    {
        /// <summary>
        /// The paint palette slice index (0-15) packed into bits 7-10.
        /// </summary>
        public uint paletteIndex = 0;
        /// <summary>
        /// The paint color slot index (0-127) within the selected palette slice, packed into bits 0-6.
        /// </summary>
        public uint paintIndex = 0;
        /// <summary>
        /// Per-renderer emission strength multiplier (0f-1f), packed into 7 bits (bits 11-17).
        /// </summary>
        public float emissionStrength = 1f;
        /// <summary>
        /// Per-renderer 3D noise offset seed (0-63), packed into bits 18-23.
        /// </summary>
        public uint noiseOffset = 0;
        /// <summary>
        /// Whether paint palette recoloring is enabled on this renderer, packed into bit 24.
        /// </summary>
        public bool isPainted = false;
        /// <summary>
        /// Per-renderer procedural crack intensity (0f-1f), packed into 7 bits (bits 25-31).
        /// </summary>
        public float crackIntensity = 0f;

        /// <summary>
        /// Initializes a new B2USolidRSUV with default values and encodes the initial RSUV uint.
        /// </summary>
        public B2USolidRSUV()
        {
            isLiquid = false;
            rsuv = EncodeRSUV();
        }

        /// <summary>
        /// Initializes a new B2USolidRSUV with clamped property values and encodes the resulting RSUV uint.
        /// </summary>
        /// <param name="paletteIndex">Paint palette slice index (0-15).</param>
        /// <param name="paintIndex">Paint slot index (0-127).</param>
        /// <param name="emissionStrength">Emission strength multiplier (0f-1f).</param>
        /// <param name="noiseOffset">3D noise offset seed (0-63).</param>
        /// <param name="isPainted">True to enable paint palette recoloring.</param>
        /// <param name="crackIntensity">Procedural crack intensity (0f-1f).</param>
        public B2USolidRSUV(uint paletteIndex, uint paintIndex, float emissionStrength, uint noiseOffset, bool isPainted, float crackIntensity)
        {
            isLiquid = false;
            this.paletteIndex = (uint)Mathf.Clamp((int)paletteIndex, 0, 15);
            this.paintIndex = (uint)Mathf.Clamp((int)paintIndex, 0, 127);
            this.emissionStrength = Mathf.Clamp01(emissionStrength);
            this.noiseOffset = (uint)Mathf.Clamp((int)noiseOffset, 0, 63);
            this.isPainted = isPainted;
            this.crackIntensity = Mathf.Clamp01(crackIntensity);
            rsuv = EncodeRSUV();
        }

        /// <summary>
        /// Initializes a new B2USolidRSUV by decoding an existing 32-bit RSUV value.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value to decode.</param>
        public B2USolidRSUV(uint rawRSUV)
        {
            isLiquid = false;
            DecodeFromUint(rawRSUV);
        }

        /// <summary>
        /// Bit-packs all Solid RSUV fields into a single 32-bit uint.
        /// </summary>
        /// <returns>The encoded 32-bit Solid RSUV value.</returns>
        public override uint EncodeRSUV()
        {
            uint encoded = 0;
            encoded |= (paintIndex & 0x7Fu);
            encoded |= (paletteIndex & 0xFu) << 7;

            uint emissionInt = (uint)Mathf.Clamp(Mathf.RoundToInt(127f * (1f - emissionStrength)), 0, 127);
            encoded |= (emissionInt & 0x7Fu) << 11;

            encoded |= (noiseOffset & 0x3Fu) << 18;

            uint isPaintedInt = isPainted ? 1u : 0u;
            encoded |= (isPaintedInt & 0x1u) << 24;

            uint crackInt = (uint)Mathf.Clamp(Mathf.RoundToInt(crackIntensity * 127f), 0, 127);
            encoded |= (crackInt & 0x7Fu) << 25;

            return encoded;
        }

        /// <summary>
        /// Unpacks a raw 32-bit Solid RSUV uint into this instance's fields.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value.</param>
        public override void DecodeFromUint(uint rawRSUV)
        {
            rsuv = rawRSUV;
            paintIndex = rawRSUV & 0x7Fu;
            paletteIndex = (rawRSUV >> 7) & 0xFu;
            emissionStrength = 1.0f - (((rawRSUV >> 11) & 0x7Fu) / 127.0f);
            noiseOffset = (rawRSUV >> 18) & 0x3Fu;
            isPainted = (rawRSUV & (1u << 24)) != 0;
            crackIntensity = ((rawRSUV >> 25) & 0x7Fu) / 127.0f;
        }

        /// <summary>
        /// Decodes a raw 32-bit uint into a new B2USolidRSUV instance.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value.</param>
        /// <returns>A newly decoded B2USolidRSUV instance.</returns>
        public static B2USolidRSUV DecodeRSUV(uint rawRSUV) => new B2USolidRSUV(rawRSUV);
    }

    /// <summary>
    /// Stores per-renderer RSUV properties for B2U Liquid workflows.
    /// </summary>
    [Serializable]
    public class B2ULiquidRSUV : B2URSUV
    {
        /// <summary>
        /// Per-renderer liquid emission strength multiplier (0f-1f), packed into 8 bits (bits 0-7).
        /// </summary>
        public float emissionStrength = 1f;
        /// <summary>
        /// Per-renderer flow speed/direction multiplier (-1f to 1f), packed into 10 bits (bits 8-17).
        /// </summary>
        public float flowStrength = 1f;
        /// <summary>
        /// Per-renderer wave height multiplier (-1f to 1f), packed into 8 bits (bits 18-25).
        /// </summary>
        public float waveMultiplier = 1f;

        /// <summary>
        /// Initializes a new B2ULiquidRSUV with default values and encodes the initial RSUV uint.
        /// </summary>
        public B2ULiquidRSUV()
        {
            isLiquid = true;
            rsuv = EncodeRSUV();
        }

        /// <summary>
        /// Initializes a new B2ULiquidRSUV with clamped property values and encodes the resulting RSUV uint.
        /// </summary>
        /// <param name="emissionStrength">Emission strength multiplier (0f-1f).</param>
        /// <param name="flowStrength">Flow strength multiplier (-1f to 1f).</param>
        /// <param name="waveMultiplier">Wave height multiplier (-1f to 1f).</param>
        public B2ULiquidRSUV(float emissionStrength, float flowStrength, float waveMultiplier)
        {
            isLiquid = true;
            this.emissionStrength = Mathf.Clamp01(emissionStrength);
            this.flowStrength = Mathf.Clamp(flowStrength, -1f, 1f);
            this.waveMultiplier = Mathf.Clamp(waveMultiplier, -1f, 1f);
            rsuv = EncodeRSUV();
        }

        /// <summary>
        /// Initializes a new B2ULiquidRSUV by decoding an existing 32-bit RSUV value.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value to decode.</param>
        public B2ULiquidRSUV(uint rawRSUV)
        {
            isLiquid = true;
            DecodeFromUint(rawRSUV);
        }

        /// <summary>
        /// Initializes a new B2ULiquidRSUV by decoding an existing 32-bit RSUV value and overriding its 6-bit MeshID.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value to decode.</param>
        /// <param name="meshID">The 0-63 liquid MeshID to pack into bits 26-31.</param>
        public B2ULiquidRSUV(uint rawRSUV, uint meshID)
        {
            isLiquid = true;
            DecodeFromUint(rawRSUV);
            rsuv = (rsuv & 0x03FFFFFFu) | ((meshID & 0x3Fu) << 26);
        }

        /// <summary>
        /// Bit-packs all Liquid RSUV fields into a 32-bit uint while preserving any existing MeshID in bits 26-31.
        /// </summary>
        /// <returns>The encoded 32-bit Liquid RSUV value.</returns>
        public override uint EncodeRSUV()
        {
            uint encoded = 0;

            uint emissionInt = (uint)Mathf.Clamp(Mathf.RoundToInt((1.0f - emissionStrength) * 255f), 0, 255);
            encoded |= (emissionInt & 0xFFu);

            uint flowInt = (uint)Mathf.Clamp(Mathf.RoundToInt(1023f * ((1f - flowStrength) * 0.5f)), 0, 1023);
            encoded |= (flowInt & 0x3FFu) << 8;

            uint waveInt = (uint)Mathf.Clamp(Mathf.RoundToInt(255f * ((1f - waveMultiplier) * 0.5f)), 0, 255);
            encoded |= (waveInt & 0xFFu) << 18;

            // Preserve any existing MeshID in bits 26-31
            encoded |= (rsuv & 0xFC00_0000u);

            return encoded;
        }

        /// <summary>
        /// Unpacks a raw 32-bit Liquid RSUV uint into this instance's fields.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value.</param>
        public override void DecodeFromUint(uint rawRSUV)
        {
            rsuv = rawRSUV;
            emissionStrength = 1.0f - ((rawRSUV & 0xFFu) / 255.0f);
            flowStrength = ((1.0f - (((rawRSUV >> 8) & 0x3FFu) / 1023.0f)) * 2.0f) - 1.0f;
            waveMultiplier = ((1.0f - (((rawRSUV >> 18) & 0xFFu) / 255.0f)) * 2.0f) - 1.0f;
        }

        /// <summary>
        /// Decodes a raw 32-bit uint into a new B2ULiquidRSUV instance.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value.</param>
        /// <returns>A newly decoded B2ULiquidRSUV instance.</returns>
        public static B2ULiquidRSUV DecodeRSUV(uint rawRSUV) => new B2ULiquidRSUV(rawRSUV);
    }

    /// <summary>
    /// Stores per-renderer RSUV properties for B2U Crystal workflows.
    /// </summary>
    [Serializable]
    public class B2UCrystalRSUV : B2URSUV
    {
        /// <summary>
        /// Per-renderer crystal emission strength multiplier (0f-1f), packed into 8 bits (bits 0-7).
        /// </summary>
        public float emissionStrength = 1f;
        /// <summary>
        /// Per-renderer crystal light transmission multiplier (0f-1f), packed into 8 bits (bits 8-15).
        /// </summary>
        public float transmissionMultiplier = 1f;
        /// <summary>
        /// Per-renderer crystal light absorption multiplier (0f-1f), packed into 8 bits (bits 16-23).
        /// </summary>
        public float absorptionMultiplier = 1f;
        /// <summary>
        /// Per-renderer procedural crack intensity (0f-1f), packed into 8 bits (bits 24-31).
        /// </summary>
        public float crackIntensity = 0f;

        /// <summary>
        /// Initializes a new B2UCrystalRSUV with default values and encodes the initial RSUV uint.
        /// </summary>
        public B2UCrystalRSUV()
        {
            isLiquid = false;
            rsuv = EncodeRSUV();
        }

        /// <summary>
        /// Initializes a new B2UCrystalRSUV with clamped property values and encodes the resulting RSUV uint.
        /// </summary>
        /// <param name="emissionStrength">Emission strength multiplier (0f-1f).</param>
        /// <param name="transmissionMultiplier">Transmission multiplier (0f-1f).</param>
        /// <param name="absorptionMultiplier">Absorption multiplier (0f-1f).</param>
        /// <param name="crackIntensity">Procedural crack intensity (0f-1f).</param>
        public B2UCrystalRSUV(float emissionStrength, float transmissionMultiplier, float absorptionMultiplier, float crackIntensity)
        {
            isLiquid = false;
            this.emissionStrength = Mathf.Clamp01(emissionStrength);
            this.transmissionMultiplier = Mathf.Clamp01(transmissionMultiplier);
            this.absorptionMultiplier = Mathf.Clamp01(absorptionMultiplier);
            this.crackIntensity = Mathf.Clamp01(crackIntensity);
            rsuv = EncodeRSUV();
        }

        /// <summary>
        /// Initializes a new B2UCrystalRSUV by decoding an existing 32-bit RSUV value.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value to decode.</param>
        public B2UCrystalRSUV(uint rawRSUV)
        {
            isLiquid = false;
            DecodeFromUint(rawRSUV);
        }

        /// <summary>
        /// Bit-packs all Crystal RSUV fields into a single 32-bit uint.
        /// </summary>
        /// <returns>The encoded 32-bit Crystal RSUV value.</returns>
        public override uint EncodeRSUV()
        {
            uint encoded = 0;

            uint emissionInt = (uint)Mathf.Clamp(Mathf.RoundToInt((1.0f - emissionStrength) * 255f), 0, 255);
            encoded |= (emissionInt & 0xFFu);

            uint transInt = (uint)Mathf.Clamp(Mathf.RoundToInt((1.0f - transmissionMultiplier) * 255f), 0, 255);
            encoded |= (transInt & 0xFFu) << 8;

            uint absInt = (uint)Mathf.Clamp(Mathf.RoundToInt((1.0f - absorptionMultiplier) * 255f), 0, 255);
            encoded |= (absInt & 0xFFu) << 16;

            uint crackInt = (uint)Mathf.Clamp(Mathf.RoundToInt(crackIntensity * 255f), 0, 255);
            encoded |= (crackInt & 0xFFu) << 24;

            return encoded;
        }

        /// <summary>
        /// Unpacks a raw 32-bit Crystal RSUV uint into this instance's fields.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value.</param>
        public override void DecodeFromUint(uint rawRSUV)
        {
            rsuv = rawRSUV;
            emissionStrength = 1.0f - ((rawRSUV & 0xFFu) / 255.0f);
            transmissionMultiplier = 1.0f - (((rawRSUV >> 8) & 0xFFu) / 255.0f);
            absorptionMultiplier = 1.0f - (((rawRSUV >> 16) & 0xFFu) / 255.0f);
            crackIntensity = ((rawRSUV >> 24) & 0xFFu) / 255.0f;
        }

        /// <summary>
        /// Decodes a raw 32-bit uint into a new B2UCrystalRSUV instance.
        /// </summary>
        /// <param name="rawRSUV">The raw 32-bit RSUV value.</param>
        /// <returns>A newly decoded B2UCrystalRSUV instance.</returns>
        public static B2UCrystalRSUV DecodeRSUV(uint rawRSUV) => new B2UCrystalRSUV(rawRSUV);
    }
    #endregion

    #region Master Shader Classes & Enums
    /// <summary>
    /// Runtime-controllable global shader properties for the B2U master shaders.
    /// </summary>
    public enum B2UShaderProperty
    {
        /// <summary>
        /// The biome texture which indicates the weights of biomes 1-4 (RGBA) in world space. Biome 8 is an absence of all weights.
        /// <para>Type Expectation: Texture2D</para>
        /// </summary>
        BiomeTexture1,
        /// <summary>
        /// The biome texture which indicates the weights of biomes 5-7 (RGB) and the biome subtype (A) in world space. Biome 8 is an absence of all weights (excluding subtype).
        /// <para>Type Expectation: Texture2D</para>
        /// </summary>
        BiomeTexture2,
        /// <summary>
        /// The Color Emission LUT generated by B2U in Blender.
        /// <para>Type Expectation: Texture2D</para>
        /// </summary>
        ColorEmissionLUT,
        /// <summary>
        /// The Property LUT generated by B2U in Blender.
        /// <para>Type Expectation: Texture2D</para>
        /// </summary>
        PropertyLUT,
        /// <summary>
        /// The Iridescence LUT. Each row is one of the different iridescence modes set in Blender and the column is the color applied by changing view/light angle.
        /// <para>Type Expectation: Texture2D</para>
        /// </summary>
        IridescenceLUT,
        /// <summary>
        /// The emission patterns. Each channel represents a different pattern.
        /// <para>Type Expectation: Texture2D</para>
        /// </summary>
        EmissionPatterns,
        /// <summary>
        /// The B2U Detail Normal atlas. RG = Normal, B = curvature, A = height.
        /// <para>Type Expectation: Texture2DArray</para>
        /// </summary>
        DetailMapAtlas,
        /// <summary>
        /// The 3D Noise texture to use. Standard is RGB = different offsets of simplex noise, A = voronoi.
        /// <para>Type Expectation: Texture3D</para>
        /// </summary>
        NoiseTexture,
        /// <summary>
        /// The paint palette to use for paintable solids. A Texture2DArray with 16 slices at 16x8 pixels each.
        /// <para>Type Expectation: Texture2DArray</para>
        /// </summary>
        PaintPalette,
        /// <summary>
        /// The number of subbiomes that the Color LUT has. Equal to (ColorLUTWidth - 8) / 8.
        /// <para>Type Expectation: int</para>
        /// </summary>
        NumberOfSubbiomes,
        /// <summary>
        /// The size of the world in units. Used for solving biome blending. Centered at the world origin.
        /// <para>Type Expectation: Vector2</para>
        /// </summary>
        WorldSize,
        /// <summary>
        /// A global multiplier for the emission strength of all B2U emissive effects.
        /// <para>Min: 0f, Max: 10f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        GlobalEmissionStrength,
        /// <summary>
        /// A global multiplier for the speed of all B2U animated emission effects.
        /// <para>Min: 0f, Max: 10f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        GlobalEmissionSpeed,
        /// <summary>
        /// Whether or not the global cracks are colored by the global color, or the material's emission color.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        GlobalCracksUseEmissionColor,
        /// <summary>
        /// The color to use for cracks.
        /// <para>Type Expectation: Color</para>
        /// </summary>
        GlobalCrackColor,
        /// <summary>
        /// The brightness of crack emission. Set it to 0 if you don't want cracks to glow.
        /// <para>Type Expectation: float</para>
        /// </summary>
        GlobalCrackBrightness,
        /// <summary>
        /// The power to raise the normal triplanar blending to. Higher values make sharper transitions between axes, lower values smooth transitions but cause muddier normals.
        /// <para>Min: 0.1f, Max: 20f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        TriplanarBlendPower,
        /// <summary>
        /// The scale for 3D noise applied to solids.
        /// <para>Min: 0.1f, Max: 10f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        SolidNoiseScale,
        /// <summary>
        /// The strength of the 3D noise applied to solids.
        /// <para>Min: 0f, Max: 2f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        SolidNoiseStrength,
        /// <summary>
        /// The edge color if the edge style is set to colored hard edge.
        /// <para>Type Expectation: Color</para>
        /// </summary>
        SolidHardEdgeColor,
        /// <summary>
        /// If using soft edges: how much the edge mask tints the color/normals. If using hard edges: the step value that determines what is or isn't an edge.
        /// <para>Min: 0f, Max: 1f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        SolidEdgeStrength,
        /// <summary>
        /// How far the edge mask on solids stretches.
        /// <para>Min: 0f, Max: 5f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        SolidEdgeThickness,
        /// <summary>
        /// The power to raise the edge mask to. Only matters for soft edges.
        /// <para>Min: 0.1f, Max: 5f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        SolidEdgePower,
        /// <summary>
        /// The scale of the procedural cracks for solids.
        /// <para>Min: 0f, Max 30f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        SolidCrackScale,
        /// <summary>
        /// A global multiplier for the bubble advection speed of liquids.
        /// <para>Min: 0f, Max: 0.5f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        LiquidBubbleMultiplier,
        /// <summary>
        /// A global multiplier for the angle offset speed of liquids.
        /// <para>Min: 0f, Max: 5f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        LiquidAngleOffsetSpeed,
        /// <summary>
        /// The distortion strength for liquid refraction.
        /// <para>Min: 0f, Max: 5f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        LiquidRefractionDistortionStrength,
        /// <summary>
        /// A multiplier for the shore distance for calculating liquid intersections/foam mask.
        /// <para>Min: 0f, Max: 2f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        LiquidShoreDistance,
        /// <summary>
        /// The strength of normals generated if using Texture3D noise mode for liquids.
        /// <para>Min: 0f, Max: 0.5f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        LiquidNormalStrength,
        /// <summary>
        /// The strength of generated caustics for liquids.
        /// <para>Min: 0f, Max: 2f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        LiquidCausticStrength,
        /// <summary>
        /// The tessellation factor for tessellated liquids. Defines the number of times a triangle is dynamically subdivided.
        /// <para>Min: 1, Max: 20</para>
        /// <para>Type Expectation: int</para>
        /// </summary>
        TessFactor,
        /// <summary>
        /// A distance where any triangles closer than this receive full tessellation.
        /// <para>Type Expectation: float</para>
        /// </summary>
        MinTessDistance,
        /// <summary>
        /// A distance where any triangles further than this receive no tessellation.
        /// <para>Type Expectation: float</para>
        /// </summary>
        MaxTessDistance,
        /// <summary>
        /// The scale of the procedural cracks for crystals.
        /// <para>Min: 0f, Max: 30f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        CrystalCrackScale,
        /// <summary>
        /// Controls if solids have noise applied to break up flat colors and normals.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        UseNoiseForSolids,
        /// <summary>
        /// Controls if solids use procedural noise instead of Texture3D baked noise.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        UseProceduralNoiseForSolids,
        /// <summary>
        /// Controls if liquids use procedural noise instead of Texture3D baked noise.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        UseProceduralNoiseForLiquids,
        /// <summary>
        /// Controls if liquids use slight upward advection to make them look like they are bubbling.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        UseBubbleAdvection,
        /// <summary>
        /// Controls if liquids in procedural noise mode animate the angle offset of voronoi cells.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        UseAngleOffset,
        /// <summary>
        /// Controls if liquids apply chromatic aberration based on their IOR.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        ChromaticAbberationForLiquids,
        /// <summary>
        /// Controls if liquids use vertex displacement in addition to normals for the surface.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        LiquidsHaveVertexDisplacement,
        /// <summary>
        /// Controls if tessellated liquids using procedural noise mode resample the noise in the fragment shader.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        TessellatedLiquidsResampleInFragment,
        /// <summary>
        /// The light response mode for iridescence effects. 0u = ambient, 1u = main light, 2u = all lights.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        IridescenceMode,
        /// <summary>
        /// The light response mode for crystal flare effects. 0u = ambient, 1u = main light, 2u = all lights.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        CrystalFlareMode,
        /// <summary>
        /// The light response mode for liquid caustic effects. 0u = caustics disabled, 1u = main light, 2u = all lights.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        CausticMode,
        /// <summary>
        /// The refraction quality mode for the shaders. 0u = low, 1u = med, 2u = high
        /// <para>Type Expectation: uint</para>
        /// </summary>
        RefractionQualityMode,
        /// <summary>
        /// The resolution of the edge detection buffer. 0u = full resolution, 1u = half, 2u = quarter, 3u = eighth.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        EdgeDetectionBufferResolution,
        /// <summary>
        /// The size of the edges generated by the edge detection feature.
        /// <para>Type Expectation: float</para>
        /// </summary>
        EdgeDetectionSize,
        /// <summary>
        /// The sensitivity of the edge detection feature in depth mode.
        /// <para>Min: 0f, Max: 1f</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        EdgeDetectionSensitivity,
        /// <summary>
        /// The color of edges generated by the edge detection feature.
        /// <para>Type Expectation: Color</para>
        /// </summary>
        EdgeDetectionColor,
        /// <summary>
        /// The layermask for the edge detection feature.
        /// <para>Type Expectation: LayerMask</para>
        /// </summary>
        EdgeDetectionLayermask,
        /// <summary>
        /// The resolution of the backface buffer. 0u = full resolution, 1u = half, 2u = quarter, 3u = eighth.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        BackfaceBufferResolution,
        /// <summary>
        /// The resolution of the color copy buffer. 0u = full resolution, 1u = half, 2u = quarter, 3u = eighth.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        ColorCopyResolution,
        /// <summary>
        /// The light model to use for rendering (0u = URP, 1u = Lightweight, 2u = Toon, 3u = Anti-Light, 4u = Retro, 5u = Gooch, 6u = Faceted, 7u = Pop-Art).
        /// <para>Type Expectation: uint</para>
        /// </summary>
        LightModel,
        /// <summary>
        /// A float that gives additional control for certain light models (Toon steps, Gooch shadow darkness, Pop-Art dot size).
        /// <para>Type Expectation: float</para>
        /// </summary>
        LightControlFloat,
        /// <summary>
        /// Whether or not the liquid immersion fullscreen effect is active.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        ImmersionActive,
        /// <summary>
        /// Whether or not the liquid immersion transition is enabled.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        ImmersionTransitionEnabled,
        /// <summary>
        /// The distance in units above and below the liquid GameObject's y value that the transition happens.
        /// <para>Type Expectation: float</para>
        /// </summary>
        ImmersionTransitionDistance,
        /// <summary>
        /// How far the player can see while immersed. Higher values increase visible distance.
        /// <para>Type Expectation: float</para>
        /// </summary>
        ImmersionClarity,
        /// <summary>
        /// The strength of distortion while immersed.
        /// <para>Min: 0, Max: 0.02</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        ImmersionDistortion,
        /// <summary>
        /// The strength of caustics generated on solids while immersed.
        /// <para>Type Expectation: float</para>
        /// </summary>
        ImmersionCausticStrength,
        /// <summary>
        /// Whether or not the recolor fullscreen effect is active.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        RecolorActive,
        /// <summary>
        /// The blend of the recolor effect.
        /// <para>Min: 0 (No visibility), Max: 1 (Full effect)</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        RecolorBlend,
        /// <summary>
        /// A list of all B2URecolorPreset objects for the B2UShaderProfile.
        /// <para>Type Expectation: B2URecolorPresetCollection</para>
        /// </summary>
        RecolorPresets,
        /// <summary>
        /// Whether or not the stylize fullscreen effect is active.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        StylizeActive,
        /// <summary>
        /// The blend of the stylize effect.
        /// <para>Min: 0 (No visibility), Max: 1 (Full effect)</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        StylizeBlend,
        /// <summary>
        /// A list of all B2UStylizePreset objects for the B2UShaderProfile.
        /// <para>Type Expectation: B2UStylizePresetCollection</para>
        /// </summary>
        StylizePresets,
        /// <summary>
        /// Whether or not the status effects fullscreen pass is enabled.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        StatusActive,
        /// <summary>
        /// A list of all B2UStatusEffectPreset objects for the B2UShaderProfile.
        /// <para>Type Expectation: B2UStatusEffectPresetCollection</para>
        /// </summary>
        StatusPresets,
        /// <summary>
        /// The strength of the active status effect.
        /// <para>Min: 0, Max: 1</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        StatusStrength,
        /// <summary>
        /// The strength of injury. 0 = uninjured, 1 = gravely injured.
        /// <para>Type Expectation: float</para>
        /// </summary>
        InjuryStrength,
        /// <summary>
        /// The color for the injury vignette.
        /// <para>Type Expectation: Color</para>
        /// </summary>
        InjuryColor,
        /// <summary>
        /// The pulse speed for the injury effect. Set at discrete values rather than animating continuously.
        /// <para>Type Expectation: float</para>
        /// </summary>
        InjuryPulseSpeed,
        /// <summary>
        /// The strength of the injury vignette effect.
        /// <para>Min: 0, Max: 1</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        InjuryVignetteStrength,
        /// <summary>
        /// How much the injury effect darkens the scene. Lower values are darker.
        /// <para>Min: 0, Max: 1</para>
        /// <para>Type Expectation: float</para>
        /// </summary>
        InjuryDarkenAmount,
        /// <summary>
        /// Controls if edge detection is enabled.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        EdgeDetectionEnabled,
        /// <summary>
        /// Controls if volumetric refraction is enabled.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        VolumetricRefractionEnabled,
        /// <summary>
        /// Controls if the fullscreen effects feature is enabled.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        FullscreenEffectsEnabled,
        /// <summary>
        /// The mode for the edge detection feature. true = Depth Buffer, false = Custom Edge Buffer.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        EdgeDetectionMode,
        /// <summary>
        /// The space to use for sizing edges in the edge detection feature. true = World Space, false = Screen Space.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        EdgeDetectionSpace,
        /// <summary>
        /// The layer that volumetric meshes are on.
        /// <para>Type Expectation: int</para>
        /// </summary>
        VolumetricLayer,
        /// <summary>
        /// Controls if Vertex Red mesh colors are curved to linear space or stay in sRGB space. true = linear, false = sRGB.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        VertexRGamma,
        /// <summary>
        /// Controls if Vertex Green mesh colors are curved to linear space or stay in sRGB space. true = linear, false = sRGB.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        VertexGGamma,
        /// <summary>
        /// Controls if Vertex Blue mesh colors are curved to linear space or stay in sRGB space. true = linear, false = sRGB.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        VertexBGamma,
        /// <summary>
        /// Controls if the detail map affects hard edge mode for solids.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        DetailMapAffectsHardEdges,
        /// <summary>
        /// The edge mode enum for solids. 0u = concave and convex, 1u = convex only, 2u = concave only.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        EdgeMode,
        /// <summary>
        /// The edge style enum for solids. 0u = soft edges, 1u = hard edges, 2u = colored hard edges.
        /// <para>Type Expectation: uint</para>
        /// </summary>
        EdgeStyle,
        /// <summary>
        /// Controls if the edge mask on solids automatically smooths the normals. true = smooth, false = hard.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        SolidsHaveSmoothEdges,
        /// <summary>
        /// Controls if patterned emission effects on liquids flow with the mesh's flow vectors. true = flow, false = static.
        /// <para>Type Expectation: bool</para>
        /// </summary>
        LiquidEmissionHasFlow,
    }

    /// <summary>
    /// Stores the shader name, cached property ID, expected type, current value, clamping bounds, and keyword options for a B2U shader property.
    /// </summary>
    public class B2UShaderPropertyBlock
    {
        /// <summary>
        /// Creates a new B2UShaderPropertyBlock and caches its shader property ID when the name starts with an underscore.
        /// </summary>
        /// <param name="shaderString">The global shader property name or shader keyword string (e.g. "_B2U_..."), or empty for URP feature-only properties.</param>
        /// <param name="dataType">The expected runtime System.Type for this property's value.</param>
        /// <param name="value">Optional initial value for the property.</param>
        /// <param name="minMax">Optional [min, max] clamping range for numeric properties. Vector2.zero disables clamping.</param>
        /// <param name="enumOptions">Optional array of mutually exclusive shader keywords indexed by a uint value.</param>
        public B2UShaderPropertyBlock(string shaderString, Type dataType, object value = null, Vector2 minMax = new(), string[] enumOptions = null)
        {
            name = shaderString;
            shaderID = (!string.IsNullOrEmpty(name) && name.StartsWith("_")) ? Shader.PropertyToID(shaderString) : 0;
            type = dataType;
            this.value = value;
            this.minMax = minMax;
            this.enumOptions = enumOptions;
        }

        /// <summary>
        /// Validates, clamps, and assigns the value for this property block.
        /// </summary>
        /// <param name="val">The value to set.</param>
        public void SetValue(object val)
        {
            if (val == null || (val is UnityEngine.Object unityObj && unityObj == null))
            {
                if (type == typeof(Texture2D) || type == typeof(Texture2DArray) || type == typeof(Texture3D) ||
                    type == typeof(B2UVolumetricRefraction) || type == typeof(B2UEdgeDetection) || type == typeof(B2UFullscreenEffects) ||
                    type == typeof(B2URecolorPresetCollection) || type == typeof(B2UStylizePresetCollection) || type == typeof(B2UStatusEffectPresetCollection))
                {
                    this.value = null;
                }
                else
                {
                    B2ULogError(B2UMessageSource.Runtime, $"Cannot set {name} to null. Expected: {type}");
                }
                return;
            }
            try
            {
                if (type == typeof(B2URecolorPresetCollection) || type == typeof(B2UStylizePresetCollection) || type == typeof(B2UStatusEffectPresetCollection))
                {
                    this.value = val;
                    return;
                }
                object convertedVal = Convert.ChangeType(val, type);

                if (type == typeof(int) && minMax != Vector2.zero)
                    this.value = Mathf.Clamp((int)convertedVal, (int)minMax.x, (int)minMax.y);
                else if (type == typeof(uint) && minMax != Vector2.zero)
                    this.value = (uint)Mathf.Clamp((int)(uint)convertedVal, (int)minMax.x, (int)minMax.y);
                else if (type == typeof(float) && minMax != Vector2.zero)
                    this.value = Mathf.Clamp((float)convertedVal, minMax.x, minMax.y);
                else
                    this.value = convertedVal;
            }
            catch
            {
                if (type == typeof(B2UVolumetricRefraction) || type == typeof(B2UEdgeDetection) || type == typeof(B2UFullscreenEffects)) return;
                B2ULogError(B2UMessageSource.Runtime, $"The type given for setting {name} cannot be converted. Expected: {type} | Given: {val.GetType()}");
            }
        }

        /// <summary>
        /// The global shader property name or shader keyword string associated with this block.
        /// </summary>
        public string name { get; private set; }
        /// <summary>
        /// Cached Shader.PropertyToID integer for fast global shader updates, or 0 if this property does not map to a shader uniform.
        /// </summary>
        public int shaderID { get; private set; }
        /// <summary>
        /// The expected runtime System.Type of the value stored in this block.
        /// </summary>
        public Type type { get; private set; }
        /// <summary>
        /// The current boxed value stored in this property block.
        /// </summary>
        public object value { get; private set; }
        /// <summary>
        /// Optional [min, max] clamping bounds applied to numeric values. Vector2.zero indicates no clamping.
        /// </summary>
        public Vector2 minMax { get; set; }
        /// <summary>
        /// Optional list of mutually exclusive shader keywords controlled when this property holds a uint enum index.
        /// </summary>
        public string[] enumOptions { get; private set; }
    }
    #endregion

    #region Palette Classes & Enums
    /// <summary>
    /// Runtime container for B2U palette materials and schema slot lookup dictionaries.
    /// </summary>
    public class B2UMaterialContainer
    {
        private string paletteName;
        /// <summary>
        /// Fixed 256-slot lookup array mapping each vertex-color alpha / LUT row index (0-255) to its B2UMaterial.
        /// </summary>
        public B2UMaterial[] matArray { get; private set; } = new B2UMaterial[256];
        /// <summary>
        /// True once InitializeRuntimeDictionaries() has populated the material and schema lookup tables.
        /// </summary>
        public bool IsInitialized { get; private set; } = false;

        private readonly Dictionary<string, B2UMaterial> matDict = new Dictionary<string, B2UMaterial>();
        private readonly Dictionary<B2USlotKey, B2USlotData> solidDataDict = new Dictionary<B2USlotKey, B2USlotData>();
        private readonly Dictionary<B2USlotKey, B2USlotData> crystalDataDict = new Dictionary<B2USlotKey, B2USlotData>();
        private readonly Dictionary<B2USlotKey, B2USlotData> liquidDataDict = new Dictionary<B2USlotKey, B2USlotData>();

        /// <summary>
        /// Populates the runtime material lookup array, name dictionary, and per-workflow slot schema dictionaries from serialized palette data.
        /// </summary>
        /// <param name="name">The display name of the owning B2UPaletteData asset (used in log messages).</param>
        /// <param name="serializedMaterials">Serialized array of B2UMaterial entries from the palette.</param>
        /// <param name="solidMaps">Serialized slot mappings for Solid workflows.</param>
        /// <param name="crystalMaps">Serialized slot mappings for the Crystal workflow.</param>
        /// <param name="liquidMaps">Serialized slot mappings for the Liquid workflow.</param>
        public void InitializeRuntimeDictionaries(string name, B2UMaterial[] serializedMaterials, SerializedSlotMap[] solidMaps, SerializedSlotMap[] crystalMaps, SerializedSlotMap[] liquidMaps)
        {
            paletteName = name;

            Array.Clear(matArray, 0, matArray.Length);
            matDict.Clear();

            if (serializedMaterials != null)
            {
                for (int i = 0; i < serializedMaterials.Length; i++)
                {
                    if (serializedMaterials[i] != null && !string.IsNullOrEmpty(serializedMaterials[i].slotName))
                    {
                        matArray[serializedMaterials[i].alphaValue] = serializedMaterials[i];
                        matDict[serializedMaterials[i].slotName] = serializedMaterials[i];
                    }
                }
            }

            IsInitialized = true;

            solidDataDict.Clear();
            if (solidMaps != null) foreach (var map in solidMaps) solidDataDict[map.key] = map.data;

            crystalDataDict.Clear();
            if (crystalMaps != null) foreach (var map in crystalMaps) crystalDataDict[map.key] = map.data;

            liquidDataDict.Clear();
            if (liquidMaps != null) foreach (var map in liquidMaps) liquidDataDict[map.key] = map.data;
        }

        /// <summary>
        /// Gets a material by the string name assigned to its slot in the palette.
        /// </summary>
        /// <param name="name">The material's slot name.</param>
        /// <returns>The matching B2UMaterial, or null if not found.</returns>
        public B2UMaterial GetMaterialByName(string name)
        {
            if (!string.IsNullOrEmpty(name) && matDict.TryGetValue(name, out B2UMaterial mat)) return mat;
            B2ULogError(B2UMessageSource.Runtime, $"Cannot find material of name: {name} in palette: {paletteName}");
            return null;
        }

        /// <summary>
        /// Gets a material by its 0-255 row index in the palette.
        /// </summary>
        /// <param name="index">The 0-255 alpha / LUT row index.</param>
        /// <returns>The B2UMaterial at the specified index, or null if unassigned.</returns>
        public B2UMaterial GetMaterialByIndex(byte index)
        {
            return matArray[index];
        }

        /// <summary>
        /// Validates and processes a raw byte value against the schema slot rules for the specified workflow.
        /// </summary>
        /// <param name="key">The target B2USlotKey channel.</param>
        /// <param name="workflow">The material's B2UWorkflow.</param>
        /// <param name="value">The raw 0-255 byte value to process.</param>
        /// <param name="processedValue">Outputs the clamped/snapped byte value if the slot exists.</param>
        /// <returns>True if schema data was found for the given key and workflow.</returns>
        public bool TryProcessValue(B2USlotKey key, B2UWorkflow workflow, byte value, out byte processedValue)
        {
            processedValue = 0;
            if (workflow == B2UWorkflow.Opaque || workflow == B2UWorkflow.OpaqueDoubleSided || workflow == B2UWorkflow.Transparent)
            {
                if (solidDataDict.TryGetValue(key, out B2USlotData minMaxObj))
                {
                    processedValue = minMaxObj.ApplyClamping(value);
                    return true;
                }
                B2ULogError(B2UMessageSource.Runtime, $"Cannot retrieve data block {key} for workflow: {workflow}");
            }
            else if (workflow == B2UWorkflow.Crystal)
            {
                if (crystalDataDict.TryGetValue(key, out B2USlotData minMaxObj))
                {
                    processedValue = minMaxObj.ApplyClamping(value);
                    return true;
                }
                B2ULogError(B2UMessageSource.Runtime, $"Cannot retrieve data block {key} for workflow: {workflow}");
            }
            else
            {
                if (liquidDataDict.TryGetValue(key, out B2USlotData minMaxObj))
                {
                    processedValue = minMaxObj.ApplyClamping(value);
                    return true;
                }
                B2ULogError(B2UMessageSource.Runtime, $"Cannot retrieve data block {key} for workflow: {workflow}");
            }
            return false;
        }

        /// <summary>
        /// Retrieves the B2USlotData describing pixel coordinates, channel index, and schema range for a slot key and workflow.
        /// </summary>
        /// <param name="key">The B2USlotKey to look up.</param>
        /// <param name="workflow">The B2UWorkflow schema to query.</param>
        /// <returns>The matching B2USlotData, or null if not defined for that workflow.</returns>
        public B2USlotData GetPixelID(B2USlotKey key, B2UWorkflow workflow)
        {
            switch (workflow)
            {
                case B2UWorkflow.Crystal:
                    if (crystalDataDict.TryGetValue(key, out B2USlotData crystalData)) return crystalData;
                    B2ULogError(B2UMessageSource.Runtime, $"Cannot retrieve crystal data block for: {key} ({workflow})");
                    return null;
                case B2UWorkflow.Liquid:
                    if (liquidDataDict.TryGetValue(key, out B2USlotData liquidData)) return liquidData;
                    B2ULogError(B2UMessageSource.Runtime, $"Cannot retrieve liquid data block for: {key} ({workflow})");
                    return null;
                default:
                    if (solidDataDict.TryGetValue(key, out B2USlotData solidData)) return solidData;
                    B2ULogError(B2UMessageSource.Runtime, $"Cannot retrieve solid data block for: {key} ({workflow})");
                    return null;
            }
        }
    }

    /// <summary>
    /// Describes the LUT coordinates, RGBA channel, value type, and schema min/max range for a single B2U property slot.
    /// </summary>
    [Serializable]
    public class B2USlotData
    {
        /// <summary>
        /// True if this slot is stored on the material's second LUT row (used by Liquid workflows).
        /// </summary>
        public bool secondRow;
        /// <summary>
        /// Horizontal pixel index (0-3) inside the Property LUT, or -1 if stored in the Color LUT alpha channel.
        /// </summary>
        public int pixelIndex;
        /// <summary>
        /// Target RGBA channel index (0 = R, 1 = G, 2 = B, 3 = A).
        /// </summary>
        public int pixelChannel;
        /// <summary>
        /// Schema-defined [min, max] range used to remap normalized 0-1 LUT values into gameplay/shader units.
        /// </summary>
        public Vector2 minMax;
        /// <summary>
        /// The underlying data representation stored in this slot.
        /// </summary>
        public B2USlotType type;

        private static readonly int[][] PixelIDTable = new int[][]
        {
            new int[] { 0, -1, 3 }, // r1_ca
            new int[] { 0, -1, 3 }, // r1_ea
            new int[] { 0, 0, 0 },  // r1_p1_r
            new int[] { 0, 0, 1 },  // r1_p1_g
            new int[] { 0, 0, 2 },  // r1_p1_b
            new int[] { 0, 0, 3 },  // r1_p1_a
            new int[] { 0, 1, 0 },  // r1_p2_r
            new int[] { 0, 1, 1 },  // r1_p2_g
            new int[] { 0, 1, 2 },  // r1_p2_b
            new int[] { 0, 1, 3 },  // r1_p2_a
            new int[] { 0, 2, 0 },  // r1_p3_r
            new int[] { 0, 2, 1 },  // r1_p3_g
            new int[] { 0, 2, 2 },  // r1_p3_b
            new int[] { 0, 2, 3 },  // r1_p3_a
            new int[] { 0, 3, 0 },  // r1_p4_r
            new int[] { 0, 3, 1 },  // r1_p4_g
            new int[] { 0, 3, 2 },  // r1_p4_b
            new int[] { 0, 3, 3 },  // r1_p4_a
            new int[] { 1, -1, 3 }, // r2_ca
            new int[] { 1, -1, 3 }, // r2_ea
            new int[] { 1, 0, 0 },  // r2_p1_r
            new int[] { 1, 0, 1 },  // r2_p1_g
            new int[] { 1, 0, 2 },  // r2_p1_b
            new int[] { 1, 0, 3 },  // r2_p1_a
            new int[] { 1, 1, 0 },  // r2_p2_r
            new int[] { 1, 1, 1 },  // r2_p2_g
            new int[] { 1, 1, 2 },  // r2_p2_b
            new int[] { 1, 1, 3 },  // r2_p2_a
            new int[] { 1, 2, 0 },  // r2_p3_r
            new int[] { 1, 2, 1 },  // r2_p3_g
            new int[] { 1, 2, 2 },  // r2_p3_b
            new int[] { 1, 2, 3 },  // r2_p3_a
            new int[] { 1, 3, 0 },  // r2_p4_r
            new int[] { 1, 3, 1 },  // r2_p4_g
            new int[] { 1, 3, 2 },  // r2_p4_b
            new int[] { 1, 3, 3 }   // r2_p4_a
        };

        /// <summary>
        /// Initializes this slot's LUT coordinates, range, and type from a B2USlotKey.
        /// </summary>
        /// <param name="key">The slot key defining row, pixel, and channel coordinates.</param>
        /// <param name="minMax">The schema-defined [min, max] range.</param>
        /// <param name="type">The slot data type.</param>
        public void InitializeFromKey(B2USlotKey key, Vector2 minMax, B2USlotType type)
        {
            this.minMax = minMax;
            this.type = type;
            int[] pixelID = GetPixelID(key);
            secondRow = pixelID[0] == 1;
            pixelIndex = pixelID[1];
            pixelChannel = pixelID[2];
        }

        /// <summary>
        /// Applies slot-type clamping to a raw byte value (snapping Bool slots to 0 or 255).
        /// </summary>
        /// <param name="value">The raw 0-255 input byte.</param>
        /// <returns>The processed byte value safe to write into the LUT.</returns>
        public byte ApplyClamping(byte value)
        {
            if (type == B2USlotType.Bool) return (byte)(value > 127 ? 255 : 0);
            return value;
        }

        /// <summary>
        /// Returns a pre-allocated [rowOffset, pixelIndex, channelIndex] array for the given slot key without heap allocation.
        /// </summary>
        /// <param name="key">The B2USlotKey to resolve.</param>
        /// <returns>A 3-element int array formatted as [rowOffset, pixelIndex, channelIndex].</returns>
        public static int[] GetPixelID(B2USlotKey key)
        {
            int idx = (int)key;
            if (idx >= 0 && idx < PixelIDTable.Length)
            {
                return PixelIDTable[idx];
            }
            return PixelIDTable[0];
        }
    }

    /// <summary>
    /// Metadata entry representing a single material slot inside a B2UPaletteData asset.
    /// </summary>
    [Serializable]
    public class B2UMaterial
    {
        /// <summary>
        /// True if this material responds to world-space biome weights.
        /// </summary>
        public bool isBiomeResponsive;
        /// <summary>
        /// Biome response mode identifier ("INDEPENDENT", "DYNAMIC", or "INDEP_DYN").
        /// </summary>
        public string biomeType = "INDEPENDENT";
        /// <summary>
        /// The 0-255 vertex-color alpha value and starting LUT row index assigned to this material.
        /// </summary>
        public byte alphaValue = 0;
        /// <summary>
        /// The display and lookup name assigned to this material slot in the palette.
        /// </summary>
        public string slotName = "";
        /// <summary>
        /// The B2U rendering workflow used by this material.
        /// </summary>
        public B2UWorkflow workflow;
    }

    /// <summary>
    /// Serializable key-value pair mapping a B2USlotKey to its B2USlotData for Unity asset serialization.
    /// </summary>
    [Serializable]
    public struct SerializedSlotMap
    {
        /// <summary>
        /// The LUT slot key.
        /// </summary>
        public B2USlotKey key;
        /// <summary>
        /// The slot coordinate, type, and range metadata associated with the key.
        /// </summary>
        public B2USlotData data;
    }

    /// <summary>
    /// Identifies the data format packed into a B2U Property LUT channel.
    /// </summary>
    public enum B2USlotType
    {
        /// <summary>
        /// Normalized floating-point value remapped between minMax.x and minMax.y.
        /// </summary>
        Float,
        /// <summary>
        /// Discrete integer value.
        /// </summary>
        Int,
        /// <summary>
        /// Boolean flag snapped to 0 (false) or 255 (true).
        /// </summary>
        Bool,
        /// <summary>
        /// Enumerated mode index.
        /// </summary>
        Enum,
        /// <summary>
        /// 8-bit boolean bitmask packing up to eight independent flags into a single byte.
        /// </summary>
        Bitmask
    }

    /// <summary>
    /// The rendering workflow for a B2U material.
    /// </summary>
    public enum B2UWorkflow
    {
        /// <summary>
        /// Standard single-sided opaque solid workflow.
        /// </summary>
        Opaque,
        /// <summary>
        /// Double-sided opaque solid workflow with backface color and normal/curvature controls.
        /// </summary>
        OpaqueDoubleSided,
        /// <summary>
        /// Alpha-blended transparent solid workflow.
        /// </summary>
        Transparent,
        /// <summary>
        /// Refractive crystalline workflow with transmission, absorption, and prismatic flare controls.
        /// </summary>
        Crystal,
        /// <summary>
        /// Two-row liquid workflow supporting flow maps, interactive waves, refraction, and immersion.
        /// </summary>
        Liquid
    }

    /// <summary>
    /// Represents all possible property channel slots across the two B2U LUT textures.
    /// </summary>
    public enum B2USlotKey
    {
        /// <summary>
        /// Stores the metallic value for all B2U workflows.
        /// </summary>
        r1_ca,
        /// <summary>
        /// Stores the smoothness value for all B2U workflows.
        /// </summary>
        r1_ea,
        /// <summary>
        /// SOLIDS/CRYSTALS: Stores the emission strength.
        /// <para>LIQUIDS: Stores the noise scale.</para>
        /// </summary>
        r1_p1_r,
        /// <summary>
        /// SOLIDS/CRYSTALS: Stores the emission speed.
        /// <para>LIQUIDS: Stores the noise power.</para>
        /// </summary>
        r1_p1_g,
        /// <summary>
        /// SOLIDS: Stores the iridescence strength.
        /// <para>CRYSTALS: Stores the Index of Refraction.</para>
        /// <para>LIQUIDS: Stores the distortion scale.</para>
        /// </summary>
        r1_p1_b,
        /// <summary>
        /// SOLIDS: Stores the iridescence power.
        /// <para>CRYSTALS: Stores the transmission.</para>
        /// <para>LIQUIDS: Stores the distortion strength.</para>
        /// </summary>
        r1_p1_a,
        /// <summary>
        /// SOLIDS: Stores the detail gradient power.
        /// <para>CRYSTALS: No value.</para>
        /// <para>LIQUIDS: Stores the phase speed.</para>
        /// </summary>
        r1_p2_r,
        /// <summary>
        /// SOLIDS: Stores the detail normal strength.
        /// <para>CRYSTALS: No value.</para>
        /// <para>LIQUIDS: Stores the flow strength.</para>
        /// </summary>
        r1_p2_g,
        /// <summary>
        /// SOLIDS: Stores the detail curvature strength.
        /// <para>CRYSTALS: No value.</para>
        /// <para>LIQUIDS: Stores the wave height.</para>
        /// </summary>
        r1_p2_b,
        /// <summary>
        /// SOLIDS: Stores the detail booleans bitmask.
        /// <para>CRYSTALS: No value.</para>
        /// <para>LIQUIDS: Stores the bubble speed.</para>
        /// </summary>
        r1_p2_a,
        /// <summary>
        /// OPAQUE DOUBLE SIDED: Stores the backface color multiplier.
        /// <para>LIQUIDS: Stores the emission pattern scale.</para>
        /// </summary>
        r1_p3_r,
        /// <summary>
        /// OPAQUE DOUBLE SIDED: Stores the backface boolean bitmask.
        /// <para>LIQUIDS: Stores the emission strength.</para>
        /// </summary>
        r1_p3_g,
        /// <summary>
        /// LIQUIDS: Stores the iridescence strength.
        /// </summary>
        r1_p3_b,
        /// <summary>
        /// LIQUIDS: Stores the iridescence power.
        /// </summary>
        r1_p3_a,
        /// <summary>
        /// Stores the workflow and biome response type for the material slot. DO NOT CHANGE THIS CHANNEL AT RUNTIME.
        /// </summary>
        r1_p4_r,
        /// <summary>
        /// SOLIDS: Stores the detail index (which slice of the detail atlas to use).
        /// <para>CRYSTALS: Stores the prismatic shift.</para>
        /// <para>LIQUIDS: No value.</para>
        /// </summary>
        r1_p4_g,
        /// <summary>
        /// SOLIDS: Stores the emission mode.
        /// <para>CRYSTALS: Stores the prismatic power.</para>
        /// <para>LIQUIDS: Stores the emission mode.</para>
        /// </summary>
        r1_p4_b,
        /// <summary>
        /// SOLIDS/LIQUIDS: Stores the iridescence mode.
        /// <para>CRYSTALS: Stores the emission mode.</para>
        /// </summary>
        r1_p4_a,
        /// <summary>
        /// LIQUIDS: Stores the edge (shore) distance.
        /// </summary>
        r2_ca,
        /// <summary>
        /// LIQUIDS: Stores the viscosity.
        /// </summary>
        r2_ea,
        /// <summary>
        /// LIQUIDS: Stores the transmission.
        /// </summary>
        r2_p1_r,
        /// <summary>
        /// LIQUIDS: Stores the absorption.
        /// </summary>
        r2_p1_g,
        /// <summary>
        /// LIQUIDS: Stores the Index of Refraction.
        /// </summary>
        r2_p1_b,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p1_a,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p2_r,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p2_g,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p2_b,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p2_a,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p3_r,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p3_g,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p3_b,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p3_a,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p4_r,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p4_g,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p4_b,
        /// <summary>
        /// LIQUIDS: Unused in default schema.
        /// </summary>
        r2_p4_a
    }
    #endregion

    #region Miscellaneous Classes & Structs
    /// <summary>
    /// Stack-allocated readonly struct storing the 8 biome weights and subtype sampled at a world position.
    /// </summary>
    public readonly struct B2UBiomeData
    {
        /// <summary>
        /// Represents an uninitialized or failed biome sample (isValidData = false).
        /// </summary>
        public static readonly B2UBiomeData Invalid = default;

        /// <summary>
        /// Raw 0-255 weight for Biome 0 (sampled from BiomeTexture1.r).
        /// </summary>
        public readonly byte b0Weight;
        /// <summary>
        /// Raw 0-255 weight for Biome 1 (sampled from BiomeTexture1.g).
        /// </summary>
        public readonly byte b1Weight;
        /// <summary>
        /// Raw 0-255 weight for Biome 2 (sampled from BiomeTexture1.b).
        /// </summary>
        public readonly byte b2Weight;
        /// <summary>
        /// Raw 0-255 weight for Biome 3 (sampled from BiomeTexture1.a).
        /// </summary>
        public readonly byte b3Weight;
        /// <summary>
        /// Raw 0-255 weight for Biome 4 (sampled from BiomeTexture2.r).
        /// </summary>
        public readonly byte b4Weight;
        /// <summary>
        /// Raw 0-255 weight for Biome 5 (sampled from BiomeTexture2.g).
        /// </summary>
        public readonly byte b5Weight;
        /// <summary>
        /// Raw 0-255 weight for Biome 6 (sampled from BiomeTexture2.b).
        /// </summary>
        public readonly byte b6Weight;
        /// <summary>
        /// Derived 0-255 weight for Biome 7, computed as the remainder not claimed by Biomes 0-6.
        /// </summary>
        public readonly byte b7Weight;

        /// <summary>
        /// Raw 0-255 biome subtype value (sampled from BiomeTexture2.a).
        /// </summary>
        public readonly byte subtype;
        /// <summary>
        /// True if this struct contains a valid biome texture sample; false for B2UBiomeData.Invalid.
        /// </summary>
        public readonly bool isValidData;

        /// <summary>
        /// Creates a valid B2UBiomeData sample from the two biome texture samples.
        /// </summary>
        public B2UBiomeData(byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte subtype)
        {
            b0Weight = b0;
            b1Weight = b1;
            b2Weight = b2;
            b3Weight = b3;
            b4Weight = b4;
            b5Weight = b5;
            b6Weight = b6;

            int sumBytes = b0 + b1 + b2 + b3 + b4 + b5 + b6;
            b7Weight = (byte)(sumBytes >= 255 ? 0 : 255 - sumBytes);

            this.subtype = subtype;
            isValidData = true;
        }

        /// <summary>
        /// Creates an invalid B2UBiomeData object.
        /// </summary>
        public B2UBiomeData(int dummy)
        {
            b0Weight = 0;
            b1Weight = 0;
            b2Weight = 0;
            b3Weight = 0;
            b4Weight = 0;
            b5Weight = 0;
            b6Weight = 0;
            b7Weight = 0;
            subtype = 0;
            isValidData = false;
        }

        /// <summary>
        /// Returns the raw byte weight (0-255) for the specified biome index (0-7) without heap allocations.
        /// </summary>
        public byte GetWeight(int biomeIndex)
        {
            if (!isValidData) return 0;
            return biomeIndex switch
            {
                0 => b0Weight,
                1 => b1Weight,
                2 => b2Weight,
                3 => b3Weight,
                4 => b4Weight,
                5 => b5Weight,
                6 => b6Weight,
                7 => b7Weight,
                _ => (byte)0
            };
        }

        /// <summary>
        /// Returns the normalized float weight (0.0 to 1.0) for the specified biome index (0-7) without heap allocations.
        /// </summary>
        public float GetNormalizedWeight(int biomeIndex)
        {
            return GetWeight(biomeIndex) / 255f;
        }

        /// <summary>
        /// Returns true if the specified biome index (0-7) has a normalized weight greater than or equal to the threshold.
        /// </summary>
        public bool IsBiomePresent(int biomeIndex, float minNormalizedWeight = 0.05f)
        {
            return isValidData && GetNormalizedWeight(biomeIndex) >= minNormalizedWeight;
        }

        /// <summary>
        /// Gets the index (0-7) of the biome with the highest weight.
        /// </summary>
        public int GetPrimaryBiomeIndex()
        {
            if (!isValidData) return -1;

            ReadOnlySpan<byte> weights = stackalloc byte[8]
            {
                    b0Weight, b1Weight, b2Weight, b3Weight, b4Weight, b5Weight, b6Weight, b7Weight
                };

            int highestIndex = 0;
            byte maxWeight = weights[0];

            for (int i = 1; i < weights.Length; i++)
            {
                if (weights[i] > maxWeight)
                {
                    maxWeight = weights[i];
                    highestIndex = i;
                }
            }

            return highestIndex;
        }

        /// <summary>
        /// Gets the discrete subtype index (0 to numSubbiomes - 1).
        /// </summary>
        public int GetSubtypeIndex()
        {
            if (!isValidData) return -1;

            if (B2URuntimeUtility.CheckActivePalette(silent: true))
            {
                int numSubbiomes = Mathf.Max(1, B2URuntimeUtility.ActiveProfile.paletteData.numSubbiomes);
                return Mathf.RoundToInt((subtype / 255f) * (numSubbiomes - 1));
            }
            return -1;
        }

        /// <summary>
        /// Gets the continuous fractional subtype index (0.0 to numSubbiomes - 1.0) used by shader bilinear LUT sampling.
        /// </summary>
        public float GetContinuousSubtype(int numSubbiomes)
        {
            if (!isValidData) return 0f;
            return (subtype / 255f) * Mathf.Max(0, numSubbiomes - 1);
        }

        /// <summary>
        /// Returns an array of biome indices (0-7) sorted from highest weight to lowest weight.
        /// Note: Allocates a new 8-element int array on the heap. Use FillOrderedBiomeIndices(Span&lt;int&gt;) in Update loops.
        /// </summary>
        public int[] GetOrderedBiomeIndices()
        {
            if (!isValidData) return null;

            int[] indices = new int[8];
            FillOrderedBiomeIndices(indices);
            return indices;
        }

        /// <summary>
        /// Populates a caller-provided Span (or stackalloc buffer) of length >= 8 with biome indices (0-7)
        /// sorted from highest weight to lowest weight, with zero heap allocations.
        /// </summary>
        public void FillOrderedBiomeIndices(Span<int> outIndices)
        {
            if (outIndices.Length < 8) return;

            Span<byte> weights = stackalloc byte[8]
            {
                    b0Weight, b1Weight, b2Weight, b3Weight, b4Weight, b5Weight, b6Weight, b7Weight
                };

            for (int i = 0; i < 8; i++) outIndices[i] = i;

            // In-place insertion sort descending by weight
            for (int i = 1; i < 8; i++)
            {
                byte keyWeight = weights[i];
                int keyIndex = outIndices[i];
                int j = i - 1;

                while (j >= 0 && weights[j] < keyWeight)
                {
                    weights[j + 1] = weights[j];
                    outIndices[j + 1] = outIndices[j];
                    j--;
                }
                weights[j + 1] = keyWeight;
                outIndices[j + 1] = keyIndex;
            }
        }
    }
    #endregion
}