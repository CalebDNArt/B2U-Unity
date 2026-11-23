#ifndef B2U_LIQUID_INCLUDED
#define B2U_LIQUID_INCLUDED

#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Core.hlsl"

// --- LIQUID NODES ---

// Allocates global texture bindings for liquids only if needed
#if !defined(SHADERGRAPH_PREVIEW)
    #if !defined(_B2U_REFRACTION_QUALITY_LOW) && !defined(__INTELLISENSE__) && defined(_B2U_VREF_ENABLED)
        #if defined(_B2U_VOLUMETRIC_REFRACTION) && !defined(B2U_VOLUMETRIC_REF_DECLARED)
            #define B2U_VOLUMETRIC_REF_DECLARED
            TEXTURE2D(_B2UVolumetricRefractionTexture); SAMPLER(sampler_B2UVolumetricRefractionTexture);
        #endif
        #if !defined(B2U_COLOR_COPY_DECLARED)
            #define B2U_COLOR_COPY_DECLARED
            TEXTURE2D(_B2UColorCopy); SAMPLER(sampler_B2UColorCopy);
        #endif
    #endif
    float _B2U_Active_Waves;
    float4 _B2U_Wave_Array[64];
#endif

#if !defined(_B2U_EDGE_DETECTION_DEPTH) && !defined(B2U_EDGE_BUFFER_DECLARED) && !defined(SHADERGRAPH_PREVIEW)
    #define B2U_EDGE_BUFFER_DECLARED
    TEXTURE2D(_B2UEdgeBuffer);
    SAMPLER(sampler_B2UEdgeBuffer);
#endif

// Extract values from the Renderer Set User Value (RSUV) for per-object control of liquid materials.
void B2UUnpackRSUVLiquidInternal(out half Emission_Strength, out half Flow_Strength, out half Wave_Multiplier, out half Mesh_ID)
{
    #if defined(SHADERGRAPH_PREVIEW)
        uint rsuv = 0u;
    #else
        uint rsuv = unity_RendererUserValue;
    #endif

    half invEmis = saturate(1.0 - ((half)(rsuv & 0xFFu) / 255.0));
    Emission_Strength = invEmis * invEmis * invEmis;

    Flow_Strength = ((1.0 - ((half)((rsuv >> 8) & 0x3FFu) / 1023.0)) * 2.0) - 1.0;

    Wave_Multiplier = ((1.0 - ((half)((rsuv >> 18) & 0xFFu) / 255.0)) * 2.0) - 1.0;

    Mesh_ID = (half)((rsuv >> 26) & 0x3Fu);
}

void B2U_Unpack_RSUV_Liquid_float(out half Emission_Strength, out half Flow_Strength, out half Wave_Multiplier, out half Mesh_ID)
{
    B2UUnpackRSUVLiquidInternal(Emission_Strength, Flow_Strength, Wave_Multiplier, Mesh_ID);
}

void B2U_Unpack_RSUV_Liquid_half(out half Emission_Strength, out half Flow_Strength, out half Wave_Multiplier, out half Mesh_ID)
{
    B2UUnpackRSUVLiquidInternal(Emission_Strength, Flow_Strength, Wave_Multiplier, Mesh_ID);
}


// A trimmed-down version of the LUT sampler that only grabs values needed for vertex displacement.
void B2USampleLUTsLiquidVertexInternal(UnityTexture2D Color_LUT, UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Num_Subtypes,
    half Material_ID, half Material_ID_Row2, float Liquid_Data_V, out half Viscosity, out half Wave_Height, out half Flow_Strength, out half Phase_Speed, out half Noise_Scale,
    out half Distortion_Scale, out half Distortion_Strength, out bool Invert_Height, out bool Ignore_Gravity, out half Bubble_Speed, out half Noise_Power)
{
    float lutWidth = ((float)Num_Subtypes * 8.0) + 8.0;
    float emissionStartIndex = 8.0 * (float)Num_Subtypes;

    float2 eUV2 = float2((emissionStartIndex + 0.5) / lutWidth, (float)Material_ID_Row2);
    half4 e2 = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, eUV2, 0);
    Viscosity = UnpackLUT(e2.a, Liquid_r2_ea_MinMax);

    half4 r1_p1 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.125, (float)Material_ID), 0);
    Noise_Scale = UnpackLUT(r1_p1.r, Liquid_r1_p1_r_MinMax);
    Noise_Power = UnpackLUT(r1_p1.g, Liquid_r1_p1_g_MinMax);
    Distortion_Scale = UnpackLUT(r1_p1.b, Liquid_r1_p1_b_MinMax);
    Distortion_Strength = UnpackLUT(r1_p1.a, Liquid_r1_p1_a_MinMax);

    half4 r1_p2 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.375, (float)Material_ID), 0);
    Phase_Speed = UnpackLUT(r1_p2.r, Liquid_r1_p2_r_MinMax);
    Flow_Strength = UnpackLUT(r1_p2.g, Liquid_r1_p2_g_MinMax);
    Wave_Height = UnpackLUT(r1_p2.b, Liquid_r1_p2_b_MinMax);
    Bubble_Speed = UnpackLUT(r1_p2.a, Liquid_r1_p2_a_MinMax);

    uint packedV = (uint)round(Liquid_Data_V);
    Ignore_Gravity = ((packedV >> 12) & 1u) != 0u;
    Invert_Height = ((packedV >> 14) & 1u) != 0u;
}

void B2U_Sample_LUTs_Liquid_Vertex_float(UnityTexture2D Color_LUT, UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Num_Subtypes,
    half Material_ID, half Material_ID_Row2, float Liquid_Data_V, out half Viscosity, out half Wave_Height, out half Flow_Strength, out half Phase_Speed, out half Noise_Scale,
    out half Distortion_Scale, out half Distortion_Strength, out bool Invert_Height, out bool Ignore_Gravity, out half Bubble_Speed, out half Noise_Power)
{
    B2USampleLUTsLiquidVertexInternal(Color_LUT, Property_LUT, Sampler, Num_Subtypes, Material_ID, Material_ID_Row2, Liquid_Data_V, Viscosity, Wave_Height, Flow_Strength, Phase_Speed, Noise_Scale, Distortion_Scale, Distortion_Strength, Invert_Height, Ignore_Gravity, Bubble_Speed, Noise_Power);
}

void B2U_Sample_LUTs_Liquid_Vertex_half(UnityTexture2D Color_LUT, UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Num_Subtypes,
    half Material_ID, half Material_ID_Row2, float Liquid_Data_V, out half Viscosity, out half Wave_Height, out half Flow_Strength, out half Phase_Speed, out half Noise_Scale,
    out half Distortion_Scale, out half Distortion_Strength, out bool Invert_Height, out bool Ignore_Gravity, out half Bubble_Speed, out half Noise_Power)
{
    B2USampleLUTsLiquidVertexInternal(Color_LUT, Property_LUT, Sampler, Num_Subtypes, Material_ID, Material_ID_Row2, Liquid_Data_V, Viscosity, Wave_Height, Flow_Strength, Phase_Speed, Noise_Scale, Distortion_Scale, Distortion_Strength, Invert_Height, Ignore_Gravity, Bubble_Speed, Noise_Power);
}


// Solve the branches for liquid materials to dynamically bypass unused GPU calculations.
void B2UDetermineBranchesLiquidInternal(UnityTexture2D Property_LUT, UnitySamplerState Sampler, float Liquid_Data_V, half Material_ID, out bool Is_Biome_Responsive,
    out bool Use_Displacement, out bool Is_Transmissive, out bool Use_Caustics, out bool Invert_Height, out bool Height_Based_Transmission, out bool Height_Based_Emission,
    out bool Ignore_Gravity, out bool Is_Emissive, out half Emission_Mode, out bool Is_Iridescent, out half Iridescence_Mode)
{
    uint packedV = (uint)round(Liquid_Data_V);
    half4 p4 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.875, (float)Material_ID), 0);

    Is_Biome_Responsive = p4.r >= 0.45;
    Use_Displacement = ((packedV >> 10) & 1u) != 0u;
    Is_Transmissive = ((packedV >> 11) & 1u) != 0u;
    Ignore_Gravity = ((packedV >> 12) & 1u) != 0u;
    Use_Caustics = ((packedV >> 13) & 1u) != 0u;
    Invert_Height = ((packedV >> 14) & 1u) != 0u;
    Height_Based_Transmission = ((packedV >> 15) & 1u) != 0u;
    Height_Based_Emission = ((packedV >> 16) & 1u) != 0u;

    Is_Emissive = p4.b > 0.01;
    Emission_Mode = round(UnpackLUT(p4.b, Liquid_r1_p4_b_MinMax));
    Is_Iridescent = p4.a > 0.01;
    Iridescence_Mode = p4.a;
}

