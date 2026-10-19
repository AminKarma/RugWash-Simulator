// Rai de lumière (V0.3.0) : volume additif très léger sous les fenêtres hautes de l'atelier.
// UV : u en travers de chaque face (0..1), v le long du rai (0 à la fenêtre, 1 au sol). L'intensité décroît le long du rai,
// s'annule sur les arêtes et s'estompe face à la caméra (les faces vues par la tranche portent l'essentiel), sans texture.
Shader "RugWash/Light Shaft"
{
    Properties
    {
        _Color("Couleur", Color) = (0.75, 0.85, 1, 1)
        _Intensity("Intensité", Range(0, 1)) = 0.08
        _Falloff("Décroissance le long du rai", Range(0.5, 4)) = 1.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Shaft"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Falloff;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half facing : TEXCOORD1; half fog : TEXCOORD2; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.uv = input.uv;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(p.positionWS));
                output.facing = abs(dot(normalWS, viewWS));
                output.fog = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half along = pow(saturate(1.0 - input.uv.y), _Falloff);
                half across = sin(saturate(input.uv.x) * PI);
                half edge = 1.0 - input.facing * 0.7;
                half3 c = _Color.rgb * (_Intensity * along * across * across * edge);
                c = MixFogColor(c, half3(0, 0, 0), input.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
