#ifndef B2U_LIGHT_MODELS_INCLUDED
#define B2U_LIGHT_MODELS_INCLUDED

#if !defined(SHADERGRAPH_PREVIEW) && !defined(__INTELLISENSE__)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

#if !defined(SHADERGRAPH_PREVIEW)
// ----------------------------------------------------------------------------------
// 1. URP STANDARD PBR LIGHTING
// ----------------------------------------------------------------------------------
float3 B2U_Light_URP(InputData inputData, SurfaceData surfaceData)
{
    return UniversalFragmentPBR(inputData, surfaceData).rgb;
}

// ----------------------------------------------------------------------------------
// 2. LIGHTWEIGHT (BLINN-PHONG) EVALUATORS
// ----------------------------------------------------------------------------------
float3 EvaluateLight_Lightweight(InputData inputData, SurfaceData surfaceData, Light light, float3 diffuseAlbedo, float specPower, float3 specColor)
{
    float3 lightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
    float NdotL = saturate(dot(inputData.normalWS, light.direction));
    float3 diffuse = diffuseAlbedo * NdotL * lightColor;

    float3 halfVector = SafeNormalize((float3)light.direction + (float3)inputData.viewDirectionWS);
    float NdotH = max(saturate(dot(inputData.normalWS, halfVector)), 0.0001);

    float3 specular = pow(NdotH, specPower) * lightColor * specColor * (surfaceData.smoothness * NdotL);

    return diffuse + specular;
}

float3 B2U_Light_Lightweight(InputData inputData, SurfaceData surfaceData)
{
    float3 color = float3(0.0, 0.0, 0.0);

    float3 diffuseAlbedo = surfaceData.albedo * (1.0 - surfaceData.metallic);
    #if defined(_ALPHAPREMULTIPLY_ON)
        diffuseAlbedo *= surfaceData.alpha;
    #endif

    float specPower = exp2(10.0 * surfaceData.smoothness + 1.0);
    float3 specColor = lerp(float3(1.0, 1.0, 1.0), surfaceData.albedo, surfaceData.metallic);

    Light mainLight = GetMainLight(inputData.shadowCoord);
    color += EvaluateLight_Lightweight(inputData, surfaceData, mainLight, diffuseAlbedo, specPower, specColor);

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
        for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            color += EvaluateLight_Lightweight(inputData, surfaceData, light, diffuseAlbedo, specPower, specColor);
        }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        color += EvaluateLight_Lightweight(inputData, surfaceData, light, diffuseAlbedo, specPower, specColor);
    LIGHT_LOOP_END

    float3 ambientDiffuse = inputData.bakedGI * diffuseAlbedo * surfaceData.occlusion;

    float3 reflectVector = reflect(-inputData.viewDirectionWS, inputData.normalWS);
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surfaceData.smoothness);
    float3 ambientSpecular = GlossyEnvironmentReflection(reflectVector, inputData.positionWS, perceptualRoughness, surfaceData.occlusion, inputData.normalizedScreenSpaceUV);

    float oneMinusNdotV = 1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), surfaceData.albedo, surfaceData.metallic);
    float fresnelTerm = Pow4(oneMinusNdotV) * oneMinusNdotV * surfaceData.smoothness;
    float3 fresnel = f0 + (1.0 - f0) * fresnelTerm;

    float roughness = 1.0 - surfaceData.smoothness;
    float surfaceReduction = 1.0 / (roughness * roughness + 1.0);

    color += ambientDiffuse + (ambientSpecular * fresnel * surfaceReduction) + surfaceData.emission;
    return color;
}