void B2U_Determine_Branches_Liquid_float(UnityTexture2D Property_LUT, UnitySamplerState Sampler, float Liquid_Data_V, half Material_ID, out bool Is_Biome_Responsive,
    out bool Use_Displacement, out bool Is_Transmissive, out bool Use_Caustics, out bool Invert_Height, out bool Height_Based_Transmission, out bool Height_Based_Emission,
    out bool Ignore_Gravity, out bool Is_Emissive, out half Emission_Mode, out bool Is_Iridescent, out half Iridescence_Mode)
{
    B2UDetermineBranchesLiquidInternal(Property_LUT, Sampler, Liquid_Data_V, Material_ID, Is_Biome_Responsive, Use_Displacement, Is_Transmissive, Use_Caustics, Invert_Height, Height_Based_Transmission, Height_Based_Emission, Ignore_Gravity, Is_Emissive, Emission_Mode, Is_Iridescent, Iridescence_Mode);
}

void B2U_Determine_Branches_Liquid_half(UnityTexture2D Property_LUT, UnitySamplerState Sampler, float Liquid_Data_V, half Material_ID, out bool Is_Biome_Responsive,
    out bool Use_Displacement, out bool Is_Transmissive, out bool Use_Caustics, out bool Invert_Height, out bool Height_Based_Transmission, out bool Height_Based_Emission,
    out bool Ignore_Gravity, out bool Is_Emissive, out half Emission_Mode, out bool Is_Iridescent, out half Iridescence_Mode)
{
    B2UDetermineBranchesLiquidInternal(Property_LUT, Sampler, Liquid_Data_V, Material_ID, Is_Biome_Responsive, Use_Displacement, Is_Transmissive, Use_Caustics, Invert_Height, Height_Based_Transmission, Height_Based_Emission, Ignore_Gravity, Is_Emissive, Emission_Mode, Is_Iridescent, Iridescence_Mode);
}


// Checks if the mesh is tagged to use vertex displacement
void B2UCheckIfDisplacementInternal(float Liquid_Data_V, out bool Use_Displacement)
{
    uint packedV = (uint)round(Liquid_Data_V);
    Use_Displacement = ((packedV >> 10) & 1u) != 0u;
}

void B2U_Check_If_Displacement_float(float Liquid_Data_V, out bool Use_Displacement)
{
    B2UCheckIfDisplacementInternal(Liquid_Data_V, Use_Displacement);
}

void B2U_Check_If_Displacement_half(float Liquid_Data_V, out bool Use_Displacement)
{
    B2UCheckIfDisplacementInternal(Liquid_Data_V, Use_Displacement);
}


// Sample the Property LUT for liquids.
void B2UPropLUTLiquidInternal(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, half Material_ID_Row2, bool Is_Emissive_Or_Iridescenct,
    bool Is_Transmissive, out half Noise_Scale, out half Noise_Power, out half Distortion_Scale, out half Distortion_Strength, out half Phase_Speed, out half Flow_Strength,
    out half Wave_Height, out half Bubble_Speed, out half Emission_Pattern_Scale, out half Emission_Strength, out half Iridescence_Strength, out half Iridescence_Power,
    out half Transmission, out half Absorption, out half IOR)
{
    Emission_Pattern_Scale = 1.0;
    Emission_Strength = 0.0;
    Iridescence_Strength = 0.0;
    Iridescence_Power = 1.0;

    Transmission = 0.0;
    Absorption = 5.0;
    IOR = 1.5;

    half4 r1_p1 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.125, (float)Material_ID), 0);
    Noise_Scale = UnpackLUT(r1_p1.r, Liquid_r1_p1_r_MinMax);
    Noise_Power = UnpackLUT(r1_p1.g, Liquid_r1_p1_g_MinMax);
    Distortion_Scale = UnpackLUT(r1_p1.b, Liquid_r1_p1_b_MinMax);
    Distortion_Strength = UnpackLUT(r1_p1.a, Liquid_r1_p1_a_MinMax);

    half4 r1_p2 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.375, (float)Material_ID), 0);
    Phase_Speed = UnpackLUT(r1_p2.r, Liquid_r1_p2_r_MinMax);
    Flow_Strength = UnpackLUT(r1_p2.g, Liquid_r1_p2_g_MinMax);
    Wave_Height = UnpackLUT(r1_p2.b, Liquid_r1_p2_b_MinMax);
    Bubble_Speed = UnpackLUT(r1_p2.a, Liquid_r1_p2_a_MinMax);

    [branch]
    if (Is_Emissive_Or_Iridescenct)
    {
        half4 r1_p3 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.625, (float)Material_ID), 0);
        Emission_Pattern_Scale = UnpackLUT(r1_p3.r, Liquid_r1_p3_r_MinMax);
        Emission_Strength = UnpackLUT(r1_p3.g, Liquid_r1_p3_g_MinMax);
        Iridescence_Strength = UnpackLUT(r1_p3.b, Liquid_r1_p3_b_MinMax);
        Iridescence_Power = UnpackLUT(r1_p3.a, Liquid_r1_p3_a_MinMax);
    }

    [branch]
    if (Is_Transmissive)
    {
        half4 r2_p1 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.125, (float)Material_ID_Row2), 0);
        Transmission = UnpackLUT(r2_p1.r, Liquid_r2_p1_r_MinMax);
        Absorption = UnpackLUT(r2_p1.g, Liquid_r2_p1_g_MinMax);
        IOR = UnpackLUT(r2_p1.b, Liquid_r2_p1_b_MinMax);
    }
}

void B2U_Prop_LUT_Liquid_float(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, half Material_ID_Row2, bool Is_Emissive_Or_Iridescenct,
    bool Is_Transmissive, out half Noise_Scale, out half Noise_Power, out half Distortion_Scale, out half Distortion_Strength, out half Phase_Speed, out half Flow_Strength,
    out half Wave_Height, out half Bubble_Speed, out half Emission_Pattern_Scale, out half Emission_Strength, out half Iridescence_Strength, out half Iridescence_Power,
    out half Transmission, out half Absorption, out half IOR)
{
    B2UPropLUTLiquidInternal(Property_LUT, Sampler, Material_ID, Material_ID_Row2, Is_Emissive_Or_Iridescenct, Is_Transmissive, Noise_Scale, Noise_Power, Distortion_Scale, Distortion_Strength, Phase_Speed, Flow_Strength, Wave_Height, Bubble_Speed, Emission_Pattern_Scale, Emission_Strength, Iridescence_Strength, Iridescence_Power, Transmission, Absorption, IOR);
}

void B2U_Prop_LUT_Liquid_half(UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Material_ID, half Material_ID_Row2, bool Is_Emissive_Or_Iridescenct,
    bool Is_Transmissive, out half Noise_Scale, out half Noise_Power, out half Distortion_Scale, out half Distortion_Strength, out half Phase_Speed, out half Flow_Strength,
    out half Wave_Height, out half Bubble_Speed, out half Emission_Pattern_Scale, out half Emission_Strength, out half Iridescence_Strength, out half Iridescence_Power,
    out half Transmission, out half Absorption, out half IOR)
{
    B2UPropLUTLiquidInternal(Property_LUT, Sampler, Material_ID, Material_ID_Row2, Is_Emissive_Or_Iridescenct, Is_Transmissive, Noise_Scale, Noise_Power, Distortion_Scale, Distortion_Strength, Phase_Speed, Flow_Strength, Wave_Height, Bubble_Speed, Emission_Pattern_Scale, Emission_Strength, Iridescence_Strength, Iridescence_Power, Transmission, Absorption, IOR);
}


