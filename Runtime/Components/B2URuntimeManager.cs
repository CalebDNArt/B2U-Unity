using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Singleton runtime manager that coordinates B2U liquid wave propagation, fullscreen effect coroutines,
    /// and multi-frame B2URuntimeAnimator tasks.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class B2URuntimeManager : MonoBehaviour
    {
        /// <summary>
        /// The singleton instance of the manager in the scene. Calling B2URuntimeManager.Instance anywhere in script will reference this instance.
        /// </summary>
        public static B2URuntimeManager Instance { get; private set; }
        /// <summary>
        /// The default B2UShaderProfile to initialize on start. This will ensure a profile is bound when the game starts. Leave it null if you are setting this in a custom script.
        /// </summary>
        public B2UShaderProfile defaultProfile = null;

        /// <summary>
        /// A constant for the maximum waves the wave system can create.
        /// </summary>
        private const int MAX_WAVES = 64;
        /// <summary>
        /// A constant for the maximum number of meshes that can have waves on them at the same time.
        /// </summary>
        private const uint MAX_MESH_IDS = 63;
        /// <summary>
        /// The maximum time a wave instance can exist before destroying itself.
        /// </summary>
        private const float MAX_WAVE_TIME = 20.0f;

        /// <summary>
        /// Global shader ID for the liquid wave array.
        /// </summary>
        private static readonly int WaveArrayID = Shader.PropertyToID("_B2U_Wave_Array");
        /// <summary>
        /// Global shader ID for the number of active waves.
        /// </summary>
        private static readonly int ActiveWaveID = Shader.PropertyToID("_B2U_Active_Waves");

        #region Status & Fullscreen Coroutine State
        /// <summary>
        /// Stores the coroutine for animating a recolor effect.
        /// </summary>
        [NonSerialized] public Coroutine recolorCoroutine = null;
        /// <summary>
        /// Stores the coroutine for animating a stylize effect.
        /// </summary>
        [NonSerialized] public Coroutine stylizeCoroutine = null;
        /// <summary>
        /// Stores the coroutine for animating a status effect.
        /// </summary>
        [NonSerialized] public Coroutine statusEffectCoroutine = null;

        /// <summary>
        /// Whether or not there is a status effect currently active on the player.
        /// </summary>
        /// <remarks>Hook into this to control behaviour during a status effect like taking fire damage.</remarks>
        [HideInInspector] public bool hasActiveStatusEffect = false;
        /// <summary>
        /// The type of status effect on the player (only updates if an effect is activated).
        /// </summary>
        /// <remarks>Hook into this to control behaviour during a status effect like taking fire damage.</remarks>
        /// <example>
        /// <code>
        /// private void Update()
        /// {
        ///     if (B2URuntimeManager.Instance.hasActiveStatusEffect)
        ///     {
        ///         switch (B2URuntimeManager.Instance.activeStatusEffectType)
        ///         {
        ///             case B2UStatusEffect.Burning:
        ///                 // Apply damage while the effect is active.
        ///                 player.health -= 5f * Time.deltaTime;
        ///                 break;
        ///             case B2UStatusEffect.Freezing:
        ///                 // Slow the player based on the effect intensity.
        ///                 float intensity = B2URuntimeUtility.GetB2UShaderPropertyValue(B2UShaderProperty.StatusStrength, 0f, true);
        ///                 player.speed = 0.1f * intensity;
        ///                 break;
        ///         }
        ///     }
        /// }
        /// </code>
        /// </example>
        [HideInInspector] public B2UStatusEffect activeStatusEffectType = B2UStatusEffect.Burning;
        #endregion

        #region Liquid Wave Data Structures
        /// <summary>
        /// Holds the data needed for a wave to interact with a liquid mesh.
        /// </summary>
        private struct WaveData
        {
            /// <summary>
            /// The origin of the wave.
            /// </summary>
            public Vector3 origin;
            /// <summary>
            /// The mesh ID of the liquid mesh the wave is on.
            /// </summary>
            public uint meshID;
            /// <summary>
            /// The amplitude of the wave.
            /// </summary>
            public float amplitude;
            /// <summary>
            /// The time since the wave was created.
            /// </summary>
            public float time;
        }

        /// <summary>
        /// A list of active WaveData.
        /// </summary>
        private readonly List<WaveData> activeWavesList = new List<WaveData>(MAX_WAVES);
        /// <summary>
        /// The array of packed wave data that gets pushed to the shader.
        /// </summary>
        private readonly Vector4[] waveArray = new Vector4[MAX_WAVES];

        /// <summary>
        /// Stores a FIFO collection of available mesh IDs (Storing a value pushes it to the end, removing one pulls from the start).
        /// </summary>
        private readonly Queue<uint> availableMeshIDs = new Queue<uint>((int)MAX_MESH_IDS);
        /// <summary>
        /// A dictionary of MeshID - GameObject that stores the ID associated with a mesh that has a wave on it.
        /// </summary>
        private readonly Dictionary<uint, GameObject> activeLiquidObjects = new Dictionary<uint, GameObject>((int)MAX_MESH_IDS);
        /// <summary>
        /// Stores how many waves are active on the mesh with a specific ID.
        /// </summary>
        private readonly int[] meshIDRefCounts = new int[MAX_WAVES];

        /// <summary>
        /// Whether or not the wave pool is initialized.
        /// </summary>
        [NonSerialized] private bool poolInitialized = false;
        /// <summary>
        /// If the controller had active waves last frame. Disables binding the array to the GPU if not needed.
        /// </summary>
        [NonSerialized] private bool hadActiveWavesLastFrame = false;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                B2ULogWarning(B2UMessageSource.Runtime, "Duplicate B2URuntimeManager detected in scene. Destroying duplicate component.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            EnsurePoolInitialized();
            Shader.SetGlobalFloat(ActiveWaveID, 0f);
        }

        private void Start()
        {
            if (defaultProfile && defaultProfile.paletteData)
            {
                B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(defaultProfile);
                B2URuntimeUtility.SetNewPalette(defaultProfile.paletteData);
            }
        }

        private void Update()
        {
            if (Instance == null) SetInstance();
            UpdateWaves();
            B2URuntimeAnimator.UpdateAnimationTasks();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                StopAllCoroutines();
                recolorCoroutine = null;
                stylizeCoroutine = null;
                statusEffectCoroutine = null;
                hasActiveStatusEffect = false;

                Shader.SetGlobalFloat(ActiveWaveID, 0f);
                Instance = null;
            }
        }
        #endregion

        #region Singleton Initialization
        /// <summary>
        /// Assigns this component as the active singleton instance. Safe to call in both Edit Mode and Play Mode.
        /// </summary>
        public void SetInstance()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(this);
                else DestroyImmediate(this);
                return;
            }
            Instance = this;
            EnsurePoolInitialized();
        }

        /// <summary>
        /// Ensures that the wave pool data is initialized, or creates it if not.
        /// </summary>
        private void EnsurePoolInitialized()
        {
            if (poolInitialized && (availableMeshIDs.Count > 0 || activeLiquidObjects.Count > 0))
            {
                return;
            }

            availableMeshIDs.Clear();
            activeLiquidObjects.Clear();
            activeWavesList.Clear();
            Array.Clear(meshIDRefCounts, 0, meshIDRefCounts.Length);

            for (uint i = 1; i <= MAX_MESH_IDS; i++)
            {
                availableMeshIDs.Enqueue(i);
            }
            poolInitialized = true;
        }
        #endregion

        #region Liquid Wave Management
        /// <summary>
        /// Advances all active liquid waves, prunes expired entries, and uploads the compacted wave buffer to the GPU.
        /// </summary>
        public void UpdateWaves()
        {
            int count = activeWavesList.Count;
            if (count == 0)
            {
                if (hadActiveWavesLastFrame)
                {
                    Shader.SetGlobalFloat(ActiveWaveID, 0f);
                    hadActiveWavesLastFrame = false;
                }
                return;
            }

            float dt = Time.deltaTime;

            // 1. Advance wave timers and remove expired waves
            for (int i = count - 1; i >= 0; i--)
            {
                WaveData wave = activeWavesList[i];
                wave.time += dt;

                if (wave.time >= MAX_WAVE_TIME)
                {
                    ReleaseMeshID(wave.meshID);
                    activeWavesList.RemoveAt(i);
                }
                else
                {
                    activeWavesList[i] = wave;
                }
            }

            // 2. Pack remaining active waves contiguously into the GPU vector array
            int activeCount = activeWavesList.Count;
            Shader.SetGlobalFloat(ActiveWaveID, activeCount);

            if (activeCount == 0)
            {
                hadActiveWavesLastFrame = false;
                return;
            }

            for (int i = 0; i < activeCount; i++)
            {
                WaveData wave = activeWavesList[i];
                uint packedW = B2URuntimeUtility.PackWaveData(wave.meshID, wave.amplitude, wave.time);
                waveArray[i] = new Vector4(wave.origin.x, wave.origin.y, wave.origin.z, math.asfloat(packedW));
            }

            Shader.SetGlobalVectorArray(WaveArrayID, waveArray);
            hadActiveWavesLastFrame = true;
        }

        /// <summary>
        /// Registers a new wave for the specified liquid MeshID. Evicts the oldest wave if the 64-wave buffer is full.
        /// </summary>
        /// <param name="origin">World-space origin of the wave.</param>
        /// <param name="meshID">The 1-63 MeshID of the target liquid body.</param>
        /// <param name="amplitude">Wave height displacement (0-2m).</param>
        /// <remarks>This method is called by B2URuntimeUtility.InstantiateWave(). Use that method to create waves as it can be called directly from a GameObject and ensures that it meets the requirements.</remarks>
        public void AddWave(Vector3 origin, uint meshID, float amplitude)
        {
            if (meshID == 0 || meshID > MAX_MESH_IDS)
            {
                B2ULogError(B2UMessageSource.Runtime, $"Invalid liquid MeshID ({meshID}) passed to AddWave. Valid range is 1-{MAX_MESH_IDS}.", this);
                return;
            }

            EnsurePoolInitialized();

            if (activeWavesList.Count >= MAX_WAVES)
            {
                uint oldestMeshID = activeWavesList[0].meshID;
                ReleaseMeshID(oldestMeshID);
                activeWavesList.RemoveAt(0);
            }

            meshIDRefCounts[meshID]++;
            activeWavesList.Add(new WaveData
            {
                origin = origin,
                meshID = meshID,
                amplitude = amplitude,
                time = 0f
            });
        }

        /// <summary>
        /// Allocates an available 1-63 MeshID from the pool and binds it to the provided liquid GameObject.
        /// </summary>
        public uint GetAvailableMeshID(GameObject liquidObject)
        {
            EnsurePoolInitialized();

            if (availableMeshIDs.Count > 0)
            {
                uint newID = availableMeshIDs.Dequeue();
                activeLiquidObjects[newID] = liquidObject;
                return newID;
            }
            return 0;
        }

        /// <summary>
        /// Decrements the active wave reference count for a MeshID and returns the ID to the pool when all its waves expire.
        /// </summary>
        private void ReleaseMeshID(uint meshID)
        {
            if (meshID == 0 || meshID > MAX_MESH_IDS) return;

            meshIDRefCounts[meshID]--;

            if (meshIDRefCounts[meshID] <= 0)
            {
                meshIDRefCounts[meshID] = 0;

                if (activeLiquidObjects.TryGetValue(meshID, out GameObject liquidObject))
                {
                    if (liquidObject != null)
                    {
                        liquidObject.SetLiquidMeshID(0);
                    }
                    activeLiquidObjects.Remove(meshID);
                }

                availableMeshIDs.Enqueue(meshID);
            }
        }

        /// <summary>
        /// Checks if the given ID is registered to an object.
        /// </summary>
        /// <param name="meshID">The 1-63 mesh ID to check.</param>
        /// <param name="liquidObject">The GameObject to check.</param>
        /// <returns>True if the mesh ID is registered to the object.</returns>
        public bool IsMeshIDRegisteredTo(uint meshID, GameObject liquidObject)
        {
            EnsurePoolInitialized();
            return meshID != 0
                && activeLiquidObjects.TryGetValue(meshID, out GameObject registeredObj)
                && registeredObj == liquidObject;
        }

        /// <summary>
        /// Evaluates the combined vertical wave displacement at a world-space position for a specific 6-bit liquid MeshID.
        /// Mirrors the GPU wave interference math in B2USolveWavesInternal (B2U_Liquid.hlsl).
        /// </summary>
        public float EvaluateWaveHeight(uint targetMeshID, Vector3 positionWS, Vector3 flowAdvectionWS, float viscosity = 0.5f)
        {
            if (targetMeshID == 0 || activeWavesList.Count == 0) return 0f;

            float totalWaveHeight = 0f;
            float visc = Mathf.Clamp01(viscosity);
            float timeDecayRate = Mathf.Max(visc, 0.15f) * 1.8f;

            for (int i = 0; i < activeWavesList.Count; i++)
            {
                WaveData wave = activeWavesList[i];
                if (wave.meshID != targetMeshID) continue;

                Vector3 originWS = wave.origin + (flowAdvectionWS * wave.time);
                float dist = Vector3.Distance(positionWS, originWS);

                float normAmp = Mathf.Sqrt(Mathf.Clamp01(wave.amplitude * 0.1f));
                float waveSpeed = Mathf.Lerp(10.0f, 3.8f, normAmp) * Mathf.Lerp(1.0f, 0.6f, visc);
                float wavelength = Mathf.Lerp(1.2f, 12.0f, normAmp);

                float travelDist = wave.time * waveSpeed;
                float u = (travelDist - dist) / wavelength;

                if (u <= 0f || u >= 1f) continue;

                float timeDecay = Mathf.Exp(-wave.time * timeDecayRate);
                float decayRate = Mathf.Lerp(0.35f, 0.04f, normAmp);
                float spatialDecay = 1.0f / (1.0f + (dist * dist * decayRate));

                float wavePacket = Mathf.Sin(u * 6.2831853f) * Mathf.Sin(u * 3.14159265f) * 0.92f;
                totalWaveHeight += wavePacket * wave.amplitude * spatialDecay * timeDecay;
            }

            return totalWaveHeight;
        }
        #endregion
    }
}