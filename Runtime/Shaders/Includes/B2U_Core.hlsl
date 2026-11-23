#ifndef B2U_CORE_INCLUDED
#define B2U_CORE_INCLUDED

#if !defined(SHADERGRAPH_PREVIEW)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Constants.hlsl"

// --- HELPER FUNCTIONS ---

// Struct for storing 8 bools in a bitmask
struct Bitmask
{
    bool bit0;
    bool bit1;
    bool bit2;
    bool bit3;
    bool bit4;
    bool bit5;
    bool bit6;
    bool bit7;
};

// Internal function for solving 8-bit bitmasks packed in a 0-1 byte
Bitmask Decode_Bitmask(half In)
{
    Bitmask bm = (Bitmask)0;
    uint bitmask = (uint)clamp(round((float)In * 255.0), 0.0, 255.0);

    bm.bit0 = (bitmask & (1u << 0)) != 0u;
    bm.bit1 = (bitmask & (1u << 1)) != 0u;
    bm.bit2 = (bitmask & (1u << 2)) != 0u;
    bm.bit3 = (bitmask & (1u << 3)) != 0u;
    bm.bit4 = (bitmask & (1u << 4)) != 0u;
    bm.bit5 = (bitmask & (1u << 5)) != 0u;
    bm.bit6 = (bitmask & (1u << 6)) != 0u;
    bm.bit7 = (bitmask & (1u << 7)) != 0u;

    return bm;
}

// Remap function identical to Shader Graph's, preserving negative/inverted input ranges safely
float Remap(float In, float2 InMinMax, float2 OutMinMax)
{
    float diff = InMinMax.y - InMinMax.x;
    float safeDiff = abs(diff) < 1e-5 ? (diff < 0.0 ? -1e-5 : 1e-5) : diff;
    return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / safeDiff;
}

// Helper functions for 3D Simplex Noise
float3 mod289(float3 x)
{
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 mod289(float4 x)
{
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 permute(float4 x)
{
    return mod289(((x * 34.0) + 1.0) * x);
}

float4 taylorInvSqrt(float4 r)
{
    return 1.79284291400159 - 0.85373472095314 * r;
}

// 3D Simplex Noise
float snoise(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);

    float3 i = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);

    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);

    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - D.yyy;

    i = mod289(i);
    float4 p = permute(permute(permute(
                i.z + float4(0.0, i1.z, i2.z, 1.0))
              + i.y + float4(0.0, i1.y, i2.y, 1.0))
              + i.x + float4(0.0, i1.x, i2.x, 1.0));

    float n_ = 0.142857142857;
    float3 ns = n_ * D.wyz - D.xzx;

    float4 j = p - 49.0 * floor(p * ns.z * ns.z);

    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_);

    float4 x = x_ * ns.x + ns.yyyy;
    float4 y = y_ * ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x) - abs(y);

    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);

    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, float4(0.0, 0.0, 0.0, 0.0));

    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

    float3 p0 = float3(a0.xy, h.x);
    float3 p1 = float3(a0.zw, h.y);
    float3 p2 = float3(a1.xy, h.z);
    float3 p3 = float3(a1.zw, h.w);

    float4 norm = taylorInvSqrt(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;

    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

// 3D Hash for Voronoi random point generation (always evaluated in 32-bit float)
float3 voronoi_hash3D(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453123);
}

