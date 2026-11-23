using System;
using System.Collections.Generic;
using UnityEngine;
using static B2U.B2UDebug;

namespace B2U
{
    /// <summary>
    /// Central dispatcher for procedural value, RSUV, and Palette LUT animations.
    /// Executed once per frame by B2URuntimeManager.
    /// </summary>
    public static class B2URuntimeAnimator
    {
        private const B2UMessageSource msgSrc = B2UMessageSource.Runtime;
        /// <summary>
        /// The warning threshold for too many tasks allocated. Will still allow new tasks to be allocated.
        /// </summary>
        private const int WARN_TASK_THRESHOLD = 1000;
        /// <summary>
        /// The maximum threshold for too many tasks allocated. Will prevent new task allocation.
        /// </summary>
        private const int MAX_TASK_THRESHOLD = 2500;

        /// <summary>
        /// A list of the active standard animation tasks.
        /// </summary>
        public static readonly List<B2UAnimTask> animTasks = new List<B2UAnimTask>(128);
        /// <summary>
        /// A list of the active palette animation tasks.
        /// </summary>
        public static readonly List<B2UAnimTask> paletteTasks = new List<B2UAnimTask>(64);
        /// <summary>
        /// A pool of animation tasks. Recycles tasks to prevent needing to allocate new tasks onto the heap.
        /// </summary>
        private static readonly Stack<B2UAnimTask> taskPool = new Stack<B2UAnimTask>(128);

        /// <summary>
        /// The method for solving the animation return value.
        /// </summary>
        public enum AnimMode
        {
            // Aperiodic
            /// <summary>
            /// Linearly interpolates from start to end.
            /// <para>Aperiodic</para>
            /// </summary>
            Linear,
            /// <summary>
            /// Uses a sigmoidal (smoothstep) curve.
            /// <para>Aperiodic</para>
            /// </summary>
            Sigmoidal,
            /// <summary>
            /// Eases in to the animation before becoming linear.
            /// <para>Aperiodic</para>
            /// </summary>
            EaseIn,
            /// <summary>
            /// Linear start and ease out.
            /// <para>Aperiodic</para>
            /// </summary>
            EaseOut,
            // Periodic (0f loops indefinitely; positive decays to start, negative decays to target)
            /// <summary>
            /// Linearly interpolates back and forth between the start and end value (triangle wave).
            /// <para>Periodic</para>
            /// </summary>
            PingPong,
            /// <summary>
            /// Uses a sine wave.
            /// <para>Periodic</para>
            /// </summary>
            Sine,
            /// <summary>
            /// Climbs to the target value linearly then drops to the start value before climbing again.
            /// <para>Periodic</para>
            /// </summary>
            Sawtooth,
            /// <summary>
            /// Jumps between the start and end value.
            /// <para>Periodic</para>
            /// </summary>
            Square,
            /// <summary>
            /// Uses repeated parabolic steps.
            /// <para>Periodic</para>
            /// </summary>
            Parabolic,
            // Custom
            /// <summary>
            /// Uses a defined AnimationCurve object.
            /// <para>Aperiodic or Periodic</para>
            /// </summary>
            AnimationCurve
        }

        /// <summary>
        /// Holds the data for animating a given value and returning it to the setter.
        /// </summary>
        public class B2UAnimTask
        {
            /// <summary>
            /// A setter action that returns the value back to the caller.
            /// </summary>
            public Action<Vector4> setter;
            /// <summary>
            /// A setter action that returns both a Color32 array representing both modified LUTs back to the caller.
            /// </summary>
            public Action<Vector4, Color32[], Color32[]> paletteSetter;

            /// <summary>
            /// The start value for the animation.
            /// </summary>
            public Vector4 startValue;
            /// <summary>
            /// The target value for the animation.
            /// </summary>
            public Vector4 targetValue;
            /// <summary>
            /// The time since the animation task was dispatched.
            /// </summary>
            public float currentTime;
            /// <summary>
            /// The maximum time the task can exist. 0f = indefinite.
            /// </summary>
            public float maxTime;
            /// <summary>
            /// The duration and decay control for the task in seconds.
            /// For aperiodic modes, &gt; 0f interpolates over maxTime seconds and &lt;= 0f snaps immediately to target.
            /// For periodic and AnimationCurve modes, &gt; 0f decays toward start, &lt; 0f decays toward target over |maxTime| seconds, and 0f loops indefinitely.
            /// </summary>
            public float speed;
            /// <summary>
            /// The mode to use for solving the return value.
            /// </summary>
            public AnimMode mode;
            /// <summary>
            /// The custom curve if being used.
            /// </summary>
            public AnimationCurve customCurve;

            /// <summary>
            /// Whether or not this task modifies the color LUT.
            /// </summary>
            public bool modifiesColorLUT;
            /// <summary>
            /// Whether or not this task modifies the property LUT.
            /// </summary>
            public bool modifiesPropLUT;

