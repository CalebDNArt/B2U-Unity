#ifndef B2U_FULLSCREEN_INCLUDED
#define B2U_FULLSCREEN_INCLUDED

#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Core.hlsl"

// Only declare the global edge buffer texture if we are NOT in depth mode
#if !defined(_B2U_EDGE_DETECTION_DEPTH) && !defined(B2U_EDGE_BUFFER_DECLARED) && !defined(SHADERGRAPH_PREVIEW)
    #define B2U_EDGE_BUFFER_DECLARED
    TEXTURE2D(_B2UEdgeBuffer);
    SAMPLER(sampler_B2UEdgeBuffer);
#endif

// Uses either the edge or depth buffers to solve where edges exist on solids.
void B2UEdgeDetectionInternal(float2 uv, float edgeSize, float edgeSensitivity, float3 edgeColor, out float3 outColor, out float edgeAlpha)
{
    float2 texelSize = _ScreenSize.zw;
    float2 offset = texelSize * edgeSize;

    #if defined(_B2U_EDGE_DETECTION_WORLD_SPACE)
        #if defined(SHADERGRAPH_PREVIEW)
            float centerDepth = 5.0;
        #else
            float rawDepth = SampleSceneDepth(uv);
            float centerDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        #endif
        offset *= (10.0 / max(centerDepth, 2.0));
    #endif

    float2 uv0 = saturate(uv + float2(-offset.x, offset.y));
    float2 uv1 = saturate(uv + float2(offset.x, -offset.y));
    float2 uv2 = saturate(uv + float2(offset.x, offset.y));
    float2 uv3 = saturate(uv + float2(-offset.x, -offset.y));

    float edgeOutput = 0.0;

    #if defined(_B2U_EDGE_DETECTION_DEPTH)
        #if !defined(SHADERGRAPH_PREVIEW)
            float d0 = LinearEyeDepth(SampleSceneDepth(uv0), _ZBufferParams);
            float d1 = LinearEyeDepth(SampleSceneDepth(uv1), _ZBufferParams);
            float d2 = LinearEyeDepth(SampleSceneDepth(uv2), _ZBufferParams);
            float d3 = LinearEyeDepth(SampleSceneDepth(uv3), _ZBufferParams);

            float diff1 = d0 - d1;
            float diff2 = d2 - d3;

            float edge = sqrt(diff1 * diff1 + diff2 * diff2);
            edgeOutput = step(edgeSensitivity, edge);
        #endif
    #else
        #if !defined(SHADERGRAPH_PREVIEW)
            float3 c0 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv0, 0).rgb;
            float3 c1 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv1, 0).rgb;
            float3 c2 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv2, 0).rgb;
            float3 c3 = SAMPLE_TEXTURE2D_LOD(_B2UEdgeBuffer, sampler_B2UEdgeBuffer, uv3, 0).rgb;

            float3 diff1 = c0 - c1;
            float3 diff2 = c2 - c3;

            float colorDelta = dot(diff1, diff1) + dot(diff2, diff2);
            edgeOutput = step(0.001, colorDelta);
        #endif
    #endif

    float borderMask = step(0.0, uv.x - offset.x) * step(uv.x + offset.x, 1.0) *
                       step(0.0, uv.y - offset.y) * step(uv.y + offset.y, 1.0);

    edgeOutput *= borderMask;

    outColor = edgeColor;
    edgeAlpha = edgeOutput;
}

void B2U_Edge_Detection_float(float2 UV, float Edge_Size, float Edge_Sensitivity, float3 Edge_Color, out float3 Out_Color, out float Edge_Alpha)
{
    B2UEdgeDetectionInternal(UV, Edge_Size, Edge_Sensitivity, Edge_Color, Out_Color, Edge_Alpha);
}

void B2U_Edge_Detection_half(float2 UV, float Edge_Size, float Edge_Sensitivity, float3 Edge_Color, out float3 Out_Color, out float Edge_Alpha)
{
    B2UEdgeDetectionInternal(UV, Edge_Size, Edge_Sensitivity, Edge_Color, Out_Color, Edge_Alpha);
}