// Sample the Color LUT for liquids.
void B2UColorLUTLiquidInternal(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    UnitySamplerState Sampler, half Num_Subtypes, half Material_ID, half Material_ID_Row2, bool Is_Biome_Responsive, out half Metallic, out half Smoothness,
    out half3 Blended_Surface, out half3 Blended_Primary_Emission, out half3 Blended_Depth, out half3 Blended_Secondary_Emission, out half Edge_Distance, out half Viscosity)
{
    half packedMetallic;
    half packedSmoothness;
    SampleColorLUTRow(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, 0.0, Sampler, Num_Subtypes, Material_ID, Is_Biome_Responsive, packedMetallic, packedSmoothness, Blended_Surface, Blended_Primary_Emission);
    Metallic = UnpackLUT(packedMetallic, Liquid_r1_ca_MinMax);
    Smoothness = UnpackLUT(packedSmoothness, Liquid_r1_ea_MinMax);

    half packedEdgeDist;
    half packedVisc;
    SampleColorLUTRow(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, 0.0, Sampler, Num_Subtypes, Material_ID_Row2, Is_Biome_Responsive, packedEdgeDist, packedVisc, Blended_Depth, Blended_Secondary_Emission);
    Edge_Distance = UnpackLUT(packedEdgeDist, Liquid_r2_ca_MinMax);
    Viscosity = UnpackLUT(packedVisc, Liquid_r2_ea_MinMax);
}

void B2U_Color_LUT_Liquid_float(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    UnitySamplerState Sampler, half Num_Subtypes, half Material_ID, half Material_ID_Row2, bool Is_Biome_Responsive, out half Metallic, out half Smoothness,
    out half3 Blended_Surface, out half3 Blended_Primary_Emission, out half3 Blended_Depth, out half3 Blended_Secondary_Emission, out half Edge_Distance, out half Viscosity)
{
    B2UColorLUTLiquidInternal(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, Sampler, Num_Subtypes, Material_ID, Material_ID_Row2, Is_Biome_Responsive, Metallic, Smoothness, Blended_Surface, Blended_Primary_Emission, Blended_Depth, Blended_Secondary_Emission, Edge_Distance, Viscosity);
}

void B2U_Color_LUT_Liquid_half(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    UnitySamplerState Sampler, half Num_Subtypes, half Material_ID, half Material_ID_Row2, bool Is_Biome_Responsive, out half Metallic, out half Smoothness,
    out half3 Blended_Surface, out half3 Blended_Primary_Emission, out half3 Blended_Depth, out half3 Blended_Secondary_Emission, out half Edge_Distance, out half Viscosity)
{
    B2UColorLUTLiquidInternal(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, Sampler, Num_Subtypes, Material_ID, Material_ID_Row2, Is_Biome_Responsive, Metallic, Smoothness, Blended_Surface, Blended_Primary_Emission, Blended_Depth, Blended_Secondary_Emission, Edge_Distance, Viscosity);
}


// Samples the emission patterns for liquids.
void B2USampleEmissionMapLiquidInternal(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, float3 Pos_WS, half Emission_Pattern_Scale, half Emission_Mode,
    bool Is_Emissive, bool Height_Based_Emission, half Heightmask, half Emission_Strength, float Global_Strength_Mult, float Global_Speed_Mult, half3 Blend_Weights,
    half Phase_Blend, half3 Flow_A, half3 Flow_B, out half Emission)
{
    Emission = 0.0;
    [branch]
    if (Is_Emissive)
    {
        #if defined(_B2U_EMISSION_HAS_FLOW)
            [branch]
            if (Emission_Mode > 3.5)
            {
                float3 samplePosA = (Pos_WS - (float3)Flow_A) * (float)Emission_Pattern_Scale;
                float3 samplePosB = (Pos_WS - (float3)Flow_B) * (float)Emission_Pattern_Scale;
                half EmissionA = SampleEmissionMap(samplePosA.yz, samplePosA.xz, samplePosA.xy, Emission_Mode, Blend_Weights, Emission_Strength, (half)Global_Strength_Mult, 1.0, (half)Global_Speed_Mult, Emission_Patterns, Sampler);
                half EmissionB = SampleEmissionMap(samplePosB.yz, samplePosB.xz, samplePosB.xy, Emission_Mode, Blend_Weights, Emission_Strength, (half)Global_Strength_Mult, 1.0, (half)Global_Speed_Mult, Emission_Patterns, Sampler);
                Emission = lerp(EmissionA, EmissionB, Phase_Blend);
            }
            else
            {
                float3 samplePos = Pos_WS * (float)Emission_Pattern_Scale;
                Emission = SampleEmissionMap(samplePos.yz, samplePos.xz, samplePos.xy, Emission_Mode, Blend_Weights, Emission_Strength, (half)Global_Strength_Mult, 1.0, (half)Global_Speed_Mult, Emission_Patterns, Sampler);
            }
        #else
            float3 samplePos = Pos_WS * (float)Emission_Pattern_Scale;
            Emission = SampleEmissionMap(samplePos.yz, samplePos.xz, samplePos.xy, Emission_Mode, Blend_Weights, Emission_Strength, (half)Global_Strength_Mult, 1.0, (half)Global_Speed_Mult, Emission_Patterns, Sampler);
        #endif

        if (Height_Based_Emission)
        {
            Emission *= Heightmask;
        }
    }
}

void B2U_Sample_Emission_Map_Liquid_float(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, float3 Pos_WS, half Emission_Pattern_Scale, half Emission_Mode,
    bool Is_Emissive, bool Height_Based_Emission, half Heightmask, half Emission_Strength, float Global_Strength_Mult, float Global_Speed_Mult, half3 Blend_Weights,
    half Phase_Blend, half3 Flow_A, half3 Flow_B, out half Emission)
{
    B2USampleEmissionMapLiquidInternal(Emission_Patterns, Sampler, Pos_WS, Emission_Pattern_Scale, Emission_Mode, Is_Emissive, Height_Based_Emission, Heightmask, Emission_Strength, Global_Strength_Mult, Global_Speed_Mult, Blend_Weights, Phase_Blend, Flow_A, Flow_B, Emission);
}

void B2U_Sample_Emission_Map_Liquid_half(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, float3 Pos_WS, half Emission_Pattern_Scale, half Emission_Mode,
    bool Is_Emissive, bool Height_Based_Emission, half Heightmask, half Emission_Strength, float Global_Strength_Mult, float Global_Speed_Mult, half3 Blend_Weights,
    half Phase_Blend, half3 Flow_A, half3 Flow_B, out half Emission)
{
    B2USampleEmissionMapLiquidInternal(Emission_Patterns, Sampler, Pos_WS, Emission_Pattern_Scale, Emission_Mode, Is_Emissive, Height_Based_Emission, Heightmask, Emission_Strength, Global_Strength_Mult, Global_Speed_Mult, Blend_Weights, Phase_Blend, Flow_A, Flow_B, Emission);
}


// Solves the two phases needed for flow advection
void B2USolvePhasesInternal(float Time, float Phase_Speed, out float Phase_A, out float Phase_B, out float Phase_Blend, out int Floored_A, out int Floored_B)
{
    float tP = Time * Phase_Speed;
    Phase_A = frac(tP + 0.5) - 0.5;
    Floored_A = (int)floor(tP + 0.5);
    Phase_B = frac(tP) - 0.5;
    Floored_B = (int)floor(tP);
    Phase_Blend = abs(Phase_A) * 2.0;
}

void B2U_Solve_Phases_float(float Time, float Phase_Speed, out float Phase_A, out float Phase_B, out float Phase_Blend, out int Floored_A, out int Floored_B)
{
    B2USolvePhasesInternal(Time, Phase_Speed, Phase_A, Phase_B, Phase_Blend, Floored_A, Floored_B);
}

void B2U_Solve_Phases_half(float Time, float Phase_Speed, out float Phase_A, out float Phase_B, out float Phase_Blend, out int Floored_A, out int Floored_B)
{
    B2USolvePhasesInternal(Time, Phase_Speed, Phase_A, Phase_B, Phase_Blend, Floored_A, Floored_B);
}


// Blends the two phases smoothly to prevent peaking when phases swap
void B2UBlendPhasesInternal(bool Invert_Height, half Blend_Factor, half Height_A, half Height_B, half3 Gradient_A, half3 Gradient_B, half3 Normal_WS, out half Blended_Height, out half3 Voronoi_Gradient)
{
    float weight0 = cos((float)Blend_Factor * 1.5707963);
    float weight1 = sin((float)Blend_Factor * 1.5707963);

    half rawBlended = lerp(Height_A, Height_B, Blend_Factor);
    Blended_Height = Invert_Height ? (1.0 - rawBlended) : rawBlended;

    float3 blendedGradient = (float3)Gradient_A * weight0 + (float3)Gradient_B * weight1;
    float3 surfaceGrad = blendedGradient - ((float3)Normal_WS * dot(blendedGradient, (float3)Normal_WS));

    Voronoi_Gradient = (half3)(surfaceGrad * (Invert_Height ? -1.0 : 1.0));
}

