#ifndef B2U_CRYSTAL_INCLUDED
#define B2U_CRYSTAL_INCLUDED

#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Core.hlsl"

// --- CRYSTAL NODES ---

#if !defined(_B2U_REFRACTION_QUALITY_LOW) && !defined(B2U_VOLUMETRIC_REF_DECLARED) && defined(_B2U_VREF_ENABLED)
    #define B2U_VOLUMETRIC_REF_DECLARED
    TEXTURE2D(_B2UVolumetricRefractionTexture); SAMPLER(sampler_B2UVolumetricRefractionTexture);
#endif

// NaN-safe integer power helpers (avoids log2(0) -> -Inf/NaN and replaces transcendentals with multiplies)
float B2UPow4(float x)
{
    float x2 = x * x;
    return x2 * x2;
}

float B2UPow5(float x)
{
    float x2 = x * x;
    return x2 * x2 * x;
}

float B2UPow128(float x)
{
    x *= x; // ^2
    x *= x; // ^4
    x *= x; // ^8
    x *= x; // ^16
    x *= x; // ^32
    x *= x; // ^64
    return x * x; // ^128
}

// Unpacks the crystal RSUV values.
void B2UUnpackRSUVCrystalInternal(out half Emission_Strength, out half Absorption, out half Transmission, out half Crack_Intensity)
{
    #if defined(SHADERGRAPH_PREVIEW)
        uint rsuv = 0u;
    #else
        uint rsuv = unity_RendererUserValue;
    #endif

    half invEmis = saturate(1.0 - ((half)(rsuv & 0xFFu) / 255.0));
    Emission_Strength = invEmis * invEmis * invEmis;

    Transmission = saturate(1.0 - ((half)((rsuv >> 8) & 0xFFu) / 255.0));
    Absorption = saturate(1.0 - ((half)((rsuv >> 16) & 0xFFu) / 255.0));
    Crack_Intensity = (half)((rsuv >> 24) & 0xFFu) / 255.0;
}

void B2U_Unpack_RSUV_Crystal_float(out half Emission_Strength, out half Absorption, out half Transmission, out half Crack_Intensity)
{
    B2UUnpackRSUVCrystalInternal(Emission_Strength, Absorption, Transmission, Crack_Intensity);
}

void B2U_Unpack_RSUV_Crystal_half(out half Emission_Strength, out half Absorption, out half Transmission, out half Crack_Intensity)
{
    B2UUnpackRSUVCrystalInternal(Emission_Strength, Absorption, Transmission, Crack_Intensity);
}


// Samples the LUTs for crystals.
void B2USampleLUTsCrystalInternal(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    UnitySamplerState LinearClamp, UnityTexture2D Property_LUT, UnitySamplerState PointClamp, half Num_Subtypes, half Material_ID, out half Metallic, out half Smoothness,
    out half Emission_Mode, out half Emission_Strength, out half Emission_Speed, out half IOR, out half Transmission, out half Prismatic_Shift, out half Prismatic_Power,
    out half3 Blended_Base_Color, out half3 Blended_Secondary_Color)
{
    half4 p1 = SAMPLE_TEXTURE2D_LOD(Property_LUT, PointClamp, float2(0.125, (float)Material_ID), 0);
    Emission_Strength = UnpackLUT(p1.r, Crystal_r1_p1_r_MinMax);
    Emission_Speed = UnpackLUT(p1.g, Crystal_r1_p1_g_MinMax);
    IOR = UnpackLUT(p1.b, Crystal_r1_p1_b_MinMax);
    Transmission = UnpackLUT(p1.a, Crystal_r1_p1_a_MinMax);

    half4 p4 = SAMPLE_TEXTURE2D_LOD(Property_LUT, PointClamp, float2(0.875, (float)Material_ID), 0);
    bool Is_Biome_Responsive = p4.r >= 0.45;
    Prismatic_Shift = UnpackLUT(p4.g, Crystal_r1_p4_g_MinMax);
    Prismatic_Power = UnpackLUT(p4.b, Crystal_r1_p4_b_MinMax);
    Emission_Mode = round(UnpackLUT(p4.a, Crystal_r1_p4_a_MinMax));

    half packedMetallic;
    half packedSmoothness;
    SampleColorLUTRow(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, 0.0, LinearClamp, Num_Subtypes, Material_ID, Is_Biome_Responsive, packedMetallic, packedSmoothness, Blended_Base_Color, Blended_Secondary_Color);
    Metallic = UnpackLUT(packedMetallic, Crystal_r1_ca_MinMax);
    Smoothness = UnpackLUT(packedSmoothness, Crystal_r1_ea_MinMax);
}

