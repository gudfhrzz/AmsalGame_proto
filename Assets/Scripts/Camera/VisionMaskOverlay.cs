using UnityEngine;

// 메인 카메라 근평면 바로 앞에 프러스텀을 꽉 채우는 쿼드를 붙여 화면 전체를
// "플레이어 주변 원형(고정) + 전방 부채꼴(레이캐스트 차폐) = 선명 / 그 외 = 완전 암전"으로
// 후처리한다 (AmsalGame/VisionMask 셰이더). 부채꼴 차폐는 조명과 무관 — 매 프레임 부채꼴 범위로
// Physics.Raycast를 쏴 각도별 도달 거리를 셰이더 전역 배열로 넘긴다 (장애물에 막히면 그 거리까지,
// 없으면 부채꼴 원래 범위 끝까지 보임). URP 오파크/깊이 텍스처를 샘플하므로 둘 다 켜져 있어야 한다.
// 배선은 Phase1SceneSetup.ApplyTacticalLighting()이 담당. 효과를 끄려면 이 컴포넌트만 비활성화하면 된다.
// 한계: 메인 카메라에 비치는 반투명(Transparent) 오브젝트는 오파크 텍스처에 없어 마스크에 덮인다
// (현재 씬의 반투명은 미니맵 레이어 전용이라 실질 영향 없음 — 파티클 등 추가 시 주의).
[RequireComponent(typeof(Camera))]
public class VisionMaskOverlay : MonoBehaviour
{
    // 셰이더 배열 선언 크기(128) 이하로 유지할 것
    private const int RayCount = 96;        // 부채꼴 (±각도 스팬)
    private const int CircleRayCount = 128; // 원형 (360° 전방위)

    private static readonly int PlayerPosId = Shader.PropertyToID("_VisionPlayerPosWS");
    private static readonly int PlayerForwardId = Shader.PropertyToID("_VisionPlayerForwardWS");
    private static readonly int RayDistId = Shader.PropertyToID("_VisionRayDist");
    private static readonly int RayCountId = Shader.PropertyToID("_VisionRayCount");
    private static readonly int RayHalfSpanId = Shader.PropertyToID("_VisionRayHalfSpanRad");
    private static readonly int CircleRayDistId = Shader.PropertyToID("_VisionCircleRayDist");
    private static readonly int CircleRayCountId = Shader.PropertyToID("_VisionCircleRayCount");

    [Tooltip("AmsalGame/VisionMask 머티리얼 — Phase1SceneSetup이 주입. 부채꼴 범위/각도의 단일 출처")]
    [SerializeField] private Material maskMaterial;
    [Tooltip("원형 시야 기준점 (플레이어) — Phase1SceneSetup이 주입, 없으면 자동 탐색")]
    [SerializeField] private Transform player;
    [Tooltip("부채꼴 시야를 막는 장애물 레이어 — 0(Nothing)이면 Player/Enemy/Minimap/Ignore Raycast 제외 전부로 자동 설정")]
    [SerializeField] private LayerMask obstacleMask;
    [Tooltip("레이 발사 높이 (플레이어 기준) — 바닥에 스치지 않고 벽에는 맞는 높이")]
    [SerializeField] private float rayHeight = 0.9f;
    [Tooltip("카메라에서 쿼드까지 거리 — 근평면보다 약간 멀게")]
    [SerializeField] private float quadDistance = 0.5f;

    private Camera _cam;
    private Transform _quad;
    private readonly float[] _rayDistances = new float[RayCount];
    private readonly float[] _circleRayDistances = new float[CircleRayCount];

    public void Bind(Material material, Transform playerTransform)
    {
        maskMaterial = material;
        player = playerTransform;
    }

