using UnityEngine;

// 메인 카메라 근평면 바로 앞에 프러스텀을 꽉 채우는 쿼드를 붙여 화면 전체를
// "손전등에 비친 곳 + 플레이어 주변 원형 반경 = 선명 / 그 외 = 완전 암전"으로 후처리한다
// (AmsalGame/VisionMask 셰이더). URP 오파크/깊이 텍스처를 샘플하므로 RP 에셋에서 둘 다 켜져 있어야 한다.
// 배선은 Phase1SceneSetup.ApplyTacticalLighting()이 담당. 효과를 끄려면 이 컴포넌트만 비활성화하면 된다.
// 한계: 메인 카메라에 비치는 반투명(Transparent) 오브젝트는 오파크 텍스처에 없어 마스크에 덮인다
// (현재 씬의 반투명은 미니맵 레이어 전용이라 실질 영향 없음 — 파티클 등 추가 시 주의).
[RequireComponent(typeof(Camera))]
public class VisionMaskOverlay : MonoBehaviour
{
    private static readonly int PlayerPosId = Shader.PropertyToID("_VisionPlayerPosWS");
    private static readonly int PlayerForwardId = Shader.PropertyToID("_VisionPlayerForwardWS");

    [Tooltip("AmsalGame/VisionMask 머티리얼 — Phase1SceneSetup이 주입")]
    [SerializeField] private Material maskMaterial;
    [Tooltip("원형 시야 기준점 (플레이어) — Phase1SceneSetup이 주입, 없으면 자동 탐색")]
    [SerializeField] private Transform player;
    [Tooltip("카메라에서 쿼드까지 거리 — 근평면보다 약간 멀게")]
    [SerializeField] private float quadDistance = 0.5f;

    private Camera _cam;
    private Transform _quad;

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
        }
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