void B2U_Blend_Phases_float(bool Invert_Height, half Blend_Factor, half Height_A, half Height_B, half3 Gradient_A, half3 Gradient_B, half3 Normal_WS, out half Blended_Height, out half3 Blended_Gradient)
{
    B2UBlendPhasesInternal(Invert_Height, Blend_Factor, Height_A, Height_B, Gradient_A, Gradient_B, Normal_WS, Blended_Height, Blended_Gradient);
}

void B2U_Blend_Phases_half(bool Invert_Height, half Blend_Factor, half Height_A, half Height_B, half3 Gradient_A, half3 Gradient_B, half3 Normal_WS, out half Blended_Height, out half3 Blended_Gradient)
{
    B2UBlendPhasesInternal(Invert_Height, Blend_Factor, Height_A, Height_B, Gradient_A, Gradient_B, Normal_WS, Blended_Height, Blended_Gradient);
}


// Transforms the 4D UV0 (XY: Tangent Flow, ZW: Upstream/Downstream Curvature) into a drag- and curvature-accounted world-space flow vector
void B2UFlowVectorsInternal(half4 Interp_Flow, half Absolute_Distance, half Viscosity, half3 Normal_Object,
    half Phase_A, half Phase_B, out half3 Flow_Dir_A, out half3 Flow_Dir_B, out half3 Unphased_Flow_World)
{
    float3 normOS = SafeNormalize((float3)Normal_Object);
    float3 n_blend = float3(-normOS.x, normOS.y, normOS.z);

    float3 t_blend;
    float3 b_blend;

    if (n_blend.z > -0.9999)
    {
        float a = 1.0 / (1.0 + n_blend.z);
        float b = -a * n_blend.x * n_blend.y;

        t_blend = float3(1.0 - a * n_blend.x * n_blend.x, b, -n_blend.x);
        b_blend = float3(b, 1.0 - a * n_blend.y * n_blend.y, -n_blend.y);
    }
    else
    {
        t_blend = float3(0.0, -1.0, 0.0);
        b_blend = float3(-1.0, 0.0, 0.0);
    }

    float3 t_unity = float3(-t_blend.x, t_blend.y, t_blend.z);
    float3 b_unity = float3(-b_blend.x, b_blend.y, b_blend.z);

    float dragRange = max((float)Viscosity * 5.0, 0.01);
    float dragFactor = smoothstep(0.0, dragRange, max((float)Absolute_Distance, 0.0));

    // 1. Unphased flow vector (computed once, used directly by B2USolveWaves)
    float3 flowObject = (((float)Interp_Flow.x * t_unity) + ((float)Interp_Flow.y * b_unity)) * dragFactor;
    float3 unphasedWS = mul(GetObjectToWorldMatrix(), float4(flowObject, 0.0)).xyz;
    float3 normWS = SafeNormalize(mul(GetObjectToWorldMatrix(), float4(normOS, 0.0)).xyz);
    Unphased_Flow_World = (half3)unphasedWS;

    float flowMag = length(unphasedWS);

    // 2. Curved Phase A
    float pA = (float)Phase_A;
    float kA = (pA >= 0.0) ? (float)Interp_Flow.z : (float)Interp_Flow.w;
    float thetaA = max(flowMag * abs(pA) * abs(kA), 0.0001);
    float thetaCA = min(thetaA, 1.5707963);
    float sinA, cosA;
    sincos(thetaCA, sinA, cosA);
    float normDistA = flowMag * ((pA >= 0.0) ? 1.0 : -1.0) * sign(kA) * (((1.0 - cosA) + (thetaA - thetaCA)) / thetaA);
    Flow_Dir_A = (half3)((unphasedWS * (sinA / thetaA)) + (normWS * normDistA));

    // 3. Curved Phase B
    float pB = (float)Phase_B;
    float kB = (pB >= 0.0) ? (float)Interp_Flow.z : (float)Interp_Flow.w;
    float thetaB = max(flowMag * abs(pB) * abs(kB), 0.0001);
    float thetaCB = min(thetaB, 1.5707963);
    float sinB, cosB;
    sincos(thetaCB, sinB, cosB);
    float normDistB = flowMag * ((pB >= 0.0) ? 1.0 : -1.0) * sign(kB) * (((1.0 - cosB) + (thetaB - thetaCB)) / thetaB);
    Flow_Dir_B = (half3)((unphasedWS * (sinB / thetaB)) + (normWS * normDistB));
}

void B2U_Flow_Vectors_float(half4 Interp_Flow, half Absolute_Distance, half Viscosity, half3 Normal_Object, half Phase_A, half Phase_B, out half3 Flow_A, out half3 Flow_B, out half3 Unphased_Flow)
{
    B2UFlowVectorsInternal(Interp_Flow, Absolute_Distance, Viscosity, Normal_Object, Phase_A, Phase_B, Flow_A, Flow_B, Unphased_Flow);
}

void B2U_Flow_Vectors_half(half4 Interp_Flow, half Absolute_Distance, half Viscosity, half3 Normal_Object, half Phase_A, half Phase_B, out half3 Flow_A, out half3 Flow_B, out half3 Unphased_Flow)
{
    B2UFlowVectorsInternal(Interp_Flow, Absolute_Distance, Viscosity, Normal_Object, Phase_A, Phase_B, Flow_A, Flow_B, Unphased_Flow);
}


// Generate a random Vector3 offset for phase swaps
void B2UPhaseOffsetInternal(float Phase_Time, float Amplitude, out float3 Offset)
{
    Offset = hash13(Phase_Time) * Amplitude;
}

void B2U_Phase_Offset_float(float Phase_Time, float Amplitude, out float3 Offset)
{
    B2UPhaseOffsetInternal(Phase_Time, Amplitude, Offset);
}

void B2U_Phase_Offset_half(float Phase_Time, float Amplitude, out float3 Offset)
{
    B2UPhaseOffsetInternal(Phase_Time, Amplitude, Offset);
}


// Solves dynamic waves with interference on the mesh
void B2USolveWavesInternal(float3 Pos_WS, half Mesh_ID, half Viscosity, half3 Flow, out half Wave_Height)
{
    uint rsuvMeshID = (uint)round(Mesh_ID);
    Wave_Height = 0.0;
    #if !defined(SHADERGRAPH_PREVIEW)
        [branch]
        if (rsuvMeshID != 0u)
        {
            int activeWaveCount = clamp((int)_B2U_Active_Waves, 0, 64);
            float visc = saturate((float)Viscosity);

            for (int i = 0; i < activeWaveCount; i++)
            {
                uint packedW = asuint(_B2U_Wave_Array[i].w);
                uint waveMeshID = packedW & 0x3Fu;

                [branch]
                if (waveMeshID == rsuvMeshID)
                {
                    float waveTime = f16tof32(packedW >> 16);
                    float amplitude = (((float)((packedW >> 6) & 0x3FFu)) / 1023.0) * 10.0;
                    float3 originWS = _B2U_Wave_Array[i].xyz + ((float3)Flow * waveTime);

                    float dist = distance(Pos_WS, originWS);

                    // Small waves travel moderately faster than heavy high-inertia waves, damped by viscosity
                    float normAmp = sqrt(saturate(amplitude * 0.1));
                    float waveSpeed = lerp(10.0, 3.8, normAmp) * lerp(1.0, 0.6, visc);
                    float wavelength = lerp(1.2, 12.0, normAmp);

                    float travelDist = waveTime * waveSpeed;
                    float u = (travelDist - dist) / wavelength;

                    // Only evaluate inside the single moving wavefront ring [travelDist - wavelength, travelDist]
                    if (u > 0.0 && u < 1.0)
                    {
                        float timeDecay = exp(-waveTime * max(visc, 0.15) * 1.8);
                        float decayRate = lerp(0.35, 0.04, normAmp);
                        float spatialDecay = 1.0 / (1.0 + (dist * dist * decayRate));

                        // C1-continuous single crest-trough wave packet (0 height and 0 slope at u=0 and u=1)
                        float wavePacket = sin(u * 6.2831853) * sin(u * 3.14159265) * 0.92;
                        Wave_Height -= (half)(wavePacket * amplitude * spatialDecay * timeDecay);
                    }
                }
            }
        }
    #endif
}

void B2U_Solve_Waves_float(float3 Pos_WS, half Mesh_ID, half Viscosity, half3 Flow, out half Wave_Height)
{
    B2USolveWavesInternal(Pos_WS, Mesh_ID, Viscosity, Flow, Wave_Height);
}

