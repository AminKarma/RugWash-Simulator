// Liquide en volume dans un contenant (grande mise à jour) : maillage fermé du liquide, coupé par un plan horizontal en espace
// monde à la hauteur du niveau (_Fill, 0..1, entre _MinY et _MaxY de l'objet) — le liquide reste à plat quand l'outil penche et
// ballotte un peu quand il bouge (_Wobble). Les faces avant donnent la masse colorée (légèrement translucide), les faces
// arrière visibles au-dessus de la coupe donnent la surface du liquide. Une seule passe, sans texture : coût minime (URP).
Shader "RugWash/Liquid Container"
{
    Properties
    {
        _LiquidColor("Liquide", Color) = (0.16, 0.58, 0.27, 0.88)
        _SurfaceColor("Surface du liquide", Color) = (0.42, 0.85, 0.5, 0.95)
        _Fill("Niveau (0-1)", Range(0, 1)) = 1
        _MinY("Fond (Y objet)", Float) = -0.2
        _MaxY("Haut (Y objet)", Float) = 0
        _Band("Ménisque (m)", Range(0.0005, 0.02)) = 0.004
        _Wobble("Ballottement (inclinaison X, Z)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        // Avant le plastique du contenant (file Transparent) : le liquide est vu à travers la paroi.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off

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
                half4 _LiquidColor;
                half4 _SurfaceColor;
                half _Fill;
                float _MinY;
                float _MaxY;
                half _Band;
                float4 _Wobble;
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

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                // Plan du niveau : passe par le point de l'axe du contenant à la hauteur voulue, normale verticale (monde)
                // légèrement inclinée par le ballottement.
                float3 level = TransformObjectToWorld(float3(0.0, lerp(_MinY, _MaxY, _Fill), 0.0));
                float3 planeNormal = normalize(float3(_Wobble.x, 1.0, _Wobble.y));
                float above = dot(input.positionWS - level, planeNormal);
                clip(-above);
                clip(_Fill - 0.002);

                Light light = GetMainLight();
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                half4 color;
                if (front)
                {
                    float3 normal = normalize(input.normalWS);
                    float diffuse = saturate(dot(normal, light.direction)) * 0.5 + 0.5;
                    float rim = pow(1.0 - saturate(dot(normal, view)), 3.0) * 0.25;
                    color = _LiquidColor;
                    // Ménisque : fine bande claire juste sous la surface.
                    float meniscus = 1.0 - smoothstep(0.0, _Band, -above);
                    color.rgb = color.rgb * diffuse * (light.color * 0.8 + SampleSH(normal) * 0.6) + rim + meniscus * 0.18;
                }
                else
                {
                    // Surface vue d'en haut : reflet du plafond et de la lumière principale.
                    float3 halfDir = normalize(light.direction + view);
                    float specular = pow(saturate(dot(planeNormal, halfDir)), 48.0) * 0.6;
                    color = _SurfaceColor;
                    color.rgb = color.rgb * (light.color * 0.6 + SampleSH(planeNormal) * 0.7) + specular;
                }
                return color;
            }
            ENDHLSL
        }
    }
}