void B2U_Sample_LUTs_Crystal_float(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    UnitySamplerState LinearClamp, UnityTexture2D Property_LUT, UnitySamplerState PointClamp, half Num_Subtypes, half Material_ID, out half Metallic, out half Smoothness,
    out half Emission_Mode, out half Emission_Strength, out half Emission_Speed, out half IOR, out half Transmission, out half Prismatic_Shift, out half Prismatic_Power,
    out half3 Blended_Base_Color, out half3 Blended_Secondary_Color)
{
    B2USampleLUTsCrystalInternal(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, LinearClamp, Property_LUT, PointClamp, Num_Subtypes, Material_ID, Metallic, Smoothness, Emission_Mode, Emission_Strength, Emission_Speed, IOR, Transmission, Prismatic_Shift, Prismatic_Power, Blended_Base_Color, Blended_Secondary_Color);
}

void B2U_Sample_LUTs_Crystal_half(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    UnitySamplerState LinearClamp, UnityTexture2D Property_LUT, UnitySamplerState PointClamp, half Num_Subtypes, half Material_ID, out half Metallic, out half Smoothness,
    out half Emission_Mode, out half Emission_Strength, out half Emission_Speed, out half IOR, out half Transmission, out half Prismatic_Shift, out half Prismatic_Power,
    out half3 Blended_Base_Color, out half3 Blended_Secondary_Color)
{
    B2USampleLUTsCrystalInternal(Biome_Tex_1, Biome_Tex_2, World_Position, World_Size, Color_LUT, LinearClamp, Property_LUT, PointClamp, Num_Subtypes, Material_ID, Metallic, Smoothness, Emission_Mode, Emission_Strength, Emission_Speed, IOR, Transmission, Prismatic_Shift, Prismatic_Power, Blended_Base_Color, Blended_Secondary_Color);
}


