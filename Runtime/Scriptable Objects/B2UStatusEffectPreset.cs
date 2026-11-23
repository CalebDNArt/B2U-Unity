using System;
using UnityEngine;
using static B2U.B2URuntimeUtility;
using static B2U.B2UDebug;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace B2U
{
    /// <summary>
    /// Stores configuration and gradient data for a fullscreen status effect preset.
    /// </summary>
    [CreateAssetMenu(fileName = "New Status Effect Preset", menuName = "B2U/Fullscreen/Status Effect Preset")]
    public class B2UStatusEffectPreset : ScriptableObject
    {
        /// <summary>
        /// A string array holding the keywords for the different status effects.
        /// </summary>
        private static readonly string[] effectTypeStrings =
        {
            "_B2U_STATUS_EFFECT_BURNING",
            "_B2U_STATUS_EFFECT_FREEZING",
            "_B2U_STATUS_EFFECT_SHOCKED",
            "_B2U_STATUS_EFFECT_POISONED",
            "_B2U_STATUS_EFFECT_CURSED",
            "_B2U_STATUS_EFFECT_BLESSED",
            "_B2U_STATUS_EFFECT_GLITCHED",
            "_B2U_STATUS_EFFECT_SHIELDED"
        };

        /// <summary>
        /// Global shader ID for the status effect scale.
        /// </summary>
        private static readonly int effectScaleID = Shader.PropertyToID("_B2U_Status_Effect_Scale");
        /// <summary>
        /// Global shader ID for status effect generic float 1.
        /// </summary>
        private static readonly int effectGeneric1ID = Shader.PropertyToID("_B2U_Status_Effect_Generic_1");
        /// <summary>
        /// Global shader ID for status effect generic float 2.
        /// </summary>
        private static readonly int effectGeneric2ID = Shader.PropertyToID("_B2U_Status_Effect_Generic_2");
        /// <summary>
        /// Global shader ID for status effect generic float 3.
        /// </summary>
        private static readonly int effectGeneric3ID = Shader.PropertyToID("_B2U_Status_Effect_Generic_3");
        /// <summary>
        /// Global shader ID for status effect generic float 4.
        /// </summary>
        private static readonly int effectGeneric4ID = Shader.PropertyToID("_B2U_Status_Effect_Generic_4");
        /// <summary>
        /// Global shader ID for status effect gradient texture.
        /// </summary>
        private static readonly int gradientTextureID = Shader.PropertyToID("_B2U_Status_Effect_Gradient_Texture");

        /// <summary>
        /// The status effect type for this preset.
        /// </summary>
        public B2UStatusEffect statusEffectType = B2UStatusEffect.Burning;
        /// <summary>
        /// The scale of the status effect.
        /// </summary>
        public float effectScale = 1f;
        /// <summary>
        /// Effect generic float 1. Each effect type uses this value differently.
        /// </summary>
        public float effectGeneric1 = 0f;
        /// <summary>
        /// Effect generic float 2. Each effect type uses this value differently.
        /// </summary>
        public float effectGeneric2 = 0f;
        /// <summary>
        /// Effect generic float 3. Each effect type uses this value differently.
        /// </summary>
        public float effectGeneric3 = 0f;
        /// <summary>
        /// Effect generic float 4. Each effect type uses this value differently.
        /// </summary>
        public float effectGeneric4 = 0f;
        /// <summary>
        /// The gradient to use for the status effect.
        /// </summary>
        public Gradient gradient = new Gradient();
        /// <summary>
        /// Whether this preset is active or not.
        /// </summary>
        public bool isActive = false;
        /// <summary>
        /// A texture representation of the gradient.
        /// </summary>
        /// <remarks>This is needed because HLSL does not natively support unity Gradient objects.</remarks>
        [NonSerialized] public Texture2D gradientTexture;

        /// <summary>
        /// Rebuilds the internal 256x1 gradient lookup texture from the current Gradient field.
        /// </summary>
        public void RebuildGradientTexture()
        {
            gradientTexture = BuildGradientTexture(gradient, gradientTexture);
            if (gradientTexture != null)
            {
                gradientTexture.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        /// <summary>
        /// Pushes this status effect preset's keywords, parameters, and gradient texture to the global shader state.
        /// </summary>
        public void Apply()
        {
            if (gradientTexture == null)
            {
                RebuildGradientTexture();
            }

            DisableKeywords(effectTypeStrings);
            int typeIdx = Mathf.Clamp((int)statusEffectType, 0, effectTypeStrings.Length - 1);
            Shader.EnableKeyword(effectTypeStrings[typeIdx]);

            Shader.SetGlobalFloat(effectScaleID, effectScale);
            Shader.SetGlobalFloat(effectGeneric1ID, effectGeneric1);
            Shader.SetGlobalFloat(effectGeneric2ID, effectGeneric2);
            Shader.SetGlobalFloat(effectGeneric3ID, effectGeneric3);
            Shader.SetGlobalFloat(effectGeneric4ID, effectGeneric4);
            Shader.SetGlobalTexture(gradientTextureID, gradientTexture);
        }

        private void OnDestroy()
        {
            if (gradientTexture != null)
            {
                if (Application.isPlaying) Destroy(gradientTexture);
                else DestroyImmediate(gradientTexture);
                gradientTexture = null;
            }
        }

#if UNITY_EDITOR
        private static Texture2D[] typeIcons;

        private void OnValidate()
        {
            if (gradientTexture != null)
            {
                RebuildGradientTexture();
            }
        }

        /// <summary>
        /// Editor-only helper that loads and caches the Inspector icon corresponding to this status effect type.
        /// </summary>
        public Texture2D GetIcon()
        {
            if (typeIcons == null)
            {
                typeIcons = new Texture2D[]
                {
                    LoadIconSafe("StatusBurningIcon"),
                    LoadIconSafe("StatusFreezingIcon"),
                    LoadIconSafe("StatusShockedIcon"),
                    LoadIconSafe("StatusPoisonedIcon"),
                    LoadIconSafe("StatusHexedIcon"),
                    LoadIconSafe("StatusBlessedIcon"),
                    LoadIconSafe("StatusGlitchedIcon"),
                    LoadIconSafe("StatusShieldedIcon")
                };
            }

            int idx = Mathf.Clamp((int)statusEffectType, 0, typeIcons.Length - 1);
            return typeIcons[idx];
        }

        private static Texture2D LoadIconSafe(string filterName)
        {
            GUID[] guids = AssetDatabase.FindAssetGUIDs(filterName + " t:Texture2D");
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetByGUID<Texture2D>(guids[0]);
            }

            B2ULogWarning(B2UMessageSource.Editor, $"Could not find icon for {filterName}. Make sure the texture exists in the project.");
            return null;
        }
#endif
    }

    /// <summary>
    /// An enum representing the status effect type.
    /// </summary>
    public enum B2UStatusEffect
    {
        /// <summary>
        /// Causes flames and heat distortion that scroll upwards from the bottom of the screen.
        /// </summary>
        Burning,
        /// <summary>
        /// Causes ice crystals and frost to grow from the edges of the screen inwards.
        /// </summary>
        Freezing,
        /// <summary>
        /// Creates lightning bolts that shoot in from the edges of the screen.
        /// </summary>
        Shocked,
        /// <summary>
        /// Causes distortion and tinting on the edges of the screen.
        /// </summary>
        Poisoned,
        /// <summary>
        /// Overlays a cursed mandala onto the screen and darkens the scene behind it.
        /// </summary>
        Hexed,
        /// <summary>
        /// Creates sunbursts in the corners of the screen.
        /// </summary>
        Blessed,
        /// <summary>
        /// Causes random boxes on screen that glitch the color buffer. Also generates static and random alien text overlaid on screen.
        /// </summary>
        Glitched,
        /// <summary>
        /// Creates a sci-fi hex shield that expands from the middle.
        /// </summary>
        Shielded
    }
}