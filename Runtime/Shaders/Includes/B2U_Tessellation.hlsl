#ifndef B2U_TESSELLATION_INCLUDED
#define B2U_TESSELLATION_INCLUDED

#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Core.hlsl"
#include "Packages/ca.dnart.b2umaterialmaster/Runtime/Shaders/Includes/B2U_Liquid.hlsl"

#pragma require tessellation
#pragma hull hull
#pragma domain domain

#define UNITY_CAN_COMPILE_TESSELLATION 1
#ifndef UNITY_domain
    #define UNITY_domain                 domain
    #define UNITY_partitioning           partitioning
    #define UNITY_outputtopology         outputtopology
    #define UNITY_patchconstantfunc      patchconstantfunc
    #define UNITY_outputcontrolpoints    outputcontrolpoints
#endif

// Triangle Tessellation Factors (3 edges, 1 inside)
struct TessellationFactors
{
    float edge[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
};

[UNITY_domain("tri")]
[UNITY_outputcontrolpoints(3)]
[UNITY_outputtopology("triangle_cw")]
[UNITY_partitioning("fractional_odd")]
[UNITY_patchconstantfunc("patchConstantFunction")]
PackedVaryings hull(InputPatch<PackedVaryings, 3> patch, uint id : SV_OutputControlPointID)
{
    return patch[id];
}

TessellationFactors UnityCalcTriEdgeTessFactors(float3 triVertexFactors)
{
    TessellationFactors tess;
    tess.edge[0] = 0.5 * (triVertexFactors.y + triVertexFactors.z);
    tess.edge[1] = 0.5 * (triVertexFactors.x + triVertexFactors.z);
    tess.edge[2] = 0.5 * (triVertexFactors.x + triVertexFactors.y);
    tess.inside = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0;
    return tess;
}

float CalcDistanceTessFactor(float3 positionWS, float minDist, float maxDist, float tess)
{
    float dist = distance(positionWS, _WorldSpaceCameraPos.xyz);
    float distRange = max(maxDist - minDist, 0.0001);
    float falloff = saturate(1.0 - (dist - minDist) / distRange);
    return lerp(1.0, max(tess, 1.0), falloff);
}

TessellationFactors DistanceBasedTess(float3 p0WS, float3 p1WS, float3 p2WS, float minDist, float maxDist, float tess)
{
    float3 f;
    f.x = CalcDistanceTessFactor(p0WS, minDist, maxDist, tess);
    f.y = CalcDistanceTessFactor(p1WS, minDist, maxDist, tess);
    f.z = CalcDistanceTessFactor(p2WS, minDist, maxDist, tess);
    return UnityCalcTriEdgeTessFactors(f);
}

TessellationFactors patchConstantFunction(InputPatch<PackedVaryings, 3> patch)
{
    float maxSubdivision = max((float)_B2U_Tess, 1.0);
    float minDist = _B2U_Min_Tessellation_Distance;
    float maxDist = max(_B2U_Max_Tessellation_Distance, minDist + 0.001);

#if defined(VARYINGS_NEED_POSITION_WS)
    Varyings v0 = UnpackVaryings(patch[0]);
    Varyings v1 = UnpackVaryings(patch[1]);
    Varyings v2 = UnpackVaryings(patch[2]);

    return DistanceBasedTess(v0.positionWS, v1.positionWS, v2.positionWS, minDist, maxDist, maxSubdivision);
#else
    TessellationFactors f;
    f.edge[0] = maxSubdivision;
    f.edge[1] = maxSubdivision;
    f.edge[2] = maxSubdivision;
    f.inside = maxSubdivision;
    return f;
#endif
}

float3 OrtoProject(float3 interpolatedPos, float3 patchPos, float3 patchNormal)
{
    return interpolatedPos - dot(interpolatedPos - patchPos, patchNormal) * patchNormal;
}

// Custom macro for interpolating unpacked Varyings fields
#define UNPACKED_INTERPOLATE(fieldName) v.fieldName = v0.fieldName * x + v1.fieldName * y + v2.fieldName * z;

// Triangle Domain Solver
[UNITY_domain("tri")]
PackedVaryings domain(TessellationFactors factors, OutputPatch<PackedVaryings, 3> patch, float3 barycentricCoordinates : SV_DomainLocation)
{
    float x = barycentricCoordinates.x;
    float y = barycentricCoordinates.y;
    float z = barycentricCoordinates.z;

    Varyings v0 = UnpackVaryings(patch[0]);
    Varyings v1 = UnpackVaryings(patch[1]);
    Varyings v2 = UnpackVaryings(patch[2]);

    UNITY_SETUP_INSTANCE_ID(v0);

    Varyings v = v0;
    UNITY_TRANSFER_VERTEX_OUTPUT_STEREO(v0, v);

    #ifdef VARYINGS_NEED_POSITION_CS
        UNPACKED_INTERPOLATE(positionCS)
    #endif
    #ifdef VARYINGS_NEED_POSITION_WS
        UNPACKED_INTERPOLATE(positionWS)
    #endif
    #ifdef VARYINGS_NEED_NORMAL_WS
        v.normalWS = SafeNormalize(v0.normalWS * x + v1.normalWS * y + v2.normalWS * z);
    #endif
    #ifdef VARYINGS_NEED_TEXCOORD0
        UNPACKED_INTERPOLATE(texCoord0)
    #endif
    #ifdef VARYINGS_NEED_TEXCOORD1
        UNPACKED_INTERPOLATE(texCoord1)
    #endif
    #ifdef VARYINGS_NEED_TEXCOORD2
        UNPACKED_INTERPOLATE(texCoord2)
    #endif
    #ifdef VARYINGS_NEED_TEXCOORD3
        UNPACKED_INTERPOLATE(texCoord3)
    #endif

    const float phongSmooth = 0.9;

#if defined(VARYINGS_NEED_POSITION_WS) && defined(VARYINGS_NEED_NORMAL_WS) && defined(VARYINGS_NEED_TEXCOORD0) && defined(VARYINGS_NEED_TEXCOORD1)
    // A. Phong Smoothing
    v.positionWS = (1.0 - phongSmooth) * v.positionWS + phongSmooth * (
        x * OrtoProject(v.positionWS, v0.positionWS, v0.normalWS) +
        y * OrtoProject(v.positionWS, v1.positionWS, v1.normalWS) +
        z * OrtoProject(v.positionWS, v2.positionWS, v2.normalWS)
    );

    // B. Reconstruct Object Space from UNITY_MATRIX_M (supports both uniform and non-uniform per-axis scale)
    float4x4 M = UNITY_MATRIX_M;
    float3x3 invRot = transpose((float3x3)M);
    float3 worldOffset = float3(M._m03, M._m13, M._m23);
    float3 scaleSq = max(float3(
        dot(invRot[0], invRot[0]),
        dot(invRot[1], invRot[1]),
        dot(invRot[2], invRot[2])
    ), float3(1e-8, 1e-8, 1e-8));

    float3 truePosOS = mul(invRot, v.positionWS - worldOffset) / scaleSq;
    half3 trueNormalOS = (half3)SafeNormalize(mul(invRot, v.normalWS));

    // C. Build Texture Structs
    UnityTexture2D texColorLUT = UnityBuildTexture2DStructNoScale(_B2U_Color_Emission_LUT);
    UnityTexture2D texPropLUT = UnityBuildTexture2DStructNoScale(_B2U_Property_LUT);
    UnityTexture3D texNoise3D = UnityBuildTexture3DStruct(_B2U_3D_Noise_Texture);

    float4 uv0 = v.texCoord0;
    float2 uv1 = float2(v.texCoord1.xy);

    // D. Declare Outputs
    half heightmask;
    float3 displacedPosOS;
    half3 gradientWS;
    half3 flowA;
    half3 flowB;

    // E. Execute Object Space Liquid Surface Solver
    B2USolveSurfaceTessellated(
        texColorLUT, texPropLUT, _B2U_World_Size, _B2U_Number_of_Biome_Subtypes,
        uv0, uv1, trueNormalOS, texNoise3D, _B2U_Global_Bubble_Multiplier,
        _B2U_Global_Angle_Offset_Speed, _B2U_Global_Edge_Distance,
        _B2U_Global_Liquid_Normal_Strength, v.positionWS, truePosOS, v.normalWS,
        heightmask, displacedPosOS, gradientWS, flowA, flowB
    );

    // F. Transform displaced Object Space coordinates back into World Space and Clip Space
    v.positionWS = mul(UNITY_MATRIX_M, float4(displacedPosOS, 1.0)).xyz;

    #if defined(_B2U_USE_PROCEDURAL_NOISE_LIQUID) && !defined(_B2U_TESS_RESAMPLE_FRAGMENT)
        v.normalWS = gradientWS;
    #endif

    #ifdef VARYINGS_NEED_TEXCOORD2
        v.texCoord2 = float4(flowA, heightmask);
    #endif
    #ifdef VARYINGS_NEED_TEXCOORD3
        v.texCoord3 = float4(flowB, 0.0);
    #endif

    v.positionCS = TransformWorldToHClip(v.positionWS);
#elif defined(VARYINGS_NEED_POSITION_WS)
    v.positionCS = TransformWorldToHClip(v.positionWS);
#endif

    return PackVaryings(v);
}

void B2U_Force_Tessellation_float(out float O) { O = 1.0; }
void B2U_Force_Tessellation_half(out float O) { O = 1.0; }

#endif