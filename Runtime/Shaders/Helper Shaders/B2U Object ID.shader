Shader "Hidden/B2U/ObjectID"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite On
        Cull Back

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            
            // Required for SRP Batcher and DOTS Instancing compatibility
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 objectIDColor : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The SRP Batcher strictly requires this CBUFFER to exist, even if it is completely empty.
            CBUFFER_START(UnityPerMaterial)
            CBUFFER_END

            // A mathematically stable PRNG Hash
            float3 Hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                // Strict DOTS Instancing Boilerplate
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // TransformObjectToWorld implicitly uses UNITY_MATRIX_M, which now successfully finds the instance ID
                float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
                output.objectIDColor = Hash33(pivot);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return half4(input.objectIDColor, 1.0);
            }
            ENDHLSL
        }
    }
}