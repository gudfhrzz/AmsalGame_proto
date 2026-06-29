using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Health))]
public class AIController : MonoBehaviour
{
    public enum AIState { Idle, Patrol, Suspicious, Chase, Alert }

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

    private NavMeshAgent _agent;
    private Health _health;
    private Transform _player;
    private AIState _state;
    private float _stateTimer;
    private Vector3 _lastKnownSoundPos;
    private Vector3 _spawnPosition;

    public AIState CurrentState => _state;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<Health>();
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

        if (_state != AIState.Chase)
            CheckFOV();

        switch (_state)
        {
            case AIState.Idle:       UpdateIdle();       break;
            case AIState.Patrol:     UpdatePatrol();     break;
            case AIState.Suspicious: UpdateSuspicious(); break;
            case AIState.Chase:      UpdateChase();      break;
            case AIState.Alert:      UpdateAlert();      break;
        }
    }

    // ── 상태 전환 ──────────────────────────────────

    private void EnterState(AIState next)
    {
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
                break;

            case AIState.Chase:
                _agent.isStopped = false;
                _agent.speed = chaseSpeed;
                break;

            case AIState.Alert:
                _agent.isStopped = true;
                _stateTimer = alertDuration;
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
            EnterState(AIState.Idle);
    }

    private void UpdateSuspicious()
    {
        if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
            _agent.isStopped = true; // 목적지 도착 후 제자리 탐색

        if (_stateTimer <= 0f) EnterState(AIState.Patrol);
    }

    private void UpdateChase()
    {
        if (_player == null) return;

        if (CanSeePlayer())
        {
            _agent.SetDestination(_player.position);
        }
        else
        {
            // 시야 잃음 → 마지막 위치 조사
            _lastKnownSoundPos = _player.position;
            EnterState(AIState.Suspicious);
        }
    }

    private void UpdateAlert()
    {
        if (_stateTimer <= 0f) EnterState(AIState.Patrol);
    }

    // ── 탐지 ──────────────────────────────────────

    private void CheckFOV()
    {
        if (CanSeePlayer()) EnterState(AIState.Chase);
    }

    private bool CanSeePlayer()
    {
        if (_player == null) return false;

        Vector3 toPlayer = _player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > viewDistance) return false;
        if (Vector3.Angle(transform.forward, toPlayer) > viewAngle * 0.5f) return false;

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
        if (_state != AIState.Chase)
            EnterState(AIState.Alert);
    }

    // ── 유틸리티 ───────────────────────────────────

    private void MoveToRandomPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 rand2D = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(rand2D.x, 0f, rand2D.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
                return;
            }
        }
        _agent.SetDestination(center);
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
    }
#endif
}
