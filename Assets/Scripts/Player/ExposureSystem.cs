using UnityEngine;

// 존버 방지 시스템 — 일정 시간 정지 시 위치가 노출된다.
// CLAUDE.md 스펙: 정지 허용 5~8초, 경고 카운트다운 2~3초, 노출 지속 1~3초 랜덤,
// 방어형 요원 정지 허용 시간 +2~3초, 초기화 조건은 이동 시작 즉시.
[RequireComponent(typeof(PlayerController))]
public class ExposureSystem : MonoBehaviour
{
    [Header("타이밍 (밸런싱 예정)")]
    [SerializeField] private float idleThreshold = 6f;
    [SerializeField] private float warningDuration = 2.5f;
    [SerializeField] private float exposureDurationMin = 1f;
    [SerializeField] private float exposureDurationMax = 3f;
    [Tooltip("방어형 요원 정지 허용 시간 보너스 (함정 설치 고려)")]
    [SerializeField] private float defensiveBonus = 2.5f;

    private PlayerController _player;
    private float _idleTimer;
    private float _exposureTimer;

    public bool IsWarning { get; private set; }
    public bool IsExposed { get; private set; }
    // 경고 단계 진행률 0~1 (게이지 UI용)
    public float WarningProgress01 { get; private set; }

    public event System.Action OnExposureStart;
    public event System.Action OnExposureEnd;

    private float EffectiveThreshold => idleThreshold +
        (_player.Data != null && _player.Data.agentType == AgentData.AgentType.Defensive ? defensiveBonus : 0f);

    private void Awake() => _player = GetComponent<PlayerController>();

    private void OnDisable()
    {
        // 사망 등으로 정지될 때 잔여 상태 정리 — 시체에서 경고 게이지/노출 오버레이가 남지 않도록
        // (ExposureGaugeUI가 IsWarning/WarningProgress01을 폴링하므로 값 리셋만으로 게이지가 숨겨진다)
        _idleTimer = 0f;
        IsWarning = false;
        WarningProgress01 = 0f;
        if (IsExposed)
        {
            IsExposed = false;
            OnExposureEnd?.Invoke();
        }
    }

    private void Update()
    {
        // 노출은 한 번 시작되면 움직여도 정해진 시간만큼 유지된다 (이동으로 페널티 회피 방지)
        if (IsExposed)
        {
            _exposureTimer -= Time.deltaTime;
            if (_exposureTimer <= 0f) EndExposure();
        }

        if (_player.IsMoving)
        {
            _idleTimer = 0f;
            IsWarning = false;
            WarningProgress01 = 0f;
            return;
        }

        if (IsExposed) return;

        _idleTimer += Time.deltaTime;

        float threshold = EffectiveThreshold;
        float warningStart = threshold - warningDuration;

        IsWarning = _idleTimer >= warningStart;
        WarningProgress01 = IsWarning ? Mathf.Clamp01((_idleTimer - warningStart) / warningDuration) : 0f;

        if (_idleTimer >= threshold)
            BeginExposure();
    }

    private void BeginExposure()
    {
        IsExposed = true;
        IsWarning = false;
        WarningProgress01 = 0f;
        _exposureTimer = Random.Range(exposureDurationMin, exposureDurationMax);
        OnExposureStart?.Invoke();
    }

    private void EndExposure()
    {
        IsExposed = false;
        _idleTimer = 0f;
        OnExposureEnd?.Invoke();
    }
}
