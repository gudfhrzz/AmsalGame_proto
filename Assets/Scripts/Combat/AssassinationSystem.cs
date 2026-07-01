using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class AssassinationSystem : MonoBehaviour
{
    [Header("암살 판정")]
    [SerializeField] private float assassinRange = 1.5f;
    [Tooltip("적 후방 몇 도 이내에서 발동 가능한지 (CLAUDE.md: 120도)")]
    [SerializeField] private float backConeAngle = 120f;
    [SerializeField] private LayerMask enemyLayer;

    private AIController _currentTarget;
    private ClusterPenaltySystem _cluster;

    public bool CanAssassinate => _currentTarget != null;
    // 인디케이터 UI가 표시 위치를 알 수 있도록 노출
    public Transform CurrentTarget => _currentTarget != null ? _currentTarget.transform : null;

    private void Awake() => _cluster = GetComponent<ClusterPenaltySystem>();

    private void Update()
    {
        // 군집 페널티 — 아군 3인 이상 집결 시 암살 무효 (CQC로 강제 전환)
        _currentTarget = (_cluster != null && _cluster.IsPenalized) ? null : FindTarget();
    }

    // LMB 입력을 받는 PlayerCombatInput에서 호출 (컨텍스트 감지형 입력: 암살 가능하면 암살, 아니면 CQC 공격)
    public bool TryAssassinate()
    {
        if (!CanAssassinate) return false;
        ExecuteAssassination(_currentTarget);
        return true;
    }

    private AIController FindTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, assassinRange, enemyLayer);
        foreach (var hit in hits)
        {
            var ai = hit.GetComponent<AIController>();
            if (ai == null) continue;
            // Chase/Combat = 이미 플레이어를 인식한 상태 → 암살 불가 (CQC로 전환됨)
            if (ai.CurrentState == AIController.AIState.Chase || ai.CurrentState == AIController.AIState.Combat) continue;

            // 플레이어가 적의 후방 backConeAngle도 이내에 있는지 확인
            Vector3 toPlayer = (transform.position - ai.transform.position);
            toPlayer.y = 0f;
            float angle = Vector3.Angle(ai.transform.forward, toPlayer.normalized);
            if (angle < 180f - backConeAngle * 0.5f) continue;

            return ai;
        }
        return null;
    }

    private void ExecuteAssassination(AIController target)
    {
        target.GetComponent<Health>()?.TakeDamage(999);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = CanAssassinate
            ? new Color(1f, 0f, 0f, 0.6f)
            : new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, assassinRange);
    }
#endif
}
