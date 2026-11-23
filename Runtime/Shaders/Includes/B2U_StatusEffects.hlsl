#ifndef B2U_STATUS_EFFECTS_INCLUDED
#define B2U_STATUS_EFFECTS_INCLUDED

#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Fullscreen.hlsl"

// --- HELPERS ---

float B2U_Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 B2U_Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float B2U_ValueNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(B2U_Hash12(i + float2(0.0, 0.0)), B2U_Hash12(i + float2(1.0, 0.0)), u.x),
                lerp(B2U_Hash12(i + float2(0.0, 1.0)), B2U_Hash12(i + float2(1.0, 1.0)), u.x), u.y);
}

float B2U_LinearNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    return lerp(lerp(B2U_Hash12(i + float2(0.0, 0.0)), B2U_Hash12(i + float2(1.0, 0.0)), f.x),
                lerp(B2U_Hash12(i + float2(0.0, 1.0)), B2U_Hash12(i + float2(1.0, 1.0)), f.x), f.y);
}

float B2U_LinearNoise1D(float x, float seed)
{
    float i = floor(x);
    float f = frac(x);
    return lerp(B2U_Hash12(float2(i, seed)), B2U_Hash12(float2(i + 1.0, seed)), f);
}

float B2U_LineSegmentDist(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.00001));
    return length(pa - ba * h);
}

float2 B2U_Hash2D(float2 seed)
{
    return B2U_Hash22(seed);
}

// NaN-safe atan2 wrapper to prevent undefined atan2(0, 0) at exact screen center or corners
float B2U_SafeAtan2(float y, float x)
{
    float safeX = (abs(x) + abs(y) < 1e-6) ? 1e-6 : x;
    return atan2(y, safeX);
}

// Applies screen-space lighting to status effects.
void B2UStatusMainLightInternal(float2 UV, float3 ScreenNormal, float SpecularPower, out float3 Diffuse, out float3 Specular)
{
    Diffuse = float3(0.0, 0.0, 0.0);
    Specular = float3(0.0, 0.0, 0.0);
    const float LensSize = 1.0;

    #if !defined(SHADERGRAPH_PREVIEW)
        float2 centeredUV = UV - 0.5;
        float3 viewSpaceOffset = float3(centeredUV.x, centeredUV.y, 0.0) * LensSize;

        float3 worldOffset = mul((float3x3)UNITY_MATRIX_I_V, viewSpaceOffset);
        float3 lensPosWS = _WorldSpaceCameraPos.xyz + worldOffset;

        Light mainLight = GetMainLight();

        half shadowAtten = 1.0;
        #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
            half cascadeIndex = ComputeCascadeIndex(lensPosWS);
            if (cascadeIndex < 4)
            {
                float4 shadowCoord = mul(_MainLightWorldToShadow[(uint)cascadeIndex], float4(lensPosWS, 1.0));
                shadowAtten = MainLightRealtimeShadow(shadowCoord);
            }
        #endif

        float3 safeNormal = SafeNormalize(ScreenNormal);
        float3 lightDirVS = SafeNormalize(mul((float3x3)UNITY_MATRIX_V, mainLight.direction));
        lightDirVS.z = -lightDirVS.z;

        float NdotL = saturate(dot(safeNormal, lightDirVS));
        Diffuse = mainLight.color * (NdotL * shadowAtten);

        float3 viewDirVS = float3(0.0, 0.0, 1.0);
        float3 halfDir = SafeNormalize(lightDirVS + viewDirVS);
        float NdotH = max(0.0001, saturate(dot(safeNormal, halfDir)));

        float spec = pow(NdotH, max(SpecularPower, 1.0));
        Specular = mainLight.color * (spec * shadowAtten);
    #endif
}

void B2U_Status_MainLight_float(float2 UV, float3 ScreenNormal, float SpecularPower, out float3 Diffuse, out float3 Specular)
{
    B2UStatusMainLightInternal(UV, ScreenNormal, SpecularPower, Diffuse, Specular);
}

void B2U_Status_MainLight_half(float2 UV, float3 ScreenNormal, float SpecularPower, out float3 Diffuse, out float3 Specular)
{
    B2UStatusMainLightInternal(UV, ScreenNormal, SpecularPower, Diffuse, Specular);
}

