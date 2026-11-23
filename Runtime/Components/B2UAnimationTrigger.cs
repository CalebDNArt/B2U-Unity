using System;
using System.Collections.Generic;
using UnityEngine;
using static B2U.B2URuntimeAnimator;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Component that stores a list of configurable B2U animation events (Global Shader Floats, Palette LUTs,
    /// per-object RSUVs, Fullscreen Effect Triggers, and Data Asset Swaps) that can be triggered via UnityEvents, timelines, or scripts.
    /// </summary>
    [ExecuteAlways]
    public class B2UAnimationTrigger : MonoBehaviour
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Runtime;
        /// <summary>
        /// Stores and serializes all different animation events on this component.
        /// </summary>
        public List<B2UAnimationEvent> events = new List<B2UAnimationEvent>();

        private void Update()
        {
            if (events == null) return;

            for (int i = 0; i < events.Count; i++)
            {
                B2UAnimationEvent e = events[i];
                if (e != null && e.task != null && e.task.killTask)
                {
                    e.task = null;
                }
            }
        }

        private void OnDisable()
        {
            KillAllChildEvents();
        }

        /// <summary>
        /// Triggers the animation event at the specified list index.
        /// </summary>
        /// <param name="index">The index to trigger.</param>
        /// <example>
        /// <code>
        /// public B2UAnimationTrigger animationTrigger;
        /// public int indexToTrigger;
        /// public bool triggerEvent = false;
        /// 
        /// private void Update()
        /// {
        ///     if (triggerEvent)
        ///     {
        ///         animationTrigger.TriggerAnimationEvent(indexToTrigger);
        ///         triggerEvent = false;
        ///     }
        /// }
        /// </code>
        /// </example>
        public void TriggerAnimationEvent(int index)
        {
            if (events == null || index < 0 || index >= events.Count)
            {
                B2ULogError(msgSrc, $"Animation event index {index} is out of bounds on '{name}'.", this);
                return;
            }
            events[index]?.TriggerAnimation();
        }

        /// <summary>
        /// Stops the animation event at the specified list index if it is currently active.
        /// </summary>
        /// <param name="index">The index to kill.</param>
        /// <example>
        /// <code>
        /// public B2UAnimationTrigger animationTrigger;
        /// public int indexToKill = 0;
        /// public bool killTarget = false;
        /// 
        /// private void Update()
        /// {
        ///     if (killTarget)
        ///     {
        ///         animationTrigger.KillChildEvent(indexToKill);
        ///         killTarget = false;
        ///     }
        /// }
        /// </code>
        /// </example>
        public void KillChildEvent(int index)
        {
            if (events == null || index < 0 || index >= events.Count) return;
            events[index]?.KillTask();
        }

        /// <summary>
        /// Stops all currently active animation events on this trigger.
        /// </summary>
        /// <example>
        /// <code>
        /// public B2UAnimationTrigger animationTrigger;
        /// 
        /// private void OnDisable()
        /// {
        ///     animationTrigger.KillAllChildEvents();
        /// }
        /// </code>
        /// </example>
        public void KillAllChildEvents()
        {
            if (events == null) return;

            for (int i = 0; i < events.Count; i++)
            {
                events[i]?.KillTask();
            }
        }


        /// <summary>
        /// Stores the instance of a single B2U animation event.
        /// </summary>
        /// <remarks>I strongly recommend using the B2UAnimationTrigger component to create these in editor. You can manually create them but you must ensure you set all of the fields for the required group.</remarks>
        /// <example>
        /// <code>
        /// public B2UAnimationTrigger animationTrigger;
        /// 
        /// private void Start()
        /// {
        ///     // Creates an animation event for animating the liquid bubble multiplier.
        ///     B2UAnimationEvent newEvent = new();
        ///     newEvent.group = B2UAnimationGroup.GlobalFloat;
        ///     newEvent.property = B2UShaderProperty.LiquidBubbleMultiplier;
        ///     newEvent.target = new Vector4(0.5f, 0f, 0f, 0f);
        ///     newEvent.duration = 3f;
        ///     newEvent.mode = AnimMode.Sigmoidal;
        ///     
        ///     // Register the event into the monobehaviour and trigger.
        ///     animationTrigger.events.Add(newEvent);
        ///     animationTrigger.TriggerAnimationEvent(animationTrigger.events.Count - 1);
        /// }
        /// </code>
        /// </example>
        [Serializable]
        public class B2UAnimationEvent
        {
            /// <summary>
            /// The animation group you want this event to apply to.
            /// </summary>
            public B2UAnimationGroup group;

            // Global Float Settings
            /// <summary>
            /// The shader property to animate. This must be a float property or the runtime animator will refuse the event.
            /// </summary>
            public B2UShaderProperty property;

            // Palette Settings
            /// <summary>
            /// Whether or not to change a property LUT pixel (false), or a color LUT pixel (true)
            /// </summary>
            public bool changeColorLUT = false;
            /// <summary>
            /// Only if changeColorLUT = true, whether or not you are trying to change the base color or emission color.
            /// </summary>
            public bool changeEmission = false;
            /// <summary>
            /// Only if changeColorLUT = true AND the material index you are changing is an Independent & Dynamic material. This controls whether to change the independent or dynamic row.
            /// </summary>
            public bool changeDynamic = false;
            /// <summary>
            /// Only if changeColorLUT = true AND the material index you are changing is liquid. Controls whether you are modifying the surface/depth base or emission colors.
            /// </summary>
            public bool changeDepth = false;
            /// <summary>
            /// The index in the LUTs for the materials start row (or just the row if it only occupies 1).
            /// </summary>
            public int materialIndex = 0;
            /// <summary>
            /// Only if changeColorLUT = true. The variant or biome to change.
            /// </summary>
            public int variantOrBiome = 0;
            /// <summary>
            /// Only if changeColorLUT = true AND the material is dynamic, the biome subtype to change.
            /// </summary>
            public int subtype = 0;
            /// <summary>
            /// Only if changeColorLUT = false. The slot key (pixel and channel) to change in the property LUT.
            /// </summary>
            public B2USlotKey slotKey;

            // RSUV Settings
            /// <summary>
            /// The B2URSUVMono component you are animating for the event.
            /// </summary>
            public B2URSUVMono rsuvMono;
            /// <summary>
            /// The RSUV value to animate. Ensure this matches a valid type for the class of RSUV being edited.
            /// </summary>
            public B2URSUVAnimType rsuvAnimType = B2URSUVAnimType.EmissionStrength;

            // Fullscreen Trigger Settings
            /// <summary>
            /// Which of the fullscreen effect classes to trigger.
            /// </summary>
            public B2UFullscreenType fullscreenType = B2UFullscreenType.Recolor;
            /// <summary>
            /// The index in the current B2U Fullscreen Collection object to trigger.
            /// </summary>
            public int fullscreenEffectIndex = 0;

            // Data Swap Settings
            /// <summary>
            /// The type of data swap being performed.
            /// </summary>
            public B2UDataSwapType dataSwapType = B2UDataSwapType.Palette;
            /// <summary>
            /// The scriptable object to swap.
            /// </summary>
            public UnityEngine.Object dataSwapTarget;

            // Shared Animation Settings
            /// <summary>
            /// A reference to the active task that this event is associated with. Only exists while an animation is running.
            /// </summary>
            [NonSerialized] public B2UAnimTask task;
            /// <summary>
            /// Whether or not to use a custom start value (will snap to it at the beginning of the animation) or to start at the existing value.
            /// </summary>
            public bool useCustomStart = false;
            /// <summary>
            /// The custom start value.
            /// </summary>
            /// <remarks>
            /// The data must enter as a Vector4, for floats, Vector2, or Vector3 values, simply set the unneeded components to 0f.
            /// </remarks>
            /// <example>
            /// <code>
            /// // For a float
            /// Vector4 start = new Vector4(startValue, 0f, 0f, 0f);
            /// 
            /// // For a Vector2
            /// Vector4 start = new Vector4(startValue.x, startValue.y, 0f, 0f);
            /// 
            /// // For a Vector3
            /// Vector4 start = new Vector4(startValue.x, startValue.y, startValue.z, 0f);
            /// </code>
            /// </example>
            public Vector4 customStart;
            /// <summary>
            /// The target value;
            /// </summary>
            /// <remarks>
            /// The data must enter as a Vector4, for floats, Vector2, or Vector3 values, simply set the unneeded components to 0f.
            /// </remarks>
            /// <example>
            /// <code>
            /// // For a float
            /// Vector4 target = new Vector4(targetValue, 0f, 0f, 0f);
            /// 
            /// // For a Vector2
            /// Vector4 target = new Vector4(targetValue.x, targetValue.y, 0f, 0f);
            /// 
            /// // For a Vector3
            /// Vector4 target = new Vector4(targetValue.x, targetValue.y, targetValue.z, 0f);
            /// </code>
            /// </example>
            public Vector4 target;
            /// <summary>
            /// Controls animation length and decay direction in seconds.
            /// <para>Aperiodic modes: positive values interpolate from start to target over the given duration; values &lt;= 0f snap immediately to the target.</para>
            /// <para>Periodic and AnimationCurve modes: positive values decay back toward the start value over {duration} seconds; negative values decay toward the target value over {duration} seconds; 0f loops indefinitely without decay.</para>
            /// <para>StatusEffect FullscreenTriggers: used as the hold duration (in seconds) before fading out (&lt;= 0f holds indefinitely).</para>
            /// </summary>
            public float duration = 1f;
            /// <summary>
            /// How fast a periodic animation cycles.
            /// </summary>
            public float speed = 1f;
            /// <summary>
            /// The animation mode to use for the event.
            /// </summary>
            public AnimMode mode = AnimMode.Linear;
            /// <summary>
            /// Only if mode = AnimMode.AnimationCurve. Defines the curve to follow for the animation.
            /// </summary>
            public AnimationCurve curve = null;

            /// <summary>
            /// Dispatches the animation event to the runtime animator.
            /// </summary>
            public void TriggerAnimation()
            {
                if (task != null && task.killTask)
                {
                    task = null;
                }

                float? sVal = useCustomStart ? customStart.x : null;
                Color? sCol = useCustomStart ? (Color)customStart : null;

                switch (group)
                {
                    case B2UAnimationGroup.GlobalFloat:
                        task = AnimateGlobalFloat(property, target.x, duration, speed, mode, curve, task, sVal);
                        break;

                    case B2UAnimationGroup.Palette:
                        if (materialIndex < 0 || materialIndex > 255) return;
                        if (!B2URuntimeUtility.CheckPaletteMaterials()) return;

                        B2UMaterialContainer mats = B2URuntimeUtility.ActiveProfile.paletteData.materials;
                        B2UMaterial mat = mats.GetMaterialByIndex((byte)materialIndex);
                        if (mat == null)
                        {
                            B2ULogError(msgSrc, $"Cannot trigger palette animation: no material exists at index {materialIndex}.");
                            return;
                        }

                        if (changeColorLUT)
                        {
                            bool isSecondRow = mat.workflow == B2UWorkflow.Liquid && changeDepth;

                            if (mat.biomeType == "INDEPENDENT")
                            {
                                task = AnimatePaletteColor(mat, target, duration, changeEmission, variantOrBiome, isSecondRow, speed, mode, curve, task, sCol);
                            }
                            else if (mat.biomeType == "DYNAMIC")
                            {
                                task = AnimatePaletteColor(mat, target, duration, changeEmission, variantOrBiome, subtype, isSecondRow, speed, mode, curve, task, sCol);
                            }
                            else if (mat.biomeType == "INDEP_DYN" || mat.biomeType == "INDEP-DYN")
                            {
                                if (changeDynamic)
                                    task = AnimatePaletteColor(mat, target, duration, changeEmission, variantOrBiome, subtype, isSecondRow, speed, mode, curve, task, sCol);
                                else
                                    task = AnimatePaletteColor(mat, target, duration, changeEmission, variantOrBiome, isSecondRow, speed, mode, curve, task, sCol);
                            }
                        }
                        else
                        {
                            task = AnimatePaletteProperty(mat, slotKey, target.x, duration, speed, mode, curve, task, sVal);
                        }
                        break;

                    case B2UAnimationGroup.RSUV:
                        if (rsuvMono == null) return;

                        switch (rsuvMono.activeType)
                        {
                            case B2URSUVMono.RSUVType.Solid:
                                if (rsuvAnimType == B2URSUVAnimType.EmissionStrength)
                                    task = rsuvMono.AnimateSolidEmission(target.x, duration, speed, mode, curve, task, sVal);
                                else if (rsuvAnimType == B2URSUVAnimType.CrackIntensity)
                                    task = rsuvMono.AnimateSolidCrackIntensity(target.x, duration, speed, mode, curve, task, sVal);
                                break;

                            case B2URSUVMono.RSUVType.Crystal:
                                if (rsuvAnimType == B2URSUVAnimType.EmissionStrength)
                                    task = rsuvMono.AnimateCrystalEmission(target.x, duration, speed, mode, curve, task, sVal);
                                else if (rsuvAnimType == B2URSUVAnimType.CrackIntensity)
                                    task = rsuvMono.AnimateCrystalCrackIntensity(target.x, duration, speed, mode, curve, task, sVal);
                                else if (rsuvAnimType == B2URSUVAnimType.Transmission)
                                    task = rsuvMono.AnimateCrystalTransmission(target.x, duration, speed, mode, curve, task, sVal);
                                else if (rsuvAnimType == B2URSUVAnimType.Absorption)
                                    task = rsuvMono.AnimateCrystalAbsorption(target.x, duration, speed, mode, curve, task, sVal);
                                break;

                            case B2URSUVMono.RSUVType.Liquid:
                                if (rsuvAnimType == B2URSUVAnimType.EmissionStrength)
                                    task = rsuvMono.AnimateLiquidEmission(target.x, duration, speed, mode, curve, task, sVal);
                                else if (rsuvAnimType == B2URSUVAnimType.FlowStrength)
                                    task = rsuvMono.AnimateLiquidFlowStrength(target.x, duration, speed, mode, curve, task, sVal);
                                else if (rsuvAnimType == B2URSUVAnimType.WaveMult)
                                    task = rsuvMono.AnimateLiquidWaveMultiplier(target.x, duration, speed, mode, curve, task, sVal);
                                break;
                        }
                        break;

                    case B2UAnimationGroup.FullscreenTrigger:
                        switch (fullscreenType)
                        {
                            case B2UFullscreenType.Recolor:
                                B2URuntimeUtility.TriggerRecolorEffect(fullscreenEffectIndex, target.x, speed);
                                break;
                            case B2UFullscreenType.Stylize:
                                B2URuntimeUtility.TriggerStylizeEffect(fullscreenEffectIndex, target.x, speed);
                                break;
                            case B2UFullscreenType.StatusEffect:
                                B2URuntimeUtility.TriggerStatusEffect(fullscreenEffectIndex, target.x, speed, duration);
                                break;
                        }
                        break;

                    case B2UAnimationGroup.DataSwap:
                        if (dataSwapTarget == null)
                        {
                            B2ULogWarning(msgSrc, $"DataSwap event for '{dataSwapType}' has no target asset assigned.");
                            return;
                        }

                        switch (dataSwapType)
                        {
                            case B2UDataSwapType.Profile:
                                if (dataSwapTarget is B2UShaderProfile profile)
                                    B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(profile);
                                else
                                    B2ULogError(msgSrc, $"DataSwap target '{dataSwapTarget.name}' is not a valid B2UShaderProfile.");
                                break;

                            case B2UDataSwapType.Palette:
                                if (dataSwapTarget is B2UPaletteData palette)
                                    B2URuntimeUtility.SetNewPalette(palette);
                                else
                                    B2ULogError(msgSrc, $"DataSwap target '{dataSwapTarget.name}' is not a valid B2UPaletteData.");
                                break;

                            case B2UDataSwapType.RecolorCollection:
                                if (dataSwapTarget is B2URecolorPresetCollection recolorCol)
                                    B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.RecolorPresets, recolorCol);
                                else
                                    B2ULogError(msgSrc, $"DataSwap target '{dataSwapTarget.name}' is not a valid B2URecolorPresetCollection.");
                                break;

                            case B2UDataSwapType.StylizeCollection:
                                if (dataSwapTarget is B2UStylizePresetCollection stylizeCol)
                                    B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.StylizePresets, stylizeCol);
                                else
                                    B2ULogError(msgSrc, $"DataSwap target '{dataSwapTarget.name}' is not a valid B2UStylizePresetCollection.");
                                break;

                            case B2UDataSwapType.StatusEffectCollection:
                                if (dataSwapTarget is B2UStatusEffectPresetCollection statusCol)
                                    B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.StatusPresets, statusCol);
                                else
                                    B2ULogError(msgSrc, $"DataSwap target '{dataSwapTarget.name}' is not a valid B2UStatusEffectPresetCollection.");
                                break;
                        }
                        break;
                }
            }

            /// <summary>
            /// Kills the task. This is an internal function, use B2UAnimationTrigger.KillChildEvent(index) instead.
            /// </summary>
            public void KillTask()
            {
                if (task != null)
                {
                    task.KillTask();
                    task = null;
                }
            }
        }

        /// <summary>
        /// The B2U animation type you want for an event.
        /// </summary>
        public enum B2UAnimationGroup
        {
            /// <summary>
            /// Animates a global float property in the active B2UShaderProfile.
            /// </summary>
            GlobalFloat,
            /// <summary>
            /// Animates a color or property value in the LUTs of the active B2UPaletteData.
            /// </summary>
            Palette,
            /// <summary>
            /// Animates an RSUV float property.
            /// </summary>
            RSUV,
            /// <summary>
            /// Activates or deactivates one of the fullscreen effects.
            /// </summary>
            FullscreenTrigger,
            /// <summary>
            /// Swaps a B2U Scriptable Object data block (Instantaneous).
            /// </summary>
            DataSwap
        }

        /// <summary>
        /// The RSUV property to animate.
        /// </summary>
        public enum B2URSUVAnimType
        {
            /// <summary>
            /// Animates the emission strength of the object. All RSUV types.
            /// </summary>
            EmissionStrength,
            /// <summary>
            /// Animates the crack intensity of the object. Solid or Crystal RSUV Types.
            /// </summary>
            CrackIntensity,
            /// <summary>
            /// Animates the transmission of the crystal. Crystal RSUV only.
            /// </summary>
            Transmission,
            /// <summary>
            /// Animates the absorption of the crystal. Crystal RSUV only.
            /// </summary>
            Absorption,
            /// <summary>
            /// Animates the flow strength of the liquid. Liquid RSUV only.
            /// </summary>
            FlowStrength,
            /// <summary>
            /// Animates the wave height of the liquid. Liquid RSUV only.
            /// </summary>
            WaveMult
        }

        /// <summary>
        /// Which of the fullscreen effects to trigger.
        /// </summary>
        public enum B2UFullscreenType
        {
            /// <summary>
            /// The fullscreen recolor effect.
            /// </summary>
            Recolor,
            /// <summary>
            /// The fullscreen stylize effect.
            /// </summary>
            Stylize,
            /// <summary>
            /// The fullscreen status effect.
            /// </summary>
            StatusEffect
        }

        /// <summary>
        /// The data block type to swap.
        /// </summary>
        public enum B2UDataSwapType
        {
            /// <summary>
            /// A B2UShaderProfile.
            /// </summary>
            Profile,
            /// <summary>
            /// A B2UPaletteData.
            /// </summary>
            Palette,
            /// <summary>
            /// A B2URecolorPresetCollection.
            /// </summary>
            RecolorCollection,
            /// <summary>
            /// A B2UStylizePresetCollection.
            /// </summary>
            StylizeCollection,
            /// <summary>
            /// A B2UStatusEffectPresetCollection.
            /// </summary>
            StatusEffectCollection
        }
    }
}