// Uses screen luminance to create stepped shadow bands for custom shadow texture overlays.
void B2UMultiBandShadowsInternal(float2 BaseUV, float Luminance, float2 MinMax, float Bands, UnityTexture2DArray ShadowTex, float TexIndex, float Mode, out float ShadowMask)
{
    float percLuma = pow(max(Luminance, 0.0001), 0.4545);

    float minLuma = min(MinMax.x, MinMax.y);
    float maxLuma = max(MinMax.x, MinMax.y);
    float shadowIntensity = 1.0 - saturate((percLuma - minLuma) / max(maxLuma - minLuma, 0.0001));
    float activeBands = shadowIntensity * Bands;

    float finalHatch = 0.0;
    int maxBands = clamp((int)Bands, 0, 8);

    static const float2x2 rot60 = float2x2(0.5, -0.8660254, 0.8660254, 0.5);
    float2 rotatedCenteredUV = BaseUV - 0.5;

    for (int i = 0; i < maxBands; i++)
    {
        float bandWeight = smoothstep(0.0, 0.05, activeBands - (float)i);
        if (bandWeight <= 0.0) break;

        float2 uv = BaseUV;

        [branch]
        if (Mode < 0.5)
        {
            if (i > 0) rotatedCenteredUV = mul(rotatedCenteredUV, rot60);
            uv = rotatedCenteredUV + 0.5;
        }
        else
        {
            uv *= (1.0 + ((float)i * 0.15));
            uv += float2(0.618, 0.382) * (float)i;
        }

        float sampleVal = SAMPLE_TEXTURE2D_ARRAY_LOD(ShadowTex.tex, ShadowTex.samplerstate, uv, TexIndex, 0).r;
        finalHatch += sampleVal * bandWeight;
    }

    ShadowMask = saturate(finalHatch);
}

void B2U_Multi_Band_Shadows_float(float2 Base_UV, float Luminance, float2 Min_Max, float Bands, UnityTexture2DArray Shadow_Tex, float Tex_Index, float Mode, out float Shadow_Mask)
{
    B2UMultiBandShadowsInternal(Base_UV, Luminance, Min_Max, Bands, Shadow_Tex, Tex_Index, Mode, Shadow_Mask);
}

void B2U_Multi_Band_Shadows_half(float2 Base_UV, float Luminance, float2 Min_Max, float Bands, UnityTexture2DArray Shadow_Tex, float Tex_Index, float Mode, out float Shadow_Mask)
{
    B2UMultiBandShadowsInternal(Base_UV, Luminance, Min_Max, Bands, Shadow_Tex, Tex_Index, Mode, Shadow_Mask);
}