// Generates cells of ice that freeze the camera inward from the edges.
void B2UStatusFreezingInternal(float2 UV, float Scale, float Randomness, float Radius, float Smoothing, out float EdgeDist, out float CellMask, out float2 CellOffset, out float3 CellNormal)
{
    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float2 centeredUV = UV - 0.5;
    centeredUV.x *= aspect;

    float safeScale = max(Scale, 0.001);
    float2 p = (centeredUV + 0.5) * safeScale;
    float2 g = floor(p);
    float2 f = frac(p);

    float2 mr = float2(0.0, 0.0);
    float2 closestLattice = float2(0.0, 0.0);
    float2 closestHash = float2(0.0, 0.0);
    float minDist = 8.0;

    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 lattice = float2(i, j);
            float2 h = B2U_Hash22(g + lattice);
            float2 r = lattice + h * Randomness - f;
            float d = dot(r, r);

            if (d < minDist)
            {
                minDist = d;
                mr = r;
                closestLattice = lattice;
                closestHash = h;
            }
        }
    }

    float edgeDist = 8.0;
    float2 closestEdgeDir = float2(0.0, 0.0);

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0) continue;

            float2 lattice = closestLattice + float2(x, y);
            float2 offset = B2U_Hash22(g + lattice) * Randomness;
            float2 r = lattice + offset - f;

            float2 diff = r - mr;
            float diffLenSq = dot(diff, diff);
            if (diffLenSq > 0.00001)
            {
                float2 diffDir = diff * rsqrt(diffLenSq);
                float d = dot(0.5 * (mr + r), diffDir);
                if (d < edgeDist)
                {
                    edgeDist = d;
                    closestEdgeDir = diffDir;
                }
            }
        }
    }

    float2 aspectVec = ((g + closestLattice + closestHash * Randomness) / safeScale) - 0.5;
    float distSq = dot(aspectVec, aspectVec);
    float distToScreenCenter = sqrt(distSq);

    float freezeProgress = smoothstep(Radius, Radius + max(Smoothing, 0.001), distToScreenCenter);
    float localGrowth = saturate(freezeProgress * 1.2 - (closestHash.x * 0.2));

    float centerDist = sqrt(minDist);
    float safeEdgeDist = max(edgeDist, 0.0);
    float fractionalDist = centerDist / (centerDist + safeEdgeDist + 0.0001);

    float growthEdge = localGrowth * 1.25;
    float minGrowth = max(growthEdge - 0.2, 0.0);
    CellMask = 1.0 - smoothstep(minGrowth, max(growthEdge, minGrowth + 0.0001), fractionalDist);

    EdgeDist = safeEdgeDist;
    float2 safeAspectDir = distSq > 1e-8 ? (aspectVec * rsqrt(distSq)) : float2(0.0, 1.0);
    CellOffset = -safeAspectDir * safeEdgeDist * CellMask;

    float2 facetTilt = (closestHash - 0.5) * 1.5;
    float3 activeNormal = SafeNormalize(float3(-closestEdgeDir.x + facetTilt.x, -closestEdgeDir.y + facetTilt.y, 1.2));
    CellNormal = SafeNormalize(lerp(float3(0.0, 0.0, 1.0), activeNormal, CellMask));
}

void B2U_Status_Freezing_float(float2 UV, float Scale, float Randomness, float Radius, float Smoothing, out float EdgeDist, out float CellMask, out float2 CellOffset, out float3 CellNormal)
{
    B2UStatusFreezingInternal(UV, Scale, Randomness, Radius, Smoothing, EdgeDist, CellMask, CellOffset, CellNormal);
}

void B2U_Status_Freezing_half(float2 UV, float Scale, float Randomness, float Radius, float Smoothing, out float EdgeDist, out float CellMask, out float2 CellOffset, out float3 CellNormal)
{
    B2UStatusFreezingInternal(UV, Scale, Randomness, Radius, Smoothing, EdgeDist, CellMask, CellOffset, CellNormal);
}