// Sine-free 3D hash for generating random 3D offsets
float3 hash13(float p)
{
    float3 p3 = frac(p * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

// Procedural caustics with chromatic dispersion and NaN-safe tanh clamping. Derived from @XorDev GLSL snippet on twitter: https://x.com/XorDev/status/1774094322637340791?a=qeLwvQg0w3J0Gr5OdMk&s=20
half3 B2UProceduralCausticsInternal(float2 uv, float time, half viscosity, half ior)
{
    float2 p = uv;
    const float2x2 m = float2x2(0.6666, 0.8888, -0.8888, 0.6666);

    // Viscosity 0 = Fast (5.0), Viscosity 1 = Thick/Slow (0.5)
    float speed = time * lerp(5.0, 0.5, (float)viscosity);

    [unroll]
    for (int i = 1; i <= 5; i++)
    {
        p += sin(p + speed + (float)i) * 0.4;
        p = mul(m, p);
    }

    // IOR drives the CA split distance (IOR 1.0 = 0 split, 2.0 = max split)
    float split = ((float)ior - 1.0) * 2.0;

    float2 domR = sin((p + split) * 0.3) * 10.0;
    float2 domG = sin(p * 0.3) * 10.0;
    float2 domB = sin((p - split) * 0.3) * 10.0;

    // Clamp fwidth length to <= 10.0 before tanh() to prevent exp(2x) Inf/Inf = NaN at depth/UV discontinuities
    half r = (half)tanh(min(length(fwidth(domR)), 10.0));
    half g = (half)tanh(min(length(fwidth(domG)), 10.0));
    half b = (half)tanh(min(length(fwidth(domB)), 10.0));

    return half3(r, g, b);
}

// Hash for creating distance-to-edge Voronoi cracks
float3 crack_hash3D(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));

    return frac(sin(p) * 43758.5453123);
}

// Lightweight 1-pass lookup: solves the closest Voronoi cell and its hash (Used by internal crystal facets)
void B2UCrackCellHash3DInternal(float3 pos, out float3 cellHash, out float3 outP, out float3 outF, out float3 outMR, out float3 outClosestLattice)
{
    pos += float3(137.149, 213.821, 394.333);
    outP = floor(pos);
    outF = frac(pos);

    float minDist = 100.0;
    outMR = float3(0.0, 0.0, 0.0);
    cellHash = float3(0.0, 0.0, 0.0);
    outClosestLattice = float3(0.0, 0.0, 0.0);

    [unroll]
    for (int k = -1; k <= 1; k++)
    {
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                float3 lattice = float3(i, j, k);
                float3 h = crack_hash3D(outP + lattice);
                float3 r = lattice + h - outF;
                float d = dot(r, r);

                if (d < minDist)
                {
                    minDist = d;
                    outMR = r;
                    cellHash = h;
                    outClosestLattice = lattice;
                }
            }
        }
    }
}

// Overload for internal crystal refraction when only the cell hash is needed
void B2UCrackCellHash3DInternal(float3 pos, out float3 cellHash)
{
    float3 p, f, mr, closestLattice;
    B2UCrackCellHash3DInternal(pos, cellHash, p, f, mr, closestLattice);
}

// Full 2-pass lookup: solves continuous distance-to-edge for surface cracks
float B2UCrackData3DInternal(float3 pos, out float3 cellHash)
{
    float3 p, f, mr, closest_lattice;
    B2UCrackCellHash3DInternal(pos, cellHash, p, f, mr, closest_lattice);

    float edgeDist = 100.0;

    [unroll]
    for (int z = -1; z <= 1; z++)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                float3 lattice = closest_lattice + float3(x, y, z);
                float3 h = crack_hash3D(p + lattice);
                float3 r = lattice + h - f;

                float3 diff = r - mr;
                float diffSq = dot(diff, diff);
                if (diffSq > 0.00001)
                {
                    float d = dot(0.5 * (mr + r), diff * rsqrt(diffSq));
                    if (d < edgeDist)
                    {
                        edgeDist = d;
                    }
                }
            }
        }
    }

    return max(edgeDist, 0.0);
}

// Function for single-channel 3D Simplex noise
void B2USimplex3DMonoInternal(float3 Position, float Scale, float Strength, out float Out)
{
    [branch]
    if (Scale != 0.0 && Strength != 0.0)
    {
        Out = (snoise(Position * Scale) * Strength) * 0.5 + 0.5;
    }
    else
    {
        Out = 0.5;
    }
}