// Solves transmission, volumetric dual-surface refraction, internal reflections, and light flares for crystals.
void B2USolveTransmissionRefractionCrystalInternal(float4 Screen_Pos_Raw, half3 Normal_WS, half3 View_WS, float3 Pos_WS, bool Is_Transmissive, half Transmission, half Absorption,
    half IOR, half3 Crystal_Color, float Crack_Scale, half Crack_Intensity, out half3 Scene_Color, out half Blend)
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
        float uvScaleMultiplier = 0.0;
        float TIR_Blend = 0.0;
        float safeIOR = max((float)IOR, 1.0);

        float camDist = max(distance(_WorldSpaceCameraPos.xyz, Pos_WS), 1.0);

        #if defined(_B2U_VOLUMETRIC_REFRACTION) && !defined(_B2U_REFRACTION_QUALITY_LOW) && defined(_B2U_VREF_ENABLED)
            float4 backfaceData = SAMPLE_TEXTURE2D(_B2UVolumetricRefractionTexture, sampler_B2UVolumetricRefractionTexture, baseUV);
            float baseDepthEye = backfaceData.a;

            if (baseDepthEye > 0.0)
            {
                float volumetricThickness = max(baseDepthEye - safeScreenW, 0.0);
                uvScaleMultiplier = volumetricThickness / safeScreenW;
            }
        #else
            float rawBaseDepth = SampleSceneDepth(baseUV);
            float baseDepthEye = LinearEyeDepth(rawBaseDepth, _ZBufferParams);
            uvScaleMultiplier = 1.0 / camDist;
        #endif

        half3 normalVS = (half3)SafeNormalize(mul((float3x3)GetWorldToViewMatrix(), (float3)Normal_WS));

        float3 objectCenterWS = mul(GetObjectToWorldMatrix(), float4(0.0, 0.0, 0.0, 1.0)).xyz;
        float3 uniformViewWS = SafeNormalize(_WorldSpaceCameraPos.xyz - objectCenterWS);
        half3 uniformViewVS = (half3)SafeNormalize(mul((float3x3)GetWorldToViewMatrix(), uniformViewWS));

        half3 uniformInternalRayVS = refract(-uniformViewVS, normalVS, (half)(1.0 / safeIOR));

        float2 offsetUV = normalVS.xy * (safeIOR - 1.0) * 0.1 * uvScaleMultiplier;
        offsetUV = clamp(offsetUV, -0.05, 0.05);
        float2 refractedUV = saturate(baseUV + offsetUV);

        half3 backfaceNormalVS = -normalVS;

        #if defined(_B2U_VOLUMETRIC_REFRACTION) && !defined(_B2U_REFRACTION_QUALITY_LOW) && defined(_B2U_VREF_ENABLED)
            float4 refractedBackfaceData = SAMPLE_TEXTURE2D(_B2UVolumetricRefractionTexture, sampler_B2UVolumetricRefractionTexture, refractedUV);

            // If refractedUV stepped outside the crystal silhouette, fall back to the unrefracted backface sample
            if (refractedBackfaceData.a <= 0.0)
            {
                refractedBackfaceData = backfaceData;
                refractedUV = baseUV;
            }

            if (refractedBackfaceData.a > 0.0)
            {
                half2 backfaceNormalVS_xy = refractedBackfaceData.rg;
                half bfN_z = sqrt(saturate(1.0 - dot(backfaceNormalVS_xy, backfaceNormalVS_xy)));
                backfaceNormalVS = (half3)SafeNormalize(float3(backfaceNormalVS_xy, bfN_z));
            }
        #endif

        [branch]
        if (Crack_Intensity > 0.0)
        {
            float3 cellHashOS;
            float3 Pos_OS = mul(GetWorldToObjectMatrix(), float4(Pos_WS, 1.0)).xyz;
            B2UCrackCellHash3DInternal(Pos_OS * Crack_Scale, cellHashOS);

            float cellRandom = frac(sin(dot(cellHashOS, float3(12.9898, 78.233, 37.719))) * 43758.5453123);
            float crackVisibility = smoothstep(1.0 - (float)Crack_Intensity, 1.05 - (float)Crack_Intensity, cellRandom);

            float3 cellHashWS = mul((float3x3)GetObjectToWorldMatrix(), cellHashOS);
            half3 cellHashVS = (half3)mul((float3x3)GetWorldToViewMatrix(), cellHashWS);

            float fixedCrackDepth = 0.5;

            backfaceNormalVS = (half3)SafeNormalize((float3)backfaceNormalVS + ((float3)cellHashVS * (crackVisibility * fixedCrackDepth)));
            refractedUV = saturate(refractedUV + cellHashVS.xy * (crackVisibility * fixedCrackDepth * 0.05));
        }

        float cosThetaI = saturate(abs(dot((float3)uniformInternalRayVS, (float3)backfaceNormalVS)));

        float sinThetaI2 = 1.0 - (cosThetaI * cosThetaI);
        float sinThetaT2 = (safeIOR * safeIOR) * sinThetaI2;
        float TIR_Mask = smoothstep(0.85, 1.0, sinThetaT2);

        float r0 = (1.0 - safeIOR) / (1.0 + safeIOR);
        r0 *= r0;
        float internalFresnel = r0 + (1.0 - r0) * B2UPow5(abs(1.0 - cosThetaI));

        TIR_Blend = saturate(TIR_Mask + internalFresnel);

        #if defined(_B2U_VOLUMETRIC_REFRACTION) && !defined(_B2U_REFRACTION_QUALITY_LOW) && defined(_B2U_VREF_ENABLED)
            float exitThickness = max(refractedBackfaceData.a - safeScreenW, 0.0);
            float exitUVScale = exitThickness / safeScreenW;

            float2 exitAngularOffset = backfaceNormalVS.xy * ((1.0 / safeIOR) - 1.0) * 0.1 * exitUVScale;

            finalUV = saturate(refractedUV + lerp(exitAngularOffset, exitAngularOffset * 4.0, TIR_Blend));
            finalDepth = refractedBackfaceData.a;
        #else
            float rawRefractedDepth = SampleSceneDepth(refractedUV);
            float refractedDepthEye = LinearEyeDepth(rawRefractedDepth, _ZBufferParams);

            float depthDelta = abs(refractedDepthEye - baseDepthEye);
            float tearFade = 1.0 - smoothstep(2.0, 5.0, depthDelta);

            if (refractedDepthEye > safeScreenW + 1e-5 && tearFade > 0.0)
            {
                finalUV = saturate(lerp(baseUV, refractedUV, tearFade));
                finalDepth = lerp(baseDepthEye, refractedDepthEye, tearFade);
            }
        #endif

        if (finalDepth <= 0.0) finalDepth = baseDepthEye;

        float2 caSpread = (finalUV - baseUV) * 0.05 * safeIOR;
        half r = SampleSceneColor(saturate(finalUV + caSpread)).r;
        half g = SampleSceneColor(finalUV).g;
        half b = SampleSceneColor(saturate(finalUV - caSpread)).b;
        half3 sampledBackground = half3(r, g, b);

        float depthDifference = max(finalDepth - safeScreenW, 0.0);

        #if !defined(_B2U_VOLUMETRIC_REFRACTION) || defined(_B2U_REFRACTION_QUALITY_LOW) || !defined(_B2U_VREF_ENABLED)
            depthDifference = min(depthDifference, 0.5);
        #endif

        float safeAbsorption = saturate((float)Absorption);
        float density = safeAbsorption * safeAbsorption * 25.0;
        float opticalDepth = depthDifference * density;

        half transmittance = (half)exp2(-opticalDepth);

        float solidOverride = B2UPow4(safeAbsorption);
        transmittance = (half)lerp((float)transmittance, 0.0, solidOverride);

        half3 transmissionTint = lerp(Crystal_Color, half3(1.0, 1.0, 1.0), transmittance);
        half3 backgroundLight = sampledBackground * transmissionTint;

        half3 internalReflections = transmissionTint * (half)(TIR_Blend * 0.5);
        half3 totalFlareLight = half3(0.0, 0.0, 0.0);

        #if defined(_B2U_CRYSTAL_FLARES_MAIN_LIGHT) || defined(_B2U_CRYSTAL_FLARES_ALL_LIGHTS)
            float4 shadowCoord = TransformWorldToShadowCoord(Pos_WS);
            Light mainLight = GetMainLight(shadowCoord);

            half3 mainLightDirVS = (half3)SafeNormalize(mul((float3x3)GetWorldToViewMatrix(), (float3)mainLight.direction));
            half3 mainInternalLightRayVS = refract(-mainLightDirVS, normalVS, (half)(1.0 / safeIOR));
            half3 mainBouncedLightRayVS = reflect(mainInternalLightRayVS, backfaceNormalVS);

            float mainFlare = B2UPow128(saturate(dot((float3)mainBouncedLightRayVS, -(float3)uniformViewVS)));
            totalFlareLight += (half)mainFlare * mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);

            #if defined(_B2U_CRYSTAL_FLARES_ALL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = Pos_WS;
                inputData.normalizedScreenSpaceUV = baseUV;

                uint pixelLightCount = GetAdditionalLightsCount();
                uint lightLimit = 0u;

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    if (lightLimit < 4u)
                    {
                        Light addLight = GetAdditionalLight(lightIndex, Pos_WS, half4(1.0, 1.0, 1.0, 1.0));

                        half3 addLightDirVS = (half3)SafeNormalize(mul((float3x3)GetWorldToViewMatrix(), (float3)addLight.direction));
                        half3 addInternalLightRayVS = refract(-addLightDirVS, normalVS, (half)(1.0 / safeIOR));
                        half3 addBouncedLightRayVS = reflect(addInternalLightRayVS, backfaceNormalVS);

                        float addFlare = B2UPow128(saturate(dot((float3)addBouncedLightRayVS, -(float3)uniformViewVS)));
                        totalFlareLight += (half)addFlare * addLight.color * (addLight.distanceAttenuation * addLight.shadowAttenuation);

                        lightLimit++;
                    }
                LIGHT_LOOP_END
            #endif
        #else
            half3 fallbackBouncedRayVS = reflect(uniformInternalRayVS, backfaceNormalVS);
            float fallbackFlareMask = B2UPow128(saturate(dot((float3)fallbackBouncedRayVS, -(float3)uniformViewVS)));
            totalFlareLight = half3(1.0, 1.0, 1.0) * (half)fallbackFlareMask;
        #endif

        Scene_Color = backgroundLight + internalReflections + saturate(totalFlareLight);
        Blend = Transmission;
    }
    #endif
}

