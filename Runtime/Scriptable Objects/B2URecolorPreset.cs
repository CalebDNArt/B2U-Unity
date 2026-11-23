using System;
using UnityEngine;
using static B2U.B2URuntimeUtility;

namespace B2U
{
    /// <summary>
    /// Stores configuration and gradient data for a fullscreen recolor effect preset.
    /// </summary>
    [CreateAssetMenu(fileName = "New Recolor Preset", menuName = "B2U/Fullscreen/Recolor Preset")]
    public class B2URecolorPreset : ScriptableObject
    {
        /// <summary>
        /// A string array holding the keywords for the different possible blend styles.
        /// </summary>
        public static readonly string[] blendStyleStrings =
        {
            "_B2U_RECOLOR_BLEND_STYLE_ALPHA",
            "_B2U_RECOLOR_BLEND_STYLE_RADIAL",
            "_B2U_RECOLOR_BLEND_STYLE_DISTORTED_RADIAL",
            "_B2U_RECOLOR_BLEND_STYLE_HORIZONTAL_SCAN",
            "_B2U_RECOLOR_BLEND_STYLE_VERTICAL_SCAN",
            "_B2U_RECOLOR_BLEND_STYLE_HORIZONTAL_DOUBLE_SCAN",
            "_B2U_RECOLOR_BLEND_STYLE_VERTICAL_DOUBLE_SCAN",
        };

        /// <summary>
        /// A string array holding the keywords for the different recolor sources.
        /// </summary>
        public static readonly string[] recolorSourceStrings =
        {
            "_B2U_RECOLOR_SOURCE_LUMINANCE",
            "_B2U_RECOLOR_SOURCE_RED",
            "_B2U_RECOLOR_SOURCE_GREEN",
            "_B2U_RECOLOR_SOURCE_BLUE",
            "_B2U_RECOLOR_SOURCE_DEPTH",
            "_B2U_RECOLOR_SOURCE_NORMAL",
        };

        /// <summary>
        /// Global shader ID for the recolor blend edge color.
        /// </summary>
        public static readonly int blendEdgeColorID = Shader.PropertyToID("_B2U_Recolor_Blend_Edge_Color");
        /// <summary>
        /// Global shader ID for the recolor depth min/max.
        /// </summary>
        public static readonly int depthMinMaxID = Shader.PropertyToID("_B2U_Recolor_Depth_MinMax");
        /// <summary>
        /// Global shader ID for the recolor inverted blend direction.
        /// </summary>
        public static readonly int invertBlendDirID = Shader.PropertyToID("_B2U_Recolor_Invert_Blend_Direction");
        /// <summary>
        /// Global shader ID for the recolor pulse speed.
        /// </summary>
        public static readonly int pulseSpeedID = Shader.PropertyToID("_B2U_Recolor_Pulse_Speed");
        /// <summary>
        /// Global shader ID for the recolor pulse frequency.
        /// </summary>
        public static readonly int pulseFreqID = Shader.PropertyToID("_B2U_Recolor_Pulse_Frequency");
        /// <summary>
        /// Global shader ID for the recolor pulse size.
        /// </summary>
        public static readonly int pulseSizeID = Shader.PropertyToID("_B2U_Recolor_Pulse_Size");
        /// <summary>
        /// Global shader ID for the recolor pulse attenuation.
        /// </summary>
        public static readonly int pulseAttenID = Shader.PropertyToID("_B2U_Recolor_Pulse_Attenuation");
        /// <summary>
        /// Global shader ID for the recolor pulse intensity.
        /// </summary>
        public static readonly int pulseIntensityID = Shader.PropertyToID("_B2U_Recolor_Pulse_Intensity");
        /// <summary>
        /// Global shader ID for the recolor gradient texture.
        /// </summary>
        public static readonly int gradTexID = Shader.PropertyToID("_B2U_Recolor_Gradient_Texture");

