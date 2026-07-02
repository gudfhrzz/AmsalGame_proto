using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Health), typeof(CombatController))]
public class AIController : MonoBehaviour
{
    public enum AIState { Idle, Patrol, Suspicious, Chase, Alert, Combat }

    [Header("FOV 탐지")]
    [SerializeField] private float viewDistance = 8f;
    [SerializeField] private float viewAngle = 90f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("청각 탐지")]
    [SerializeField] private float hearingRadius = 15f;

    [Header("순찰")]
    [SerializeField] private float patrolRadius = 10f;
    [SerializeField] private float idleMinDuration = 1f;
    [SerializeField] private float idleMaxDuration = 3f;
    [SerializeField] private float suspiciousDuration = 5f;
    [SerializeField] private float alertDuration = 4f;

    [Header("이동 속도")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float chaseSpeed = 4.5f;

    [Header("CQC 교전")]
    [Tooltip("Chase 중 이 거리 이내로 들어오면 CQC 교전(Combat) 시작")]
    [SerializeField] private float combatEngageRange = 2f;
    [Tooltip("이 거리를 벗어나면 교전 해제 후 재수색")]
    [SerializeField] private float combatDisengageRange = 3.5f;
    [SerializeField] private float combatRotationSpeed = 480f;
    [Tooltip("공격/막기 중 무엇을 할지 결정하는 간격 (밸런싱 예정)")]
    [SerializeField] private float combatActionIntervalMin = 0.8f;
    [SerializeField] private float combatActionIntervalMax = 1.6f;
    [SerializeField] private float combatBlockDuration = 1f;

    [Header("적 간 거리 유지 (밸런싱 예정)")]
    [Tooltip("이 거리 이내에 다른 아군이 있으면 순찰/수색 목적지를 재조정")]
    [SerializeField] private float separationRadius = 4f;
    [Tooltip("근접 여부 재확인 주기(초)")]
    [SerializeField] private float separationCheckInterval = 1f;
    [Tooltip("Chase 중 이미 교전 중인 아군이 있으면 그 아군 기준 이 각도 범위로 우회 접근")]
    [SerializeField] private float flankAngleMin = 110f;
    [SerializeField] private float flankAngleMax = 150f;

    [Header("코너 확인 — Suspicious 전용 (밸런싱 예정)")]
    [Tooltip("이 각도 이상 꺾이는 경로 코너에서 잠깐 멈춰 확인")]
    [SerializeField] private float peekTurnAngleThreshold = 65f;
    [Tooltip("코너로부터 이 거리 이내로 접근하면 확인 동작 시작")]
    [SerializeField] private float peekTriggerDistance = 2f;
    [SerializeField] private float peekDurationMin = 0.4f;
    [SerializeField] private float peekDurationMax = 0.6f;

    [Header("CQC 거리 유지 (밸런싱 예정)")]
    [Tooltip("공격을 선택할 확률 (사거리 밖이면 막기로 대체)")]
    [SerializeField] private float attackChance = 0.45f;
    [Tooltip("후퇴(거리 벌리기)를 선택할 확률 — 나머지는 막기")]
    [SerializeField] private float retreatChance = 0.3f;
    [SerializeField] private float combatRetreatDistance = 1.3f;
    [SerializeField] private float combatRetreatSpeed = 2.5f;

    private NavMeshAgent _agent;
    private Health _health;
    private CombatController _combat;
    private Transform _player;
    private AIState _state;
    private float _stateTimer;
    private Vector3 _lastKnownSoundPos;
    private Vector3 _spawnPosition;
    private float _combatActionTimer;
    private float _combatBlockTimer;
    private float _separationTimer;
    private bool _hasFlankSign;
    private float _flankSide;
    private bool _isPeeking;
    private float _peekTimer;
    private Vector3 _lastPeekedCornerPos = Vector3.positiveInfinity;
    private bool _isRetreating;
    private float _retreatTimer;

    public static readonly List<AIController> All = new List<AIController>();

    public AIState CurrentState => _state;

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<Health>();
        _combat = GetComponent<CombatController>();
        _spawnPosition = transform.position;
    }

    private void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) _player = playerObj.transform;

        if (SoundEventSystem.Instance != null)
            SoundEventSystem.Instance.OnSoundEmitted += OnSoundHeard;

        _health.OnDeath += OnDeath;

        EnterState(AIState.Patrol);
    }

    private void OnDestroy()
    {
        if (SoundEventSystem.Instance != null)
            SoundEventSystem.Instance.OnSoundEmitted -= OnSoundHeard;
        _health.OnDeath -= OnDeath;
    }

    private void Update()
    {
        _stateTimer -= Time.deltaTime;

        if (_state != AIState.Chase && _state != AIState.Combat)
            CheckFOV();

        switch (_state)
        {
            case AIState.Idle:       UpdateIdle();       break;
            case AIState.Patrol:     UpdatePatrol();     break;
            case AIState.Suspicious: UpdateSuspicious(); break;
            case AIState.Chase:      UpdateChase();      break;
            case AIState.Alert:      UpdateAlert();      break;
            case AIState.Combat:     UpdateCombat();     break;
        }
    }

    // ── 상태 전환 ──────────────────────────────────

    private void EnterState(AIState next)
    {
        // Combat 상태에서만 수동 회전(플레이어 응시)을 쓰고, 그 외엔 NavMesh 자체 회전에 맡긴다
        _agent.updateRotation = (next != AIState.Combat);

        _state = next;
        switch (next)
        {
            case AIState.Idle:
                _agent.isStopped = true;
                _stateTimer = Random.Range(idleMinDuration, idleMaxDuration);
                break;

            case AIState.Patrol:
                _agent.isStopped = false;
                _agent.speed = patrolSpeed;
                MoveToRandomPoint(_spawnPosition, patrolRadius);
                break;

            case AIState.Suspicious:
                _agent.isStopped = false;
                _agent.speed = patrolSpeed;
                _agent.SetDestination(_lastKnownSoundPos);
                _stateTimer = suspiciousDuration;
                _isPeeking = false;
                _lastPeekedCornerPos = Vector3.positiveInfinity;
                break;

            case AIState.Chase:
                _agent.isStopped = false;
                _agent.speed = chaseSpeed;
                _hasFlankSign = false;
                break;

            case AIState.Alert:
                _agent.isStopped = true;
                _stateTimer = alertDuration;
                break;

            case AIState.Combat:
                _agent.isStopped = true;
                _combatActionTimer = 0f; // 즉시 첫 행동 결정
                _combatBlockTimer = 0f;
                _isRetreating = false;
                break;
        }
    }

    // ── 상태별 업데이트 ────────────────────────────

    private void UpdateIdle()
    {
        if (_stateTimer <= 0f) EnterState(AIState.Patrol);
    }

    private void UpdatePatrol()
    {
        if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
        {
            EnterState(AIState.Idle);
            return;
        }

        TrySeparationNudge();
    }

    private void UpdateSuspicious()
    {
        if (_isPeeking)
        {
            _peekTimer -= Time.deltaTime;
            if (_peekTimer <= 0f)
            {
                _isPeeking = false;
                _agent.isStopped = false;
            }
        }
        else if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
        {
            _agent.isStopped = true; // 목적지 도착 후 제자리 탐색
        }
        else
        {
            CheckCornerPeek();
            TrySeparationNudge();
        }

        if (_stateTimer <= 0f) EnterState(AIState.Patrol);
    }

    private void UpdateChase()
    {
        if (_player == null) return;

        // 측면 우회 중엔 이동 방향이 플레이어 방향과 크게 벌어질 수 있으므로 각도 체크는 생략 (거리/장애물 체크는 유지)
        if (!CanSeePlayer(requireAngle: false))
        {
            // 시야 잃음 → 마지막 위치 조사
            _lastKnownSoundPos = _player.position;
            EnterState(AIState.Suspicious);
            return;
        }

        Vector3 toPlayer = _player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude <= combatEngageRange)
        {
            EnterState(AIState.Combat);
            return;
        }

        _agent.SetDestination(GetChaseDestination());
    }

    // 이미 교전 중인 아군이 있으면 겹치지 않도록 측면 우회 지점을 계산 (없으면 플레이어 위치 그대로)
    private Vector3 GetChaseDestination()
    {
        AIController engagedAlly = FindEngagedAlly();
        if (engagedAlly == null) return _player.position;

        // 동시에 플레이어를 발견해도 정확히 한쪽만 우회를 맡도록 결정적 타이브레이커 사용
        if (GetInstanceID() <= engagedAlly.GetInstanceID()) return _player.position;

        if (!_hasFlankSign)
        {
            _flankSide = Random.value < 0.5f ? -1f : 1f;
            _hasFlankSign = true;
        }

        Vector3 toAlly = engagedAlly.transform.position - _player.position;
        toAlly.y = 0f;
        if (toAlly.sqrMagnitude < 0.01f) toAlly = -_player.forward;

        float flankAngle = Random.Range(flankAngleMin, flankAngleMax) * _flankSide;
        Vector3 flankDir = Quaternion.Euler(0f, flankAngle, 0f) * toAlly.normalized;
        Vector3 flankPoint = _player.position + flankDir * combatEngageRange;

        if (NavMesh.SamplePosition(flankPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            return hit.position;

        return _player.position;
    }

    private AIController FindEngagedAlly()
    {
        foreach (var other in All)
        {
            if (other == this) continue;
            if (other.CurrentState == AIState.Chase || other.CurrentState == AIState.Combat)
                return other;
        }
        return null;
    }

    private void UpdateAlert()
    {
        if (_stateTimer <= 0f) EnterState(AIState.Patrol);
    }

    private void UpdateCombat()
    {
        if (_player == null)
        {
            EnterState(AIState.Patrol);
            return;
        }

        Vector3 toPlayer = _player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > combatDisengageRange || !CanSeePlayer())
        {
            _lastKnownSoundPos = _player.position;
            EnterState(AIState.Suspicious);
            return;
        }

        if (toPlayer.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, combatRotationSpeed * Time.deltaTime);
        }

        _combatBlockTimer -= Time.deltaTime;
        _combat.SetBlocking(_combatBlockTimer > 0f);

        // 후퇴 이동 중엔 별도 로직으로 위임 (행동 결정은 후퇴가 끝난 뒤 재개)
        if (_isRetreating)
        {
            UpdateRetreat();
            return;
        }

        _combatActionTimer -= Time.deltaTime;
        if (_combatActionTimer <= 0f)
            DecideCombatAction(toPlayer.magnitude);
    }

    // 후퇴 이동 진행 — 도착하거나 시간 초과되면 정지 후 다음 행동 결정으로 복귀
    private void UpdateRetreat()
    {
        _retreatTimer -= Time.deltaTime;
        bool arrived = !_agent.pathPending && _agent.remainingDistance < 0.2f;

        if (arrived || _retreatTimer <= 0f)
        {
            _isRetreating = false;
            _agent.isStopped = true;
            _combatActionTimer = Random.Range(combatActionIntervalMin, combatActionIntervalMax);
        }
    }

    // 간단한 확률 기반 CQC 행동 (플레이스홀더 — 밸런싱 예정): 공격 / 후퇴 / 막기
    private void DecideCombatAction(float distanceToPlayer)
    {
        _combatActionTimer = Random.Range(combatActionIntervalMin, combatActionIntervalMax);

        float roll = Random.value;
        if (roll < attackChance)
        {
            // 후퇴 직후 등 사거리 밖이면 헛공격 대신 막기로 대체
            if (distanceToPlayer <= _combat.AttackRange) _combat.TryAttack();
            else _combatBlockTimer = combatBlockDuration;
        }
        else if (roll < attackChance + retreatChance)
        {
            BeginRetreat();
        }
        else
        {
            _combatBlockTimer = combatBlockDuration;
        }
    }

    // 플레이어 반대 방향으로 짧게 거리를 벌린다 (격투기의 백스텝 스페이싱 재현)
    private void BeginRetreat()
    {
        Vector3 away = transform.position - _player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;

        Vector3 candidate = transform.position + away.normalized * combatRetreatDistance;
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, combatRetreatDistance, NavMesh.AllAreas))
        {
            // 후퇴할 공간이 없으면 막기로 대체
            _combatBlockTimer = combatBlockDuration;
            return;
        }

        _isRetreating = true;
        _retreatTimer = 0.6f; // 목적지 도착 못해도 이 시간 후 강제 종료 (안전장치)
        _agent.speed = combatRetreatSpeed;
        _agent.isStopped = false;
        _agent.SetDestination(hit.position);
    }

    // ── 탐지 ──────────────────────────────────────

    private void CheckFOV()
    {
        if (CanSeePlayer()) EnterState(AIState.Chase);
    }

    private bool CanSeePlayer(bool requireAngle = true)
    {
        if (_player == null) return false;

        Vector3 toPlayer = _player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > viewDistance) return false;
        if (requireAngle && Vector3.Angle(transform.forward, toPlayer) > viewAngle * 0.5f) return false;

        // 장애물 차단 확인
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, toPlayer.normalized, toPlayer.magnitude, obstacleLayer))
            return false;

        return true;
    }

    private void OnSoundHeard(SoundEvent e)
    {
        if (e.Source == gameObject) return;

        float dist = Vector3.Distance(transform.position, e.Position);
        if (dist > e.Radius || dist > hearingRadius) return;

        _lastKnownSoundPos = e.Position;

        if (_state == AIState.Idle || _state == AIState.Patrol || _state == AIState.Alert)
            EnterState(AIState.Suspicious);
    }

    public void TriggerAlert()
    {
        if (_state != AIState.Chase && _state != AIState.Combat)
            EnterState(AIState.Alert);
    }

    // ── 유틸리티 ───────────────────────────────────

    private void MoveToRandomPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 rand2D = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(rand2D.x, 0f, rand2D.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                continue;

            if (IsNearAlly(hit.position)) continue; // 아군과 겹치는 순찰 지점은 재시도

            _agent.SetDestination(hit.position);
            return;
        }
        _agent.SetDestination(center);
    }

    private bool IsNearAlly(Vector3 point)
    {
        foreach (var other in All)
        {
            if (other == this) continue;
            if (Vector3.Distance(point, other.transform.position) < separationRadius)
                return true;
        }
        return false;
    }

    // 이동 중(Patrol/Suspicious) 가까운 아군이 있으면 반대 방향으로 목적지를 재조정 (주기적으로만 체크)
    private void TrySeparationNudge()
    {
        if (_agent.isStopped) return;

        _separationTimer -= Time.deltaTime;
        if (_separationTimer > 0f) return;
        _separationTimer = separationCheckInterval;

        AIController closest = null;
        float closestDist = float.MaxValue;
        foreach (var other in All)
        {
            if (other == this) continue;
            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < closestDist)
            {
                closest = other;
                closestDist = dist;
            }
        }

        if (closest == null || closestDist >= separationRadius) return;

        Vector3 away = transform.position - closest.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));

        Vector3 candidate = transform.position + away.normalized * separationRadius;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, separationRadius, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    // 경로상 급격히 꺾이는 코너에 접근하면 잠깐 멈춰 방향을 살핀다 (Suspicious 전용 전술 동작)
    private void CheckCornerPeek()
    {
        if (_agent.pathPending || !_agent.hasPath || _agent.path.status != NavMeshPathStatus.PathComplete)
            return;

        Vector3[] corners = _agent.path.corners;
        if (corners.Length < 3) return;

        for (int i = 1; i < corners.Length - 1; i++)
        {
            Vector3 corner = corners[i];
            if ((corner - _lastPeekedCornerPos).sqrMagnitude < 0.25f) continue; // 이미 확인한 코너

            if (Vector3.Distance(transform.position, corner) > peekTriggerDistance) continue;

            Vector3 incoming = corner - corners[i - 1]; incoming.y = 0f;
            Vector3 outgoing = corners[i + 1] - corner; outgoing.y = 0f;
            if (incoming.sqrMagnitude < 0.01f || outgoing.sqrMagnitude < 0.01f) continue;

            if (Vector3.Angle(incoming.normalized, outgoing.normalized) < peekTurnAngleThreshold) continue;

            _lastPeekedCornerPos = corner;
            _isPeeking = true;
            _peekTimer = Random.Range(peekDurationMin, peekDurationMax);
            _agent.isStopped = true;
            transform.rotation = Quaternion.LookRotation(outgoing.normalized);
            return;
        }
    }

    private void OnDeath()
    {
        _agent.isStopped = true;
        enabled = false;
        Destroy(gameObject, 2f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // FOV 시야각
        Vector3 fwd = transform.forward;
        Vector3 left  = Quaternion.Euler(0, -viewAngle * 0.5f, 0) * fwd;
        Vector3 right = Quaternion.Euler(0,  viewAngle * 0.5f, 0) * fwd;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, left  * viewDistance);
        Gizmos.DrawRay(transform.position, right * viewDistance);
        Gizmos.DrawWireSphere(transform.position, viewDistance);

        // 청각 반경
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);

        // 순찰 반경
        Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
        Vector3 origin = Application.isPlaying ? _spawnPosition : transform.position;
        Gizmos.DrawWireSphere(origin, patrolRadius);

        // CQC 교전/해제 반경
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, combatEngageRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, combatDisengageRange);
    }
#endif
}