// Solves underwater transmission, absorption, and projected caustics for the fullscreen immersion shader.
void B2UFullscreenImmersionTransmissionInternal(bool isTransmissive, half3 BlitSource, float2 UV, float3 WorldPos, float3 ViewDir, float LiquidSurfaceY, half Absorption, half3 LiquidColor,
    half CausticStrength, half NoiseScale, half Viscosity, half IOR, float Transition_Distance, out half3 FinalColor)
{
    #if !defined(SHADERGRAPH_PREVIEW)
        [branch]
        if (_WorldSpaceCameraPos.y > LiquidSurfaceY)
        {
            FinalColor = BlitSource;
            return;
        }

        [branch]
        if (isTransmissive)
        {
            float3 camToPixel = WorldPos - _WorldSpaceCameraPos.xyz;
            float pixelDist = length(camToPixel);
            float3 rayDir = pixelDist > 0.0001 ? (camToPixel / pixelDist) : SafeNormalize(ViewDir);

            float liquidTravelDist = pixelDist;

            // Camera is below water: if looking up, clamp travel distance to the water exit point
            if (rayDir.y > 0.0001)
            {
                float distToSurface = (LiquidSurfaceY - _WorldSpaceCameraPos.y) / rayDir.y;
                liquidTravelDist = min(pixelDist, max(distToSurface, 0.0));
            }

            float safeAbsorption = max((float)Absorption, 0.001);
            float opticalDepth = liquidTravelDist / safeAbsorption;
            half transmittance = (half)exp2(-opticalDepth);

            half3 sampledBackground = BlitSource;
            float floorDepthY = max(LiquidSurfaceY - WorldPos.y, 0.0);

            #if !defined(_B2U_CAUSTICS_OFF)
                [branch]
                if (CausticStrength > 0.0 && floorDepthY > 0.0)
                {
                    Light mainLight = GetMainLight();

                    half shadowAtten = 1.0;
                    #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                        half cascadeIndex = ComputeCascadeIndex(WorldPos);
                        if (cascadeIndex < 4)
                        {
                            float4 shadowCoord = mul(_MainLightWorldToShadow[(uint)cascadeIndex], float4(WorldPos, 1.0));
                            shadowAtten = MainLightRealtimeShadow(shadowCoord);
                        }
                    #endif

                    float spreadFactor = 1.0 + (floorDepthY * 0.25);
                    float dynamicNoiseScale = ((float)NoiseScale * 24.0) / spreadFactor;

                    float3 up = abs(mainLight.direction.y) > 0.999 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
                    float3 right = SafeNormalize(cross(up, mainLight.direction));
                    float3 forward = SafeNormalize(cross(mainLight.direction, right));

                    float2 causticUV = float2(dot(WorldPos, right), dot(WorldPos, forward)) * dynamicNoiseScale;

                    half3 mainCausticNoise = B2UProceduralCausticsInternal(causticUV, _Time.y, Viscosity, IOR);

                    float shallowFade = smoothstep(0.0, 2.0, floorDepthY);
                    float deepFade = saturate(exp2(-((floorDepthY / safeAbsorption) * 1.5)));

                    half3 totalCausticLight = mainCausticNoise * mainLight.color * shadowAtten;
                    totalCausticLight *= CausticStrength * (half)(shallowFade * deepFade);

                    sampledBackground += totalCausticLight;
                }
            #endif

            half3 backgroundLight = sampledBackground * transmittance;
            half3 scatteredLight = LiquidColor * (1.0 - transmittance);

            FinalColor = backgroundLight + scatteredLight;
        }
        else
        {
            FinalColor = LiquidColor;
        }
    #else
        FinalColor = LiquidColor;
    #endif
}

void B2U_Fullscreen_Immersion_Transmission_float(bool isTransmissive, half3 Blit_Source, float2 UV, float3 World_Pos, float3 View_Dir, float Liquid_Surface_Y, half Absorption, half3 Liquid_Color,
    half Caustic_Strength, half Noise_Scale, half Viscosity, half IOR, float Transition_Distance, out half3 Final_Color)
{
    B2UFullscreenImmersionTransmissionInternal(isTransmissive, Blit_Source, UV, World_Pos, View_Dir, Liquid_Surface_Y, Absorption, Liquid_Color, Caustic_Strength, Noise_Scale, Viscosity, IOR, Transition_Distance, Final_Color);
}

void B2U_Fullscreen_Immersion_Transmission_half(bool isTransmissive, half3 Blit_Source, float2 UV, float3 World_Pos, float3 View_Dir, float Liquid_Surface_Y, half Absorption, half3 Liquid_Color,
    half Caustic_Strength, half Noise_Scale, half Viscosity, half IOR, float Transition_Distance, out half3 Final_Color)
{
    B2UFullscreenImmersionTransmissionInternal(isTransmissive, Blit_Source, UV, World_Pos, View_Dir, Liquid_Surface_Y, Absorption, Liquid_Color, Caustic_Strength, Noise_Scale, Viscosity, IOR, Transition_Distance, Final_Color);
}