    private void OnEnable()
    {
        _cam = GetComponent<Camera>();
        if (player == null)
        {
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
        }

        // 장애물 마스크 기본값: 시야를 막지 말아야 할 레이어(자기 자신/적/미니맵 클론/Ignore Raycast)만 제외
        if (obstacleMask.value == 0)
        {
            int mask = ~(1 << 2); // Ignore Raycast(2)
            foreach (var layerName in new[] { "Player", "Enemy", "Minimap" })
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0) mask &= ~(1 << layer);
            }
            obstacleMask = mask;
        }
        if (maskMaterial == null)
        {
            Debug.LogWarning("[VisionMaskOverlay] 머티리얼 미주입 — 효과 비활성화 (특수부대 조명 메뉴 재실행 필요)");
            enabled = false;
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "VisionMaskQuad";
        Destroy(go.GetComponent<Collider>());

        var quadRenderer = go.GetComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = maskMaterial;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;

        go.transform.SetParent(transform, false);
        _quad = go.transform;
        FitToFrustum();
    }

    private void OnDisable()
    {
        if (_quad != null) Destroy(_quad.gameObject);
    }

    // 해상도/종횡비가 런타임에 바뀔 수 있어 매 프레임 맞춘다 (계산 몇 줄이라 부담 없음)
    private void LateUpdate()
    {
        FitToFrustum();
        // 원형/부채꼴 시야 기준 갱신 — 사망(컴포넌트 비활성)해도 transform은 남으니 마지막 상태 유지
        if (player != null)
        {
            Shader.SetGlobalVector(PlayerPosId, player.position);
            Shader.SetGlobalVector(PlayerForwardId, player.forward);
            CastVisionRays();
        }
    }

    // 부채꼴/원형 차폐 판정 — 각도별 레이 도달 거리를 셰이더로 넘긴다.
    // 범위/각도는 머티리얼 값을 단일 출처로 읽어 셰이더의 기하 판정과 항상 일치시킨다.
    private void CastVisionRays()
    {
        float sectorRange = maskMaterial.GetFloat("_SectorRange");
        float halfSpanDeg = maskMaterial.GetFloat("_SectorHalfAngleDeg") + maskMaterial.GetFloat("_SectorAngleFeatherDeg");
        float clearRadius = maskMaterial.GetFloat("_ClearRadius");

        Vector3 origin = player.position + Vector3.up * rayHeight;
        Vector3 forward = player.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;

        // 부채꼴: 전방(마우스 방향) 기준 ±halfSpan
        for (int i = 0; i < RayCount; i++)
        {
            float angleDeg = Mathf.Lerp(-halfSpanDeg, halfSpanDeg, i / (RayCount - 1f));
            Vector3 dir = Quaternion.AngleAxis(angleDeg, Vector3.up) * forward;
            _rayDistances[i] = Physics.Raycast(origin, dir, out RaycastHit hit, sectorRange, obstacleMask, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : sectorRange; // 장애물 없음 → 원래 시야 범위 끝까지
        }

        // 원형: 월드 +z 기준 시계방향 360° — 벽에 붙으면 벽 뒤가 안 보이도록 원형도 차폐 (플레이테스트 피드백)
        for (int i = 0; i < CircleRayCount; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(i * 360f / CircleRayCount, Vector3.up) * Vector3.forward;
            _circleRayDistances[i] = Physics.Raycast(origin, dir, out RaycastHit hit, clearRadius, obstacleMask, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : clearRadius;
        }

        Shader.SetGlobalFloatArray(RayDistId, _rayDistances);
        Shader.SetGlobalFloat(RayCountId, RayCount);
        Shader.SetGlobalFloat(RayHalfSpanId, halfSpanDeg * Mathf.Deg2Rad);
        Shader.SetGlobalFloatArray(CircleRayDistId, _circleRayDistances);
        Shader.SetGlobalFloat(CircleRayCountId, CircleRayCount);
    }

    private void FitToFrustum()
    {
        if (_quad == null) return;

        float dist = Mathf.Max(quadDistance, _cam.nearClipPlane + 0.05f);
        float height = 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        _quad.localPosition = new Vector3(0f, 0f, dist);
        _quad.localRotation = Quaternion.identity;
        // 가장자리 미세 틈으로 원본 씬이 새어 보이지 않게 5% 여유
        _quad.localScale = new Vector3(height * _cam.aspect * 1.05f, height * 1.05f, 1f);
    }
}