void B2U_Solve_Transmission_Refraction_Crystal_float(float4 Screen_Pos_Raw, half3 Normal_WS, half3 View_WS, float3 Pos_WS, bool Is_Transmissive, half Transmission, half Absorption,
    half IOR, half3 Crystal_Color, float Crack_Scale, half Crack_Intensity, out half3 Scene_Color, out half Blend)
{
    B2USolveTransmissionRefractionCrystalInternal(Screen_Pos_Raw, Normal_WS, View_WS, Pos_WS, Is_Transmissive, Transmission, Absorption, IOR, Crystal_Color, Crack_Scale, Crack_Intensity, Scene_Color, Blend);
}

void B2U_Solve_Transmission_Refraction_Crystal_half(float4 Screen_Pos_Raw, half3 Normal_WS, half3 View_WS, float3 Pos_WS, bool Is_Transmissive, half Transmission, half Absorption,
    half IOR, half3 Crystal_Color, float Crack_Scale, half Crack_Intensity, out half3 Scene_Color, out half Blend)
{
    B2USolveTransmissionRefractionCrystalInternal(Screen_Pos_Raw, Normal_WS, View_WS, Pos_WS, Is_Transmissive, Transmission, Absorption, IOR, Crystal_Color, Crack_Scale, Crack_Intensity, Scene_Color, Blend);
}


