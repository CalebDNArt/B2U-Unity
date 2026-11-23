using UnityEngine;
using static B2U.B2URuntimeUtility;

namespace B2U
{
    /// <summary>
    /// Stores configuration, shadow banding, and matting parameters for a fullscreen stylize effect preset.
    /// </summary>
    [CreateAssetMenu(fileName = "New Stylize Preset", menuName = "B2U/Fullscreen/Stylize Preset")]
    public class B2UStylizePreset : ScriptableObject
    {
        /// <summary>
        /// A string array for holding the keywords for the different matting styles.
        /// </summary>
        public static readonly string[] mattingStyleStrings =
        {
            "_B2U_MATTING_STYLE_NONE",
            "_B2U_MATTING_STYLE_CIRCLE",
            "_B2U_MATTING_STYLE_RECTANGLE",
            "_B2U_MATTING_STYLE_NOISY_RECTANGLE"
        };

        /// <summary>
        /// Global shader ID for the style texture array.
        /// </summary>
        public static readonly int stylizeTexArrayID = Shader.PropertyToID("_B2U_Style_Texture_Array");
        /// <summary>
        /// Global shader ID for the base style index.
        /// </summary>
        public static readonly int baseStyleIndexID = Shader.PropertyToID("_B2U_Stylize_Base_Style_Index");
        /// <summary>
        /// Global shader ID for the base texture scale.
        /// </summary>
        public static readonly int baseScaleID = Shader.PropertyToID("_B2U_Stylize_Base_Scale");
        /// <summary>
        /// Global shader ID for the base texture intensity.
        /// </summary>
        public static readonly int baseIntensityID = Shader.PropertyToID("_B2U_Stylize_Base_Intensity");
        /// <summary>
        /// Global shader ID for the shadow band number.
        /// </summary>
        public static readonly int shadowBandsID = Shader.PropertyToID("_B2U_Stylize_Shadow_Bands");
        /// <summary>
        /// Global shader ID for the shadow offset mode.
        /// </summary>
        public static readonly int shadowOffsetModeID = Shader.PropertyToID("_B2U_Stylize_Shadow_Offset_Mode");
        /// <summary>
        /// Global shader ID for the shadow style index.
        /// </summary>
        public static readonly int shadowStyleIndexID = Shader.PropertyToID("_B2U_Stylize_Shadow_Style_Index");
        /// <summary>
        /// Global shader ID for the shadow texture scale.
        /// </summary>
        public static readonly int shadowScaleID = Shader.PropertyToID("_B2U_Stylize_Shadow_Scale");
        /// <summary>
        /// Global shader ID for the shadow texture intensity.
        /// </summary>
        public static readonly int shadowIntensityID = Shader.PropertyToID("_B2U_Stylize_Shadow_Intensity");
        /// <summary>
        /// Global shader ID for the shadow min/max.
        /// </summary>
        public static readonly int shadowMinMaxID = Shader.PropertyToID("_B2U_Stylize_Shadow_MinMax");
        /// <summary>
        /// Global shader ID for the shadow tint color.
        /// </summary>
        public static readonly int shadowTintID = Shader.PropertyToID("_B2U_Stylize_Shadow_Tint");
        /// <summary>
        /// Global shader ID for the matting size.
        /// </summary>
        public static readonly int mattingSizeID = Shader.PropertyToID("_B2U_Stylize_Matting_Size");
        /// <summary>
        /// Global shader ID for the matting falloff.
        /// </summary>
        public static readonly int mattingFalloffID = Shader.PropertyToID("_B2U_Stylize_Matting_Falloff");
        /// <summary>
        /// Global shader ID for the matting color.
        /// </summary>
        public static readonly int mattingColorID = Shader.PropertyToID("_B2U_Stylize_Matting_Color");

