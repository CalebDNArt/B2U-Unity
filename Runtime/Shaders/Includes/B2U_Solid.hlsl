#ifndef B2U_SOLID_INCLUDED
#define B2U_SOLID_INCLUDED

#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Core.hlsl"

// --- SOLID NODES (OPAQUE, OPAQUE DS, TRANSPARENT) ---

// Extract values from the Renderer Set User Value (RSUV) for per-object control of solid materials.
void B2UUnpackRSUVSolidInternal(out uint Palette_Index, out float2 Palette_UV, out half Emission_Strength, out float3 Noise_Offset, out bool Is_Painted, out half Crack_Intensity)
{
    #if defined(SHADERGRAPH_PREVIEW)
        uint rsuv = 0u;
    #else
        uint rsuv = unity_RendererUserValue;
    #endif

    uint colorIndexInt = rsuv & 0x7Fu;
    uint pixelX = colorIndexInt % 16u;
    uint pixelY = colorIndexInt / 16u;
    Palette_UV = float2(((float)pixelX + 0.5) / 16.0, ((float)pixelY + 0.5) / 8.0);

    Palette_Index = (rsuv >> 7) & 0xFu;

    half invEmis = saturate(1.0 - ((half)((rsuv >> 11) & 0x7Fu) / 127.0));
    Emission_Strength = invEmis * invEmis * invEmis;

    uint noiseOffsetInt = (rsuv >> 18) & 0x3Fu;
    float3 noiseOffset3D = float3((float)(noiseOffsetInt % 4u), (float)((noiseOffsetInt / 4u) % 4u), (float)((noiseOffsetInt / 16u) % 4u));
    Noise_Offset = noiseOffset3D * 137.5;

    Is_Painted = ((rsuv >> 24) & 0x1u) != 0u;

    Crack_Intensity = (half)((rsuv >> 25) & 0x7Fu) / 127.0;
}

void B2U_Unpack_RSUV_Solid_float(out uint Palette_Index, out float2 Palette_UV, out half Emission_Strength, out float3 Noise_Offset, out bool Is_Painted, out half Crack_Intensity)
{
    B2UUnpackRSUVSolidInternal(Palette_Index, Palette_UV, Emission_Strength, Noise_Offset, Is_Painted, Crack_Intensity);
}

void B2U_Unpack_RSUV_Solid_half(out uint Palette_Index, out float2 Palette_UV, out half Emission_Strength, out float3 Noise_Offset, out bool Is_Painted, out half Crack_Intensity)
{
    B2UUnpackRSUVSolidInternal(Palette_Index, Palette_UV, Emission_Strength, Noise_Offset, Is_Painted, Crack_Intensity);
}


// Get variant index (0-7) from the IEEE-754 sign bits of the 3 edge distances.
// Only applies to Opaque, Opaque Double-Sided, and Transparent workflows.
void B2UGetVariantIndexInternal(float3 Edge_Distances, out int Variant_Index)
{
    uint bit0 = asuint(Edge_Distances.x) >> 31;
    uint bit1 = asuint(Edge_Distances.y) >> 31;
    uint bit2 = asuint(Edge_Distances.z) >> 31;

    Variant_Index = (int)(bit0 | (bit1 << 1) | (bit2 << 2));
}

void B2U_Get_Variant_Index_float(float3 Edge_Distances, out int Variant_Index)
{
    B2UGetVariantIndexInternal(Edge_Distances, Variant_Index);
}

void B2U_Get_Variant_Index_half(half3 Edge_Distances, out int Variant_Index)
{
    B2UGetVariantIndexInternal((float3)Edge_Distances, Variant_Index);
}


// Solve the branches for solid materials to dynamically bypass unused GPU calculations.
void B2UDetermineBranchesSolidInternal(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, out bool Is_Biome_Responsive, out bool Is_Detail_Mapped,
    out half Detail_Index, out bool Is_Emissive, out half Emission_Mode, out bool Is_Iridescent, out half Iridescence_Mode)
{
    half4 p4 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.875, (float)Material_ID), 0);

    // Threshold at 0.45 cleanly separates Independent workflows (0.0 - 0.4) from Dynamic workflows (0.5 - 0.9)
    Is_Biome_Responsive = p4.r >= 0.45;
    Is_Detail_Mapped = p4.g > 0.004;
    Is_Emissive = p4.b > 0.004;

    #if defined(_SURFACE_TYPE_TRANSPARENT)
        // Subtract 1 because enum slot 0 is "None", and clamp to >= 0 for array slice safety
        Detail_Index = max(0.0, round(UnpackLUT(p4.g, Transparent_r1_p4_g_MinMax)) - 1.0);
        Emission_Mode = round(UnpackLUT(p4.b, Transparent_r1_p4_b_MinMax));
    #else
        Detail_Index = max(0.0, round(UnpackLUT(p4.g, Opaque_r1_p4_g_MinMax)) - 1.0);
        Emission_Mode = round(UnpackLUT(p4.b, Opaque_r1_p4_b_MinMax));
    #endif

    Is_Iridescent = p4.a > 0.004;
    Iridescence_Mode = p4.a; // Unpacked universally for all workflows in B2UIridescenceInternal
}