// Solves spectral dispersion/prismatic color shifting for crystals based on viewing angle.
void B2UPrismaticDispersionInternal(half3 In_Color, half3 Normal_WS, half IOR, half Prismatic_Shift, half Prismatic_Power, out half3 Out_Color)
{
    float3 N = SafeNormalize((float3)Normal_WS);

    float3 objectCenterWS = mul(GetObjectToWorldMatrix(), float4(0.0, 0.0, 0.0, 1.0)).xyz;
    float3 V = SafeNormalize(_WorldSpaceCameraPos.xyz - objectCenterWS);

    float NdotV = max(abs(dot(N, V)), 0.0001);
    float rim = 1.0 - NdotV;

    float frequency = (float)IOR * 2.0;
    float phase = rim * frequency;

    float3 spectralColor = 0.5 + 0.5 * cos(6.2831853 * (phase - float3(0.0, 0.333, 0.667)));
    spectralColor = saturate(spectralColor);
    spectralColor *= spectralColor;
    spectralColor *= spectralColor; // ^4

    float mask = pow(max(saturate(rim), 0.00001), max((float)Prismatic_Power, 0.001));

    Out_Color = In_Color + ((half3)spectralColor * (half)(mask * (float)Prismatic_Shift * 2.5));
}

void B2U_Prismatic_Dispersion_float(half3 In_Color, half3 Normal_WS, half IOR, half Prismatic_Shift, half Prismatic_Power, out half3 Out_Color)
{
    B2UPrismaticDispersionInternal(In_Color, Normal_WS, IOR, Prismatic_Shift, Prismatic_Power, Out_Color);
}

void B2U_Prismatic_Dispersion_half(half3 In_Color, half3 Normal_WS, half IOR, half Prismatic_Shift, half Prismatic_Power, out half3 Out_Color)
{
    B2UPrismaticDispersionInternal(In_Color, Normal_WS, IOR, Prismatic_Shift, Prismatic_Power, Out_Color);
}


// Samples the emission map for crystals.
void B2USampleEmissionMapCrystalInternal(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, half Emission_Mode, bool Is_Emissive, half Emission_Strength,
    float Global_Strength_Mult, half Emission_Speed, float Global_Speed_Mult, float2 UV_X, float2 UV_Y, float2 UV_Z, half3 Blend_Weights, half Vertex_Green, out half Emission)
{
    Emission = 0.0;
    [branch]
    if (Is_Emissive)
    {
        Emission = SampleEmissionMap(UV_X, UV_Y, UV_Z, Emission_Mode, Blend_Weights, Emission_Strength, (half)Global_Strength_Mult, Emission_Speed, (half)Global_Speed_Mult, Emission_Patterns, Sampler);
        Emission *= Vertex_Green;
    }
}

void B2U_Sample_Emission_Map_Crystal_float(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, half Emission_Mode, bool Is_Emissive, half Emission_Strength,
    float Global_Strength_Mult, half Emission_Speed, float Global_Speed_Mult, float2 UV_X, float2 UV_Y, float2 UV_Z, half3 Blend_Weights, half Vertex_Green, out half Emission)
{
    B2USampleEmissionMapCrystalInternal(Emission_Patterns, Sampler, Emission_Mode, Is_Emissive, Emission_Strength, Global_Strength_Mult, Emission_Speed, Global_Speed_Mult, UV_X, UV_Y, UV_Z, Blend_Weights, Vertex_Green, Emission);
}

void B2U_Sample_Emission_Map_Crystal_half(UnityTexture2D Emission_Patterns, UnitySamplerState Sampler, half Emission_Mode, bool Is_Emissive, half Emission_Strength,
    float Global_Strength_Mult, half Emission_Speed, float Global_Speed_Mult, float2 UV_X, float2 UV_Y, float2 UV_Z, half3 Blend_Weights, half Vertex_Green, out half Emission)
{
    B2USampleEmissionMapCrystalInternal(Emission_Patterns, Sampler, Emission_Mode, Is_Emissive, Emission_Strength, Global_Strength_Mult, Emission_Speed, Global_Speed_Mult, UV_X, UV_Y, UV_Z, Blend_Weights, Vertex_Green, Emission);
}

// --- END CRYSTAL NODES ---

#endif