// Function for 3-channel 3D Simplex noise
void B2USimplex3DDirectionalInternal(float3 Position, float Scale, float Strength, out float3 Out)
{
    [branch]
    if (Scale != 0.0 && Strength != 0.0)
    {
        float3 pos = Position * Scale;
        float x = snoise(pos);
        float y = snoise(pos + float3(127.1, 311.7, 74.7));
        float z = snoise(pos + float3(269.5, 183.3, 246.1));

        Out = float3(x, y, z) * Strength;
    }
    else
    {
        Out = float3(0.0, 0.0, 0.0);
    }
}

// Function for single-channel 3D Voronoi
void B2UVoronoi3DMonoInternal(float3 Position, float Scale, float Randomness, bool vertex, float Angle_Offset, out float Out, out float Cells)
{
    [branch]
    if (Scale != 0.0)
    {
        Position *= Scale;
        float3 g = floor(Position);
        float3 f = frac(Position);

        float minDist = 8.0;
        float cellID = 0.0;

        [unroll]
        for (int k = -1; k <= 1; k++)
        {
            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    float3 lattice = float3((float)i, (float)j, (float)k);

                    float3 raw_hash = voronoi_hash3D(g + lattice);
                    float3 offset = raw_hash;

                    #if defined(_B2U_ANGLE_OFFSET)
                    offset = 0.5 + 0.5 * sin(Angle_Offset + offset * 6.2831853);
                    #endif

                    offset *= Randomness;

                    float3 diff = lattice + offset - f;
                    float d = dot(diff, diff);

                    if (d < minDist)
                    {
                        minDist = d;
                        cellID = raw_hash.x;
                    }
                }
            }
        }

        [branch]
        if (vertex)
        {
            Out = saturate(sqrt(minDist) / 1.732);
        }
        else
        {
            Out = smoothstep(0.0, 0.85, sqrt(minDist));
        }

        Cells = cellID;
    }
    else
    {
        Out = 0.0;
        Cells = 0.0;
    }
}

// Function for single-channel 3D Voronoi with gradient vectors for normal displacement
void B2UVoronoi3DAnalyticalInternal(float3 Position, float Scale, float Randomness, bool vertex, float Angle_Offset, out float3 Gradient, out float Height)
{
    [branch]
    if (Scale != 0.0)
    {
        Position *= Scale;
        float3 g = floor(Position);
        float3 f = frac(Position);

        float minDist = 8.0;
        float3 minVec = float3(0.0, 0.0, 0.0);

        [unroll]
        for (int k = -1; k <= 1; k++)
        {
            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    float3 lattice = float3((float)i, (float)j, (float)k);
                    float3 offset = voronoi_hash3D(g + lattice);

                    #if defined(_B2U_ANGLE_OFFSET)
                    offset = 0.5 + 0.5 * sin(Angle_Offset + offset * 6.2831853);
                    #endif

                    offset *= Randomness;

                    float3 diff = lattice + offset - f;
                    float d = dot(diff, diff);

                    if (d < minDist)
                    {
                        minDist = d;
                        minVec = diff;
                    }
                }
            }
        }

        [branch]
        if (vertex)
        {
            Height = saturate(sqrt(minDist) / 1.732);
        }
        else
        {
            Height = smoothstep(0.0, 0.85, sqrt(minDist));
        }

        Gradient = -minVec;
    }
    else
    {
        Height = 0.0;
        Gradient = float3(0.0, 0.0, 0.0);
    }
}