// ----------------------------------------------------------------------------------
// 3. TOON (POSTERIZED) EVALUATORS
// ----------------------------------------------------------------------------------
float3 EvaluateLight_Toon(InputData inputData, SurfaceData surfaceData, Light light, float3 diffuseAlbedo, float safeSteps, float specThreshold, float3 specColor, float specStrength)
{
    float NdotL = saturate(dot(inputData.normalWS, light.direction));

    // Expand the upper cosine range so small normal shifts on flat/rippled surfaces cross step boundaries
    float curvedNdotL = NdotL * NdotL;
    float rawIntensity = curvedNdotL * light.distanceAttenuation * light.shadowAttenuation;

    float bandedIntensity = ceil(rawIntensity * safeSteps) / safeSteps;
    float shadowBand = smoothstep(0.0, 0.05, rawIntensity) * bandedIntensity;

    float3 diffuse = diffuseAlbedo * light.color * shadowBand;

    // Posterized Specular
    float3 halfVector = SafeNormalize((float3)light.direction + (float3)inputData.viewDirectionWS);
    float NdotH = saturate(dot(inputData.normalWS, halfVector));

    float specCone = saturate((NdotH - specThreshold) / max(1.0 - specThreshold, 0.0001));
    float linearSpec = 1.0 - sqrt(saturate(1.0 - specCone));
    float specBand = (ceil(linearSpec * safeSteps) / safeSteps) * smoothstep(0.0, 0.05, rawIntensity);

    float3 specular = specBand * light.color * specColor * specStrength;

    return diffuse + specular;
}

float3 B2U_Light_Toon(InputData inputData, SurfaceData surfaceData, float steps)
{
    float3 color = float3(0.0, 0.0, 0.0);
    float safeSteps = max(steps, 1.0);

    float maxChannel = max(surfaceData.albedo.r, max(surfaceData.albedo.g, surfaceData.albedo.b));
    if (maxChannel > 0.02)
    {
        float colorSteps = 6.0;
        float steppedMax = ceil(maxChannel * colorSteps) / colorSteps;
        surfaceData.albedo = saturate(surfaceData.albedo * (steppedMax / maxChannel));
    }

    // Retain 35% diffuse on metals so cel bands remain visible while specular steps pop
    float3 diffuseAlbedo = surfaceData.albedo * lerp(1.0, 0.35, surfaceData.metallic);
    #if defined(_ALPHAPREMULTIPLY_ON)
        diffuseAlbedo *= surfaceData.alpha;
    #endif

    float maxCone = lerp(0.80, 0.50, surfaceData.metallic);
    float specThreshold = lerp(0.99, maxCone, surfaceData.smoothness);
    float3 specColor = lerp(float3(1.0, 1.0, 1.0), surfaceData.albedo, surfaceData.metallic);
    float specStrength = surfaceData.smoothness * lerp(1.0, 1.8, surfaceData.metallic);

    Light mainLight = GetMainLight(inputData.shadowCoord);
    color += EvaluateLight_Toon(inputData, surfaceData, mainLight, diffuseAlbedo, safeSteps, specThreshold, specColor, specStrength);

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
        for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            color += EvaluateLight_Toon(inputData, surfaceData, light, diffuseAlbedo, safeSteps, specThreshold, specColor, specStrength);
        }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        color += EvaluateLight_Toon(inputData, surfaceData, light, diffuseAlbedo, safeSteps, specThreshold, specColor, specStrength);
    LIGHT_LOOP_END

    float3 shadowTint = float3(0.75, 0.8, 0.9);
    float3 ambientDiffuse = inputData.bakedGI * diffuseAlbedo * shadowTint * surfaceData.occlusion;

    // Stepped Toon Rim Light
    float oneMinusNdotV = 1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    float rimThreshold = lerp(0.75, 0.55, surfaceData.metallic);
    float rimCone = saturate((oneMinusNdotV - rimThreshold) / max(1.0 - rimThreshold, 0.0001));
    float steppedRim = (ceil(rimCone * safeSteps) / safeSteps) * surfaceData.smoothness;

    float3 rimTint = lerp(float3(0.04, 0.04, 0.04), surfaceData.albedo, surfaceData.metallic);
    float3 ambientRim = inputData.bakedGI * rimTint * steppedRim * surfaceData.occlusion;

    color += ambientDiffuse + ambientRim + surfaceData.emission;
    return color;
}

