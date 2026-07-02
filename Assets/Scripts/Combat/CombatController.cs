using UnityEngine;

// CQC(근접 전투) 상태머신. Player/AI 공용 — 누가 붙이든 동일한 규칙으로 싸운다.
// 흐름은 CLAUDE.md "CQC — 근접 전투" 스펙 그대로: 공격/막기/패링/경직/그랩.
[RequireComponent(typeof(Health))]
public class CombatController : MonoBehaviour
{
    [Header("공격")]
    [Tooltip("기본 타격 피해량 (maxHp=3 기준 2회 피격 시 사망)")]
    [SerializeField] private int attackDamage = 2;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 0.6f;
    [SerializeField] private LayerMask opponentLayer;

    [Header("막기/패링")]
    [Tooltip("막기 중 받는 피해 감소 비율 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float blockDamageReduction = 0.5f;
    [Tooltip("막기 입력 직후 패링으로 인정되는 시간(초). CLAUDE.md: CQC 패링 타이밍 윈도우, 밸런싱 예정")]
    [SerializeField] private float parryWindow = 0.3f;

    [Header("경직")]
    [SerializeField] private float staggerDuration = 1.5f;

    private Health _health;
    private ClusterPenaltySystem _cluster; // 있으면 군집 페널티로 받는 피해 증가 (CLAUDE.md: CQC 피해 증가)
    private float _attackCooldownTimer;
    private float _parryWindowTimer;
    private float _staggerTimer;

    public bool IsBlocking { get; private set; }
    public bool IsStaggered => _staggerTimer > 0f;
    public bool IsParryWindowActive => _parryWindowTimer > 0f;
    // 막기 중엔 공격/그랩 불가 (막기와 공격을 동시에 할 수 없도록)
    public bool IsAttackReady => _attackCooldownTimer <= 0f && !IsStaggered && !IsBlocking;
    // AI가 사거리 밖에서 헛공격하지 않도록 판단하는 데 사용 (AIController.DecideCombatAction)
    public float AttackRange => attackRange;

    private void Awake()
    {
        _health = GetComponent<Health>();
        _cluster = GetComponent<ClusterPenaltySystem>();
        _health.OnDeath += HandleDeath;
    }

    private void OnDestroy() => _health.OnDeath -= HandleDeath;

    private void Update()
    {
        if (_attackCooldownTimer > 0f) _attackCooldownTimer -= Time.deltaTime;
        if (_parryWindowTimer > 0f) _parryWindowTimer -= Time.deltaTime;
        if (_staggerTimer > 0f) _staggerTimer -= Time.deltaTime;
    }

    // RMB를 누르고 있는 동안 매 프레임 호출. 누른 직후 parryWindow 동안은 패링 판정.
    public void SetBlocking(bool blocking)
    {
        if (IsStaggered)
        {
            IsBlocking = false;
            return;
        }

        if (blocking && !IsBlocking) _parryWindowTimer = parryWindow;
        IsBlocking = blocking;
    }

    public bool TryAttack()
    {
        if (!IsAttackReady) return false;

        _attackCooldownTimer = attackCooldown;
        FindTarget()?.ReceiveAttack(this, attackDamage);
        return true;
    }

    // F키 그랩 — 막기 중인 상대에게만 유효, 성공 시 즉시 처치
    public bool TryGrab()
    {
        if (!IsAttackReady) return false;

        var target = FindTarget();
        if (target == null || !target.IsBlocking) return false;

        _attackCooldownTimer = attackCooldown;
        target._health.TakeDamage(999);
        return true;
    }

    private CombatController FindTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, opponentLayer);
        CombatController closest = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            var cc = hit.GetComponent<CombatController>();
            if (cc == null || cc == this) continue;

            // Player → AI 방향 CQC는 AI가 Combat 상태(서로 인식)일 때만 유효
            var ai = cc.GetComponent<AIController>();
            if (ai != null && ai.CurrentState != AIController.AIState.Combat) continue;

            float dist = Vector3.Distance(transform.position, cc.transform.position);
            if (dist < closestDist)
            {
                closest = cc;
                closestDist = dist;
            }
        }
        return closest;
    }

    private void ReceiveAttack(CombatController attacker, int damage)
    {
        if (IsStaggered)
        {
            // 경직 상태 = 무방비 → 피니시
            _health.TakeDamage(999);
            return;
        }

        if (IsParryWindowActive)
        {
            _parryWindowTimer = 0f;
            attacker.Stagger();
            return;
        }

        int finalDamage = IsBlocking ? Mathf.Max(0, Mathf.RoundToInt(damage * (1f - blockDamageReduction))) : damage;
        if (_cluster != null) finalDamage = Mathf.RoundToInt(finalDamage * _cluster.DamageMultiplier);
        _health.TakeDamage(finalDamage);
    }

    public void Stagger()
    {
        _staggerTimer = staggerDuration;
        IsBlocking = false;
    }

    private void HandleDeath()
    {
        enabled = false;

        // 시체가 발소리·노출 경고·공격/무기 입력을 계속 내지 않도록 플레이어 계열 컴포넌트 일괄 정지
        // (AI에는 해당 컴포넌트가 없어 무해 — AI 사망 처리는 AIController.OnDeath)
        foreach (var type in new[] {
            typeof(PlayerController), typeof(PlayerCombatInput), typeof(RangedWeaponController),
            typeof(SoundEmitter), typeof(ExposureSystem) })
        {
            if (GetComponent(type) is MonoBehaviour mb) mb.enabled = false;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsStaggered ? Color.red : new Color(1f, 0.6f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
#endif
}