// Helper for sampling emission textures (uses 32-bit float time/hashes to prevent FP16 overflow)
half SampleEmissionMap(float2 UV_X, float2 UV_Y, float2 UV_Z, half Emission_Mode, half3 Blend_Weights, half Emission_Strength, half Global_Strength_Mult,
    half Emission_Speed, half Global_Speed_Mult, UnityTexture2D Emission_Patterns, UnitySamplerState Sampler)
{
    half Emission = 0.0;

    float2 dxX = ddx(UV_X.xy);
    float2 dyX = ddy(UV_X.xy);
    float2 dxY = ddx(UV_Y.xy);
    float2 dyY = ddy(UV_Y.xy);
    float2 dxZ = ddx(UV_Z.xy);
    float2 dyZ = ddy(UV_Z.xy);

    float totalStrength = (float)Emission_Strength * (float)Global_Strength_Mult;
    float totalSpeed = (float)Emission_Speed * (float)Global_Speed_Mult;
    int mode = (int)round(Emission_Mode);

    [branch]
    switch (mode)
    {
        case 1:
            Emission = (half)totalStrength;
            break;
        case 2:
            Emission = (half)(((sin(_Time.y * totalSpeed) + 1.0) * 0.5) * totalStrength);
            break;
        case 3:
        {
            float t = _Time.w * totalSpeed;
            float i = floor(t);
            float f = frac(t);
            float u = f * f * (3.0 - 2.0 * f);
            float hash0 = frac(sin(i) * 43758.5453123);
            float hash1 = frac(sin(i + 1.0) * 43758.5453123);
            Emission = (half)(lerp(hash0, hash1, u) * totalStrength);
            break;
        }
        case 4:
        case 5:
        case 6:
        case 7:
        {
            // Channel selector mask (4 = R, 5 = G, 6 = B, 7 = A)
            half4 chMask = half4(mode == 4, mode == 5, mode == 6, mode == 7);
            half sX = dot((half4)SAMPLE_TEXTURE2D_GRAD(Emission_Patterns, Sampler, UV_X, dxX, dyX), chMask);
            half sY = dot((half4)SAMPLE_TEXTURE2D_GRAD(Emission_Patterns, Sampler, UV_Y, dxY, dyY), chMask);
            half sZ = dot((half4)SAMPLE_TEXTURE2D_GRAD(Emission_Patterns, Sampler, UV_Z, dxZ, dyZ), chMask);

            float blendedSample = (float)dot(Blend_Weights, half3(sX, sY, sZ)) * 10.0;
            Emission = (half)(((sin(blendedSample + _Time.y * totalSpeed) + 1.0) * 0.5) * totalStrength);
            break;
        }
    }
    return Emission;
}