void B2U_Solve_Waves_half(float3 Pos_WS, half Mesh_ID, half Viscosity, half3 Flow, out half Wave_Height)
{
    B2USolveWavesInternal(Pos_WS, Mesh_ID, Viscosity, Flow, Wave_Height);
}


// Solves dynamic waves with interference and analytical normal derivatives
void B2USolveWavesAnalyticalInternal(float3 Pos_WS, half Mesh_ID, half Viscosity, half3 Flow, out half Wave_Height, out half3 Wave_Normal)
{
    uint rsuvMeshID = (uint)round(Mesh_ID);
    float2 waveGradient = float2(0.0, 0.0);
    Wave_Normal = half3(0.0, 0.0, 0.0);
    Wave_Height = 0.0;
    #if !defined(SHADERGRAPH_PREVIEW)
        [branch]
        if (rsuvMeshID != 0u)
        {
            int activeWaveCount = clamp((int)_B2U_Active_Waves, 0, 64);
            float visc = saturate((float)Viscosity);

            for (int i = 0; i < activeWaveCount; i++)
            {
                uint packedW = asuint(_B2U_Wave_Array[i].w);
                uint waveMeshID = packedW & 0x3Fu;

                [branch]
                if (waveMeshID == rsuvMeshID)
                {
                    float waveTime = f16tof32(packedW >> 16);
                    float amplitude = (((float)((packedW >> 6) & 0x3FFu)) / 1023.0) * 10.0;
                    float3 originWS = _B2U_Wave_Array[i].xyz + ((float3)Flow * waveTime);

                    float dist = distance(Pos_WS, originWS);

                    float normAmp = sqrt(saturate(amplitude * 0.1));
                    float waveSpeed = lerp(10.0, 3.8, normAmp) * lerp(1.0, 0.6, visc);
                    float wavelength = lerp(1.2, 12.0, normAmp);

                    float travelDist = waveTime * waveSpeed;
                    float u = (travelDist - dist) / wavelength;

                    if (u > 0.0 && u < 1.0)
                    {
                        float timeDecay = exp(-waveTime * max(visc, 0.15) * 1.8);
                        float decayRate = lerp(0.35, 0.04, normAmp);
                        float spatialDecay = 1.0 / (1.0 + (dist * dist * decayRate));

                        float sinPiU, cosPiU, sin2PiU, cos2PiU;
                        sincos(u * 3.14159265, sinPiU, cosPiU);
                        sincos(u * 6.2831853, sin2PiU, cos2PiU);

                        float envelope = amplitude * spatialDecay * timeDecay * 0.92;
                        Wave_Height -= (half)(sin2PiU * sinPiU * envelope);

                        float dPacket_du = 3.14159265 * (2.0 * cos2PiU * sinPiU + sin2PiU * cosPiU);
                        float waveDerivative = -dPacket_du * (envelope / wavelength);

                        float2 dir = (Pos_WS.xz - originWS.xz) / max(dist, 0.0001);
                        waveGradient += dir * waveDerivative;
                    }
                }
            }
        }
        Wave_Normal = half3(-waveGradient.x, 0.0, -waveGradient.y);
    #endif
}

void B2U_Solve_Waves_Analytical_float(float3 Pos_WS, half Mesh_ID, half Viscosity, half3 Flow, out half Wave_Height, out half3 Wave_Normal)
{
    B2USolveWavesAnalyticalInternal(Pos_WS, Mesh_ID, Viscosity, Flow, Wave_Height, Wave_Normal);
}

void B2U_Solve_Waves_Analytical_half(float3 Pos_WS, half Mesh_ID, half Viscosity, half3 Flow, out half Wave_Height, out half3 Wave_Normal)
{
    B2USolveWavesAnalyticalInternal(Pos_WS, Mesh_ID, Viscosity, Flow, Wave_Height, Wave_Normal);
}


// Solves intersections to match the B2U edge detection feature for solids.
void B2ULiquidIntersectionEdgeInternal(float4 rawScreenPos, float edgeSize, float edgeSensitivity, float3 edgeColor, float3 liquidColor, out float3 outColor, out float intersectionMask)
{
    #if !defined(SHADERGRAPH_PREVIEW)
        float waterDepth = max(rawScreenPos.w, 0.0001);
        float2 screenUV = saturate(rawScreenPos.xy / waterDepth);

        float2 offset = _ScreenSize.zw * edgeSize;

        #if defined(_B2U_EDGE_DETECTION_WORLD_SPACE)
            offset *= (10.0 / max(waterDepth, 2.0));
        #endif

        float2 uv0 = saturate(screenUV + float2(-offset.x, offset.y));
        float2 uv1 = saturate(screenUV + float2(offset.x, -offset.y));
        float2 uv2 = saturate(screenUV + float2(offset.x, offset.y));
        float2 uv3 = saturate(screenUV + float2(-offset.x, -offset.y));

        float d0 = LinearEyeDepth(SampleSceneDepth(uv0), _ZBufferParams);
        float d1 = LinearEyeDepth(SampleSceneDepth(uv1), _ZBufferParams);
        float d2 = LinearEyeDepth(SampleSceneDepth(uv2), _ZBufferParams);
        float d3 = LinearEyeDepth(SampleSceneDepth(uv3), _ZBufferParams);

        float minNeighborDepth = min(min(d0, d1), min(d2, d3));
        float isIntersection = step(minNeighborDepth, waterDepth);

        float sobelEdge = 0.0;

        #if defined(_B2U_EDGE_DETECTION_DEPTH)
            float diff1 = d0 - d1;
            float diff2 = d2 - d3;
            sobelEdge = step(edgeSensitivity, sqrt(diff1 * diff1 + diff2 * diff2));
        #else
            float3 c0 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv0, 0).rgb;
            float3 c1 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv1, 0).rgb;
            float3 c2 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv2, 0).rgb;
            float3 c3 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv3, 0).rgb;

            float3 cDiff1 = c0 - c1;
            float3 cDiff2 = c2 - c3;
            sobelEdge = step(0.001, dot(cDiff1, cDiff1) + dot(cDiff2, cDiff2));
        #endif

        float isSurfaceObject = step(minNeighborDepth, waterDepth + 0.05);
        sobelEdge *= isSurfaceObject;

        float borderMask = step(0.0, screenUV.x - offset.x) * step(screenUV.x + offset.x, 1.0) *
                           step(0.0, screenUV.y - offset.y) * step(screenUV.y + offset.y, 1.0);

        sobelEdge *= borderMask;

        intersectionMask = saturate(isIntersection + sobelEdge);
        outColor = lerp(liquidColor, edgeColor, intersectionMask);
    #else
        intersectionMask = 0.0;
        outColor = liquidColor;
    #endif
}

void B2U_Liquid_Intersection_Edge_float(float4 Screen_Pos_Raw, float Edge_Size, float Edge_Sensitivity, float3 Edge_Color, float3 Liquid_Color, out float3 Out_Color, out float Intersection_Mask)
{
    B2ULiquidIntersectionEdgeInternal(Screen_Pos_Raw, Edge_Size, Edge_Sensitivity, Edge_Color, Liquid_Color, Out_Color, Intersection_Mask);
}

void B2U_Liquid_Intersection_Edge_half(float4 Screen_Pos_Raw, float Edge_Size, float Edge_Sensitivity, float3 Edge_Color, float3 Liquid_Color, out float3 Out_Color, out float Intersection_Mask)
{
    B2ULiquidIntersectionEdgeInternal(Screen_Pos_Raw, Edge_Size, Edge_Sensitivity, Edge_Color, Liquid_Color, Out_Color, Intersection_Mask);
}