void B2U_Determine_Branches_Solid_float(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, out bool Is_Biome_Responsive, out bool Is_Detail_Mapped,
    out half Detail_Index, out bool Is_Emissive, out half Emission_Mode, out bool Is_Iridescent, out half Iridescence_Mode)
{
    B2UDetermineBranchesSolidInternal(Property_LUT, Sampler, Material_ID, Is_Biome_Responsive, Is_Detail_Mapped, Detail_Index, Is_Emissive, Emission_Mode, Is_Iridescent, Iridescence_Mode);
}

void B2U_Determine_Branches_Solid_half(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, out bool Is_Biome_Responsive, out bool Is_Detail_Mapped,
    out half Detail_Index, out bool Is_Emissive, out half Emission_Mode, out bool Is_Iridescent, out half Iridescence_Mode)
{
    B2UDetermineBranchesSolidInternal(Property_LUT, Sampler, Material_ID, Is_Biome_Responsive, Is_Detail_Mapped, Detail_Index, Is_Emissive, Emission_Mode, Is_Iridescent, Iridescence_Mode);
}


// Sample the Property LUT for solids.
void B2UPropLUTSolidInternal(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, bool Is_Detail_Mapped,
    out half Emission_Strength, out half Emission_Speed, out half Iridescence_Strength, out half Iridescence_Power, out half Detail_Gradient_Power,
    out half Detail_Normal_Strength, out half Detail_Curvature_Strength, out half Detail_Booleans, out half Backface_Color_Multiplier, out half Backface_Booleans)
{
    Backface_Color_Multiplier = 0.5;
    Backface_Booleans = 0.0;

    #if defined(_B2U_DOUBLE_SIDED)
        half4 p3 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.625, (float)Material_ID), 0);
        Backface_Color_Multiplier = UnpackLUT(p3.r, Opaque_r1_p3_r_MinMax);
        Backface_Booleans = p3.g; // Bitmask is unpacked downstream
    #endif

    #if defined(_SURFACE_TYPE_TRANSPARENT)
        half4 p1 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.125, (float)Material_ID), 0);
        Emission_Strength = UnpackLUT(p1.r, Transparent_r1_p1_r_MinMax);
        Emission_Speed = UnpackLUT(p1.g, Transparent_r1_p1_g_MinMax);
        Iridescence_Strength = UnpackLUT(p1.b, Transparent_r1_p1_b_MinMax);
        Iridescence_Power = UnpackLUT(p1.a, Transparent_r1_p1_a_MinMax);

        [branch]
        if (Is_Detail_Mapped)
        {
            half4 p2 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.375, (float)Material_ID), 0);
            Detail_Gradient_Power = UnpackLUT(p2.r, Transparent_r1_p2_r_MinMax);
            Detail_Normal_Strength = UnpackLUT(p2.g, Transparent_r1_p2_g_MinMax);
            Detail_Curvature_Strength = UnpackLUT(p2.b, Transparent_r1_p2_b_MinMax);
            Detail_Booleans = p2.a;
        }
        else
        {
            Detail_Gradient_Power = 1.0;
            Detail_Normal_Strength = 0.0;
            Detail_Curvature_Strength = 0.0;
            Detail_Booleans = 0.0;
        }
    #else
        half4 p1 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.125, (float)Material_ID), 0);
        Emission_Strength = UnpackLUT(p1.r, Opaque_r1_p1_r_MinMax);
        Emission_Speed = UnpackLUT(p1.g, Opaque_r1_p1_g_MinMax);
        Iridescence_Strength = UnpackLUT(p1.b, Opaque_r1_p1_b_MinMax);
        Iridescence_Power = UnpackLUT(p1.a, Opaque_r1_p1_a_MinMax);

        [branch]
        if (Is_Detail_Mapped)
        {
            half4 p2 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.375, (float)Material_ID), 0);
            Detail_Gradient_Power = UnpackLUT(p2.r, Opaque_r1_p2_r_MinMax);
            Detail_Normal_Strength = UnpackLUT(p2.g, Opaque_r1_p2_g_MinMax);
            Detail_Curvature_Strength = UnpackLUT(p2.b, Opaque_r1_p2_b_MinMax);
            Detail_Booleans = p2.a;
        }
        else
        {
            Detail_Gradient_Power = 1.0;
            Detail_Normal_Strength = 0.0;
            Detail_Curvature_Strength = 0.0;
            Detail_Booleans = 0.0;
        }
    #endif
}