void SampleColorLUTRow(UnityTexture2D Biome_Tex_1, UnityTexture2D Biome_Tex_2, float3 World_Position, float2 World_Size, UnityTexture2D Color_LUT,
    half Variant_Index, UnitySamplerState Sampler, half Num_Subtypes, half Material_ID, bool Is_Biome_Responsive, out half Color_Alpha, out half Emission_Alpha,
    out half3 Blended_Color, out half3 Blended_Emission)
{
    float lutWidth = ((float)Num_Subtypes * 8.0) + 8.0;
    float emissionStartIndex = 8.0 * (float)Num_Subtypes;

    #if defined(_IS_BIOME_RESPONSIVE)
        [branch]
        if (Is_Biome_Responsive)
        {
            float2 safeWorldSize = max(abs(World_Size), float2(0.0001, 0.0001));
            float2 biomeUV = (World_Position.xz / safeWorldSize) + 0.5;

            half4 t1 = SAMPLE_TEXTURE2D_LOD(Biome_Tex_1, Sampler, biomeUV, 0);
            half4 t2 = SAMPLE_TEXTURE2D_LOD(Biome_Tex_2, Sampler, biomeUV, 0);

            half w0 = t1.r;
            half w1 = t1.g;
            half w2 = t1.b;
            half w3 = t1.a;
            half w4 = t2.r;
            half w5 = t2.g;
            half w6 = t2.b;

            half totalNaturalWeight = w0 + w1 + w2 + w3 + w4 + w5 + w6;
            half w7 = saturate(1.0 - totalNaturalWeight);

            half weights[8] = { w0, w1, w2, w3, w4, w5, w6, w7 };
            half3 finalColor = half3(0.0, 0.0, 0.0);
            half3 finalEmissionColor = half3(0.0, 0.0, 0.0);

            float maxSubtype = max(1.0, (float)Num_Subtypes) - 1.0;
            float subtypeIndex = saturate((float)t2.a) * maxSubtype;

            Color_Alpha = 0.0;
            Emission_Alpha = 0.5;

            [unroll]
            for (int i = 0; i < 8; i++)
            {
                if (weights[i] > 0.01)
                {
                    float xColorIndex = ((float)i * (float)Num_Subtypes) + subtypeIndex;
                    float xColorUV = (xColorIndex + 0.5) / lutWidth;
                    half4 sampledColor = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, float2(xColorUV, (float)Material_ID), 0);
                    finalColor += sampledColor.rgb * weights[i];
                    Color_Alpha = sampledColor.a;

                    float xEmisIndex = emissionStartIndex + (float)i;
                    float xEmisUV = (xEmisIndex + 0.5) / lutWidth;
                    half4 sampledEmis = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, float2(xEmisUV, (float)Material_ID), 0);
                    finalEmissionColor += sampledEmis.rgb * weights[i];
                    Emission_Alpha = sampledEmis.a;
                }
            }
            Blended_Color = finalColor;
            Blended_Emission = finalEmissionColor;
        }
        else
        {
            float2 cUV = float2((((float)Variant_Index * (float)Num_Subtypes) + 0.5) / lutWidth, (float)Material_ID);
            half4 c = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, cUV, 0);
            Blended_Color = c.rgb;
            Color_Alpha = c.a;

            float2 eUV = float2(((float)Variant_Index + emissionStartIndex + 0.5) / lutWidth, (float)Material_ID);
            half4 e = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, eUV, 0);
            Blended_Emission = e.rgb;
            Emission_Alpha = e.a;
        }
    #else
        float2 cUV = float2((((float)Variant_Index * (float)Num_Subtypes) + 0.5) / lutWidth, (float)Material_ID);
        half4 c = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, cUV, 0);
        Blended_Color = c.rgb;
        Color_Alpha = c.a;

        float2 eUV = float2(((float)Variant_Index + emissionStartIndex + 0.5) / lutWidth, (float)Material_ID);
        half4 e = SAMPLE_TEXTURE2D_LOD(Color_LUT, Sampler, eUV, 0);
        Blended_Emission = e.rgb;
        Emission_Alpha = e.a;
    #endif
}

// Unpacks 0-1 data from the LUTs to the schema-defined range
half UnpackLUT(half rawSample, half2 minMax)
{
    return mad(rawSample, minMax.y - minMax.x, minMax.x);
}

// --- END HELPER FUNCTIONS ---



// --- GENERAL NODES ---

// Creates procedural cracks on the surface of solids/crystals
void B2UApplySurfaceCracksInternal(half3 Normal_WS, float3 Pos_WS, float Crack_Scale, half Crack_Intensity, out half3 Out_Normal_WS, out float Out_Crack_Mask)
{
    Out_Normal_WS = Normal_WS;
    Out_Crack_Mask = 0.0;

    [branch]
    if (Crack_Intensity > 0.0)
    {
        #if !defined(SHADERGRAPH_PREVIEW)
            float fixedCrackDepth = 0.75;

            float3 Pos_OS = mul(GetWorldToObjectMatrix(), float4(Pos_WS, 1.0)).xyz;
            float3 cellHashOS;

            float edgeDist = B2UCrackData3DInternal(Pos_OS * Crack_Scale, cellHashOS);
            float noiseMask = snoise(Pos_OS * Crack_Scale * 0.5) * 0.5 + 0.5;
            float crackVisibility = smoothstep(1.0 - (float)Crack_Intensity, 1.05 - (float)Crack_Intensity, noiseMask);

            half3 cellHashWS = (half3)mul((float3x3)GetObjectToWorldMatrix(), cellHashOS);

            float surfCrackMask = 1.0 - smoothstep(0.0, 0.05, edgeDist);

            Out_Crack_Mask = surfCrackMask * crackVisibility;
            Out_Normal_WS = normalize(Normal_WS + (cellHashWS * (half)(Out_Crack_Mask * fixedCrackDepth)));
        #endif
    }
}