            /// <summary>
            /// Whether or not the task should die on the next frame.
            /// </summary>
            public bool killTask { get; private set; } = false;

            /// <summary>
            /// Resets a task to empty for returning to the pool.
            /// </summary>
            public void Reset()
            {
                setter = null;
                paletteSetter = null;
                startValue = Vector4.zero;
                targetValue = Vector4.zero;
                currentTime = 0f;
                maxTime = 0f;
                speed = 1f;
                mode = AnimMode.Linear;
                customCurve = null;
                modifiesColorLUT = false;
                modifiesPropLUT = false;
                killTask = false;
            }

            /// <summary>
            /// Updates the task and its return value.
            /// </summary>
            /// <param name="deltaTime">Time since the last frame.</param>
            /// <param name="colorBuf">The color LUT pixel buffer, if needed by the task.</param>
            /// <param name="propBuf">The property LUT pixel buffer, if needed by the task.</param>
            /// <returns>True if the task should die on the next frame.</returns>
            public bool UpdateTask(float deltaTime, Color32[] colorBuf = null, Color32[] propBuf = null)
            {
                if (killTask) return true;

                currentTime += deltaTime;

                bool isAperiodic = mode == AnimMode.Linear ||
                                   mode == AnimMode.Sigmoidal ||
                                   mode == AnimMode.EaseIn ||
                                   mode == AnimMode.EaseOut;

                bool decayToTarget = !isAperiodic && maxTime < 0f;
                float effectiveMaxTime = isAperiodic ? maxTime : Mathf.Abs(maxTime);
                bool hasDuration = effectiveMaxTime > 0f;

                // 1. Progress & Decay Multiplier (0-duration aperiodic snaps immediately to 1.0)
                float progress = hasDuration ? Mathf.Clamp01(currentTime / effectiveMaxTime) : (isAperiodic ? 1f : 0f);
                float decay = hasDuration ? (1f - progress) : 1f;

                // 2. Periodic Wave Time
                float periodicT = currentTime * speed;
                float evaluatedT = progress;

                switch (mode)
                {
                    case AnimMode.Linear:
                        evaluatedT = progress;
                        break;
                    case AnimMode.Sigmoidal:
                        evaluatedT = progress * progress * (3f - (2f * progress));
                        break;
                    case AnimMode.EaseIn:
                        evaluatedT = progress * progress;
                        break;
                    case AnimMode.EaseOut:
                        evaluatedT = progress * (2f - progress);
                        break;
                    case AnimMode.Parabolic:
                        {
                            float pT = Mathf.Repeat(periodicT, 1f);
                            float wave = 4f * pT * (1f - pT);
                            evaluatedT = decayToTarget ? Mathf.Lerp(1f, wave, decay) : wave * decay;
                            break;
                        }
                    case AnimMode.PingPong:
                        {
                            float wave = Mathf.PingPong(Mathf.Abs(periodicT) * 2f, 1f);
                            evaluatedT = decayToTarget ? Mathf.Lerp(1f, wave, decay) : wave * decay;
                            break;
                        }
                    case AnimMode.Sine:
                        {
                            float wave = (Mathf.Sin((periodicT * Mathf.PI * 2f) - (Mathf.PI * 0.5f)) + 1f) * 0.5f;
                            evaluatedT = decayToTarget ? Mathf.Lerp(1f, wave, decay) : wave * decay;
                            break;
                        }
                    case AnimMode.Sawtooth:
                        {
                            float wave = Mathf.Repeat(periodicT, 1f);
                            evaluatedT = decayToTarget ? Mathf.Lerp(1f, wave, decay) : wave * decay;
                            break;
                        }
                    case AnimMode.Square:
                        {
                            float wave = Mathf.Repeat(periodicT, 1f) < 0.5f ? 1f : 0f;
                            evaluatedT = decayToTarget ? Mathf.Lerp(1f, wave, decay) : wave * decay;
                            break;
                        }
                    case AnimMode.AnimationCurve:
                        if (customCurve != null && customCurve.length > 0)
                        {
                            float curveDuration = customCurve[customCurve.length - 1].time;
                            float cT = curveDuration > 0f
                                ? (hasDuration ? progress * curveDuration : Mathf.Repeat(periodicT, curveDuration))
                                : 0f;
                            float wave = customCurve.Evaluate(cT);
                            evaluatedT = decayToTarget ? Mathf.Lerp(1f, wave, decay) : wave * decay;
                        }
                        break;
                }

                Vector4 finalVal = Vector4.LerpUnclamped(startValue, targetValue, evaluatedT);
                if (paletteSetter != null && (colorBuf != null || propBuf != null))
                {
                    paletteSetter(finalVal, colorBuf, propBuf);
                }
                else if (setter != null)
                {
                    setter(finalVal);
                }

                return killTask || (hasDuration && currentTime >= effectiveMaxTime) || (!hasDuration && isAperiodic);
            }