// ----------------------------------------------------------------------------------
// 4. ANTILIGHT (LIGHT CASTS DARKNESS)
// ----------------------------------------------------------------------------------
float3 B2U_Light_Antilight(InputData inputData, SurfaceData surfaceData)
{
    float3 totalLight = float3(0.0, 0.0, 0.0);
    Light mainLight = GetMainLight(inputData.shadowCoord);
    float NdotL = saturate(dot(inputData.normalWS, mainLight.direction));
    totalLight += mainLight.color * (NdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
        for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            float dirNdotL = saturate(dot(inputData.normalWS, light.direction));
            totalLight += light.color * (dirNdotL * light.distanceAttenuation * light.shadowAttenuation);
        }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        float addNdotL = saturate(dot(inputData.normalWS, light.direction));
        totalLight += light.color * (addNdotL * light.distanceAttenuation * light.shadowAttenuation);
    LIGHT_LOOP_END

    totalLight += inputData.bakedGI * surfaceData.occlusion;
    float invertedMask = saturate(1.0 - dot(totalLight, float3(0.3333, 0.3333, 0.3333)));

    return (surfaceData.albedo * invertedMask) + surfaceData.emission;
}

// ----------------------------------------------------------------------------------
// 5. FACETED (PROCEDURAL FLAT SHADING)
// ----------------------------------------------------------------------------------
float3 EvaluateLight_Faceted(InputData inputData, SurfaceData surfaceData, Light light, float3 diffuseAlbedo, float3 flatNormal, float specPower, float3 specColor)
{
    float NdotL = saturate(dot(flatNormal, light.direction));
    float3 attenuatedLight = light.color * (NdotL * light.distanceAttenuation * light.shadowAttenuation);
    float3 diffuse = diffuseAlbedo * attenuatedLight;

    float3 halfVector = SafeNormalize((float3)light.direction + (float3)inputData.viewDirectionWS);
    float NdotH = max(saturate(dot(flatNormal, halfVector)), 0.0001);
    float3 specular = pow(NdotH, specPower) * attenuatedLight * specColor * surfaceData.smoothness;

    return diffuse + specular;
}

float3 B2U_Light_Faceted(InputData inputData, SurfaceData surfaceData, float3 flatNormal)
{
    float3 color = float3(0.0, 0.0, 0.0);
    float3 diffuseAlbedo = surfaceData.albedo * (1.0 - surfaceData.metallic);

    #if defined(_ALPHAPREMULTIPLY_ON)
        diffuseAlbedo *= surfaceData.alpha;
    #endif

    float specPower = exp2(10.0 * surfaceData.smoothness + 1.0);
    float3 specColor = lerp(float3(1.0, 1.0, 1.0), surfaceData.albedo, surfaceData.metallic);

    Light mainLight = GetMainLight(inputData.shadowCoord);
    color += EvaluateLight_Faceted(inputData, surfaceData, mainLight, diffuseAlbedo, flatNormal, specPower, specColor);

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
        for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            color += EvaluateLight_Faceted(inputData, surfaceData, light, diffuseAlbedo, flatNormal, specPower, specColor);
        }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        color += EvaluateLight_Faceted(inputData, surfaceData, light, diffuseAlbedo, flatNormal, specPower, specColor);
    LIGHT_LOOP_END

    float3 ambientDiffuse = inputData.bakedGI * diffuseAlbedo * surfaceData.occlusion;

    float3 reflectVector = reflect(-inputData.viewDirectionWS, flatNormal);
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surfaceData.smoothness);
    float3 ambientSpecular = GlossyEnvironmentReflection(reflectVector, inputData.positionWS, perceptualRoughness, surfaceData.occlusion, inputData.normalizedScreenSpaceUV);

    float oneMinusNdotV = 1.0 - saturate(dot(flatNormal, inputData.viewDirectionWS));
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), surfaceData.albedo, surfaceData.metallic);
    float3 fresnel = f0 + (1.0 - f0) * (Pow4(oneMinusNdotV) * oneMinusNdotV * surfaceData.smoothness);

    float roughness = 1.0 - surfaceData.smoothness;
    color += ambientDiffuse + (ambientSpecular * fresnel * (1.0 / (roughness * roughness + 1.0))) + surfaceData.emission;

    return color;
}