        /// <summary>
        /// The blend style to use for the preset.
        /// </summary>
        public B2URecolorBlendStyle blendStyle = B2URecolorBlendStyle.Alpha;
        /// <summary>
        /// The recolor source for the preset.
        /// </summary>
        public B2URecolorSource source = B2URecolorSource.Luminance;
        /// <summary>
        /// The edge color of the blend effect.
        /// </summary>
        public Color blendEdgeColor = Color.black;
        /// <summary>
        /// The depth min/max value if using Depth as the source.
        /// </summary>
        public Vector2 depthMinMax = new Vector2(0f, 50f);
        /// <summary>
        /// Whether or not to invert where the blend starts and ends.
        /// </summary>
        public bool invertBlendDirection = false;
        /// <summary>
        /// Whether or not to use the pulse effect.
        /// </summary>
        public bool usePulse = false;
        /// <summary>
        /// The speed of the pulse effect.
        /// </summary>
        public float pulseSpeed = 5f;
        /// <summary>
        /// How often the pulses shoot out from the camera.
        /// </summary>
        public float pulseFrequency = 10f;
        /// <summary>
        /// The size of each pulse wave.
        /// </summary>
        public float pulseSize = 1f;
        /// <summary>
        /// How quickly the pulse decays as it gets further from the camera.
        /// </summary>
        public float pulseAttenuation = 0.1f;
        /// <summary>
        /// The intensity of the pulse.
        /// </summary>
        public float pulseIntensity = 1f;
        /// <summary>
        /// The gradient to use for the recolor effect.
        /// </summary>
        public Gradient gradient = new Gradient();

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
        /// Pushes this recolor preset's keywords, parameters, and gradient texture to the global shader state.
        /// </summary>
        public void Apply()
        {
            if (gradientTexture == null)
            {
                RebuildGradientTexture();
            }

            DisableKeywords(blendStyleStrings);
            int styleIdx = Mathf.Clamp((int)blendStyle, 0, blendStyleStrings.Length - 1);
            Shader.EnableKeyword(blendStyleStrings[styleIdx]);

            DisableKeywords(recolorSourceStrings);
            int sourceIdx = Mathf.Clamp((int)source, 0, recolorSourceStrings.Length - 1);
            Shader.EnableKeyword(recolorSourceStrings[sourceIdx]);

            Shader.SetGlobalColor(blendEdgeColorID, blendEdgeColor);
            Shader.SetGlobalVector(depthMinMaxID, depthMinMax);
            Shader.SetGlobalFloat(invertBlendDirID, invertBlendDirection ? 1f : 0f);
            Shader.SetGlobalTexture(gradTexID, gradientTexture);

            if (usePulse)
            {
                Shader.EnableKeyword("_B2U_RECOLOR_PULSE");
                Shader.SetGlobalFloat(pulseSpeedID, pulseSpeed);
                Shader.SetGlobalFloat(pulseFreqID, pulseFrequency);
                Shader.SetGlobalFloat(pulseSizeID, pulseSize);
                Shader.SetGlobalFloat(pulseAttenID, pulseAttenuation);
                Shader.SetGlobalFloat(pulseIntensityID, pulseIntensity);
            }
            else
            {
                Shader.DisableKeyword("_B2U_RECOLOR_PULSE");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (gradientTexture != null)
            {
                RebuildGradientTexture();
            }
        }
#endif

        private void OnDestroy()
        {
            if (gradientTexture != null)
            {
                if (Application.isPlaying) Destroy(gradientTexture);
                else DestroyImmediate(gradientTexture);
                gradientTexture = null;
            }
        }
    }

    /// <summary>
    /// The source data for evaluating the recolor gradient.
    /// </summary>
    public enum B2URecolorSource
    {
        /// <summary>
        /// The relative brightness of the scene. Maintains transparent objects.
        /// </summary>
        Luminance,
        /// <summary>
        /// The red channel of the scene. Maintains transparent objects.
        /// </summary>
        Red,
        /// <summary>
        /// The green channel of the scene. Maintains transparent objects.
        /// </summary>
        Green,
        /// <summary>
        /// The blue channel of the scene. Maintains transparent objects.
        /// </summary>
        Blue,
        /// <summary>
        /// The eye depth normalized 1-0 based on the depth min/max. Erases transparent objects (Transparent, Crystal, Liquid).
        /// </summary>
        Depth,
        /// <summary>
        /// The dot product of the view normal and the world normal. Erases transparent objects (Transparent, Crystal, Liquid).
        /// </summary>
        Normal
    }

    /// <summary>
    /// The blend style for the recolor effect.
    /// </summary>
    public enum B2URecolorBlendStyle
    {
        /// <summary>
        /// Fades in and out evenly across the whole screen.
        /// </summary>
        Alpha,
        /// <summary>
        /// Blends from/to the center of the screen.
        /// </summary>
        Radial,
        /// <summary>
        /// Blends from/to the center of the screen with distortion so it is not a perfect circle.
        /// </summary>
        DistortedRadial,
        /// <summary>
        /// Scans across the screen horizontally.
        /// </summary>
        HorizontalScan,
        /// <summary>
        /// Scans across the screen vertically.
        /// </summary>
        VerticalScan,
        /// <summary>
        /// Scans across the screen horizontally from both sides.
        /// </summary>
        HorizontalDoubleScan,
        /// <summary>
        /// Scans across the screen vertically from the top and bottom.
        /// </summary>
        VerticalDoubleScan
    }
}