        /// <summary>
        /// The Texture2DArray holding the different styles.
        /// </summary>
        public Texture2DArray stylizeTextureArray;
        /// <summary>
        /// The index to use for the base texture.
        /// </summary>
        public B2UStylizeIndex baseIndex = B2UStylizeIndex.Cloud;
        /// <summary>
        /// The scale of the base texture.
        /// </summary>
        public float baseScale = 3f;
        /// <summary>
        /// The intensity of the base texture.
        /// </summary>
        public float baseIntensity = 0.1f;
        /// <summary>
        /// The number of shadow bands.
        /// </summary>
        public int shadowBands = 3;
        /// <summary>
        /// The offset mode for shadow layers.
        /// </summary>
        public bool shadowOffsetMode = false;
        /// <summary>
        /// The index to use for the shadow texture.
        /// </summary>
        public B2UStylizeIndex shadowIndex = B2UStylizeIndex.Hatched;
        /// <summary>
        /// The scale of the shadow texture.
        /// </summary>
        public float shadowScale = 5f;
        /// <summary>
        /// The intensity of the shadow texture.
        /// </summary>
        public float shadowIntensity = 1f;
        /// <summary>
        /// The min/max value for what defines the shadow steps.
        /// </summary>
        public Vector2 shadowMinMax = new Vector2(0.25f, 0f);
        /// <summary>
        /// The tint color for the shadows.
        /// </summary>
        public Color shadowTint = Color.black;
        /// <summary>
        /// The matting style to use.
        /// </summary>
        public B2UStylizeMattingStyle mattingStyle = B2UStylizeMattingStyle.None;
        /// <summary>
        /// The size of the matting.
        /// </summary>
        public float mattingSize = 0.1f;
        /// <summary>
        /// The falloff for the matting.
        /// </summary>
        public float mattingFalloff = 0.1f;
        /// <summary>
        /// The color of the matting.
        /// </summary>
        public Color mattingColor = Color.beige;

        /// <summary>
        /// Pushes this stylize preset's keywords, textures, and parameters to the global shader state.
        /// </summary>
        public void Apply()
        {
            DisableKeywords(mattingStyleStrings);
            int mattingIdx = Mathf.Clamp((int)mattingStyle, 0, mattingStyleStrings.Length - 1);
            Shader.EnableKeyword(mattingStyleStrings[mattingIdx]);

            Shader.SetGlobalTexture(stylizeTexArrayID, stylizeTextureArray);
            Shader.SetGlobalFloat(baseStyleIndexID, (int)baseIndex);
            Shader.SetGlobalFloat(baseScaleID, baseScale);
            Shader.SetGlobalFloat(baseIntensityID, baseIntensity);
            Shader.SetGlobalFloat(shadowBandsID, shadowBands);
            Shader.SetGlobalFloat(shadowOffsetModeID, shadowOffsetMode ? 1f : 0f);
            Shader.SetGlobalFloat(shadowStyleIndexID, (int)shadowIndex);
            Shader.SetGlobalFloat(shadowScaleID, shadowScale);
            Shader.SetGlobalFloat(shadowIntensityID, shadowIntensity);
            Shader.SetGlobalVector(shadowMinMaxID, shadowMinMax);
            Shader.SetGlobalColor(shadowTintID, shadowTint);
            Shader.SetGlobalFloat(mattingSizeID, mattingSize);
            Shader.SetGlobalFloat(mattingFalloffID, mattingFalloff);
            Shader.SetGlobalColor(mattingColorID, mattingColor);
        }
    }

    /// <summary>
    /// What slice in the Styles Texture2DArray to use.
    /// </summary>
    public enum B2UStylizeIndex
    {
        Cloud,
        Gouache,
        Splotchy,
        Nebula,
        Swirled,
        Sweeping,
        Hatched,
        Crosshatched,
        Cloth,
        Watercolor,
        Sparkles,
        Circuits,
        StarGrid,
        DiamondGrid,
        DotGrid,
        RingGrid
    }

    /// <summary>
    /// The matting style to use for the edges of the screen.
    /// </summary>
    public enum B2UStylizeMattingStyle
    {
        /// <summary>
        /// No matting.
        /// </summary>
        None,
        /// <summary>
        /// Circular matting from the center of the screen.
        /// </summary>
        Circle,
        /// <summary>
        /// Rectangular matting.
        /// </summary>
        Rectangle,
        /// <summary>
        /// Rectangular matting with noise introduced so it doesn't create a perfect edge.
        /// </summary>
        NoisyRectangle
    }
}