            /// <summary>
            /// Kills a task on the next frame.
            /// </summary>
            public void KillTask() => killTask = true;
        }

        #region Task Lifecycle & Loop
        /// <summary>
        /// Gets a pooled task if one is available to prevent needing to allocate a new one on the heap.
        /// </summary>
        /// <returns>The pooled task or a new one if none are available in the pool.</returns>
        private static B2UAnimTask GetPooledTask()
        {
            if (taskPool.Count > 0)
            {
                B2UAnimTask pooled = taskPool.Pop();
                pooled.Reset();
                return pooled;
            }
            return new B2UAnimTask();
        }

        /// <summary>
        /// Recycles a task back into the pool.
        /// </summary>
        /// <param name="list">The active task list where the task returning to the pool lives.</param>
        /// <param name="index">The index of the task in the list.</param>
        private static void RecycleTaskAt(List<B2UAnimTask> list, int index)
        {
            B2UAnimTask task = list[index];
            task.KillTask();

            int lastIndex = list.Count - 1;
            list[index] = list[lastIndex];
            list.RemoveAt(lastIndex);

            taskPool.Push(task);
        }

        /// <summary>
        /// Advances all active standard and palette animation tasks. Called once per frame by B2URuntimeManager.
        /// </summary>
        public static void UpdateAnimationTasks()
        {
            float dt = Time.deltaTime;

            for (int i = animTasks.Count - 1; i >= 0; i--)
            {
                if (animTasks[i].UpdateTask(dt))
                {
                    RecycleTaskAt(animTasks, i);
                }
            }

            if (paletteTasks.Count > 0 && B2URuntimeUtility.ActiveProfile != null && B2URuntimeUtility.ActiveProfile.paletteData != null)
            {
                B2UPaletteData data = B2URuntimeUtility.ActiveProfile.paletteData;

                // Determine which LUTs actually require a CPU->GPU transfer this frame
                bool needColorLUT = false;
                bool needPropLUT = false;

                for (int i = paletteTasks.Count - 1; i >= 0; i--)
                {
                    B2UAnimTask pt = paletteTasks[i];
                    if (pt.killTask)
                    {
                        RecycleTaskAt(paletteTasks, i);
                        continue;
                    }
                    if (pt.modifiesColorLUT) needColorLUT = true;
                    if (pt.modifiesPropLUT) needPropLUT = true;
                }

                if (paletteTasks.Count == 0) return;

                Color32[] cBuf = (needColorLUT && data.colorLUT != null) ? data.colorLUT.GetPixels32() : null;
                Color32[] pBuf = (needPropLUT && data.propertyLUT != null) ? data.propertyLUT.GetPixels32() : null;

                for (int i = paletteTasks.Count - 1; i >= 0; i--)
                {
                    if (paletteTasks[i].UpdateTask(dt, cBuf, pBuf))
                    {
                        RecycleTaskAt(paletteTasks, i);
                    }
                }

                if (cBuf != null)
                {
                    data.colorLUT.SetPixels32(cBuf);
                    data.colorLUT.Apply();
                }
                if (pBuf != null)
                {
                    data.propertyLUT.SetPixels32(pBuf);
                    data.propertyLUT.Apply();
                }
            }
        }

        /// <summary>
        /// Immediately stops and recycles all active animation and palette tasks.
        /// </summary>
        public static void KillAllAnimationTasks()
        {
            for (int i = animTasks.Count - 1; i >= 0; i--) RecycleTaskAt(animTasks, i);
            for (int i = paletteTasks.Count - 1; i >= 0; i--) RecycleTaskAt(paletteTasks, i);
        }
        #endregion

        #region Generic Animation Overloads
        /// <summary>
        /// Animates a float value. Creates a new B2UAnimTask for the animator to update.
        /// </summary>
        /// <param name="setter">The float action to return the value to.</param>
        /// <param name="start">The start value.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <returns>The B2UAnimTask that was dispatched.</returns>
        /// <remarks>Using an action and an expression enables the animation to push the value back to the setter without an explicit reference to the memory address.</remarks>
        public static B2UAnimTask Animate(Action<float> setter, float start, float target, float duration, float speed, AnimMode mode = AnimMode.Linear, B2UAnimTask taskToOverride = null, AnimationCurve curve = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            if (setter == null) return null;
            return QueueTask(v => setter(v.x), new Vector4(start, 0f, 0f, 0f), new Vector4(target, 0f, 0f, 0f), duration, speed, mode, curve);
        }