void B2U_Apply_Surface_Cracks_float(half3 Normal_WS, float3 Pos_WS, float Crack_Scale, half Crack_Intensity, out half3 Out_Normal_WS, out float Out_Crack_Mask)
{
    B2UApplySurfaceCracksInternal(Normal_WS, Pos_WS, Crack_Scale, Crack_Intensity, Out_Normal_WS, Out_Crack_Mask);
}

void B2U_Apply_Surface_Cracks_half(half3 Normal_WS, float3 Pos_WS, float Crack_Scale, half Crack_Intensity, out half3 Out_Normal_WS, out float Out_Crack_Mask)
{
    B2UApplySurfaceCracksInternal(Normal_WS, Pos_WS, Crack_Scale, Crack_Intensity, Out_Normal_WS, Out_Crack_Mask);
}


// Solves the iridescence LUT lookup color shifting
void B2UIridescenceInternal(bool Is_Iridescent, half Iridescence_Mode, half3 Transformed_Normal_WS, half3 View_WS, half3 In_Color, half3 In_Emission,
    half Iridescence_Strength, half Iridescence_Power, UnityTexture2D Iridescence_LUT, UnitySamplerState Sampler, float3 Position_WS, out half3 Out_Color,
    out half3 Out_Emission)
{
    Out_Color = In_Color;
    Out_Emission = In_Emission;

    [branch]
    if (Is_Iridescent)
    {
        half decodedMode = round(Iridescence_Mode * 8.0);
        half lutRow = clamp(decodedMode - 1.0, 0.0, 7.0);
        float vUV = ((7.0 - (float)lutRow) + 0.5) / 8.0;

        float safePower = max((float)Iridescence_Power, 0.00001);
        float NdotV = pow(max(saturate(dot((float3)Transformed_Normal_WS, (float3)View_WS)), 0.00001), safePower);
        half3 ambientIrid = (half3)SAMPLE_TEXTURE2D_LOD(Iridescence_LUT, Sampler, float2(NdotV, vUV), 0).rgb;

        half3 specularIrid = half3(0.0, 0.0, 0.0);

        #ifndef SHADERGRAPH_PREVIEW
            #if defined(_B2U_IRIDESCENCE_MAIN_LIGHT) || defined(_B2U_IRIDESCENCE_ALL_LIGHTS)
                Light mainLight = GetMainLight();
                half NdotL_main = max(saturate(dot(Transformed_Normal_WS, mainLight.direction)), 0.00001);

                float3 H_main = SafeNormalize((float3)mainLight.direction + (float3)View_WS);
                float NdotH_main = pow(max(saturate(dot((float3)Transformed_Normal_WS, H_main)), 0.00001), safePower);
                half3 mainIrid = (half3)SAMPLE_TEXTURE2D_LOD(Iridescence_LUT, Sampler, float2(NdotH_main, vUV), 0).rgb;

                specularIrid += mainIrid * mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation * NdotL_main);
            #endif

            #if defined(_B2U_IRIDESCENCE_ALL_LIGHTS)
                InputData inputData = (InputData)0;
                float4 clipPos = TransformWorldToHClip(Position_WS);
                float4 screenPos = ComputeScreenPos(clipPos);
                inputData.normalizedScreenSpaceUV = screenPos.xy / max(screenPos.w, 0.0001);
                inputData.positionWS = Position_WS;

                uint pixelLightCount = GetAdditionalLightsCount();
                uint lightLimit = 0u;

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    if (lightLimit < 4u)
                    {
                        Light light = GetAdditionalLight(lightIndex, Position_WS, half4(1.0, 1.0, 1.0, 1.0));
                        half NdotL = max(saturate(dot(Transformed_Normal_WS, light.direction)), 0.00001);

                        float3 H = SafeNormalize((float3)light.direction + (float3)View_WS);
                        float NdotH = pow(max(saturate(dot((float3)Transformed_Normal_WS, H)), 0.00001), safePower);
                        half3 addIrid = (half3)SAMPLE_TEXTURE2D_LOD(Iridescence_LUT, Sampler, float2(NdotH, vUV), 0).rgb;

                        specularIrid += addIrid * light.color * (light.distanceAttenuation * NdotL);
                        lightLimit++;
                    }
                LIGHT_LOOP_END
            #endif
        #endif

        #if defined(_B2U_IRIDESCENCE_MAIN_LIGHT) || defined(_B2U_IRIDESCENCE_ALL_LIGHTS)
            Out_Color = saturate(In_Color + ((ambientIrid * 0.15 + specularIrid) * Iridescence_Strength));
        #else
            Out_Color = saturate(In_Color + (ambientIrid * Iridescence_Strength));
        #endif

        Out_Emission = In_Emission + (ambientIrid * Iridescence_Strength);
    }
}

