Shader "Hidden/B2U/VolumetricBackface"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Cull Front
        Blend Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 normalVS_XY: TEXCOORD0;
                float2 depthZW    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                float3 normSlide = input.normalOS * 0.25;
                if (any(input.color < 0.99))
                {
                    normSlide = float3(0, 0, 0);
                }

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz + normSlide);
                
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalVS = mul((float3x3)GetWorldToViewMatrix(), normalWS);
                
                output.normalVS_XY = normalVS.xy;
                output.depthZW = output.positionCS.zw;
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float rawDepth = input.depthZW.x / input.depthZW.y;
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // R & G: View Space Normal XY. B: Unused. A: Raw Eye Depth.
                return half4(input.normalVS_XY, 0.0, eyeDepth);
            }
            ENDHLSL
        }
    }
}