// Solves transparency, refraction, absorption, and projected caustics for liquids.
void B2USolveTransmissionRefractionInternal(bool Is_Front_Face, float4 Screen_Pos_Raw, half3 Normal_WS, half3 View_WS, float3 Pos_WS, half Heightmask,
    half Distortion_Strength, half Caustic_Strength, bool Is_Transmissive, bool Use_Caustics, bool Height_Based_Transmission, half Transmission, half Absorption, half Viscosity,
    half Noise_Scale, half IOR, half3 Liquid_Color, out half3 Scene_Color, out half Blend)
{
    Blend = 0.0;
    Scene_Color = half3(0.0, 0.0, 0.0);

    #if !defined(SHADERGRAPH_PREVIEW)
    [branch]
    if (Is_Transmissive)
    {
        float safeScreenW = max(Screen_Pos_Raw.w, 0.0001);
        float2 baseUV = saturate(Screen_Pos_Raw.xy / safeScreenW);
        float2 finalUV = baseUV;
        float finalDepth = 0.0;
        float initialDepthDiff = 0.0;
        float uvScaleMultiplier = 0.0;
        float safeIOR = max((float)IOR, 1.0);

        [branch]
        if (!Is_Front_Face)
        {
            Normal_WS = -Normal_WS;
        }

        float camDist = max(distance(_WorldSpaceCameraPos.xyz, Pos_WS), 1.0);

        #if defined(_B2U_VOLUMETRIC_REFRACTION) && !defined(_B2U_REFRACTION_QUALITY_LOW) && !defined(_B2U_REFRACTION_QUALITY_MEDIUM) && defined(_B2U_VREF_ENABLED)
            float4 backfaceData = SAMPLE_TEXTURE2D(_B2UVolumetricRefractionTexture, sampler_B2UVolumetricRefractionTexture, baseUV);
            float baseDepthEye = backfaceData.a;

            if (baseDepthEye > 0.0)
            {
                float volumetricThickness = max(baseDepthEye - safeScreenW, 0.0);
                initialDepthDiff = volumetricThickness;
                uvScaleMultiplier = volumetricThickness / safeScreenW;
            }
        #else
            float rawBaseDepth = SampleSceneDepth(baseUV);
            float baseDepthEye = LinearEyeDepth(rawBaseDepth, _ZBufferParams);

            initialDepthDiff = max(baseDepthEye - safeScreenW, 0.0);
            uvScaleMultiplier = 1.0 / camDist;
        #endif

        half3 normalVS = (half3)mul((float3x3)GetWorldToViewMatrix(), (float3)Normal_WS);
        half NdotV = saturate(dot(Normal_WS, (half3)SafeNormalize((float3)View_WS)));
        half edgeMask = saturate(NdotV * 4.0);

        [branch]
        if (edgeMask > 0.001 && Distortion_Strength > 0.0)
        {
            float shoreFade = saturate(initialDepthDiff * 2.0);
            float dynamicMultiplier = lerp(0.0, 0.5, (float)NdotV);

            float2 offsetUV = normalVS.xy * ((safeIOR + ((float)Heightmask * 0.1)) - 1.0) * (float)Distortion_Strength * dynamicMultiplier * (float)edgeMask * uvScaleMultiplier * shoreFade;
            offsetUV = clamp(offsetUV, -0.05, 0.05);

            float2 refractedUV = saturate(baseUV + offsetUV);

            #if defined(_B2U_VOLUMETRIC_REFRACTION) && !defined(_B2U_REFRACTION_QUALITY_LOW) && !defined(_B2U_REFRACTION_QUALITY_MEDIUM) && defined(_B2U_VREF_ENABLED)
                float4 refractedBackfaceData = SAMPLE_TEXTURE2D(_B2UVolumetricRefractionTexture, sampler_B2UVolumetricRefractionTexture, refractedUV);

                if (refractedBackfaceData.a <= 0.0)
                {
                    refractedBackfaceData = backfaceData;
                    refractedUV = baseUV;
                }

                half2 backfaceNormalVS_xy = refractedBackfaceData.rg;

                float exitThickness = max(refractedBackfaceData.a - safeScreenW, 0.0);
                float exitUVScale = exitThickness / safeScreenW;

                float2 exitAngularOffset = backfaceNormalVS_xy * ((1.0 / safeIOR) - 1.0) * (float)Distortion_Strength * 0.1 * exitUVScale * shoreFade;

                finalUV = saturate(refractedUV + exitAngularOffset);
                finalDepth = refractedBackfaceData.a;
            #else
                float rawRefractedDepth = SampleSceneDepth(refractedUV);
                float refractedDepthEye = LinearEyeDepth(rawRefractedDepth, _ZBufferParams);

                float depthDelta = abs(refractedDepthEye - baseDepthEye);

                float tearFade = 1.0;
                if (Is_Front_Face)
                {
                    tearFade = 1.0 - smoothstep(2.0, 5.0, depthDelta);
                }

                if (refractedDepthEye > safeScreenW + 1e-5 && tearFade > 0.0)
                {
                    finalUV = saturate(lerp(baseUV, refractedUV, tearFade));
                    finalDepth = lerp(baseDepthEye, refractedDepthEye, tearFade);
                }
            #endif
        }

        if (finalDepth <= 0.0)
        {
            finalDepth = baseDepthEye;
        }

        #if !defined(_B2U_REFRACTION_QUALITY_LOW) && defined(_B2U_VREF_ENABLED)
            #define B2U_SAMPLE_COLOR(uv) SAMPLE_TEXTURE2D(_B2UColorCopy, sampler_B2UColorCopy, uv).rgb
        #else
            #define B2U_SAMPLE_COLOR(uv) SampleSceneColor(uv)
        #endif

        half3 sampledBackground = half3(0.0, 0.0, 0.0);
        #if defined(_B2U_LIQUID_CHROMATIC_ABERRATION) || defined(_B2U_LIQUID_CHROMATIC_ABBERATION)
            float2 caSpread = (finalUV - baseUV) * 0.1 * safeIOR;
            half r = B2U_SAMPLE_COLOR(saturate(finalUV + caSpread)).r;
            half g = B2U_SAMPLE_COLOR(finalUV).g;
            half b = B2U_SAMPLE_COLOR(saturate(finalUV - caSpread)).b;
            sampledBackground = half3(r, g, b);
        #else
            sampledBackground = B2U_SAMPLE_COLOR(finalUV);
        #endif

        #undef B2U_SAMPLE_COLOR

        float depthDifference = 0.0;

        [branch]
        if (!Is_Front_Face)
        {
            depthDifference = safeScreenW;
        }
        else
        {
            depthDifference = max(finalDepth - safeScreenW, 0.0);

            #if !defined(_B2U_VOLUMETRIC_REFRACTION) || defined(_B2U_REFRACTION_QUALITY_LOW) || !defined(_B2U_VREF_ENABLED)
                depthDifference = min(depthDifference, (float)Absorption * 4.0);
            #endif
        }

        float safeAbsorption = max((float)Absorption, 0.001);
        float opticalDepth = depthDifference / safeAbsorption;

        half transmittance = (half)exp2(-opticalDepth);

        #if (defined(_B2U_CAUSTICS_MAIN_LIGHT) || defined(_B2U_CAUSTICS_ALL_LIGHTS)) && !defined(_B2U_VOLUMETRIC_REFRACTION)
        [branch]
        if (Caustic_Strength > 0.0 && Is_Front_Face && Use_Caustics)
        {
            // Reconstruct floor world position directly from the surface view ray and eye-depth ratio (avoids UNITY_MATRIX_I_VP Y-flip issues)
            float3 viewRayPerEyeDepth = (Pos_WS - _WorldSpaceCameraPos.xyz) / safeScreenW;
            float2 uvDelta = finalUV - baseUV;
            float3 refractOffsetWS = (UNITY_MATRIX_I_V._m00_m10_m20 * uvDelta.x + UNITY_MATRIX_I_V._m01_m11_m21 * uvDelta.y) * (finalDepth * 2.0);
            float3 floorPosWS = _WorldSpaceCameraPos.xyz + (viewRayPerEyeDepth * finalDepth) + refractOffsetWS;

            float floorDepthY = max(Pos_WS.y - floorPosWS.y, 0.0);

            [branch]
            if (floorDepthY > 0.0)
            {
                half3 totalCausticLight = half3(0.0, 0.0, 0.0);

                float spreadFactor = 1.0 + (floorDepthY * 0.25);
                float dynamicNoiseScale = ((float)Noise_Scale * 24.0) / spreadFactor;

                Light baseMain = GetMainLight();
                float4 shadowCoord = TransformWorldToShadowCoord(floorPosWS + baseMain.direction * 0.5);
                Light mainLight = GetMainLight(shadowCoord);

                float3 up = abs(mainLight.direction.y) > 0.999 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
                float3 right = SafeNormalize(cross(up, mainLight.direction));
                float3 forward = SafeNormalize(cross(mainLight.direction, right));

                float2 causticUV = float2(dot(floorPosWS, right), dot(floorPosWS, forward)) * dynamicNoiseScale;

                half3 mainCausticNoise = B2UProceduralCausticsInternal(causticUV, _Time.y, Viscosity, IOR);
                totalCausticLight += mainCausticNoise * mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                #if defined(_B2U_CAUSTICS_ALL_LIGHTS)
                    InputData inputData = (InputData)0;
                    float4 shadowClipPos = TransformWorldToHClip(floorPosWS);
                    float4 screenPos = ComputeScreenPos(shadowClipPos);
                    inputData.normalizedScreenSpaceUV = screenPos.xy / max(screenPos.w, 0.0001);
                    inputData.positionWS = floorPosWS;

                    uint pixelLightCount = GetAdditionalLightsCount();
                    uint lightLimit = 0u;

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        if (lightLimit < 4u)
                        {
                            Light addLight = GetAdditionalLight(lightIndex, floorPosWS, half4(1.0, 1.0, 1.0, 1.0));

                            float2 addCausticUV = (floorPosWS.xz * dynamicNoiseScale) + (addLight.direction.xz * floorDepthY);

                            half3 addCausticNoise = B2UProceduralCausticsInternal(addCausticUV, _Time.y, Viscosity, IOR);
                            totalCausticLight += addCausticNoise * addLight.color * (addLight.distanceAttenuation * addLight.shadowAttenuation);

                            lightLimit++;
                        }
                    LIGHT_LOOP_END
                #endif

                float shallowFade = smoothstep(0.0, 2.0, floorDepthY);
                float deepFade = saturate(exp2(-((floorDepthY / safeAbsorption) * 1.5)));

                totalCausticLight *= Caustic_Strength * (half)(shallowFade * deepFade);

                sampledBackground += totalCausticLight;
            }
        }
        #endif

        half3 backgroundLight = sampledBackground * transmittance;
        half3 scatteredLight = Liquid_Color * (1.0 - transmittance);

        Scene_Color = backgroundLight + scatteredLight;

        half baseAlpha = Transmission;
        [branch]
        if (Height_Based_Transmission)
        {
            baseAlpha *= (1.0 - Heightmask);
        }

        Blend = baseAlpha;
    }
    #endif
}