// Generates random lightning bolts striking inward from the edges of the screen.
void B2UStatusShockedInternal(float2 UV, float Time, float Scale, float FlashSpeed, float Jaggedness, float Thickness, out float ShockMask)
{
    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float2 uvCentered = UV - 0.5;
    float2 baseP = uvCentered;
    baseP.x *= aspect;

    float r = length(baseP);
    float safeScale = max(Scale, 0.5);
    float safeSpeed = abs(FlashSpeed);

    float2 edgeUV2 = (uvCentered * 2.0) * (uvCentered * 2.0);
    float normEdgeRadius = sqrt(sqrt(edgeUV2.x * edgeUV2.x + edgeUV2.y * edgeUV2.y));
    float distFromEdge = 1.0 - normEdgeRadius;
    float centerFade = smoothstep(0.05, 0.2, r);

    float mask = 0.0;

    [unroll]
    for (int i = 1; i <= 4; i++)
    {
        float layerSpeed = safeSpeed * (0.65 + (float)i * 0.21);
        float rawTime = fmod(abs(Time) * layerSpeed + (float)i * 1.73, 512.0);
        float timeStep = floor(rawTime);
        float cyclePhase = frac(rawTime);

        float rotAngle = B2U_Hash12(float2(timeStep * 2.71, (float)i * 17.3)) * 6.2831853;
        float s, c;
        sincos(rotAngle, s, c);
        float2 rotP = mul(baseP, float2x2(c, -s, s, c));

        float a = B2U_SafeAtan2(rotP.y, rotP.x) * 0.15915494 + 0.5;

        float sectorCount = max(3.0, round(safeScale * (0.8 + (float)i * 0.35)));
        float angularCoord = a * sectorCount;
        float boltID = floor(angularCoord);
        float sectorUV = frac(angularCoord) - 0.5;

        float2 boltSeed = B2U_Hash22(float2(boltID + timeStep * 11.3, (float)i * 29.7)) * 100.0;
        float boltHash = frac(boltSeed.x * 0.137);
        float activeBolt = step(0.4, boltHash);

        float rCoord = r * safeScale;
        float n1 = B2U_LinearNoise1D(rCoord * 1.8 + boltSeed.x, boltSeed.y) - 0.5;
        float n2 = B2U_LinearNoise1D(rCoord * 5.2 - boltSeed.y, boltSeed.x + 19.1) - 0.5;
        float n3 = B2U_LinearNoise1D(rCoord * 13.5 + boltSeed.x * 0.5, boltSeed.y + 47.3) - 0.5;

        float rawWarp = (n1 * 0.55 + n2 * 0.30 + n3 * 0.15) * Jaggedness;
        float warp = 0.42 * (rawWarp / (0.42 + abs(rawWarp)));

        float sectorDelta = abs(sectorUV - warp);
        float lineDist = sectorDelta * lerp(0.65, r * 1.6, 0.6);

        float localPhase = saturate((cyclePhase - (boltHash - 0.4) * 0.4) * 1.35);
        float maxReach = 0.35 + 0.45 * frac(boltSeed.y * 0.31);

        float growthProgress = saturate(localPhase * 3.6);
        float head = lerp(-0.22, maxReach, growthProgress);
        float headMask = 1.0 - smoothstep(head - 0.08, head + 0.04, distFromEdge);

        float fadeIn = smoothstep(0.0, 0.08, localPhase);
        float fadeOut = 1.0 - smoothstep(0.22, 1.0, localPhase);
        float flashEnvelope = fadeIn * fadeOut * fadeOut;

        float tipTaper = 1.0 - smoothstep(-0.1, maxReach, distFromEdge) * 0.65;
        float spreadMask = headMask * tipTaper * flashEnvelope;

        float stripFalloff = 1.0 - smoothstep(0.1, 0.45, sectorDelta);
        float currentThickness = Thickness * spreadMask;
        float glow = (currentThickness / (lineDist + 0.004)) * stripFalloff;

        mask += glow * activeBolt * centerFade * rsqrt((float)i);
    }

    ShockMask = saturate(mask);
}

void B2U_Status_Shocked_float(float2 UV, float Time, float Scale, float FlashSpeed, float Jaggedness, float Thickness, out float ShockMask)
{
    B2UStatusShockedInternal(UV, Time, Scale, FlashSpeed, Jaggedness, Thickness, ShockMask);
}

void B2U_Status_Shocked_half(float2 UV, float Time, float Scale, float FlashSpeed, float Jaggedness, float Thickness, out float ShockMask)
{
    B2UStatusShockedInternal(UV, Time, Scale, FlashSpeed, Jaggedness, Thickness, ShockMask);
}

