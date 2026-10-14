// Contenant translucide dont le niveau de liquide suit une valeur (_Fill, 0..1) : sous la ligne, la couleur du liquide ;
// au-dessus, le plastique vide. Hauteurs en espace objet (_MinY, _MaxY). Une seule passe, sans texture : coût minime.
Shader "RugWash/Liquid Container"
{
    Properties
    {
        _EmptyColor("Plastique (vide)", Color) = (0.78, 0.86, 0.8, 0.32)
        _LiquidColor("Liquide", Color) = (0.2, 0.62, 0.32, 0.93)
        _Fill("Niveau (0-1)", Range(0, 1)) = 1
        _MinY("Fond (Y objet)", Float) = -0.2
        _MaxY("Haut (Y objet)", Float) = 0
        _Band("Épaisseur de la ligne de niveau", Range(0.001, 0.08)) = 0.02
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
                half4 _EmptyColor;
                half4 _LiquidColor;
                half _Fill;
                float _MinY;
                float _MaxY;
                half _Band;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float heightOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.heightOS = input.positionOS.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = saturate((input.heightOS - _MinY) / max(1e-4, _MaxY - _MinY));
                float aa = fwidth(t) * 1.5 + 1e-4;
                float liquid = 1.0 - smoothstep(_Fill - aa, _Fill + aa, t);
                float edge = (1.0 - smoothstep(0.0, _Band + aa, abs(t - _Fill))) * step(0.001, _Fill);
                half4 color = lerp(_EmptyColor, _LiquidColor, liquid);
                color.rgb = lerp(color.rgb, saturate(color.rgb * 1.4 + 0.1), edge);
                color.a = max(color.a, edge * 0.95);

                Light light = GetMainLight();
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                float diffuse = saturate(dot(normal, light.direction)) * 0.55 + 0.45;
                float rim = pow(1.0 - saturate(dot(normal, view)), 3.0) * 0.3;
                color.rgb = color.rgb * diffuse * light.color + rim;
                return color;
            }
            ENDHLSL
        }
    }
}
