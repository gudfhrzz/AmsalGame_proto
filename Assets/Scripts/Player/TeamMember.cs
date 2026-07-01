using System.Collections.Generic;
using UnityEngine;

// 군집 페널티 감지를 위한 아군 식별 마커. 현재는 Player 1명뿐이라 실전 트리거는
// 멀티플레이(Phase 3)에서 팀원이 추가된 뒤 검증 필요.
public class TeamMember : MonoBehaviour
{
    public static readonly List<TeamMember> All = new List<TeamMember>();

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);
}
