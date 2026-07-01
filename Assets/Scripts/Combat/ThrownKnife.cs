using UnityEngine;

// 날아가는 칼. Enemy 레이어에 맞으면 즉사, 그 외(벽 등)에 맞으면 착지음을 내고 박힌다.
// CLAUDE.md: 무음, 빗나가면 착지음 발생 → 주변 AI/플레이어 반응
[RequireComponent(typeof(Rigidbody))]
public class ThrownKnife : MonoBehaviour
{
    private const float LandingSoundRadius = 6f;

    private Vector3 _velocity;
    private Transform _owner;
    private int _enemyLayer;

    public bool IsStuck { get; private set; }

    public void Launch(Vector3 velocity, Transform owner, int enemyLayer)
    {
        _velocity = velocity;
        _owner = owner;
        _enemyLayer = enemyLayer;
        transform.rotation = Quaternion.LookRotation(velocity.normalized);
        Destroy(gameObject, 6f); // 아무것도 맞추지 못했을 때의 안전장치
    }

    private void Update()
    {
        if (IsStuck) return;
        transform.position += _velocity * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsStuck || other.transform == _owner) return;

        if (other.gameObject.layer == _enemyLayer)
        {
            other.GetComponent<Health>()?.TakeDamage(999);
            Stick();
            return;
        }

        SoundEventSystem.Instance?.Emit(new SoundEvent(transform.position, LandingSoundRadius, gameObject));
        Stick();
    }

    private void Stick()
    {
        IsStuck = true;
        _velocity = Vector3.zero;
    }

    public bool CanBeRecoveredFrom(Vector3 fromPosition, float recoverDistance) =>
        IsStuck && Vector3.Distance(fromPosition, transform.position) <= recoverDistance;
}
