Shader "Custom/XRayWall"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Transparency ("Transparency", Range(0,1)) = 1
    }
    
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off

        // ===== Pass 1：半透明（ZWrite Off，混合）。仅当 alpha < 0.98 时生效 =====
        Pass
        {
            Name "TRANSPARENT"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _XRAY_OPAQUE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 positionCS : SV_POSITION;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float _Transparency;

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 不透明态由 OPAQUE Pass 负责，这里直接丢弃，避免重复绘制
                #if _XRAY_OPAQUE
                    clip(-1);
                #endif

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;

                // URP 主光（Lambert）+ 球谐环境光
                Light mainLight = GetMainLight();
                half ndl = saturate(dot(normalize(i.normalWS), mainLight.direction));
                col.rgb *= (SampleSH(i.normalWS) + mainLight.color * ndl);

                col.a *= _Transparency;
                return col;
            }
            ENDHLSL
        }

        // ===== Pass 2：不透明（ZWrite On，不混合）。仅当 alpha 接近 1 时生效 =====
        Pass
        {
            Name "OPAQUE"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _XRAY_OPAQUE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 positionCS : SV_POSITION;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float _Transparency;

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 半透明态由 TRANSPARENT Pass 负责，这里直接丢弃
                #if !_XRAY_OPAQUE
                    clip(-1);
                #endif

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;

                Light mainLight = GetMainLight();
                half ndl = saturate(dot(normalize(i.normalWS), mainLight.direction));
                col.rgb *= (SampleSH(i.normalWS) + mainLight.color * ndl);

                // 不透明态：alpha 固定 1，正常遮挡并写入深度
                return col;
            }
            ENDHLSL
        }
    }
}