        /// <summary>
        /// Animates a Vector2 value. Creates a new B2UAnimTask for the animator to update.
        /// </summary>
        /// <param name="setter">The Vector2 action to return the value to.</param>
        /// <param name="start">The start value.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <returns>The B2UAnimTask that was dispatched.</returns>
        public static B2UAnimTask Animate(Action<Vector2> setter, Vector2 start, Vector2 target, float duration, float speed, AnimMode mode = AnimMode.Linear, B2UAnimTask taskToOverride = null, AnimationCurve curve = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            if (setter == null) return null;
            return QueueTask(v => setter(v), start, target, duration, speed, mode, curve);
        }

        /// <summary>
        /// Animates a Vector3 value. Creates a new B2UAnimTask for the animator to update.
        /// </summary>
        /// <param name="setter">The Vector3 action to return the value to.</param>
        /// <param name="start">The start value.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <returns>The B2UAnimTask that was dispatched.</returns>
        public static B2UAnimTask Animate(Action<Vector3> setter, Vector3 start, Vector3 target, float duration, float speed, AnimMode mode = AnimMode.Linear, B2UAnimTask taskToOverride = null, AnimationCurve curve = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            if (setter == null) return null;
            return QueueTask(v => setter(v), start, target, duration, speed, mode, curve);
        }

        /// <summary>
        /// Animates a Vector4 value. Creates a new B2UAnimTask for the animator to update.
        /// </summary>
        /// <param name="setter">The Vector4 action to return the value to.</param>
        /// <param name="start">The start value.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <returns>The B2UAnimTask that was dispatched.</returns>
        public static B2UAnimTask Animate(Action<Vector4> setter, Vector4 start, Vector4 target, float duration, float speed, AnimMode mode = AnimMode.Linear, B2UAnimTask taskToOverride = null, AnimationCurve curve = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            if (setter == null) return null;
            return QueueTask(setter, start, target, duration, speed, mode, curve);
        }

        /// <summary>
        /// Animates a Color value. Creates a new B2UAnimTask for the animator to update.
        /// </summary>
        /// <param name="setter">The Color action to return the value to.</param>
        /// <param name="start">The start color.</param>
        /// <param name="target">The target color.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <returns>The B2UAnimTask that was dispatched.</returns>
        public static B2UAnimTask Animate(Action<Color> setter, Color start, Color target, float duration, float speed, AnimMode mode = AnimMode.Linear, B2UAnimTask taskToOverride = null, AnimationCurve curve = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            if (setter == null) return null;
            return QueueTask(v => setter(v), start, target, duration, speed, mode, curve);
        }

        /// <summary>
        /// Queues a standard (non-palette) task into the active animation tasks list.
        /// </summary>
        /// <param name="wrappedSetter">The expression-wrapped setter.</param>
        /// <param name="start">The start value.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <returns>The B2UAnimTask that was dispatched.</returns>
        private static B2UAnimTask QueueTask(Action<Vector4> wrappedSetter, Vector4 start, Vector4 target, float duration, float speed, AnimMode mode, AnimationCurve curve)
        {
            if (B2URuntimeManager.Instance == null)
            {
                B2ULogError(msgSrc, "Cannot animate. No B2URuntimeManager exists in the scene.");
                return null;
            }

            if (animTasks.Count > WARN_TASK_THRESHOLD)
            {
                B2ULogWarning(msgSrc, $"Over {WARN_TASK_THRESHOLD} active animation tasks detected. Ensure you are not calling B2URuntimeAnimator.Animate() inside an Update loop without passing an overriding task reference.");
            }
            if (animTasks.Count > MAX_TASK_THRESHOLD)
            {
                B2ULogError(msgSrc, $"Over {MAX_TASK_THRESHOLD} animation tasks are allocated. Disabling allocation to prevent memory leaks.");
                return null;
            }

            B2UAnimTask task = GetPooledTask();
            task.setter = wrappedSetter;
            task.startValue = start;
            task.targetValue = target;
            task.currentTime = 0f;
            task.maxTime = duration;
            task.speed = speed;
            task.mode = mode;
            task.customCurve = curve;

            animTasks.Add(task);
            return task;
        }
        #endregion

        #region Global Shader & Palette Animations
        /// <summary>
        /// Animates a global B2UShaderProperty float.
        /// </summary>
        /// <param name="property">The property to animate.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null uses the existing value as the start.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if the property is not a valid float property.</returns>
        public static B2UAnimTask AnimateGlobalFloat(B2UShaderProperty property, float target, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (B2URuntimeUtility.ActiveProfile == null || B2URuntimeUtility.ActiveProfile.propertyBlocks == null) return null;
            if (!B2URuntimeUtility.ActiveProfile.propertyBlocks.TryGetValue(property, out B2UShaderPropertyBlock block) || block.type != typeof(float)) return null;

            float start = customStart ?? (float)block.value;

            return Animate(v => B2URuntimeUtility.SetB2UShaderPropertyValue(property, v), start, target, duration, speed, mode, taskToOverride, curve);
        }

