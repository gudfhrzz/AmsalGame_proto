// 시야 마스크 — "플레이어 주변 원형 + 전방 부채꼴(손전등 도달 범위 한정) = 선명 / 그 외 = 완전 암전".
// 깊이 텍스처로 픽셀의 월드 좌표를 복원해 원형(반경)/부채꼴(방향+거리)을 기하로 판정하고,
// 부채꼴 안에서는 픽셀 밝기(luminance)를 곱한다 — 손전등 SpotLight의 실시간 그림자가
// 벽 차폐를 이미 계산하므로 "어두움 = 손전등 미도달 = 벽 뒤"가 성립.
// VisionMaskOverlay가 메인 카메라 프러스텀을 채우는 쿼드에 이 셰이더를 입혀 사용.
// URP Opaque Texture + Depth Texture 필수.
Shader "AmsalGame/VisionMask"
{
    Properties
    {
        _VisibleLumMin("이 밝기 이하 = 손전등 미도달 (부채꼴 내 은폐)", Float) = 0.13
        _VisibleLumMax("이 밝기 이상 = 완전 선명", Float) = 0.25
        _ClearRadius("플레이어 주변 선명 반경(m)", Float) = 5.5
        _ClearFeather("원형 시야 가장자리 페더(m)", Float) = 1.5
        _SectorRange("전방 부채꼴 시야 길이(m) — 원형 반경의 2.5배", Float) = 13.75
        _SectorRangeFeather("부채꼴 끝 페더(m)", Float) = 2
        _SectorHalfAngleDeg("부채꼴 반각(도) — 손전등 반각(35도)보다 약간 안쪽", Float) = 33
        _SectorAngleFeatherDeg("부채꼴 각도 페더(도)", Float) = 6
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
                float _SectorRange;
                float _SectorRangeFeather;
                float _SectorHalfAngleDeg;
                float _SectorAngleFeatherDeg;
                float _BlurRadiusPx;
                float _HiddenBrightness;
                float _HiddenDesaturate;
                half4 _HiddenTint;
            CBUFFER_END

            // 머티리얼이 아닌 전역 — VisionMaskOverlay가 매 프레임 Shader.SetGlobalVector로 갱신
            float3 _VisionPlayerPosWS;
            float3 _VisionPlayerForwardWS;

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

                // ── 밝기 판정 (손전등 실제 도달 여부 — 부채꼴 안에서 벽 차폐를 담당) ──
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

                // ── 기하 판정: 깊이로 픽셀의 월드 좌표를 복원해 플레이어 기준 실거리/방향 계산 ──
                float rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float2 toPixel = worldPos.xz - _VisionPlayerPosWS.xz;
                float playerDist = length(toPixel);

                // ① 주변 원형 시야 — 손전등과 무관하게 정상 가시.
                //    주의: 탑뷰 카메라가 벽 너머 바닥도 내려다보므로, 반경 내라면 벽 뒤도 보인다
                //    ("근접 감각" 컨셉 — 원치 않으면 반경을 줄이거나 이 항을 제거).
                float circleVis = 1.0 - smoothstep(_ClearRadius - _ClearFeather, _ClearRadius, playerDist);

                // ② 전방 부채꼴 시야 — 바라보는 방향 ±반각, 길이 _SectorRange (원형 반경의 2.5배).
                //    기하 판정에 밝기 판정을 곱해 벽 뒤(손전등 그림자)는 부채꼴 안이라도 은폐 유지.
                //    부채꼴 밖으로 새는 손전등 빛 번짐은 여기서 잘려 시야 모양이 또렷해진다.
                float2 fwd = normalize(_VisionPlayerForwardWS.xz + float2(1e-5, 1e-5));
                float2 dirToPixel = toPixel / max(playerDist, 1e-4);
                float cosInner = cos(radians(_SectorHalfAngleDeg));
                float cosOuter = cos(radians(_SectorHalfAngleDeg + _SectorAngleFeatherDeg));
                float angleVis = smoothstep(cosOuter, cosInner, dot(fwd, dirToPixel));
                float rangeVis = 1.0 - smoothstep(_SectorRange - _SectorRangeFeather, _SectorRange, playerDist);
                float sectorVis = angleVis * rangeVis * lumVis;

                float vis = max(circleVis, sectorVis);

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