// ----------------------------------------------------------------------------------
// 6. RETRO (4x4 BAYER DITHERING)
// ----------------------------------------------------------------------------------
float3 B2U_Light_Retro(InputData inputData, SurfaceData surfaceData)
{
    static const float bayer[16] = {
        0.0,  8.0,  2.0, 10.0,
       12.0,  4.0, 14.0,  6.0,
        3.0, 11.0,  1.0,  9.0,
       15.0,  7.0, 13.0,  5.0
    };

    float3 totalLight = float3(0.0, 0.0, 0.0);
    Light mainLight = GetMainLight(inputData.shadowCoord);
    float NdotL = saturate(dot(inputData.normalWS, mainLight.direction));
    totalLight += mainLight.color * (NdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
        for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            float dirNdotL = saturate(dot(inputData.normalWS, light.direction));
            totalLight += light.color * (dirNdotL * light.distanceAttenuation * light.shadowAttenuation);
        }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        float addNdotL = saturate(dot(inputData.normalWS, light.direction));
        totalLight += light.color * (addNdotL * light.distanceAttenuation * light.shadowAttenuation);
    LIGHT_LOOP_END

    totalLight += inputData.bakedGI * surfaceData.occlusion;
    float lightIntensity = dot(totalLight, float3(0.3333, 0.3333, 0.3333));

    uint2 pixel = (uint2)inputData.positionCS.xy & 3u;
    uint index = (pixel.y << 2u) | pixel.x;

    float ditherVal = (bayer[index] * 0.0625) - 0.5;
    float ditheredLight = step(0.5, lightIntensity + ditherVal);

    float3 lightTint = totalLight / max(lightIntensity, 0.0001);
    return surfaceData.albedo * (ditheredLight * lightTint) + surfaceData.emission;
}

// ----------------------------------------------------------------------------------
// 7. GOOCH (COMPLEMENTARY SHADOWS)
// ----------------------------------------------------------------------------------
float3 B2U_Light_Gooch(InputData inputData, SurfaceData surfaceData, float darkness)
{
    float maxC = max(surfaceData.albedo.r, max(surfaceData.albedo.g, surfaceData.albedo.b));
    float minC = min(surfaceData.albedo.r, min(surfaceData.albedo.g, surfaceData.albedo.b));
    float3 complement = (maxC + minC) - surfaceData.albedo;

    float3 totalDiffuse = float3(0.0, 0.0, 0.0);
    float3 totalSpecular = float3(0.0, 0.0, 0.0);
    float specPower = exp2(10.0 * surfaceData.smoothness + 1.0);

    Light mainLight = GetMainLight(inputData.shadowCoord);
    float mainNdotL = saturate(dot(inputData.normalWS, mainLight.direction));
    float3 mainAttenLight = mainLight.color * (mainNdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
    totalDiffuse += mainAttenLight;

    float3 mainHalf = SafeNormalize((float3)mainLight.direction + (float3)inputData.viewDirectionWS);
    float mainNdotH = max(saturate(dot(inputData.normalWS, mainHalf)), 0.0001);
    totalSpecular += pow(mainNdotH, specPower) * mainAttenLight * surfaceData.smoothness;

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
        for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            float dirNdotL = saturate(dot(inputData.normalWS, light.direction));
            float3 dirAttenLight = light.color * (dirNdotL * light.distanceAttenuation * light.shadowAttenuation);
            totalDiffuse += dirAttenLight;

            float3 dirHalf = SafeNormalize((float3)light.direction + (float3)inputData.viewDirectionWS);
            float dirNdotH = max(saturate(dot(inputData.normalWS, dirHalf)), 0.0001);
            totalSpecular += pow(dirNdotH, specPower) * dirAttenLight * surfaceData.smoothness;
        }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        float addNdotL = saturate(dot(inputData.normalWS, light.direction));
        float3 addAttenLight = light.color * (addNdotL * light.distanceAttenuation * light.shadowAttenuation);
        totalDiffuse += addAttenLight;

        float3 addHalf = SafeNormalize((float3)light.direction + (float3)inputData.viewDirectionWS);
        float addNdotH = max(saturate(dot(inputData.normalWS, addHalf)), 0.0001);
        totalSpecular += pow(addNdotH, specPower) * addAttenLight * surfaceData.smoothness;
    LIGHT_LOOP_END

    totalDiffuse += inputData.bakedGI * surfaceData.occlusion;

    float intensity = saturate(dot(totalDiffuse, float3(0.299, 0.587, 0.114)));
    float shadowAmount = 1.0 - intensity;

    float3 blendedColor = lerp(surfaceData.albedo, complement, shadowAmount);
    float darkenFactor = max(0.0, lerp(1.0, darkness, shadowAmount));
    float3 finalDiffuse = blendedColor * darkenFactor;

    return finalDiffuse + totalSpecular + surfaceData.emission;
}