        /// <summary>
        /// Animates a Color LUT base or emission color for a non-biome-responsive (Independent) B2UMaterial.
        /// Batched with all other active palette tasks into a single LUT GPU upload per frame.
        /// </summary>
        /// <param name="material">The B2UMaterial whose Color LUT entry will be animated.</param>
        /// <param name="target">The target color.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="emissionColor">If true, animates the material's emission color instead of its base albedo color.</param>
        /// <param name="variant">Optional variant index (0-7) to animate. Use -1 to animate all variants on the row.</param>
        /// <param name="secondRow">For Liquid workflows, if true animates the second row (depth color) instead of the surface row.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start color. Leaving this null samples the current Color LUT pixel.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no active palette or material is available.</returns>
        /// <remarks>LUT changes performed at runtime are volatile and do not write back to disk.</remarks>
        public static B2UAnimTask AnimatePaletteColor(B2UMaterial material, Color target, float duration, bool emissionColor, int variant = -1, bool secondRow = false, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, Color? customStart = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            B2UPaletteData data = B2URuntimeUtility.ActiveProfile?.paletteData;
            if (data == null || material == null) return null;

            Color start = customStart ?? B2URuntimeUtility.GetColorLUTPixel(data, material, emissionColor, variant, secondRow);

            return QueuePaletteTask(
                (v, cBuf, pBuf) => B2URuntimeUtility.SetColorLUTPixelNonAlloc(data, (Color)v, material, emissionColor, variant, secondRow, cBuf),
                start,
                target,
                duration,
                speed,
                mode,
                curve,
                modifiesColor: true,
                modifiesProp: false
            );
        }

        /// <summary>
        /// Animates a Color LUT base or emission color for a biome-responsive (Dynamic) B2UMaterial.
        /// Batched with all other active palette tasks into a single LUT GPU upload per frame.
        /// </summary>
        /// <param name="material">The B2UMaterial whose Color LUT entry will be animated.</param>
        /// <param name="target">The target color.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="emissionColor">If true, animates the material's emission color instead of its base albedo color.</param>
        /// <param name="biome">The biome index (0-7) to animate.</param>
        /// <param name="subtype">The biome subtype index to animate (ignored when animating emission color).</param>
        /// <param name="secondRow">For Liquid workflows, if true animates the second row (depth color) instead of the surface row.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start color. Leaving this null samples the current Color LUT pixel.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no active palette or material is available.</returns>
        /// <remarks>LUT changes performed at runtime are volatile and do not write back to disk.</remarks>
        public static B2UAnimTask AnimatePaletteColor(B2UMaterial material, Color target, float duration, bool emissionColor, int biome, int subtype, bool secondRow = false, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, Color? customStart = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            B2UPaletteData data = B2URuntimeUtility.ActiveProfile?.paletteData;
            if (data == null || material == null) return null;

            Color start = customStart ?? B2URuntimeUtility.GetColorLUTPixel(data, material, emissionColor, biome, subtype, secondRow);

            return QueuePaletteTask(
                (v, cBuf, pBuf) => B2URuntimeUtility.SetColorLUTPixelNonAlloc(data, (Color)v, material, biome, subtype, emissionColor, secondRow, cBuf),
                start,
                target,
                duration,
                speed,
                mode,
                curve,
                modifiesColor: true,
                modifiesProp: false
            );
        }

        /// <summary>
        /// Animates a normalized (0-1) property channel in the active palette's Property LUT (or Color LUT alpha channel for ca/ea slots).
        /// Batched with all other active palette tasks into a single LUT GPU upload per frame.
        /// </summary>
        /// <param name="material">The B2UMaterial whose LUT property will be animated.</param>
        /// <param name="property">The B2USlotKey channel to animate.</param>
        /// <param name="target">The normalized target value (0f to 1f), automatically quantized to 0-255 each frame.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional normalized custom start value (0f to 1f). Leaving this null reads the current LUT value.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no active palette or material is available.</returns>
        /// <remarks>Target and customStart values operate in normalized 0-1 LUT space rather than schema-remapped units. Do not animate r1_p4_r at runtime.</remarks>
        public static B2UAnimTask AnimatePaletteProperty(B2UMaterial material, B2USlotKey property, float target, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (taskToOverride != null) taskToOverride.KillTask();
            B2UPaletteData data = B2URuntimeUtility.ActiveProfile?.paletteData;
            if (data == null || material == null) return null;

            float start = customStart ?? B2URuntimeUtility.GetPropertyLUTValue(data, material, property);

            bool isColorLUTAlpha = property == B2USlotKey.r1_ca ||
                                   property == B2USlotKey.r1_ea ||
                                   property == B2USlotKey.r2_ca ||
                                   property == B2USlotKey.r2_ea;

            return QueuePaletteTask(
                (v, cBuf, pBuf) =>
                {
                    byte byteVal = (byte)Mathf.Clamp(Mathf.RoundToInt(v.x * 255f), 0, 255);
                    B2URuntimeUtility.SetPropertyLUTValueNonAlloc(data, material, property, byteVal, pBuf, cBuf);
                },
                new Vector4(start, 0f, 0f, 0f),
                new Vector4(target, 0f, 0f, 0f),
                duration,
                speed,
                mode,
                curve,
                modifiesColor: isColorLUTAlpha,
                modifiesProp: !isColorLUTAlpha
            );
        }

