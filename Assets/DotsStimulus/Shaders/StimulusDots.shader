Shader "DotsStimulus/ScreenSizedDots"
{
    Properties
    {
        _DiameterPixels("Diameter in render pixels", Float) = 6
        _DiameterDegrees("Diameter in degrees", Float) = 0.3528
        _UseAngularSize("Angular size", Float) = 0
        _MinimumDepth("Minimum stimulus depth", Float) = 0.715
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite On ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _DiameterPixels, _DiameterDegrees, _UseAngularSize, _MinimumDepth;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 corner : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float depth : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                float2 radiusNDC = _DiameterPixels / _ScaledScreenParams.xy;
                if (_UseAngularSize > 0.5)
                    radiusNDC = tan(radians(_DiameterDegrees) * 0.5) * float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11);
                output.positionCS.xy += input.corner * radiusNDC * output.positionCS.w;
                output.corner = input.corner;
                output.depth = input.positionOS.z;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(input.depth - _MinimumDepth);
                float radius = length(input.corner);
                float edge = max(fwidth(radius), 0.001);
                half alpha = 1 - smoothstep(1 - edge, 1, radius);
                clip(alpha - 0.001);
                return half4(1, 1, 1, alpha);
            }
            ENDHLSL
        }
    }
}
