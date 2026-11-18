// ADDRESS pool water (URP). Transparent surface that:
//  - refracts the scene behind it (_CameraOpaqueTexture) with two scrolling normal maps
//  - absorbs light with depth (_CameraDepthTexture): clear shallow water, deep aqua further down
//  - reflects the environment (reflection probe) by Fresnel, plus a sharp specular from the main/brightest light
//  - softens where it meets the basin walls
Shader "ADDRESS/Water"
{
    Properties
    {
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Strength", Range(0,2)) = 0.6
        _Tiling ("Tiling (m)", Float) = 2.2
        _Speed ("Flow Speed", Float) = 0.035
        _ShallowColor ("Shallow", Color) = (0.55, 0.92, 0.95, 1)
        _DeepColor ("Deep", Color) = (0.05, 0.45, 0.55, 1)
        _Absorb ("Absorption per m", Float) = 0.45
        _Refraction ("Refraction", Range(0,0.2)) = 0.045
        _Smoothness ("Smoothness", Range(0,1)) = 0.95
        _EdgeFade ("Edge Fade (m)", Float) = 0.08
        _ReflStrength ("Reflection", Range(0,1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _NormalMap_ST; float _NormalScale, _Tiling, _Speed, _Absorb, _Refraction, _Smoothness, _EdgeFade, _ReflStrength;
                half4 _ShallowColor, _DeepColor;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float4 sp : TEXCOORD1; float fog : TEXCOORD2; };

            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.ws = p.positionWS; o.sp = ComputeScreenPos(p.positionCS); o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.ws.xz / _Tiling;
                float t = _Time.y * _Speed;
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv + float2(t, t * 0.6)), _NormalScale);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 1.7 + float2(-t * 0.8, t * 0.35)), _NormalScale * 0.7);
                half3 nT = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
                half3 N = normalize(half3(nT.x, nT.z, nT.y));              // tangent space (xz plane) -> world (y up)

                float2 suv = i.sp.xy / i.sp.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float surfZ = LinearEyeDepth(i.cs.z, _ZBufferParams);
                float depth = max(0, sceneZ - surfZ);
                // refraction: offset only by things that are actually behind the water
                float2 ruv = suv + nT.xy * _Refraction * saturate(depth * 2);
                float rz = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (rz < surfZ) ruv = suv;
                float rDepth = max(0, LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams) - surfZ);
                half3 behind = SampleSceneColor(ruv);
                half absorb = 1 - exp(-rDepth * _Absorb);
                half3 water = lerp(behind * _ShallowColor.rgb, _DeepColor.rgb, absorb);

                float3 Vd = normalize(GetWorldSpaceViewDir(i.ws));
                half fres = pow(1 - saturate(dot(N, Vd)), 5) * 0.96 + 0.04;
                half3 refl = GlossyEnvironmentReflection(reflect(-Vd, N), 1 - _Smoothness, 1);
                half3 col = lerp(water, refl, fres * _ReflStrength);

                // specular highlights from the main light and nearby lamps
                Light ml = GetMainLight();
                half3 H = normalize(ml.direction + Vd);
                col += ml.color * pow(saturate(dot(N, H)), 600) * 3 * ml.distanceAttenuation;
                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws; inputData.normalizedScreenSpaceUV = suv; inputData.normalWS = N; inputData.viewDirectionWS = Vd;
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, i.ws);
                    half3 h = normalize(l.direction + Vd);
                    col += l.color * l.distanceAttenuation * l.shadowAttenuation * pow(saturate(dot(N, h)), 400) * 1.5;
                LIGHT_LOOP_END
                // soft contact with walls / steps
                col = lerp(behind, col, saturate(depth / _EdgeFade));
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
