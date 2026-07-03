// 시야 마스크 — "밝게 비춰진 곳(손전등) 또는 플레이어 주변 원형 반경 = 선명 / 그 외 = 완전 암전".
// 손전등 SpotLight의 실시간 그림자가 이미 벽 차폐를 계산하므로 별도 시야 판정 없이
// 픽셀 밝기(luminance)로 은폐 여부를 결정하고, 여기에 깊이 텍스처로 복원한 월드 좌표 기준
// 플레이어 반경 원형 시야를 더한다. VisionMaskOverlay가 메인 카메라 프러스텀을 채우는
// 쿼드에 이 셰이더를 입혀 사용. URP Opaque Texture + Depth Texture 필수.
Shader "AmsalGame/VisionMask"
{
    Properties
    {
        _VisibleLumMin("이 밝기 이하 = 완전 은폐", Float) = 0.16
        _VisibleLumMax("이 밝기 이상 = 완전 선명", Float) = 0.35
        _ClearRadius("플레이어 주변 선명 반경(m)", Float) = 5.5
        _ClearFeather("원형 시야 가장자리 페더(m)", Float) = 1.5
        _BlurRadiusPx("은폐 블러 반경(px)", Float) = 12
        _HiddenBrightness("은폐 영역 밝기 배율 (0=완전 암전)", Range(0, 2)) = 0
        _HiddenDesaturate("은폐 영역 탈색 정도", Range(0, 1)) = 0.8
        _HiddenTint("은폐 영역 틴트", Color) = (0.5, 0.6, 0.85, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "VisionMask"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero // 화면 전체를 완전히 대체 (오파크 텍스처를 가공해 다시 그리는 방식)

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _VisibleLumMin;
                float _VisibleLumMax;
                float _ClearRadius;
                float _ClearFeather;
                float _BlurRadiusPx;
                float _HiddenBrightness;
                float _HiddenDesaturate;
                half4 _HiddenTint;
            CBUFFER_END

            // 머티리얼이 아닌 전역 — VisionMaskOverlay가 매 프레임 Shader.SetGlobalVector로 갱신
            float3 _VisionPlayerPosWS;

            // 2링 12탭 디스크 블러 오프셋 (안쪽 4 + 바깥 8)
            static const float2 kTaps[12] =
            {
                float2( 0.40,  0.00), float2(-0.40,  0.00), float2( 0.00,  0.40), float2( 0.00, -0.40),
                float2( 1.00,  0.00), float2(-1.00,  0.00), float2( 0.00,  1.00), float2( 0.00, -1.00),
                float2( 0.71,  0.71), float2(-0.71,  0.71), float2( 0.71, -0.71), float2(-0.71, -0.71)
            };

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings  { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionCS);
                float3 sharpCol = SampleSceneColor(uv);

                // ── 밝기 기반 가시성 (손전등) ──
                float2 radiusUV = _BlurRadiusPx / _ScreenParams.xy;
                float3 blurCol = sharpCol;
                [unroll]
                for (int t = 0; t < 12; t++)
                    blurCol += SampleSceneColor(saturate(uv + kTaps[t] * radiusUV));
                blurCol /= 13.0;

                // 선명/블러 밝기 중 큰 쪽으로 판정 — 시야 경계가 지글거리지 않게
                float lum = dot(sharpCol, float3(0.2126, 0.7152, 0.0722));
                float blurLum = dot(blurCol, float3(0.2126, 0.7152, 0.0722));
                float lumVis = smoothstep(_VisibleLumMin, _VisibleLumMax, max(lum, blurLum));

                // ── 플레이어 주변 원형 시야 ──
                // 깊이로 이 픽셀의 월드 좌표를 복원해 플레이어와의 수평(xz) 실거리로 판정.
                // 주의: 탑뷰 카메라가 벽 너머 바닥도 내려다보므로, 반경 내라면 벽 뒤도 보인다
                // ("근접 감각" 컨셉 — 원치 않으면 반경을 줄이거나 이 항을 제거).
                float rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float playerDist = distance(worldPos.xz, _VisionPlayerPosWS.xz);
                float circleVis = 1.0 - smoothstep(_ClearRadius - _ClearFeather, _ClearRadius, playerDist);

                float vis = max(lumVis, circleVis);

                // 은폐 색 — 기본값 밝기 0이라 완전 검정 (블러/탈색/틴트는 밝기 > 0일 때만 의미)
                float gray = dot(blurCol, float3(0.299, 0.587, 0.114));
                float3 hiddenCol = lerp(blurCol, gray.xxx, _HiddenDesaturate) * _HiddenTint.rgb * _HiddenBrightness;

                return half4(lerp(hiddenCol, sharpCol, vis), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
