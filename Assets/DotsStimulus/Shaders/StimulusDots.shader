Shader "DotsStimulus/ScreenSizedDots"
{
    Properties
    {
        _DiameterPixels("Diameter in render pixels", Float) = 6
        _DiameterDegrees("Diameter in degrees", Float) = 0.3528
        _UseAngularSize("Angular size", Float) = 0
        _DiameterMetres("Diameter in metres", Float) = 0.06
        _UseWorldSize("World-space size", Float) = 1
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
                float _DiameterPixels, _DiameterDegrees, _UseAngularSize, _DiameterMetres, _UseWorldSize, _MinimumDepth;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 corner : TEXCOORD0;
                half4 colour : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float depth : TEXCOORD1;
                half opacity : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                if (_UseWorldSize > 0.5)
                {
                    float3 positionWS = TransformObjectToWorld(input.positionOS);
                    float3 positionVS = TransformWorldToView(positionWS);
                    positionVS.xy += input.corner * (_DiameterMetres * 0.5);
                    output.positionCS = TransformWViewToHClip(positionVS);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(input.positionOS);
                    float2 radiusNDC = _DiameterPixels / _ScaledScreenParams.xy;
                    if (_UseAngularSize > 0.5)
                        radiusNDC = tan(radians(_DiameterDegrees) * 0.5) * float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11);
                    output.positionCS.xy += input.corner * radiusNDC * output.positionCS.w;
                }
                output.corner = input.corner;
                output.depth = input.positionOS.z;
                output.opacity = input.colour.a;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(input.depth - _MinimumDepth);
                float radius = length(input.corner);
                float edge = max(fwidth(radius), 0.001);
                half alpha = (1 - smoothstep(1 - edge, 1, radius)) * input.opacity;
                clip(alpha - 0.001);
                return half4(1, 1, 1, alpha);
            }
            ENDHLSL
        }
    }
}
