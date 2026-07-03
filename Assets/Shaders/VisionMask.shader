// 시야 마스크 — "플레이어 주변 원형 + 전방 부채꼴 = 선명 / 그 외 = 완전 암전". 둘 다 레이캐스트 차폐.
// 깊이 텍스처로 픽셀의 월드 좌표를 복원해 원형(반경)/부채꼴(방향+거리)을 기하로 판정한다.
// 차폐는 조명과 무관 — VisionMaskOverlay가 매 프레임 플레이어 위치에서 레이캐스트한 거리 배열
// (부채꼴 _VisionRayDist, 원형 360° _VisionCircleRayDist)을 넘겨주고, 픽셀이 자기 각도의
// 레이 거리보다 멀면 은폐. 장애물이 없으면 각자의 원래 범위 끝까지 보인다.
// VisionMaskOverlay가 메인 카메라 프러스텀을 채우는 쿼드에 이 셰이더를 입혀 사용.
// URP Opaque Texture + Depth Texture 필수.
Shader "AmsalGame/VisionMask"
{
    Properties
    {
        _ClearRadius("플레이어 주변 선명 반경(m)", Float) = 4.5
        _ClearFeather("원형 시야 가장자리 페더(m)", Float) = 1.5
        _SectorRange("전방 부채꼴 시야 길이(m)", Float) = 13.75
        _SectorRangeFeather("부채꼴 끝 페더(m)", Float) = 2
        _SectorHalfAngleDeg("부채꼴 반각(도)", Float) = 33
        _SectorAngleFeatherDeg("부채꼴 각도 페더(도)", Float) = 6
        _OcclusionFeather("장애물 차폐 경계 페더(m)", Float) = 0.35
        _HiddenBrightness("은폐 영역 밝기 (0=완전 암전)", Range(0, 1)) = 0
        _HiddenTint("은폐 영역 색", Color) = (0.5, 0.6, 0.85, 1)
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
                float _ClearRadius;
                float _ClearFeather;
                float _SectorRange;
                float _SectorRangeFeather;
                float _SectorHalfAngleDeg;
                float _SectorAngleFeatherDeg;
                float _OcclusionFeather;
                float _HiddenBrightness;
                half4 _HiddenTint;
            CBUFFER_END

            // 머티리얼이 아닌 전역 — VisionMaskOverlay가 매 프레임 갱신
            float3 _VisionPlayerPosWS;
            float3 _VisionPlayerForwardWS;
            float  _VisionRayCount;          // 부채꼴 레이 개수 (배열 선언 크기 이하)
            float  _VisionRayHalfSpanRad;    // 부채꼴 레이 반각(라디안) = 부채꼴 반각 + 각도 페더
            float  _VisionRayDist[128];      // 부채꼴 각도순 도달 거리 — 장애물 없으면 _SectorRange
            float  _VisionCircleRayCount;    // 원형 360° 레이 개수
            float  _VisionCircleRayDist[128]; // 월드 +z 기준 시계방향 각도순 — 장애물 없으면 _ClearRadius

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

                // 깊이로 픽셀의 월드 좌표를 복원해 플레이어 기준 실거리/방향 계산
                float rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float2 toPixel = worldPos.xz - _VisionPlayerPosWS.xz;
                float playerDist = length(toPixel);

                float2 fwd = normalize(_VisionPlayerForwardWS.xz + float2(1e-5, 1e-5));
                float2 dirToPixel = toPixel / max(playerDist, 1e-4);

                // ① 주변 원형 시야 — 반경은 고정이지만 벽 차폐는 적용 (360° 레이 배열).
                //    각도 기준: 월드 +z에서 시계방향 = C#의 AngleAxis(i*360/N, up)*forward와 동일
                float circleGeom = 1.0 - smoothstep(_ClearRadius - _ClearFeather, _ClearRadius, playerDist);
                float angCW = atan2(dirToPixel.x, dirToPixel.y); // [-π, π]
                float tc = frac(angCW / 6.28318530718 + 1.0) * _VisionCircleRayCount;
                int c0 = min((int)tc, (int)_VisionCircleRayCount - 1);
                int c1 = (c0 + 1) % max((int)_VisionCircleRayCount, 1); // 360° 랩어라운드
                float circleRayDist = min(_VisionCircleRayDist[c0], _VisionCircleRayDist[c1]);
                float circleOccl = 1.0 - smoothstep(circleRayDist - _OcclusionFeather, circleRayDist, playerDist);
                float circleVis = circleGeom * circleOccl;

                // ② 전방 부채꼴 시야 — 원형과 별개의 단독 범위, 레이캐스트 차폐

                float cosInner = cos(radians(_SectorHalfAngleDeg));
                float cosOuter = cos(radians(_SectorHalfAngleDeg + _SectorAngleFeatherDeg));
                float cosAngle = dot(fwd, dirToPixel);
                float angleVis = smoothstep(cosOuter, cosInner, cosAngle);
                float rangeVis = 1.0 - smoothstep(_SectorRange - _SectorRangeFeather, _SectorRange, playerDist);

                // 픽셀 각도에 해당하는 레이 거리 조회 — 장애물이 있으면 그 거리에서 시야가 끊기고,
                // 없으면 레이 거리 = _SectorRange라 끝까지 보인다.
                // 부호 주의: Unity의 AngleAxis(+각도, up)는 위에서 볼 때 시계방향이라
                // xz 평면 외적은 (fwd.y*dir.x - fwd.x*dir.y)여야 C# 레이 인덱스와 방향이 일치한다
                // (반대로 쓰면 좌우 차폐가 거울상으로 뒤집혀 벽 너머가 보이는 버그).
                float sinAngle = fwd.y * dirToPixel.x - fwd.x * dirToPixel.y;
                float ang = atan2(sinAngle, cosAngle);
                float halfSpan = max(_VisionRayHalfSpanRad, 1e-3);
                float t = saturate((ang + halfSpan) / (2.0 * halfSpan)) * (_VisionRayCount - 1.0);
                int i0 = (int)t;
                int i1 = min(i0 + 1, (int)_VisionRayCount - 1);
                // 이웃 레이 중 짧은 쪽 채택(보간 금지) — 문틈/모서리에서 짧은 레이와 긴 레이를
                // 섞으면 벽을 뚫는 쐐기가 생긴다. min의 계단은 차폐 페더가 가려줌
                float rayDist = min(_VisionRayDist[i0], _VisionRayDist[i1]);
                float occlVis = 1.0 - smoothstep(rayDist - _OcclusionFeather, rayDist, playerDist);

                float sectorVis = angleVis * rangeVis * occlVis;

                float vis = max(circleVis, sectorVis);
                float3 hiddenCol = _HiddenTint.rgb * _HiddenBrightness;
                return half4(lerp(hiddenCol, sharpCol, vis), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
