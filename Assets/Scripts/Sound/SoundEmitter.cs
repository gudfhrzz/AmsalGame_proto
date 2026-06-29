using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class SoundEmitter : MonoBehaviour
{
    [SerializeField] private float emitInterval = 0.3f;

    private PlayerController _player;
    private float _timer;

    private void Awake() => _player = GetComponent<PlayerController>();

    private void Update()
    {
        if (!_player.IsMoving || _player.CurrentSoundRadius <= 0f) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        _timer = emitInterval;
        SoundEventSystem.Instance?.Emit(new SoundEvent(
            transform.position,
            _player.CurrentSoundRadius,
            gameObject
        ));
    }
}
