// Particule ronde douce (eau, débris, poussière), couleur par sommet, sans texture.
Shader "RugWash/Particle Soft"
{
    Properties
    {
        _Color("Teinte", Color) = (1, 1, 1, 1)
        _Softness("Douceur du bord", Range(0.05, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Softness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = input.uv * 2.0 - 1.0;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half r = length(input.uv);
                half alpha = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                half highlight = 1.0 + 0.35 * (1.0 - smoothstep(0.0, 0.5, length(input.uv - half2(-0.3, 0.3))));
                half3 color = MixFog(input.color.rgb * highlight, input.fog);
                return half4(color, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