void B2U_Prop_LUT_Solid_float(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, bool Is_Detail_Mapped,
    out half Emission_Strength, out half Emission_Speed, out half Iridescence_Strength, out half Iridescence_Power, out half Detail_Gradient_Power,
    out half Detail_Normal_Strength, out half Detail_Curvature_Strength, out half Detail_Booleans, out half Backface_Color_Multiplier, out half Backface_Booleans)
{
    B2UPropLUTSolidInternal(Property_LUT, Sampler, Material_ID, Is_Detail_Mapped, Emission_Strength, Emission_Speed, Iridescence_Strength, Iridescence_Power, Detail_Gradient_Power, Detail_Normal_Strength, Detail_Curvature_Strength, Detail_Booleans, Backface_Color_Multiplier, Backface_Booleans);
}

void B2U_Prop_LUT_Solid_half(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, bool Is_Detail_Mapped,
    out half Emission_Strength, out half Emission_Speed, out half Iridescence_Strength, out half Iridescence_Power, out half Detail_Gradient_Power,
    out half Detail_Normal_Strength, out half Detail_Curvature_Strength, out half Detail_Booleans, out half Backface_Color_Multiplier, out half Backface_Booleans)
{
    B2UPropLUTSolidInternal(Property_LUT, Sampler, Material_ID, Is_Detail_Mapped, Emission_Strength, Emission_Speed, Iridescence_Strength, Iridescence_Power, Detail_Gradient_Power, Detail_Normal_Strength, Detail_Curvature_Strength, Detail_Booleans, Backface_Color_Multiplier, Backface_Booleans);
}


// Sample the Color LUT for solids.
void B2UColorLUTSolidInternal(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    half Variant_Index, UnitySamplerState Sampler, half Num_Subtypes, half Material_ID, bool Is_Biome_Responsive, out half Metallic, out half Smoothness,
    out half3 Blended_Color, out half3 Blended_Emission)
{
    half packedMetallic;
    half packedSmoothness;
    SampleColorLUTRow(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, Variant_Index, Sampler, Num_Subtypes, Material_ID, Is_Biome_Responsive, packedMetallic, packedSmoothness, Blended_Color, Blended_Emission);

    #if defined(_SURFACE_TYPE_TRANSPARENT)
        Metallic = UnpackLUT(packedMetallic, Transparent_r1_ca_MinMax);
        Smoothness = UnpackLUT(packedSmoothness, Transparent_r1_ea_MinMax);
    #else
        Metallic = UnpackLUT(packedMetallic, Opaque_r1_ca_MinMax);
        Smoothness = UnpackLUT(packedSmoothness, Opaque_r1_ea_MinMax);
    #endif
}

void B2U_Color_LUT_Solid_float(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    half Variant_Index, UnitySamplerState Sampler, half Num_Subtypes, half Material_ID, bool Is_Biome_Responsive, out half Metallic, out half Smoothness,
    out half3 Blended_Color, out half3 Blended_Emission)
{
    B2UColorLUTSolidInternal(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, Variant_Index, Sampler, Num_Subtypes, Material_ID, Is_Biome_Responsive, Metallic, Smoothness, Blended_Color, Blended_Emission);
}

void B2U_Color_LUT_Solid_half(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    half Variant_Index, UnitySamplerState Sampler, half Num_Subtypes, half Material_ID, bool Is_Biome_Responsive, out half Metallic, out half Smoothness,
    out half3 Blended_Color, out half3 Blended_Emission)
{
    B2UColorLUTSolidInternal(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, Variant_Index, Sampler, Num_Subtypes, Material_ID, Is_Biome_Responsive, Metallic, Smoothness, Blended_Color, Blended_Emission);
}