        /// <summary>
        /// Queues a batched LUT animation task into the active palette tasks list.
        /// </summary>
        /// <param name="wrappedSetter">The palette setter callback receiving the evaluated value and LUT pixel buffers.</param>
        /// <param name="start">The start value.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="modifiesColor">True if this task writes to the Color LUT buffer.</param>
        /// <param name="modifiesProp">True if this task writes to the Property LUT buffer.</param>
        /// <returns>The B2UAnimTask that was dispatched.</returns>
        private static B2UAnimTask QueuePaletteTask(Action<Vector4, Color32[], Color32[]> wrappedSetter, Vector4 start, Vector4 target, float duration, float speed, AnimMode mode, AnimationCurve curve, bool modifiesColor, bool modifiesProp)
        {
            if (B2URuntimeManager.Instance == null)
            {
                B2ULogError(msgSrc, "Cannot animate. No B2URuntimeManager exists in the scene.");
                return null;
            }

            if (paletteTasks.Count > WARN_TASK_THRESHOLD)
            {
                B2ULogWarning(msgSrc, $"Over {WARN_TASK_THRESHOLD} active palette tasks detected. Ensure you are not calling B2URuntimeAnimator.Animate() inside an Update loop without passing an overriding task reference.");
            }
            if (paletteTasks.Count > MAX_TASK_THRESHOLD)
            {
                B2ULogError(msgSrc, $"Over {MAX_TASK_THRESHOLD} palette tasks are allocated. Disabling allocation to prevent memory leaks.");
                return null;
            }

            B2UAnimTask task = GetPooledTask();
            task.paletteSetter = wrappedSetter;
            task.startValue = start;
            task.targetValue = target;
            task.currentTime = 0f;
            task.maxTime = duration;
            task.speed = speed;
            task.mode = mode;
            task.customCurve = curve;
            task.modifiesColorLUT = modifiesColor;
            task.modifiesPropLUT = modifiesProp;

            paletteTasks.Add(task);
            return task;
        }
        #endregion

        #region RSUV Component Animations
        /// <summary>
        /// Caches either the MeshRenderer or SkinnedMeshRenderer attached to a B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component.</param>
        /// <param name="mr">Outputs the MeshRenderer if present.</param>
        /// <param name="smr">Outputs the SkinnedMeshRenderer if present.</param>
        /// <returns>True if a valid renderer was located.</returns>
        private static bool TryCacheRenderers(B2URSUVMono mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)
        {
            mr = null;
            smr = null;
            if (mono == null) return false;
            if (mono.TryGetComponent(out mr)) return true;
            if (mono.TryGetComponent(out smr)) return true;
            B2ULogError(msgSrc, $"Cannot animate RSUV on '{mono.name}': no MeshRenderer or SkinnedMeshRenderer found.", mono);
            return false;
        }

        /// <summary>
        /// Re-encodes a B2URSUV container and pushes it to the cached renderer.
        /// </summary>
        /// <param name="mr">The cached MeshRenderer, if applicable.</param>
        /// <param name="smr">The cached SkinnedMeshRenderer, if applicable.</param>
        /// <param name="rsuv">The RSUV container to encode and apply.</param>
        private static void PushCachedRSUV(MeshRenderer mr, SkinnedMeshRenderer smr, B2URSUV rsuv)
        {
            rsuv.UpdateRSUV();
            if (mr != null) mr.SetRSUV(rsuv);
            else if (smr != null) smr.SetRSUV(rsuv);
        }

