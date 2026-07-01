using UnityEngine;
using UnityEngine.InputSystem;

// 원거리 무기 — 칼 던지기(기본 보유, 무음, 회수 필요) + 권총(파밍 필요, 1발 소모성, 발사 시 맵 전체 사운드)
// 입력: Q 칼 던지기 / R 칼 회수 / MMB 권총 발사 (InputSystem_Actions에 없는 액션이라 직접 폴링)
public class RangedWeaponController : MonoBehaviour
{
    [Header("칼 던지기")]
    [SerializeField] private float knifeSpeed = 18f;
    [SerializeField] private float knifeRecoverDistance = 1.5f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("권총 (파밍 필요)")]
    [SerializeField] private float pistolRange = 30f;
    [Tooltip("벽/적 모두 포함 — 벽에 막히는지, 적에게 명중하는지 판정")]
    [SerializeField] private LayerMask pistolHitMask;

    private ThrownKnife _activeKnife;

    public bool HasPistol { get; private set; }

    public void GrantPistol() => HasPistol = true;

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame && _activeKnife == null)
            ThrowKnife();

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            RecoverKnife();

        if (HasPistol && Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
            FirePistol();
    }

    private Vector3 GetMouseAimDirection()
    {
        if (Camera.main == null || Mouse.current == null) return transform.forward;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane groundPlane = new Plane(Vector3.up, transform.position);

        if (groundPlane.Raycast(ray, out float distance))
        {
            Vector3 dir = ray.GetPoint(distance) - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) return dir.normalized;
        }
        return transform.forward;
    }

    private void ThrowKnife()
    {
        Vector3 dir = GetMouseAimDirection();

        var knifeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        knifeGO.name = "ThrownKnife";
        knifeGO.transform.localScale = new Vector3(0.08f, 0.08f, 0.4f);
        knifeGO.transform.position = transform.position + Vector3.up * 1f + dir * 0.6f;

        var col = knifeGO.GetComponent<Collider>();
        col.isTrigger = true;

        var rb = knifeGO.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        _activeKnife = knifeGO.AddComponent<ThrownKnife>();
        _activeKnife.Launch(dir * knifeSpeed, transform, LayerMaskToLayer(enemyLayer));
    }

    private void RecoverKnife()
    {
        if (_activeKnife != null && _activeKnife.CanBeRecoveredFrom(transform.position, knifeRecoverDistance))
        {
            Destroy(_activeKnife.gameObject);
            _activeKnife = null;
        }
    }

    private void FirePistol()
    {
        Vector3 dir = GetMouseAimDirection();
        Vector3 origin = transform.position + Vector3.up * 1f;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, pistolRange, pistolHitMask))
            hit.collider.GetComponent<Health>()?.TakeDamage(999);

        // 총소리 = 맵 전체 노출 (CLAUDE.md: 총소리는 맵 전체 + 위치 노출)
        SoundEventSystem.Instance?.Emit(new SoundEvent(transform.position, 9999f, gameObject));

        HasPistol = false; // 1발 소모성
    }

    private static int LayerMaskToLayer(LayerMask mask)
    {
        int value = mask.value;
        for (int i = 0; i < 32; i++)
        {
            if ((value & (1 << i)) != 0) return i;
        }
        return -1;
    }
}