// ----------------------------------------------------------------------------------
// 8. POP-ART (BEN-DAY DOTS)
// ----------------------------------------------------------------------------------
float3 B2U_Light_PopArt(InputData inputData, SurfaceData surfaceData, float dotSize)
{
    float3 totalLight = float3(0.0, 0.0, 0.0);
    Light mainLight = GetMainLight(inputData.shadowCoord);
    float NdotL = saturate(dot(inputData.normalWS, mainLight.direction));
    totalLight += mainLight.color * (NdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
        for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
            float dirNdotL = saturate(dot(inputData.normalWS, light.direction));
            totalLight += light.color * (dirNdotL * light.distanceAttenuation * light.shadowAttenuation);
        }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        float addNdotL = saturate(dot(inputData.normalWS, light.direction));
        totalLight += light.color * (addNdotL * light.distanceAttenuation * light.shadowAttenuation);
    LIGHT_LOOP_END

    totalLight += inputData.bakedGI * surfaceData.occlusion;
    float lightIntensity = dot(totalLight, float3(0.299, 0.587, 0.114));

    float3 posterColor = surfaceData.albedo;
    float maxChannel = max(posterColor.r, max(posterColor.g, posterColor.b));
    if (maxChannel > 0.02)
    {
        float colorSteps = 4.0;
        float steppedMax = ceil(maxChannel * colorSteps) / colorSteps;
        posterColor = saturate(posterColor * (steppedMax / maxChannel));
    }

    // Scale relative to a 1080p reference height so halftone dot density remains resolution-independent
    float resScale = 1080.0 / max(_ScreenParams.y, 1.0);
    float2 sc = inputData.positionCS.xy * resScale * max(dotSize, 0.0005);
    sc.x += step(0.5, frac(sc.y * 0.5)) * 0.5;

    float2 localUV = frac(sc) * 2.0 - 1.0;
    float dist = dot(localUV, localUV);

    float radius = (1.0 - smoothstep(0.1, 0.8, lightIntensity)) * 1.5;
    float dotMask = step(dist, radius);

    float3 finalColor = lerp(posterColor, posterColor * 0.2, dotMask);

    return finalColor + surfaceData.emission;
}
#endif