// Generates a procedural mandala seal effect.
void B2UStatusCursedInternal(float2 UV, float Time, float Scale, float EvolutionSpeed, float Thickness, out float SealMask)
{
    float2 p = UV - 0.5;
    p.x *= (_ScreenParams.x / max(_ScreenParams.y, 1.0));
    p *= Scale;

    float r = length(p);
    float a = B2U_SafeAtan2(p.y, p.x);
    float t = fmod(Time * EvolutionSpeed, 628.31853);
    float T = Thickness * 0.5;

    const float fold = 1.0471975;

    float a_fold = fmod(abs(a) + fold * 0.5 + 6.2831853, fold);
    a_fold = abs(a_fold - fold * 0.5);

    float sFold, cFold;
    sincos(a_fold, sFold, cFold);
    float2 q = float2(cFold, sFold) * r;

    const float innerRing = 1.0;

    float d_anchors = 100.0;
    float d_geo = 100.0;
    float d_arches = 100.0;
    float d_clock = 100.0;

    d_anchors = min(d_anchors, abs(r - innerRing) - T);
    d_anchors = min(d_anchors, abs(r - 1.15) + T * 0.4);
    d_anchors = min(d_anchors, abs(r - 1.7) - T * 0.2);

    float dash1 = sin(a * 48.0 - t * 3.0) * sin(a * 16.0 + t);
    float dash2 = sin(a * 64.0 + t * 3.0);
    d_anchors = min(d_anchors, abs(r - 1.8) + (1.0 - step(0.1, dash1)) * 5.0);
    d_anchors = min(d_anchors, abs(r - 1.05) + (1.0 - step(0.0, dash2)) * 5.0 + T * 0.5);

    d_geo = min(d_geo, abs(q.x - 1.15));
    d_geo = min(d_geo, abs(dot(q, float2(0.5, 0.866025)) - 1.35));
    d_geo = min(d_geo, abs(q.x - 0.71));
    d_geo = min(d_geo, abs(dot(q, float2(0.5, 0.866025)) - 0.35));

    float archWave = sin(a_fold * 3.0) * 0.275;
    d_arches = min(d_arches, abs(r - (1.425 + archWave)));
    d_arches = min(d_arches, abs(r - (1.425 - archWave)));
    d_arches = min(d_arches, abs(r - (2.6 - sin(a_fold * 15.0 + (t * 5.0)) * 0.2)));

    float moonDist = length(q - float2(1.425, 0.0));
    d_arches = min(d_arches, abs(moonDist - 0.12));
    d_arches = min(d_arches, abs(moonDist - 0.03) + T * 0.5);

    float2 k = q;
    k.x -= 1.425;

    float2 shift = float2(sin(t * 0.8), cos(t * 0.5)) * 0.15;
    float rot = t * 0.6;
    float sRot, cRot;
    sincos(rot, sRot, cRot);
    float2x2 rotMat = float2x2(cRot, -sRot, sRot, cRot);

    [unroll]
    for (int i = 0; i < 3; i++)
    {
        k = abs(k) - shift - float2(0.08, 0.04) * (float)i;
        k = mul(k, rotMat);

        float lenK = length(k);
        d_clock = min(d_clock, abs(lenK - 0.16 + (sin(k.x * 3.1415) * 0.1)) + T * 0.3);
        d_clock = min(d_clock, abs(lenK - 0.5 + (sin(k.y * 9.4245) * 0.1)) + T * 0.3);
    }

    float minD = min(min(d_anchors, d_geo), min(d_arches, d_clock));
    float combinedLayers = 1.0 - smoothstep(T - 0.01, T + 0.01, minD);

    float outerFade = 1.0 - smoothstep(2.2, 2.8, r);

    SealMask = combinedLayers * outerFade;
}

void B2U_Status_Cursed_float(float2 UV, float Time, float Scale, float EvolutionSpeed, float Thickness, out float SealMask)
{
    B2UStatusCursedInternal(UV, Time, Scale, EvolutionSpeed, Thickness, SealMask);
}

void B2U_Status_Cursed_half(float2 UV, float Time, float Scale, float EvolutionSpeed, float Thickness, out float SealMask)
{
    B2UStatusCursedInternal(UV, Time, Scale, EvolutionSpeed, Thickness, SealMask);
}

