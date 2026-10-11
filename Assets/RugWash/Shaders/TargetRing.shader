// Anneau de visée procédural (sans texture), transparent, toujours lisible sur le tapis.
Shader "RugWash/Target Ring"
{
    Properties
    {
        _Color("Couleur", Color) = (1, 1, 1, 0.6)
        _Thickness("Épaisseur", Range(0.01, 0.4)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Thickness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv);
                float aa = fwidth(r) * 1.5;
                half ring = smoothstep(1.0 - _Thickness - aa, 1.0 - _Thickness, r) * (1.0 - smoothstep(1.0 - aa, 1.0, r));
                half fill = (1.0 - smoothstep(1.0 - _Thickness - aa, 1.0 - _Thickness, r)) * 0.08;
                half center = 1.0 - smoothstep(0.04, 0.04 + aa, r);
                return half4(_Color.rgb, _Color.a * saturate(ring + fill + center));
            }
            ENDHLSL
        }
    }
}