// Solves an opaque, domain-warped 3D Voronoi wave cross-section and meniscus transition across [-Transition_Distance, +Transition_Distance].
void B2UFullscreenImmersionTransitionInternal(bool Is_Transmissive, half3 Blit_Source, half3 Immersed_Color, float2 UV, float Liquid_Surface_Y,
    half3 Surface_Color, half3 Depth_Color, half Viscosity, half IOR, half Noise_Scale, half Distortion_Strength, float Transition_Distance, out half3 Out_Color)
{
    #if !defined(SHADERGRAPH_PREVIEW)
        float camDeltaY = _WorldSpaceCameraPos.y - Liquid_Surface_Y;

        // If transition distance is <= 0 (disabled), snap immediately at the surface plane
        [branch]
        if (Transition_Distance <= 0.0001)
        {
            Out_Color = (camDeltaY >= 0.0) ? Blit_Source : Immersed_Color;
            return;
        }

        [branch]
        if (camDeltaY >= Transition_Distance)
        {
            Out_Color = Blit_Source;
            return;
        }

        [branch]
        if (camDeltaY <= -Transition_Distance)
        {
            Out_Color = Immersed_Color;
            return;
        }

        // Normalize camera height delta into [-1.0, +1.0] across [-Transition_Distance, +Transition_Distance]
        float normDeltaY = clamp(camDeltaY / Transition_Distance, -1.0, 1.0);

        float visc = saturate((float)Viscosity);
        float timeSpeed = lerp(2.0, 0.35, visc);
        float t = _Time.y * timeSpeed;
        float scale = clamp((float)Noise_Scale * 8.0, 1.5, 12.0);
        float safeIOR = max((float)IOR, 1.0);

        float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
        float2 screenPos = float2((UV.x - 0.5) * aspect, UV.y);

        // 1. Undulating screen-space wave horizon
        float swell1 = sin(screenPos.x * scale * 1.4 + t * 1.7) * 0.045;
        float swell2 = sin(screenPos.x * scale * 2.9 - t * 2.3 + 1.4) * 0.025;
        float swellNoise = snoise(float3(screenPos.x * scale * 0.8, normDeltaY * 2.0, t * 0.6)) * 0.04;
        float waveOffset = swell1 + swell2 + swellNoise;

        // 2. Domain-warped 3D analytical Voronoi for the fluid cross-section
        float3 samplePos = float3(screenPos.x * scale, (screenPos.y - normDeltaY * 0.65) * scale, t * 0.45);
        float3 domainWarp;
        B2USimplex3DDirectionalInternal(samplePos * 0.65, 1.0, max((float)Distortion_Strength, 0.15) * 0.45, domainWarp);
        samplePos += domainWarp;

        float3 grad1, grad2;
        float vHeight1, vHeight2;
        B2UVoronoi3DAnalyticalInternal(samplePos, 1.0, 1.0, false, t * 1.2, grad1, vHeight1);
        B2UVoronoi3DAnalyticalInternal(samplePos * 2.1 + float3(5.2, 11.7, 3.4), 1.0, 1.0, false, -t * 1.6, grad2, vHeight2);

        float voronoiMask = saturate(vHeight1 * 0.65 + vHeight2 * 0.35);
        float2 voronoiGrad = grad1.xy * 0.65 + grad2.xy * 0.35;

        // 3. Evaluate Entry [+1.0 -> +0.15], Opaque Intersection Plateau [+0.15 -> -0.15], and Soft Submerge [-0.15 -> -1.0]
        float enterProgress = saturate((1.0 - normDeltaY) / 0.85);
        float submergeProgress = saturate((-normDeltaY - 0.15) / 0.85);

        float topSoftness = 0.045;
        float bottomSoftness = 0.25;

        float upperHorizonY = lerp(-0.18, 1.18, smoothstep(0.0, 1.0, enterProgress)) + waveOffset;
        float lowerWaveOffset = (waveOffset * 0.6) + (voronoiMask - 0.5) * 0.05;
        float lowerHorizonY = lerp(-0.34, 1.34, smoothstep(0.0, 1.0, submergeProgress)) + lowerWaveOffset;

        float belowUpperEdge = 1.0 - smoothstep(upperHorizonY - topSoftness, upperHorizonY + topSoftness, UV.y);
        float belowLowerEdge = 1.0 - smoothstep(lowerHorizonY - bottomSoftness, lowerHorizonY + bottomSoftness, UV.y);

        float waveBandMask = saturate(belowUpperEdge - belowLowerEdge);
        float underwaterMask = belowLowerEdge;

        // 4. Shade the fluid cross-section and smoothly blend toward Immersed_Color near the lower gradient
        float distFromTopLip = saturate((upperHorizonY - UV.y) * 2.0);
        float depthBlend = saturate(distFromTopLip * 0.65 + (1.0 - voronoiMask) * 0.35);
        half3 fluidBodyColor = lerp(Surface_Color, Depth_Color, (half)depthBlend);

        float cellRidge = smoothstep(0.2, 0.85, voronoiMask);
        fluidBodyColor = lerp(Depth_Color, fluidBodyColor, (half)(0.35 + 0.65 * cellRidge));

        float topMeniscus = exp2(-max(upperHorizonY - UV.y, 0.0) * 18.0) * belowUpperEdge;
        float iorBoost = saturate((safeIOR - 1.0) * 0.65);
        float internalHighlight = saturate(length(voronoiGrad) * 0.75 * iorBoost + topMeniscus * 0.75);

        half3 foamColor = lerp(Surface_Color, half3(1.0, 1.0, 1.0), (half)(Is_Transmissive ? 0.35 : 0.15));
        half3 opaqueWaveColor = lerp(fluidBodyColor, foamColor, (half)saturate(internalHighlight * cellRidge + topMeniscus * 0.5));

        // 5. Final 3-way composite: Air (Blit_Source) -> Wave Cross-Section -> Underwater (Immersed_Color)
        half3 backgroundView = lerp(Blit_Source, Immersed_Color, (half)underwaterMask);
        Out_Color = lerp(backgroundView, opaqueWaveColor, (half)waveBandMask);
    #else
        Out_Color = Immersed_Color;
    #endif
}