void B2U_Iridescence_float(bool Is_Iridescent, half Iridescence_Mode, half3 Transformed_Normal_WS, half3 View_WS, half3 In_Color, half3 In_Emission,
    half Iridescence_Strength, half Iridescence_Power, UnityTexture2D Iridescence_LUT, UnitySamplerState Sampler, float3 Position_WS, out half3 Out_Color,
    out half3 Out_Emission)
{
    B2UIridescenceInternal(Is_Iridescent, Iridescence_Mode, Transformed_Normal_WS, View_WS, In_Color, In_Emission, Iridescence_Strength, Iridescence_Power, Iridescence_LUT, Sampler, Position_WS, Out_Color, Out_Emission);
}

void B2U_Iridescence_half(bool Is_Iridescent, half Iridescence_Mode, half3 Transformed_Normal_WS, half3 View_WS, half3 In_Color, half3 In_Emission,
    half Iridescence_Strength, half Iridescence_Power, UnityTexture2D Iridescence_LUT, UnitySamplerState Sampler, float3 Position_WS, out half3 Out_Color,
    out half3 Out_Emission)
{
    B2UIridescenceInternal(Is_Iridescent, Iridescence_Mode, Transformed_Normal_WS, View_WS, In_Color, In_Emission, Iridescence_Strength, Iridescence_Power, Iridescence_LUT, Sampler, Position_WS, Out_Color, Out_Emission);
}


// Helper function to unpack 4 half-precision values from 2 single-precision UV values
void UnpackFourFloatsInternal(float2 packedUV, out half4 Out)
{
    uint packedX = asuint(packedUV.x);
    uint packedY = asuint(packedUV.y);

    half val1 = (half)f16tof32(packedX >> 16);
    half val2 = (half)f16tof32(packedX & 0xFFFFu);

    half val3 = (half)f16tof32(packedY >> 16);
    half val4 = (half)f16tof32(packedY & 0xFFFFu);

    Out = half4(val1, val2, val3, val4);
}

void UnpackFourFloats_float(float2 packedUV, out half4 Out)
{
    UnpackFourFloatsInternal(packedUV, Out);
}

void UnpackFourFloats_half(float2 packedUV, out half4 Out)
{
    UnpackFourFloatsInternal(packedUV, Out);
}


// Shader Graph node for decoding bitmask values
void B2UDecodeBitmaskInternal(half In, out bool Bit0, out bool Bit1, out bool Bit2, out bool Bit3, out bool Bit4, out bool Bit5, out bool Bit6, out bool Bit7)
{
    Bitmask bm = Decode_Bitmask(In);

    Bit0 = bm.bit0;
    Bit1 = bm.bit1;
    Bit2 = bm.bit2;
    Bit3 = bm.bit3;
    Bit4 = bm.bit4;
    Bit5 = bm.bit5;
    Bit6 = bm.bit6;
    Bit7 = bm.bit7;
}