// Sample the 64-slice detail map array to extract normal shift, curvature, and heightmaps.
void B2USampleDetailMapsInternal(half Detail_Index, bool Is_Detail_Mapped, UnityTexture2DArray Detail_Atlas, UnitySamplerState Sampler, float2 UV_X, float2 UV_Y,
    float2 UV_Z, out half4 Sample_X, out half4 Sample_Y, out half4 Sample_Z)
{
    half4 flatDefault = half4(0.5, 0.5, 0.5, 0.0);

    Sample_X = flatDefault;
    Sample_Y = flatDefault;
    Sample_Z = flatDefault;

    float2 dxX = ddx(UV_X.xy) + 1e-5;
    float2 dyX = ddy(UV_X.xy) + 1e-5;
    float2 dxY = ddx(UV_Y.xy) + 1e-5;
    float2 dyY = ddy(UV_Y.xy) + 1e-5;
    float2 dxZ = ddx(UV_Z.xy) + 1e-5;
    float2 dyZ = ddy(UV_Z.xy) + 1e-5;

    [branch]
    if (Is_Detail_Mapped)
    {
        Sample_X = (half4)SAMPLE_TEXTURE2D_ARRAY_GRAD(Detail_Atlas, Sampler, UV_X, Detail_Index, dxX, dyX);
        Sample_Y = (half4)SAMPLE_TEXTURE2D_ARRAY_GRAD(Detail_Atlas, Sampler, UV_Y, Detail_Index, dxY, dyY);
        Sample_Z = (half4)SAMPLE_TEXTURE2D_ARRAY_GRAD(Detail_Atlas, Sampler, UV_Z, Detail_Index, dxZ, dyZ);
    }
}

void B2U_Sample_Detail_Maps_float(half Detail_Index, bool Is_Detail_Mapped, UnityTexture2DArray Detail_Atlas, UnitySamplerState Sampler, float2 UV_X, float2 UV_Y,
    float2 UV_Z, out half4 Sample_X, out half4 Sample_Y, out half4 Sample_Z)
{
    B2USampleDetailMapsInternal(Detail_Index, Is_Detail_Mapped, Detail_Atlas, Sampler, UV_X, UV_Y, UV_Z, Sample_X, Sample_Y, Sample_Z);
}

void B2U_Sample_Detail_Maps_half(half Detail_Index, bool Is_Detail_Mapped, UnityTexture2DArray Detail_Atlas, UnitySamplerState Sampler, float2 UV_X, float2 UV_Y,
    float2 UV_Z, out half4 Sample_X, out half4 Sample_Y, out half4 Sample_Z)
{
    B2USampleDetailMapsInternal(Detail_Index, Is_Detail_Mapped, Detail_Atlas, Sampler, UV_X, UV_Y, UV_Z, Sample_X, Sample_Y, Sample_Z);
}


// Unpacks the normal shift, curvature, and heightmaps from the detail texture samples.
void B2UUnpackDetailSamplesInternal(bool Inverted_Normal, bool Inverted_Curvature, bool Inverted_Heightmap, half Normal_Strength, half Curvature_Strength, half4 IN_X,
    half4 IN_Y, half4 IN_Z, out half2 Unpacked_Normal_Shift_X, out half2 Unpacked_Normal_Shift_Y, out half2 Unpacked_Normal_Shift_Z, out half3 Texture_Curvature,
    out half3 Heightmaps)
{
    half2 unpackX = (IN_X.xy * 2.0) - 1.0;
    half2 unpackY = (IN_Y.xy * 2.0) - 1.0;
    half2 unpackZ = (IN_Z.xy * 2.0) - 1.0;

    Unpacked_Normal_Shift_X = half2(unpackX.x, Inverted_Normal ? -unpackX.y : unpackX.y) * Normal_Strength;
    Unpacked_Normal_Shift_Y = half2(unpackY.x, Inverted_Normal ? -unpackY.y : unpackY.y) * Normal_Strength;
    Unpacked_Normal_Shift_Z = half2(unpackZ.x, Inverted_Normal ? -unpackZ.y : unpackZ.y) * Normal_Strength;

    #if defined(_B2U_EDGE_MODE_BOTH)
        half3 unpacked_curvature = ((half3(IN_X.z, IN_Y.z, IN_Z.z) * 2.0) - 1.0) * Curvature_Strength;
    #elif defined(_B2U_EDGE_MODE_CONVEX_ONLY)
        half3 unpacked_curvature = saturate(((half3(IN_X.z, IN_Y.z, IN_Z.z) * 2.0) - 1.0) * Curvature_Strength);
    #else
        half3 unpacked_curvature = clamp(((half3(IN_X.z, IN_Y.z, IN_Z.z) * 2.0) - 1.0) * Curvature_Strength, -1.0, 0.0);
    #endif
    half3 unpacked_heightmaps = half3(IN_X.w, IN_Y.w, IN_Z.w);

    Texture_Curvature = Inverted_Curvature ? -unpacked_curvature : unpacked_curvature;
    Heightmaps = Inverted_Heightmap ? (1.0 - unpacked_heightmaps) : unpacked_heightmaps;
}

