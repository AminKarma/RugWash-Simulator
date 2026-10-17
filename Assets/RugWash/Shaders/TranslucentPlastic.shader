// Plastique translucide d'un contenant (grande mise à jour) : teinte transparente, reflet spéculaire de la lumière principale et
// liseré plus opaque sur les bords (épaisseur de la paroi vue de biais). Le liquide (« Liquid Container ») est vu au travers.
// Une seule passe, sans texture : coût minime (URP).
Shader "RugWash/Translucent Plastic"
{
    Properties
    {
        _Color("Teinte", Color) = (0.42, 0.88, 0.52, 0.22)
        _RimColor("Bords", Color) = (0.7, 1, 0.75, 0.55)
        _RimPower("Finesse des bords", Range(0.5, 8)) = 2.5
        _Specular("Reflet", Range(0, 2)) = 0.8
        _Gloss("Brillance", Range(4, 128)) = 64
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _Specular;
                half _Gloss;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light light = GetMainLight();
                float diffuse = saturate(dot(normal, light.direction)) * 0.5 + 0.5;
                float rim = pow(1.0 - saturate(dot(normal, view)), _RimPower);
                float specular = pow(saturate(dot(normal, normalize(light.direction + view))), _Gloss) * _Specular;
                half4 color = _Color;
                color.rgb = color.rgb * diffuse * (light.color * 0.7 + SampleSH(normal) * 0.6);
                color.rgb = lerp(color.rgb, _RimColor.rgb, rim * _RimColor.a) + specular * light.color;
                color.a = saturate(color.a + rim * _RimColor.a + specular * 0.6);
                return color;
            }
            ENDHLSL
        }
    }
}