void B2U_Solve_Transmission_Refraction_float(bool Is_Front_Face, float4 Screen_Pos_Raw, half3 Normal_WS, half3 View_WS, float3 Pos_WS, half Heightmask,
    half Distortion_Strength, half Caustic_Strength, bool Is_Transmissive, bool Use_Caustics, bool Height_Based_Transmission, half Transmission, half Absorption, half Viscosity,
    half Noise_Scale, half IOR, half3 Liquid_Color, out half3 Scene_Color, out half Blend)
{
    B2USolveTransmissionRefractionInternal(Is_Front_Face, Screen_Pos_Raw, Normal_WS, View_WS, Pos_WS, Heightmask, Distortion_Strength, Caustic_Strength, Is_Transmissive, Use_Caustics, Height_Based_Transmission, Transmission, Absorption, Viscosity, Noise_Scale, IOR, Liquid_Color, Scene_Color, Blend);
}

void B2U_Solve_Transmission_Refraction_half(bool Is_Front_Face, float4 Screen_Pos_Raw, half3 Normal_WS, half3 View_WS, float3 Pos_WS, half Heightmask,
    half Distortion_Strength, half Caustic_Strength, bool Is_Transmissive, bool Use_Caustics, bool Height_Based_Transmission, half Transmission, half Absorption, half Viscosity,
    half Noise_Scale, half IOR, half3 Liquid_Color, out half3 Scene_Color, out half Blend)
{
    B2USolveTransmissionRefractionInternal(Is_Front_Face, Screen_Pos_Raw, Normal_WS, View_WS, Pos_WS, Heightmask, Distortion_Strength, Caustic_Strength, Is_Transmissive, Use_Caustics, Height_Based_Transmission, Transmission, Absorption, Viscosity, Noise_Scale, IOR, Liquid_Color, Scene_Color, Blend);
}

// --- TESSELLATED LIQUID FUNCTIONS ---

// Trimmed-down version of the LUT sampler that only grabs values needed for the tessellator's domain stage
void B2USampleLUTsLiquidTessDomain(UnityTexture2D Color_LUT, UnityTexture2D Property_LUT, UnitySamplerState Sampler, half Num_Subtypes,
    half Material_ID, half Material_ID_Row2, out half Viscosity, out half Edge_Distance, out half Wave_Height, out half Flow_Strength, out half Phase_Speed, out half Noise_Scale,
    out half Distortion_Scale, out half Distortion_Strength, out half Bubble_Speed, out half Noise_Power)
{
    float lutWidth = ((float)Num_Subtypes * 8.0) + 8.0;
    float emissionStartIndex = 8.0 * (float)Num_Subtypes;

    float2 cUV2 = float2(0.5 / lutWidth, (float)Material_ID_Row2);
    half4 c2 = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, cUV2, 0);
    Edge_Distance = UnpackLUT(c2.a, Liquid_r2_ca_MinMax);

    float2 eUV2 = float2((emissionStartIndex + 0.5) / lutWidth, (float)Material_ID_Row2);
    half4 e2 = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, eUV2, 0);
    Viscosity = UnpackLUT(e2.a, Liquid_r2_ea_MinMax);

    half4 r1_p1 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.125, (float)Material_ID), 0);
    Noise_Scale = UnpackLUT(r1_p1.r, Liquid_r1_p1_r_MinMax);
    Noise_Power = UnpackLUT(r1_p1.g, Liquid_r1_p1_g_MinMax);
    Distortion_Scale = UnpackLUT(r1_p1.b, Liquid_r1_p1_b_MinMax);
    Distortion_Strength = UnpackLUT(r1_p1.a, Liquid_r1_p1_a_MinMax);

    half4 r1_p2 = SAMPLE_TEXTURE2D_LOD(Property_LUT, Sampler, float2(0.375, (float)Material_ID), 0);
    Phase_Speed = UnpackLUT(r1_p2.r, Liquid_r1_p2_r_MinMax);
    Flow_Strength = UnpackLUT(r1_p2.g, Liquid_r1_p2_g_MinMax);
    Wave_Height = UnpackLUT(r1_p2.b, Liquid_r1_p2_b_MinMax);
    Bubble_Speed = UnpackLUT(r1_p2.a, Liquid_r1_p2_a_MinMax);
}

