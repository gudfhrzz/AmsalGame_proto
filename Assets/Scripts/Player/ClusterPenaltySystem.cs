using UnityEngine;

// 군집 페널티 — 아군(TeamMember) 3인 이상이 동일 반경 내 집결 시 발동.
// SoundEmitter(발소리 누적), CombatController(CQC 피해 증가), AssassinationSystem(암살 무효)가 이 컴포넌트를 참조한다.
// 레이더 노출은 미니맵 시스템이 없어 미구현 (IsPenalized 이벤트만 노출, 추후 미니맵에서 소비).
[RequireComponent(typeof(TeamMember))]
public class ClusterPenaltySystem : MonoBehaviour
{
    [Tooltip("이 반경 내 아군 인원을 센다 (밸런싱 예정)")]
    [SerializeField] private float clusterRadius = 5f;
    [Tooltip("자신 포함 이 인원 이상이면 페널티 발동")]
    [SerializeField] private int clusterThreshold = 3;
    [Tooltip("발소리 반경 배율 = 인원수 × 이 값")]
    [SerializeField] private float soundRadiusMultiplierPerMember = 1.5f;
    [Tooltip("CQC 피해 배율")]
    [SerializeField] private float cqcDamageMultiplier = 1.5f;

    private TeamMember _self;

    public bool IsPenalized { get; private set; }
    public int NearbyTeamCount { get; private set; }
    public float SoundRadiusMultiplier => IsPenalized ? NearbyTeamCount * soundRadiusMultiplierPerMember : 1f;
    public float DamageMultiplier => IsPenalized ? cqcDamageMultiplier : 1f;

    private void Awake() => _self = GetComponent<TeamMember>();

    private void Update()
    {
        int count = 1; // 자신 포함
        foreach (var other in TeamMember.All)
        {
            if (other == _self) continue;
            if (Vector3.Distance(transform.position, other.transform.position) <= clusterRadius)
                count++;
        }

        NearbyTeamCount = count;
        IsPenalized = count >= clusterThreshold;
    }
}