        /// <summary>
        /// Animates the per-object emission strength on a Solid B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Solid RSUV workflow.</param>
        /// <param name="targetStrength">The target emission strength (clamped to 0f-1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.solid.emissionStrength.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        /// <example>
        /// <code>
        /// // Smoothly pulse a solid object's emission strength using a sine wave
        /// B2UAnimTask pulseTask = rsuvMono.AnimateSolidEmission(
        ///     targetStrength: 1f,
        ///     duration: 0f,
        ///     speed: 0.5f,
        ///     mode: AnimMode.Sine,
        ///     customStart: 0.2f
        /// );
        /// </code>
        /// </example>
        public static B2UAnimTask AnimateSolidEmission(this B2URSUVMono mono, float targetStrength, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.solid.emissionStrength;
            B2USolidRSUV cachedRSUV = new B2USolidRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.solid.emissionStrength = Mathf.Clamp01(v);
                cachedRSUV.paletteIndex = (uint)Mathf.Clamp((int)mono.solid.paletteIndex, 0, 15);
                cachedRSUV.paintIndex = (uint)Mathf.Clamp((int)mono.solid.paintIndex, 0, 127);
                cachedRSUV.emissionStrength = mono.solid.emissionStrength;
                cachedRSUV.noiseOffset = (uint)Mathf.Clamp((int)mono.solid.noiseOffset, 0, 63);
                cachedRSUV.isPainted = mono.solid.isPainted;
                cachedRSUV.crackIntensity = Mathf.Clamp01(mono.solid.crackIntensity);
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetStrength, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the procedural crack intensity on a Solid B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Solid RSUV workflow.</param>
        /// <param name="targetIntensity">The target crack intensity (clamped to 0f-1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.solid.crackIntensity.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateSolidCrackIntensity(this B2URSUVMono mono, float targetIntensity, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.solid.crackIntensity;
            B2USolidRSUV cachedRSUV = new B2USolidRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.solid.crackIntensity = Mathf.Clamp01(v);
                cachedRSUV.paletteIndex = (uint)Mathf.Clamp((int)mono.solid.paletteIndex, 0, 15);
                cachedRSUV.paintIndex = (uint)Mathf.Clamp((int)mono.solid.paintIndex, 0, 127);
                cachedRSUV.emissionStrength = Mathf.Clamp01(mono.solid.emissionStrength);
                cachedRSUV.noiseOffset = (uint)Mathf.Clamp((int)mono.solid.noiseOffset, 0, 63);
                cachedRSUV.isPainted = mono.solid.isPainted;
                cachedRSUV.crackIntensity = mono.solid.crackIntensity;
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetIntensity, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the per-object emission strength on a Crystal B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Crystal RSUV workflow.</param>
        /// <param name="targetStrength">The target emission strength (clamped to 0f-1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.crystal.emissionStrength.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateCrystalEmission(this B2URSUVMono mono, float targetStrength, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.crystal.emissionStrength;
            B2UCrystalRSUV cachedRSUV = new B2UCrystalRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.crystal.emissionStrength = Mathf.Clamp01(v);
                cachedRSUV.emissionStrength = mono.crystal.emissionStrength;
                cachedRSUV.transmissionMultiplier = Mathf.Clamp01(mono.crystal.transmissionMultiplier);
                cachedRSUV.absorptionMultiplier = Mathf.Clamp01(mono.crystal.absorptionMultiplier);
                cachedRSUV.crackIntensity = Mathf.Clamp01(mono.crystal.crackIntensity);
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetStrength, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the procedural crack intensity on a Crystal B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Crystal RSUV workflow.</param>
        /// <param name="targetIntensity">The target crack intensity (clamped to 0f-1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.crystal.crackIntensity.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateCrystalCrackIntensity(this B2URSUVMono mono, float targetIntensity, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.crystal.crackIntensity;
            B2UCrystalRSUV cachedRSUV = new B2UCrystalRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.crystal.crackIntensity = Mathf.Clamp01(v);
                cachedRSUV.emissionStrength = Mathf.Clamp01(mono.crystal.emissionStrength);
                cachedRSUV.transmissionMultiplier = Mathf.Clamp01(mono.crystal.transmissionMultiplier);
                cachedRSUV.absorptionMultiplier = Mathf.Clamp01(mono.crystal.absorptionMultiplier);
                cachedRSUV.crackIntensity = mono.crystal.crackIntensity;
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetIntensity, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the transmission multiplier on a Crystal B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Crystal RSUV workflow.</param>
        /// <param name="targetIntensity">The target transmission multiplier (clamped to 0f-1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.crystal.transmissionMultiplier.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateCrystalTransmission(this B2URSUVMono mono, float targetIntensity, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.crystal.transmissionMultiplier;
            B2UCrystalRSUV cachedRSUV = new B2UCrystalRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.crystal.transmissionMultiplier = Mathf.Clamp01(v);
                cachedRSUV.emissionStrength = Mathf.Clamp01(mono.crystal.emissionStrength);
                cachedRSUV.transmissionMultiplier = mono.crystal.transmissionMultiplier;
                cachedRSUV.absorptionMultiplier = Mathf.Clamp01(mono.crystal.absorptionMultiplier);
                cachedRSUV.crackIntensity = Mathf.Clamp01(mono.crystal.crackIntensity);
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetIntensity, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the absorption multiplier on a Crystal B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Crystal RSUV workflow.</param>
        /// <param name="targetIntensity">The target absorption multiplier (clamped to 0f-1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.crystal.absorptionMultiplier.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateCrystalAbsorption(this B2URSUVMono mono, float targetIntensity, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.crystal.absorptionMultiplier;
            B2UCrystalRSUV cachedRSUV = new B2UCrystalRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.crystal.absorptionMultiplier = Mathf.Clamp01(v);
                cachedRSUV.emissionStrength = Mathf.Clamp01(mono.crystal.emissionStrength);
                cachedRSUV.transmissionMultiplier = Mathf.Clamp01(mono.crystal.transmissionMultiplier);
                cachedRSUV.absorptionMultiplier = mono.crystal.absorptionMultiplier;
                cachedRSUV.crackIntensity = Mathf.Clamp01(mono.crystal.crackIntensity);
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetIntensity, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the per-object emission strength on a Liquid B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Liquid RSUV workflow.</param>
        /// <param name="targetStrength">The target emission strength (clamped to 0f-1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.liquid.emissionStrength.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateLiquidEmission(this B2URSUVMono mono, float targetStrength, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.liquid.emissionStrength;
            B2ULiquidRSUV cachedRSUV = new B2ULiquidRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.liquid.emissionStrength = Mathf.Clamp01(v);
                cachedRSUV.emissionStrength = mono.liquid.emissionStrength;
                cachedRSUV.flowStrength = Mathf.Clamp(mono.liquid.flowStrength, -1f, 1f);
                cachedRSUV.waveMultiplier = Mathf.Clamp(mono.liquid.waveMultiplier, -1f, 1f);
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetStrength, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the flow strength multiplier on a Liquid B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Liquid RSUV workflow.</param>
        /// <param name="targetFlow">The target flow strength (clamped to -1f to 1f; negative values reverse flow direction).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.liquid.flowStrength.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateLiquidFlowStrength(this B2URSUVMono mono, float targetFlow, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.liquid.flowStrength;
            B2ULiquidRSUV cachedRSUV = new B2ULiquidRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.liquid.flowStrength = Mathf.Clamp(v, -1f, 1f);
                cachedRSUV.emissionStrength = Mathf.Clamp01(mono.liquid.emissionStrength);
                cachedRSUV.flowStrength = mono.liquid.flowStrength;
                cachedRSUV.waveMultiplier = Mathf.Clamp(mono.liquid.waveMultiplier, -1f, 1f);
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetFlow, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }

