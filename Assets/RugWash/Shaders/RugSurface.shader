// Surface de tapis nettoyable (URP, éclairage PBR).
// Le motif d'origine (_BaseMap) n'est jamais remplacé : la saleté, la mousse et l'humidité sont
// superposées selon deux textures de contrôle produites par RugDirtRenderer à partir de la grille :
//   _ControlA : R poussière, G saleté fixée, B saleté détachée, A mousse
//   _ControlB : RGB couleur de la saleté (linéaire), A humidité
//   _ControlC : R résidus de produit, G usure des fibres
// Un bruit de détail découpe les seuils pour que la frontière propre/sale soit organique.
// Scan de saleté (DirtScanner, variables globales) : surligne les cellules réellement sales — même seuil
// que la grille — et les résidus de produit, derrière une onde partie du joueur. Le motif reste visible.
Shader "RugWash/Rug Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Motif du tapis", 2D) = "white" {}
        [MainColor] _BaseColor("Teinte", Color) = (1,1,1,1)
        [Normal][NoScaleOffset] _FiberNormal("Normal des fibres", 2D) = "bump" {}
        _FiberTiling("Répétitions fibres / m", Float) = 6
        _FiberNormalStrength("Force du relief des fibres", Range(0, 2)) = 1
        [NoScaleOffset] _DirtDetail("Détail (R taches, G poussière, B mousse, A grain)", 2D) = "gray" {}
        _DirtDetailTiling("Répétitions détail / m", Float) = 1.4
        [NoScaleOffset] _ControlA("Contrôle A", 2D) = "black" {}
        [NoScaleOffset] _ControlB("Contrôle B", 2D) = "black" {}
        [NoScaleOffset] _ControlC("Contrôle C", 2D) = "black" {}
        _ResidueColor("Couleur des résidus", Color) = (0.86, 0.88, 0.84, 1)
        _RugSize("Taille du tapis (m)", Vector) = (3, 2, 0, 0)
        _DustColor("Couleur poussière", Color) = (0.6, 0.56, 0.48, 1)
        _FoamColor("Couleur mousse", Color) = (0.96, 0.97, 0.98, 1)
        _DrySmoothness("Lissé sec", Range(0, 1)) = 0.08
        _WetSmoothness("Lissé humide", Range(0, 1)) = 0.42
        _WetDarkening("Assombrissement humide", Range(0, 1)) = 0.32
        _CleanBoost("Éclat des zones propres", Range(0, 0.3)) = 0.08
        _EdgeNoise("Irrégularité des bords", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _FiberTiling;
            half _FiberNormalStrength;
            float _DirtDetailTiling;
            float4 _RugSize;
            half4 _DustColor;
            half4 _FoamColor;
            half4 _ResidueColor;
            half _DrySmoothness;
            half _WetSmoothness;
            half _WetDarkening;
            half _CleanBoost;
            half _EdgeNoise;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FiberNormal);  SAMPLER(sampler_FiberNormal);
            TEXTURE2D(_DirtDetail);   SAMPLER(sampler_DirtDetail);
            TEXTURE2D(_ControlA);     SAMPLER(sampler_ControlA);
            TEXTURE2D(_ControlB);     SAMPLER(sampler_ControlB);
            TEXTURE2D(_ControlC);     SAMPLER(sampler_ControlC);

            // Globales du scan (hors CBUFFER matériau : partagées, compatibles SRP Batcher).
            float4 _RugScanParams;      // x intensité 0..1, y rayon de l'onde (m), z largeur du front (m), w temps
            float4 _RugScanOrigin;      // xyz origine de l'onde (monde)
            float4 _RugScanThresholds;  // x seuil de saleté (cellule propre), y seuil de résidus
            half4 _RugScanDirtColor;
            half4 _RugScanResidueColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                half4 tangentWS   : TEXCOORD3;
                half fogFactor    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half luminanceOf(half3 c) { return dot(c, half3(0.299, 0.587, 0.114)); }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.tangentWS = half4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 uv = input.uv;
                float2 meters = uv * _RugSize.xy;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap)).rgb * _BaseColor.rgb;
                half4 a = SAMPLE_TEXTURE2D(_ControlA, sampler_ControlA, uv);
                half4 b = SAMPLE_TEXTURE2D(_ControlB, sampler_ControlB, uv);
                half4 cc = SAMPLE_TEXTURE2D(_ControlC, sampler_ControlC, uv);
                half4 detail = SAMPLE_TEXTURE2D(_DirtDetail, sampler_DirtDetail, meters * _DirtDetailTiling);
                half4 fineDetail = SAMPLE_TEXTURE2D(_DirtDetail, sampler_DirtDetail, meters * _DirtDetailTiling * 4.7);

                half3 stainTint = b.rgb;
                half moisture = b.a;

                // Saleté fixée : le seuil suit le bruit, la zone sale « s'érode » à mesure que la valeur baisse.
                half stainThreshold = detail.r * _EdgeNoise * 0.75;
                half stainMask = smoothstep(stainThreshold, stainThreshold + 0.18, a.g) * saturate(a.g * 4.0);
                half3 stainedColor = lerp(albedo * lerp(1.0, stainTint * 2.3, 0.85), stainTint * 0.9, 0.22);
                half3 color = lerp(albedo, stainedColor, stainMask * saturate(0.75 + a.g * 0.25));

                // Saleté détachée : boue humide, plus sombre et brillante.
                half looseThreshold = detail.r * _EdgeNoise * 0.4;
                half looseMask = smoothstep(looseThreshold, looseThreshold + 0.22, a.b) * saturate(a.b * 4.0);
                color = lerp(color, stainTint * 0.75, looseMask * 0.8);

                // Poussière : voile mat et désaturé.
                half dustThreshold = fineDetail.g * _EdgeNoise * 0.6;
                half dustMask = smoothstep(dustThreshold, dustThreshold + 0.25, a.r) * saturate(a.r * 4.0);
                half3 dusty = lerp(luminanceOf(color).xxx, _DustColor.rgb, 0.7) * (0.85 + 0.3 * fineDetail.g);
                color = lerp(color, dusty, dustMask * 0.8);

                // Zones propres : légère saturation/éclat pour accentuer la satisfaction du nettoyage.
                half cleanness = 1.0 - saturate((a.r + a.g + a.b) * 3.0);
                half luminance = dot(color, half3(0.299, 0.587, 0.114));
                color = lerp(color, lerp(luminance.xxx, color, 1.0 + _CleanBoost * 2.0) * (1.0 + _CleanBoost), cleanness);

                // Résidus de produit : voile pâle en traînées, visible surtout une fois la mousse partie.
                half residueMask = smoothstep(0.05, 0.5, cc.r) * (0.55 + 0.45 * detail.g);
                color = lerp(color, lerp(color, _ResidueColor.rgb, 0.45), residueMask * 0.8);

                // Humidité : assombrit et rend brillant.
                color *= lerp(1.0, 1.0 - _WetDarkening, moisture);
                half smoothness = lerp(_DrySmoothness, _WetSmoothness, moisture);
                smoothness = lerp(smoothness, 0.0, dustMask * 0.8);
                smoothness = lerp(smoothness, 0.5, looseMask * 0.6);

                // Mousse : bulles blanches, teintées par la saleté détachée qu'elles contiennent.
                half foamCoverage = saturate(a.a * 1.25);
                half bubbles = detail.b * 0.75 + fineDetail.b * 0.25;
                half foamMask = smoothstep(1.0 - foamCoverage, 1.0 - foamCoverage + 0.22, bubbles * 0.85 + 0.12) * saturate(a.a * 5.0);
                half bubbleShade = lerp(0.68, 1.0, saturate(fineDetail.b * 1.3));
                half3 foamColor = lerp(_FoamColor.rgb, _FoamColor.rgb * (stainTint * 1.6 + 0.35), looseMask * 0.7) * bubbleShade;
                color = lerp(color, foamColor, foamMask * 0.95);
                smoothness = lerp(smoothness, 0.55 * fineDetail.b + 0.2, foamMask);

                // Relief des fibres, écrasé par la poussière et masqué par la mousse.
                half strength = _FiberNormalStrength * (1.0 - dustMask * 0.5) * (1.0 - foamMask) * (1.0 - moisture * 0.35);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_FiberNormal, sampler_FiberNormal, meters * _FiberTiling), strength);
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = color;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = smoothness;
                surface.normalTS = normalTS;
                surface.occlusion = lerp(1.0, 0.85 + 0.15 * detail.a, 1.0 - foamMask);
                surface.alpha = 1;

                half4 lit = UniversalFragmentPBR(inputData, surface);

                // Scan de saleté : teinte multipliée (le motif reste lisible), plus marquée là où il reste beaucoup de saleté,
                // hachures animées, contour des zones signalées et front d'onde plus lumineux. Rien sur les cellules propres.
                half dirtAmount = a.r + a.g + a.b;
                half dirtFlag = smoothstep(_RugScanThresholds.x * 0.8, _RugScanThresholds.x * 1.6, dirtAmount);
                half residueFlag = smoothstep(_RugScanThresholds.y * 0.8, _RugScanThresholds.y * 1.4, cc.r) * (1.0 - dirtFlag);
                half dirtWeight = dirtFlag * lerp(0.3, 1.0, saturate((dirtAmount - _RugScanThresholds.x) * 2.5));
                half flags = max(dirtFlag, residueFlag);
                half weight = max(dirtWeight, residueFlag * 0.7);
                float scanDistance = distance(input.positionWS.xz, _RugScanOrigin.xz);
                half halfWidth = max(_RugScanParams.z, 0.01) * 0.5;
                half revealed = smoothstep(_RugScanParams.y + halfWidth, _RugScanParams.y - halfWidth, scanDistance);
                half front = saturate(1.0 - abs(scanDistance - _RugScanParams.y) / (halfWidth * 2.0));
                half stripe = smoothstep(0.3, 0.5, frac((meters.x + meters.y) * 6.0 - _RugScanParams.w * 0.8));
                half edge = saturate(fwidth(flags) * 6.0);
                half3 scanHue = dirtFlag >= residueFlag ? _RugScanDirtColor.rgb : _RugScanResidueColor.rgb;
                half reveal = weight * revealed;
                half3 scanned = lit.rgb * (1.0 + scanHue * reveal * (0.45 + 0.55 * stripe))
                              + scanHue * (reveal * 0.015 + weight * front * 0.18 + edge * revealed * 0.2);
                lit.rgb = lerp(lit.rgb, scanned, _RugScanParams.x);

                lit.rgb = MixFog(lit.rgb, inputData.fogCoord);
                return half4(lit.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