void B2U_Unpack_Detail_Samples_float(bool Inverted_Normal, bool Inverted_Curvature, bool Inverted_Heightmap, half Normal_Strength, half Curvature_Strength, half4 IN_X,
    half4 IN_Y, half4 IN_Z, out half2 Unpacked_Normal_Shift_X, out half2 Unpacked_Normal_Shift_Y, out half2 Unpacked_Normal_Shift_Z, out half3 Texture_Curvature,
    out half3 Heightmaps)
{
    B2UUnpackDetailSamplesInternal(Inverted_Normal, Inverted_Curvature, Inverted_Heightmap, Normal_Strength, Curvature_Strength, IN_X, IN_Y, IN_Z, Unpacked_Normal_Shift_X, Unpacked_Normal_Shift_Y, Unpacked_Normal_Shift_Z, Texture_Curvature, Heightmaps);
}

void B2U_Unpack_Detail_Samples_half(bool Inverted_Normal, bool Inverted_Curvature, bool Inverted_Heightmap, half Normal_Strength, half Curvature_Strength, half4 IN_X,
    half4 IN_Y, half4 IN_Z, out half2 Unpacked_Normal_Shift_X, out half2 Unpacked_Normal_Shift_Y, out half2 Unpacked_Normal_Shift_Z, out half3 Texture_Curvature,
    out half3 Heightmaps)
{
    B2UUnpackDetailSamplesInternal(Inverted_Normal, Inverted_Curvature, Inverted_Heightmap, Normal_Strength, Curvature_Strength, IN_X, IN_Y, IN_Z, Unpacked_Normal_Shift_X, Unpacked_Normal_Shift_Y, Unpacked_Normal_Shift_Z, Texture_Curvature, Heightmaps);
}


// Determine the emission mode and calculate the emission strength and pattern used if applicable.
void B2USampleEmissionMapSolidInternal(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, half Emission_Mode, bool Is_Emissive, bool Height_Based_Emission,
    bool Curvature_Based_Emission, half Heightmap, half Curvature, half Emission_Strength, float Global_Strength_Mult, half Emission_Speed, float Global_Speed_Mult,
    float2 UV_X, float2 UV_Y, float2 UV_Z, half3 Blend_Weights, half Vertex_Green, out half Emission)
{
    Emission = 0.0;
    [branch]
    if (Is_Emissive)
    {
        Emission = SampleEmissionMap(UV_X, UV_Y, UV_Z, Emission_Mode, Blend_Weights, Emission_Strength, (half)Global_Strength_Mult, Emission_Speed, (half)Global_Speed_Mult, Emission_Patterns, Sampler);
        Emission *= Vertex_Green;
        if (Height_Based_Emission) Emission *= Heightmap;
        if (Curvature_Based_Emission) Emission *= abs(Curvature);
    }
}

void B2U_Sample_Emission_Map_Solid_float(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, half Emission_Mode, bool Is_Emissive, bool Height_Based_Emission,
    bool Curvature_Based_Emission, half Heightmap, half Curvature, half Emission_Strength, float Global_Strength_Mult, half Emission_Speed, float Global_Speed_Mult,
    float2 UV_X, float2 UV_Y, float2 UV_Z, half3 Blend_Weights, half Vertex_Green, out half Emission)
{
    B2USampleEmissionMapSolidInternal(Emission_Patterns, Sampler, Emission_Mode, Is_Emissive, Height_Based_Emission, Curvature_Based_Emission, Heightmap, Curvature, Emission_Strength, Global_Strength_Mult, Emission_Speed, Global_Speed_Mult, UV_X, UV_Y, UV_Z, Blend_Weights, Vertex_Green, Emission);
}

void B2U_Sample_Emission_Map_Solid_half(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, half Emission_Mode, bool Is_Emissive, bool Height_Based_Emission,
    bool Curvature_Based_Emission, half Heightmap, half Curvature, half Emission_Strength, float Global_Strength_Mult, half Emission_Speed, float Global_Speed_Mult,
    float2 UV_X, float2 UV_Y, float2 UV_Z, half3 Blend_Weights, half Vertex_Green, out half Emission)
{
    B2USampleEmissionMapSolidInternal(Emission_Patterns, Sampler, Emission_Mode, Is_Emissive, Height_Based_Emission, Curvature_Based_Emission, Heightmap, Curvature, Emission_Strength, Global_Strength_Mult, Emission_Speed, Global_Speed_Mult, UV_X, UV_Y, UV_Z, Blend_Weights, Vertex_Green, Emission);
}

// --- END SOLID NODES ---

#endif