// Creates sunbursts with light rays at the corners of the screen.
void B2UStatusRaysInternal(float2 UV, float Time, float RayCount, float EdgeFade, float ColorShiftSpeed, UnityTexture2D GradientTex, out float3 RayColor, out float RayAlpha)
{
    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);

    float2 p = UV - 0.5;
    p.x *= aspect;

    float2 q = abs(p);
    float2 corner = float2(0.5 * aspect, 0.5);
    float2 localP = corner - q;

    float dist = length(localP);
    float angle = B2U_SafeAtan2(localP.y, localP.x);

    float t = fmod(Time * 0.5, 628.31853);
    float colorTime = fmod(Time * ColorShiftSpeed, 628.31853);

    float safeEdgeFade = max(EdgeFade, 0.001);
    float baseFalloff = saturate(1.0 - (dist / safeEdgeFade));

    float3 accumulatedColor = float3(0.0, 0.0, 0.0);
    float accumulatedMask = 0.0;

    static const float3 freqMults = float3(0.35, 0.65, 1.0);
    static const float3 timeMults = float3(0.8, -1.1, 1.5);

    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float freq = max(1.0, RayCount * freqMults[i]);

        float rayCoord = (angle * freq + t * timeMults[i]) * 0.15915494;
        float rayID = floor(rayCoord);
        float localUV = frac(rayCoord);

        float sectorDist = abs(localUV * 2.0 - 1.0);

        float2 rayHash = B2U_Hash22(float2(rayID, (float)i * 19.37 + 7.13));

        float pulseSpeed = 1.2 + rayHash.x * 2.8;
        float pulsePhase = rayHash.y * 6.2831853;
        float morph = sin(t * pulseSpeed + pulsePhase) * 0.5 + 1.2;

        float rayHalfWidth = lerp(0.07, 0.92, morph * morph);

        float normDist = saturate(sectorDist / max(rayHalfWidth, 0.001));
        float profile = cos(normDist * 1.5707963);

        float sharpness = lerp(2.2, 0.75, morph);
        float rayShape = pow(max(0.0001, profile), max(sharpness, 0.001));

        float lengthScale = lerp(0.55, 1.35, sin(t * (pulseSpeed * 0.7) + rayHash.x * 6.2831853) * 0.5 + 0.5);
        float rayFalloff = saturate(1.0 - (dist / (safeEdgeFade * lengthScale)));
        float rayAtten = rayFalloff * rayFalloff;

        float intensity = lerp(1.4, 0.85, morph) * (0.7 + 0.3 * rayHash.y);
        float layerMask = rayShape * intensity * rayAtten;

        float gradPhase = rayHash.x * 6.2831853 + colorTime * (0.6 + rayHash.y * 0.8) + (normDist * 0.35) - (dist * 0.5);
        float gradU = sin(gradPhase) * 0.5 + 0.5;

        float3 sectorColor = SAMPLE_TEXTURE2D_LOD(GradientTex, GradientTex.samplerstate, float2(gradU, 0.5), 0).rgb;

        accumulatedColor += sectorColor * layerMask;
        accumulatedMask += layerMask;
    }

    float sun = ((1.0 - smoothstep(0.0, 0.15, dist)) + (1.0 - smoothstep(0.0, 0.4, dist)) * 0.5) * baseFalloff;

    accumulatedColor += sun;
    accumulatedMask = saturate(accumulatedMask + sun);

    RayColor = accumulatedColor;
    RayAlpha = accumulatedMask;
}

void B2U_Status_Rays_float(float2 UV, float Time, float RayCount, float EdgeFade, float ColorShiftSpeed, UnityTexture2D GradientTex, out float3 RayColor, out float RayAlpha)
{
    B2UStatusRaysInternal(UV, Time, RayCount, EdgeFade, ColorShiftSpeed, GradientTex, RayColor, RayAlpha);
}

void B2U_Status_Rays_half(float2 UV, float Time, float RayCount, float EdgeFade, float ColorShiftSpeed, UnityTexture2D GradientTex, out float3 RayColor, out float RayAlpha)
{
    B2UStatusRaysInternal(UV, Time, RayCount, EdgeFade, ColorShiftSpeed, GradientTex, RayColor, RayAlpha);
}