void B2U_Decode_Bitmask_float(half In, out bool Bit0, out bool Bit1, out bool Bit2, out bool Bit3, out bool Bit4, out bool Bit5, out bool Bit6, out bool Bit7)
{
    B2UDecodeBitmaskInternal(In, Bit0, Bit1, Bit2, Bit3, Bit4, Bit5, Bit6, Bit7);
}

void B2U_Decode_Bitmask_half(half In, out bool Bit0, out bool Bit1, out bool Bit2, out bool Bit3, out bool Bit4, out bool Bit5, out bool Bit6, out bool Bit7)
{
    B2UDecodeBitmaskInternal(In, Bit0, Bit1, Bit2, Bit3, Bit4, Bit5, Bit6, Bit7);
}


// DEPRECATED: Dummy node needed to force the vertex stage to pass necessary data to the tessellator
void B2U_Dummy_Tess_Varyings_float(float3 Force_Pos_WS, float3 Force_Pos_OS, float3 Force_Norm_WS, float3 Force_Norm_OS, float4 Force_UV0, float4 Force_UV1, float4 Force_UV2, out float Active)
{
    Active = 1.0;
}

void B2U_Dummy_Tess_Varyings_half(float3 Force_Pos_WS, float3 Force_Pos_OS, float3 Force_Norm_WS, float3 Force_Norm_OS, float4 Force_UV0, float4 Force_UV1, float4 Force_UV2, out float Active)
{
    Active = 1.0;
}


// Shader Graph node for single-channel 3D Simplex noise
void B2U_Simplex3D_Mono_float(float3 Position, float Scale, float Strength, out float Out)
{
    B2USimplex3DMonoInternal(Position, Scale, Strength, Out);
}

void B2U_Simplex3D_Mono_half(float3 Position, float Scale, float Strength, out float Out)
{
    B2USimplex3DMonoInternal(Position, Scale, Strength, Out);
}


// Shader Graph node for 3-channel 3D Simplex noise
void B2U_Simplex3D_Directional_float(float3 Position, float Scale, float Strength, out float3 Out)
{
    B2USimplex3DDirectionalInternal(Position, Scale, Strength, Out);
}

void B2U_Simplex3D_Directional_half(float3 Position, float Scale, float Strength, out float3 Out)
{
    B2USimplex3DDirectionalInternal(Position, Scale, Strength, Out);
}


// Shader Graph node for single-channel 3D Voronoi noise
void B2U_Voronoi3D_Mono_float(float3 Position, float Scale, float Randomness, float Angle_Offset, bool vertex, out float Out, out float Cells)
{
    B2UVoronoi3DMonoInternal(Position, Scale, Randomness, vertex, Angle_Offset, Out, Cells);
}

void B2U_Voronoi3D_Mono_half(float3 Position, float Scale, float Randomness, float Angle_Offset, bool vertex, out float Out, out float Cells)
{
    B2UVoronoi3DMonoInternal(Position, Scale, Randomness, vertex, Angle_Offset, Out, Cells);
}


// Shader Graph node for analytical 3D Voronoi noise where the gradient vector returned represents the shift of the surface normal based on the height differential
void B2U_Voronoi3D_Analytical_float(float3 Position, float Scale, float Randomness, float Angle_Offset, bool vertex, out float3 Gradient, out float Height)
{
    B2UVoronoi3DAnalyticalInternal(Position, Scale, Randomness, vertex, Angle_Offset, Gradient, Height);
}

void B2U_Voronoi3D_Analytical_half(float3 Position, float Scale, float Randomness, float Angle_Offset, bool vertex, out float3 Gradient, out float Height)
{
    B2UVoronoi3DAnalyticalInternal(Position, Scale, Randomness, vertex, Angle_Offset, Gradient, Height);
}

// --- END OTHER NODES ---
#endif