// Solves the surface mask and displacement in the tessellation domain stage (NaN-hardened for vertex positions).
void B2USolveSurfaceTessellated(UnityTexture2D Color_Emission_LUT, UnityTexture2D Property_LUT, float2 World_Size,
    half Num_Subtypes, float4 UV0, float2 UV1, half3 Normal_OS, UnityTexture3D Noise_3D, half Global_Bubble_Multiplier, half Global_Angle_Offset_Speed,
    half Global_Edge_Distance, half Global_Normal_Strength, float3 Pos_WS, float3 Pos_OS, half3 Normal_WS, out half Heightmask, out float3 Displaced_Pos, out half3 Gradient_WS,
    out half3 Flow_A, out half3 Flow_B)
{
    #if !defined(SHADERGRAPH_PREVIEW)
        half4 Interp_Flow = (half4)UV0;
        half Absolute_Distance = (half)UV1.x;
        uint packedV = (uint)round(UV1.y);
        half Material_ID = (half)(packedV & 0xFFu) / 255.0;
        bool Ignore_Gravity = ((packedV >> 12) & 1u) != 0u;
        bool Invert_Height = ((packedV >> 14) & 1u) != 0u;

        UnitySamplerState PointClamp;
        PointClamp.samplerstate = sampler_PointClamp;
        UnitySamplerState LinearRepeat;
        LinearRepeat.samplerstate = sampler_LinearRepeat;

        half Viscosity, Edge_Distance, Wave_Height, Flow_Strength, Phase_Speed, Noise_Scale, Distortion_Scale, Distortion_Strength, Bubble_Speed, Noise_Power;
        B2USampleLUTsLiquidTessDomain(Color_Emission_LUT, Property_LUT, PointClamp, Num_Subtypes, Material_ID, Material_ID + (1.0 / 255.0), Viscosity,
            Edge_Distance, Wave_Height, Flow_Strength, Phase_Speed, Noise_Scale, Distortion_Scale, Distortion_Strength, Bubble_Speed, Noise_Power);

        half Emission_Strength, Flow_Strength_RSUV, Wave_Multiplier, Mesh_ID;
        B2UUnpackRSUVLiquidInternal(Emission_Strength, Flow_Strength_RSUV, Wave_Multiplier, Mesh_ID);

        float Phase_A, Phase_B, Phase_Blend;
        int Floored_A, Floored_B;
        B2USolvePhasesInternal(_Time.y, (float)Phase_Speed, Phase_A, Phase_B, Phase_Blend, Floored_A, Floored_B);

        half3 Flow_Dir_World_A, Flow_Dir_World_B, Base_Flow_Dir_World;
        B2UFlowVectorsInternal(Interp_Flow, Absolute_Distance, Viscosity, Normal_OS, (half)Phase_A, (half)Phase_B, Flow_Dir_World_A, Flow_Dir_World_B, Base_Flow_Dir_World);


        #if !defined(_B2U_USE_PROCEDURAL_NOISE_LIQUID)
            Flow_Dir_World_A *= 0.3;
            Flow_Dir_World_B *= 0.3;
            Base_Flow_Dir_World *= 0.3;
        #endif

        float3 scaledFlowWS = (float3)Base_Flow_Dir_World * (float)Flow_Strength_RSUV;

        float3 domainDistortion = float3(0.0, 0.0, 0.0);
        [branch]
        if (Distortion_Strength > 0.0 && Distortion_Scale > 0.0)
        {
            #if defined(_B2U_USE_PROCEDURAL_NOISE_LIQUID)
                B2USimplex3DDirectionalInternal(Pos_WS, (float)Distortion_Scale, (float)Distortion_Strength, domainDistortion);
            #else
                domainDistortion = SAMPLE_TEXTURE3D_LOD(Noise_3D, LinearRepeat, Pos_WS * (float)Distortion_Scale, 0).rgb * (float)Distortion_Strength;
            #endif
        }

        float gravityFlowInfluence = 1.0;
        float gravityHeightInfluence = 1.0;

        [branch]
        if (!Ignore_Gravity)
        {
            float absNdotUp = abs(dot((float3)Normal_WS, float3(0.0, 1.0, 0.0)));
            gravityFlowInfluence = ((1.0 - absNdotUp) * 2.0) + 1.0;
            gravityHeightInfluence = (absNdotUp * 0.9) + 0.1;
        }

        #if defined(_B2U_USE_BUBBLE_ADVECTION)
            float bubbleSample1 = SAMPLE_TEXTURE3D_LOD(Noise_3D, LinearRepeat, Pos_WS * 0.32785, 0).r;
            float bubbleSample2 = SAMPLE_TEXTURE3D_LOD(Noise_3D, LinearRepeat, Pos_WS * 0.13427, 0).g;
            float bubbleMult = (float)Global_Bubble_Multiplier * (float)Bubble_Speed;
            float3 advectionOffset = (float3)Normal_WS * ((bubbleSample1 * bubbleSample2 * bubbleMult) + (bubbleMult * 10));
        #else
            float3 advectionOffset = float3(0.0, 0.0, 0.0);
        #endif

        #if defined(_B2U_ANGLE_OFFSET)
            float angleOffset = (float)Global_Angle_Offset_Speed * (float)Bubble_Speed * _Time.y;
        #else
            float angleOffset = 5.0;
        #endif

        float totalFlowMult = (float)Flow_Strength_RSUV * (float)Flow_Strength * gravityFlowInfluence;
        Flow_A = (half3)((float3)Flow_Dir_World_A * totalFlowMult * Phase_A);
        Flow_B = (half3)((float3)Flow_Dir_World_B * totalFlowMult * Phase_B);

        float waveMult = (float)Wave_Multiplier * (float)Wave_Height * gravityHeightInfluence;
        float safeNoisePower = max((float)Noise_Power, 0.0001);

        #if defined(_B2U_USE_PROCEDURAL_NOISE_LIQUID)
            float3 offsetA, offsetB;
            B2UPhaseOffsetInternal((float)Floored_A, 134.56, offsetA);
            B2UPhaseOffsetInternal((float)Floored_B, 134.56, offsetB);

            float3 noiseSamplePosA = ((float3)Flow_A - Pos_WS) + domainDistortion + offsetA + (advectionOffset * Phase_A);
            float3 noiseSamplePosB = ((float3)Flow_B - Pos_WS) + domainDistortion + offsetB + (advectionOffset * Phase_B);

            float3 gradientA, gradientB;
            float noiseSampleA, noiseSampleB;
            B2UVoronoi3DAnalyticalInternal(noiseSamplePosA, (float)Noise_Scale * 4.0, 1.0, true, angleOffset, gradientA, noiseSampleA);
            B2UVoronoi3DAnalyticalInternal(noiseSamplePosB, (float)Noise_Scale * 4.0, 1.0, true, angleOffset, gradientB, noiseSampleB);

            half blendedHeight;
            half3 blendedGradient;
            B2UBlendPhasesInternal(Invert_Height, (half)Phase_Blend, (half)noiseSampleA, (half)noiseSampleB, (half3)gradientA, (half3)gradientB, Normal_WS, blendedHeight, blendedGradient);
            float solvedHeight = pow(max(saturate((float)blendedHeight), 0.00001), safeNoisePower);

            half dynamicWaveHeight;
            half3 waveGradient;
            B2USolveWavesAnalyticalInternal(Pos_WS, Mesh_ID, Viscosity, (half3)scaledFlowWS, dynamicWaveHeight, waveGradient);
        #else
            float3 offsetA, offsetB;
            B2UPhaseOffsetInternal((float)Floored_A, 134.56, offsetA);
            B2UPhaseOffsetInternal((float)Floored_B, 134.56, offsetB);

            float3 scaledPosWS = Pos_WS * (float)Noise_Scale * 1.15;

            float3 noiseSamplePosA = ((float3)Flow_A - scaledPosWS) + domainDistortion + offsetA + advectionOffset;
            float3 noiseSamplePosB = ((float3)Flow_B - scaledPosWS) + domainDistortion + offsetB + advectionOffset;

            float noiseSampleA = SAMPLE_TEXTURE3D_LOD(Noise_3D, LinearRepeat, noiseSamplePosA, 0).a;
            float noiseSampleB = SAMPLE_TEXTURE3D_LOD(Noise_3D, LinearRepeat, noiseSamplePosB, 0).a;

            half blendedHeight;
            half3 doesNotUse;
            B2UBlendPhasesInternal(Invert_Height, (half)Phase_Blend, (half)noiseSampleA, (half)noiseSampleB, half3(0.0, 0.0, 0.0), half3(0.0, 0.0, 0.0), Normal_WS, blendedHeight, doesNotUse);
            float solvedHeight = pow(max(saturate((float)blendedHeight), 0.00001), safeNoisePower);

            half dynamicWaveHeight;
            B2USolveWavesInternal(Pos_WS, Mesh_ID, Viscosity, (half3)scaledFlowWS, dynamicWaveHeight);
        #endif

        solvedHeight *= waveMult;

        float maxShoreDist = max((float)Edge_Distance * (float)Global_Edge_Distance, 0.0001);
        float edgeMask = saturate(1.0 - smoothstep(0.0, maxShoreDist, (float)Absolute_Distance));
        float shoreExp = 1.0 / max(Remap((float)Viscosity, float2(0.0, 1.0), float2(0.2, 5.0)), 0.001);
        float shoreMask = edgeMask > 0.00001 ? pow(edgeMask, shoreExp) : 0.0;

        Heightmask = (half)saturate(solvedHeight + shoreMask + (float)dynamicWaveHeight);

        #if defined(_B2U_LIGHT_MODEL_TOON)
            float safeToonSteps = max(_B2U_Light_Control_Float, 1.0);
            Heightmask = (half)(round((float)Heightmask * safeToonSteps) / safeToonSteps);
        #endif

        #if defined(_B2U_USE_DISPLACEMENT)
            bool Use_Displacement = ((packedV >> 10) & 1u) != 0u;
            Displaced_Pos = Use_Displacement ? (((solvedHeight + (float)dynamicWaveHeight) * (float3)Normal_OS) + Pos_OS) : Pos_OS;
        #else
            Displaced_Pos = Pos_OS;
        #endif

        #if defined(_B2U_USE_PROCEDURAL_NOISE_LIQUID)
            #if defined(_B2U_USE_DISPLACEMENT)
                float normStrength = waveMult;
            #else
                float normStrength = (float)Global_Normal_Strength;
            #endif
            float3 totalGrad = ((float3)blendedGradient + (float3)waveGradient) * normStrength;
            Gradient_WS = (half3)SafeNormalize((float3)Normal_WS + totalGrad);
        #else
            Gradient_WS = Normal_WS;
        #endif
    #else
        Heightmask = 0.0;
        Gradient_WS = half3(0.0, 0.0, 0.0);
        Displaced_Pos = Pos_OS;
        Flow_A = half3(0.0, 0.0, 0.0);
        Flow_B = half3(0.0, 0.0, 0.0);
    #endif
}

// --- END TESSELLATED LIQUID FUNCTIONS ---

// --- END LIQUID NODES ---

#endif