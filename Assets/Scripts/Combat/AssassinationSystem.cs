using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
public class AssassinationSystem : MonoBehaviour
{
    [Header("암살 판정")]
    [SerializeField] private float assassinRange = 1.5f;
    [Tooltip("적 후방 몇 도 이내에서 발동 가능한지 (CLAUDE.md: 120도)")]
    [SerializeField] private float backConeAngle = 120f;
    [SerializeField] private LayerMask enemyLayer;

    private AIController _currentTarget;

    public bool CanAssassinate => _currentTarget != null;

    private void Update()
    {
        _currentTarget = FindTarget();

        if (CanAssassinate && Keyboard.current.fKey.wasPressedThisFrame)
            ExecuteAssassination(_currentTarget);
    }

    private AIController FindTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, assassinRange, enemyLayer);
        foreach (var hit in hits)
        {
            var ai = hit.GetComponent<AIController>();
            if (ai == null || ai.CurrentState == AIController.AIState.Chase) continue;

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