void B2U_Fullscreen_Immersion_Transition_float(bool Is_Transmissive, half3 Blit_Source, half3 Immersed_Color, float2 UV, float Liquid_Surface_Y,
    half3 Surface_Color, half3 Depth_Color, half Viscosity, half IOR, half Noise_Scale, half Distortion_Strength, float Transition_Distance, out half3 Out_Color)
{
    B2UFullscreenImmersionTransitionInternal(Is_Transmissive, Blit_Source, Immersed_Color, UV, Liquid_Surface_Y, Surface_Color, Depth_Color, Viscosity, IOR, Noise_Scale, Distortion_Strength, Transition_Distance, Out_Color);
}

void B2U_Fullscreen_Immersion_Transition_half(bool Is_Transmissive, half3 Blit_Source, half3 Immersed_Color, float2 UV, float Liquid_Surface_Y,
    half3 Surface_Color, half3 Depth_Color, half Viscosity, half IOR, half Noise_Scale, half Distortion_Strength, float Transition_Distance, out half3 Out_Color)
{
    B2UFullscreenImmersionTransitionInternal(Is_Transmissive, Blit_Source, Immersed_Color, UV, Liquid_Surface_Y, Surface_Color, Depth_Color, Viscosity, IOR, Noise_Scale, Distortion_Strength, Transition_Distance, Out_Color);
}

void B2URecolorSonarPulseInternal(float3 PixelWorldPos, float3 CameraWorldPos, float Time, float PulseSpeed, float PulseSpacing, float PulseSize, float PulseAttenuation, float PulseIntensity, out float PulseMask)
{
    float dist = distance(PixelWorldPos, CameraWorldPos);

    float safeSpacing = max(PulseSpacing, 0.001);
    float phase = (dist - (Time * PulseSpeed)) / safeSpacing;

    float distToRingCenter = abs(frac(phase) - 0.5) * safeSpacing;

    float halfSize = max(PulseSize * 0.5, 0.0001);
    float ringMask = 1.0 - smoothstep(0.0, halfSize, distToRingCenter);

    float attenuation = exp(-dist * PulseAttenuation);

    PulseMask = saturate(ringMask * attenuation * PulseIntensity);
}

void B2U_Recolor_Sonar_float(float3 PixelWorldPos, float3 CameraWorldPos, float Time, float PulseSpeed, float PulseSpacing, float PulseSize, float PulseAttenuation, float PulseIntensity, out float PulseMask)
{
    B2URecolorSonarPulseInternal(PixelWorldPos, CameraWorldPos, Time, PulseSpeed, PulseSpacing, PulseSize, PulseAttenuation, PulseIntensity, PulseMask);
}

void B2U_Recolor_Sonar_half(float3 PixelWorldPos, float3 CameraWorldPos, float Time, float PulseSpeed, float PulseSpacing, float PulseSize, float PulseAttenuation, float PulseIntensity, out float PulseMask)
{
    B2URecolorSonarPulseInternal(PixelWorldPos, CameraWorldPos, Time, PulseSpeed, PulseSpacing, PulseSize, PulseAttenuation, PulseIntensity, PulseMask);
}

#endif