// ----------------------------------------------------------------------------------
// MASTER NODE WRAPPER
// ----------------------------------------------------------------------------------
void B2U_EvaluateLightModelInternal(float3 BaseColor, float Alpha, float3 NormalWS, float Metallic, float Smoothness, float3 Emission, float AmbientOcclusion, float3 PositionWS, float3 ViewDirectionWS,
    float LightMask, float additionalFloat, out float3 OutColor, out float OutAlpha)
{
    #if !defined(SHADERGRAPH_PREVIEW)
        float3 unlitResult = BaseColor + Emission;
        float3 litResult = unlitResult;
        float clampedMask = saturate(LightMask);
        float3 safeNormalWS = SafeNormalize(NormalWS);

        #if defined(_B2U_LIGHT_MODEL_FACETED)
            // Compute screen-space derivatives outside dynamic flow control and align with geometric normal hemisphere
            float3 dpdx = ddx(PositionWS);
            float3 dpdy = ddy(PositionWS);
            float3 rawCross = cross(dpdy, dpdx);
            float3 flatNormal = dot(rawCross, rawCross) > 1e-12 ? SafeNormalize(rawCross) : safeNormalWS;
            if (dot(flatNormal, safeNormalWS) < 0.0)
            {
                flatNormal = -flatNormal;
            }
        #endif

        [branch]
        if (clampedMask > 0.0001)
        {
            SurfaceData surfaceData = (SurfaceData)0;
            surfaceData.albedo = BaseColor;
            surfaceData.metallic = saturate(Metallic);
            surfaceData.smoothness = saturate(Smoothness);
            surfaceData.normalTS = half3(0.0, 0.0, 1.0);
            surfaceData.emission = Emission;
            surfaceData.occlusion = saturate(AmbientOcclusion);
            surfaceData.alpha = saturate(Alpha);

            InputData inputData = (InputData)0;
            inputData.positionWS = PositionWS;
            inputData.normalWS = safeNormalWS;
            inputData.viewDirectionWS = SafeNormalize(ViewDirectionWS);
            inputData.shadowCoord = TransformWorldToShadowCoord(PositionWS);
            inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

            float4 clipPos = TransformWorldToHClip(PositionWS);
            float4 ndc = ComputeScreenPos(clipPos);
            float2 normalizedUV = saturate(ndc.xy / max(ndc.w, 0.0001));
            float2 pixelCoords = normalizedUV * _ScreenParams.xy;

            inputData.positionCS = float4(pixelCoords, clipPos.z, clipPos.w);
            inputData.bakedGI = SampleSH(inputData.normalWS);
            inputData.normalizedScreenSpaceUV = normalizedUV;

            #if defined(_B2U_LIGHT_MODEL_LIGHTWEIGHT)
                litResult = B2U_Light_Lightweight(inputData, surfaceData);
            #elif defined(_B2U_LIGHT_MODEL_TOON)
                litResult = B2U_Light_Toon(inputData, surfaceData, additionalFloat);
            #elif defined(_B2U_LIGHT_MODEL_ANTILIGHT)
                litResult = B2U_Light_Antilight(inputData, surfaceData);
            #elif defined(_B2U_LIGHT_MODEL_FACETED)
                litResult = B2U_Light_Faceted(inputData, surfaceData, flatNormal);
            #elif defined(_B2U_LIGHT_MODEL_RETRO)
                litResult = B2U_Light_Retro(inputData, surfaceData);
            #elif defined(_B2U_LIGHT_MODEL_GOOCH)
                litResult = B2U_Light_Gooch(inputData, surfaceData, additionalFloat);
            #elif defined(_B2U_LIGHT_MODEL_POPART)
                litResult = B2U_Light_PopArt(inputData, surfaceData, additionalFloat);
            #else
                litResult = B2U_Light_URP(inputData, surfaceData);
            #endif
        }

        OutColor = lerp(unlitResult, litResult, clampedMask);
        OutAlpha = saturate(Alpha);
    #else
        OutColor = BaseColor;
        OutAlpha = Alpha;
    #endif
}

void B2U_Evaluate_Light_Model_float(float3 BaseColor, float Alpha, float3 NormalWS, float Metallic, float Smoothness, float3 Emission, float AmbientOcclusion, float3 PositionWS, float3 ViewDirectionWS, float LightMask, float AdditionalFloat,
    out float3 OutColor, out float OutAlpha)
{
    B2U_EvaluateLightModelInternal(BaseColor, Alpha, NormalWS, Metallic, Smoothness, Emission, AmbientOcclusion, PositionWS, ViewDirectionWS, LightMask, AdditionalFloat, OutColor, OutAlpha);
}

void B2U_Evaluate_Light_Model_half(float3 BaseColor, float Alpha, float3 NormalWS, float Metallic, float Smoothness, float3 Emission, float AmbientOcclusion, float3 PositionWS, float3 ViewDirectionWS, float LightMask, float AdditionalFloat,
    out float3 OutColor, out float OutAlpha)
{
    B2U_EvaluateLightModelInternal(BaseColor, Alpha, NormalWS, Metallic, Smoothness, Emission, AmbientOcclusion, PositionWS, ViewDirectionWS, LightMask, AdditionalFloat, OutColor, OutAlpha);
}

#endif