// Generates procedural alien glyphs to overlay on the screen.
void B2UGlitchTextInternal(float2 UV, float Time, float GridScale, float LineThickness, out float TextMask)
{
    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float2 p = (UV - 0.5) * float2(aspect, 1.0) * GridScale;
    float2 id = floor(p);
    float2 gv = frac(p) - 0.5;

    float cellTime = fmod(floor(abs(Time) * 4.0), 1024.0);
    float seed = B2U_Hash12(id + cellTime * 10.0);

    float activeCell = step(0.4, seed);
    float halfThick = LineThickness * 0.5;
    float maxBounds = halfThick + 0.03;

    TextMask = 0.0;

    [branch]
    if (activeCell > 0.5 && abs(gv.x) < (0.2 + maxBounds) && abs(gv.y) < (0.3 + maxBounds))
    {
        float2 p0 = float2(-0.2, -0.3);
        float2 p1 = float2( 0.2, -0.3);
        float2 p2 = float2(-0.2,  0.0);
        float2 p3 = float2( 0.2,  0.0);
        float2 p4 = float2(-0.2,  0.3);
        float2 p5 = float2( 0.2,  0.3);

        float d = 1.0;
        uint hashBits = (uint)(seed * 10000.0);

        if ((hashBits & 1u) != 0u)   d = min(d, B2U_LineSegmentDist(gv, p0, p1));
        if ((hashBits & 2u) != 0u)   d = min(d, B2U_LineSegmentDist(gv, p0, p2));
        if ((hashBits & 4u) != 0u)   d = min(d, B2U_LineSegmentDist(gv, p1, p3));
        if ((hashBits & 8u) != 0u)   d = min(d, B2U_LineSegmentDist(gv, p2, p3));
        if ((hashBits & 16u) != 0u)  d = min(d, B2U_LineSegmentDist(gv, p2, p4));
        if ((hashBits & 32u) != 0u)  d = min(d, B2U_LineSegmentDist(gv, p3, p5));
        if ((hashBits & 64u) != 0u)  d = min(d, B2U_LineSegmentDist(gv, p4, p5));
        if ((hashBits & 128u) != 0u) d = min(d, B2U_LineSegmentDist(gv, p0, p5));

        TextMask = 1.0 - smoothstep(halfThick, halfThick + 0.02, d);
    }
}

void B2U_Status_Glitch_Text_float(float2 UV, float Time, float GridScale, float LineThickness, out float TextMask)
{
    B2UGlitchTextInternal(UV, Time, GridScale, LineThickness, TextMask);
}

void B2U_Status_Glitch_Text_half(float2 UV, float Time, float GridScale, float LineThickness, out float TextMask)
{
    B2UGlitchTextInternal(UV, Time, GridScale, LineThickness, TextMask);
}

// Creates random boxes on screen that offset the screen UVs for a digital glitch effect.
void B2UStatusGlitchInternal(float2 UV, float Time, float JumpSpeed, float2 MaxSize, float ShiftIntensity, out float2 UVOffset)
{
    float2 finalOffset = float2(0.0, 0.0);
    float safeTime = fmod(abs(Time * JumpSpeed), 256.0);

    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float t_step = floor(safeTime + (float)i * 0.37);
        float safeTimeSeed = B2U_Hash12(float2(t_step, (float)i * 7.13)) * 100.0;

        float2 seed = float2((float)i * 12.34, safeTimeSeed);
        float2 center = B2U_Hash22(seed);
        float2 size = B2U_Hash22(seed + 1.42) * MaxSize;
        float2 shift = (B2U_Hash22(seed + 2.84) * 2.0 - 1.0) * ShiftIntensity;

        float2 inside = step(abs(UV - center), size);
        finalOffset += shift * (inside.x * inside.y);
    }

    UVOffset = finalOffset;
}

void B2U_Status_Glitch_float(float2 UV, float Time, float JumpSpeed, float2 MaxSize, float ShiftIntensity, out float2 UVOffset)
{
    B2UStatusGlitchInternal(UV, Time, JumpSpeed, MaxSize, ShiftIntensity, UVOffset);
}

void B2U_Status_Glitch_half(float2 UV, float Time, float JumpSpeed, float2 MaxSize, float ShiftIntensity, out float2 UVOffset)
{
    B2UStatusGlitchInternal(UV, Time, JumpSpeed, MaxSize, ShiftIntensity, UVOffset);
}