        /// <summary>
        /// Animates the wave height multiplier on a Liquid B2URSUVMono component.
        /// </summary>
        /// <param name="mono">The target B2URSUVMono component using the Liquid RSUV workflow.</param>
        /// <param name="targetWave">The target wave multiplier (clamped to -1f to 1f).</param>
        /// <param name="duration">Duration in seconds. Aperiodic: &gt; 0f interpolates, &lt;= 0f snaps instantly. Periodic/AnimationCurve: &gt; 0f decays to start, &lt; 0f decays to target over |duration| seconds, 0f loops indefinitely.</param>
        /// <param name="speed">How fast a periodic animation cycles.</param>
        /// <param name="mode">The mode for animating.</param>
        /// <param name="curve">An optional curve if using AnimationCurve mode.</param>
        /// <param name="taskToOverride">An optional existing task to kill and override.</param>
        /// <param name="customStart">An optional custom start value. Leaving this null starts from mono.liquid.waveMultiplier.</param>
        /// <returns>The B2UAnimTask that was dispatched, or null if no renderer was found.</returns>
        public static B2UAnimTask AnimateLiquidWaveMultiplier(this B2URSUVMono mono, float targetWave, float duration, float speed = 1f, AnimMode mode = AnimMode.Linear, AnimationCurve curve = null, B2UAnimTask taskToOverride = null, float? customStart = null)
        {
            if (!TryCacheRenderers(mono, out MeshRenderer mr, out SkinnedMeshRenderer smr)) return null;

            float start = customStart ?? mono.liquid.waveMultiplier;
            B2ULiquidRSUV cachedRSUV = new B2ULiquidRSUV();
            B2UAnimTask createdTask = null;

            createdTask = Animate(v =>
            {
                if (mono == null) { createdTask?.KillTask(); return; }
                mono.liquid.waveMultiplier = Mathf.Clamp(v, -1f, 1f);
                cachedRSUV.emissionStrength = Mathf.Clamp01(mono.liquid.emissionStrength);
                cachedRSUV.flowStrength = Mathf.Clamp(mono.liquid.flowStrength, -1f, 1f);
                cachedRSUV.waveMultiplier = mono.liquid.waveMultiplier;
                PushCachedRSUV(mr, smr, cachedRSUV);
            }, start, targetWave, duration, speed, mode, taskToOverride, curve);

            return createdTask;
        }
        #endregion
    }
}