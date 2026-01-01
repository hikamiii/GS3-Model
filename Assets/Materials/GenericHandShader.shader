Shader "Ultraleap/GenericHandShader"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _MainColor ("Main Color (DEPRICATED)", Color) = (0,0,0,1)
        [HDR] _Color ("Main Color", Color) = (0,0,0,1)

        [MaterialToggle] _useOutline ("Use Outline", Float) = 0
        [HDR] _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _Outline ("Outline width", Range(0,0.2)) = 0.01

        [MaterialToggle] _useLighting ("Use Lighting", Float) = 0
        _LightIntensity ("Light Intensity", Range(0,1)) = 1

        [MaterialToggle] _useFresnel ("Use Fresnel", Float) = 0
        [HDR] _FresnelColor ("Fresnel Color", Color) = (1,1,1,0)
        _FresnelPower ("Fresnel Power", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        // ---------- Outline pass ----------
        // In URP, this pass is best drawn via a Renderer Feature (Render Objects) using pass name "Outline".
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="Outline" }

            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _Outline;
                float  _useOutline;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

                float4 positionCS = TransformWorldToHClip(positionWS);

                // Expand in view-space XY
                float3 normalVS = mul((float3x3)GetWorldToViewMatrix(), normalWS);
                float2 offset   = normalize(normalVS.xy) * _Outline;

                if (_useOutline > 0.5)
                    positionCS.xy += offset * positionCS.w;

                output.positionCS = positionCS;
                output.uv = input.uv;
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return tex * _OutlineColor;
            }
            ENDHLSL
        }

        // ---------- Main forward pass ----------
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            Cull Back

            // If you want true translucency/fade-through, change this to ZWrite Off.
            // Your original shader behaved more "solid", so ZWrite On is usually nicer for hands.
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            // Main light + shadows variants (safe even if shadows are off)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainColor;

                float  _useLighting;
                float  _LightIntensity;

                float  _useFresnel;
                float4 _FresnelColor;
                float  _FresnelPower;
            CBUFFER_END

            float4 GetBaseColor()
            {
                // Heuristic: if _Color is still default-black, fall back to deprecated _MainColor.
                float colorSum = _Color.r + _Color.g + _Color.b;
                float useDeprecated = step(colorSum, 0.001);
                return lerp(_Color, _MainColor, useDeprecated);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv           : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                half3  normalWS     : TEXCOORD2;
                half3  viewDirWS    : TEXCOORD3;
                float4 shadowCoord  : TEXCOORD4;
                half   fogFactor    : TEXCOORD5;
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                half3  normalWS   = (half3)normalize(TransformObjectToWorldNormal(input.normalOS));

                output.positionWS  = positionWS;
                output.normalWS    = normalWS;
                output.viewDirWS   = (half3)normalize(GetWorldSpaceViewDir(positionWS));
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);

                output.uv = input.uv;
                return output;
            }

            float Unity_FresnelEffect_float(float3 Normal, float3 ViewDir, float Power)
            {
                return pow((1.0 - saturate(dot(normalize(Normal), normalize(ViewDir)))), Power);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float4 baseCol = GetBaseColor();

                half4 col;
                col.rgb = tex.rgb * (half3)baseCol.rgb;
                col.a   = tex.a   * (half)baseCol.a;

                if (_useLighting > 0.5)
                {
                    Light mainLight = GetMainLight(input.shadowCoord);
                    half nl = saturate(dot(input.normalWS, mainLight.direction));
                    half3 ambient = SampleSH(input.normalWS);

                    half3 direct = (half3)mainLight.color * nl * mainLight.shadowAttenuation;
                    col.rgb *= (ambient + direct) * (half)_LightIntensity;
                }

                if (_useFresnel > 0.5)
                {
                    half f = (half)Unity_FresnelEffect_float(input.normalWS, input.viewDirWS, _FresnelPower);
                    col.rgb *= (half3)_FresnelColor.rgb * f * (half)_FresnelColor.a;
                }

                col.rgb = MixFog(col.rgb, input.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
