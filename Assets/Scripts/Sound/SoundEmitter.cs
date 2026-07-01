using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class SoundEmitter : MonoBehaviour
{
    [SerializeField] private float emitInterval = 0.3f;

    private PlayerController _player;
    private ClusterPenaltySystem _cluster; // 있으면 군집 페널티로 발소리 반경 누적 (CLAUDE.md: 인원수 × 1.5배)
    private float _timer;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _cluster = GetComponent<ClusterPenaltySystem>();
    }

    private void Update()
    {
        if (!_player.IsMoving || _player.CurrentSoundRadius <= 0f) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        _timer = emitInterval;
        float radius = _player.CurrentSoundRadius * (_cluster != null ? _cluster.SoundRadiusMultiplier : 1f);
        SoundEventSystem.Instance?.Emit(new SoundEvent(
            transform.position,
            radius,
            gameObject
        ));
    }
}