// Generates a hexagonal energy shield overlay.
void B2UStatusShieldedInternal(float2 UV, float Time, float Scale, float Spacing, float Strength, float BulgeStrength, out float ShieldMask, out float DistanceMask, out float2 DistortionOffset, out float ColorMask)
{
    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float safeTime = fmod(Time, 628.31853);

    float2 p = UV - 0.5;
    p.x *= aspect;

    float r2 = dot(p, p);
    float bulge = 1.0 + (r2 * BulgeStrength);
    float2 warpedP = p * bulge * Scale;

    const float2 r = float2(1.0, 1.7320508);
    const float2 h = r * 0.5;

    float2 a = warpedP - r * floor(warpedP / r) - h;
    float2 b = (warpedP - h) - r * floor((warpedP - h) / r) - h;

    float2 localUV = (dot(a, a) < dot(b, b)) ? a : b;
    float2 hexID = round((warpedP - localUV) * 100.0) * 0.01;

    float dHex = max(abs(localUV.x), dot(abs(localUV), float2(0.5, 0.866025)));
    float baseSize = max(0.5 - max(Spacing, 0.0), 0.06);

    float hash = B2U_Hash12(hexID);
    float sizeAnim = (sin(safeTime * 3.0 + hash * 6.2831853) * 0.5 + 0.5) * 0.2;
    float currentSize = max(baseSize * (1.0 - sizeAnim), 0.06);

    const float aa = 0.05;
    float hexMask = 1.0 - smoothstep(max(currentSize - aa, 0.0), currentSize, dHex);
    float midSize = max(currentSize * 0.8, aa + 0.001);
    float midRing = 1.0 - smoothstep(midSize - aa, midSize, dHex);
    hexMask -= midRing * 0.5;
    float centerSize = max(currentSize * 0.5, aa + 0.001);
    float centerRing = 1.0 - smoothstep(centerSize - aa, centerSize, dHex);
    hexMask += centerRing * 0.25;

    float hexDist = length(hexID);
    float warpedPixelDist = length(warpedP);

    float maxCorner = length(float2(0.5 * aspect, 0.5));
    float maxCornerBulge = 1.0 + (maxCorner * maxCorner * BulgeStrength);
    float maxDist = (maxCorner * maxCornerBulge * Scale) + 2.0;

    float xEdge = 0.5 * aspect;
    float xEdgeWarped = xEdge * (1.0 + (xEdge * xEdge) * BulgeStrength) * Scale;
    float targetInner = xEdgeWarped * 0.95;

    float outerRadius = lerp(-1.0, maxDist, saturate(Strength * 2.0));
    float innerRadius = lerp(-1.0, targetInner, saturate((Strength - 0.5) * 2.0));

    float gridFade = smoothstep(hexDist - 0.8, hexDist + 0.8, outerRadius);
    gridFade *= smoothstep(innerRadius - 0.8, innerRadius + 0.8, hexDist);

    float softFade = smoothstep(warpedPixelDist - 1.5, warpedPixelDist + 1.5, outerRadius);
    softFade *= smoothstep(innerRadius - 1.5, innerRadius + 1.5, warpedPixelDist);

    float waveStart = innerRadius - 1.5;
    float waveEnd = outerRadius + 1.5;
    float bandWidth = max(waveEnd - waveStart, 0.001);

    float normalizedPos = saturate((warpedPixelDist - waveStart) / bandWidth);
    float waveIntensity = sin(normalizedPos * 3.14159265);

    float pixelDist = sqrt(r2);
    float2 toCenterPhys = -p / (pixelDist + 0.0001);
    float2 toCenterUV = toCenterPhys * float2(1.0 / aspect, 1.0);

    float concentricWave = (hexDist * 0.8) - (safeTime * 3.0);
    float colorPhase = concentricWave + (hexMask * 3.14159265);
    ColorMask = sin(colorPhase) * 0.5 + 0.5;

    ShieldMask = saturate(hexMask * gridFade);
    DistanceMask = saturate(softFade);
    DistortionOffset = toCenterUV * waveIntensity;
}

void B2U_Status_Shielded_float(float2 UV, float Time, float Scale, float Spacing, float Strength, float BulgeStrength, out float ShieldMask, out float DistanceMask, out float2 DistortionOffset, out float ColorMask)
{
    B2UStatusShieldedInternal(UV, Time, Scale, Spacing, Strength, BulgeStrength, ShieldMask, DistanceMask, DistortionOffset, ColorMask);
}

void B2U_Status_Shielded_half(float2 UV, float Time, float Scale, float Spacing, float Strength, float BulgeStrength, out float ShieldMask, out float DistanceMask, out float2 DistortionOffset, out float ColorMask)
{
    B2UStatusShieldedInternal(UV, Time, Scale, Spacing, Strength, BulgeStrength, ShieldMask, DistanceMask, DistortionOffset, ColorMask);
}

#endif