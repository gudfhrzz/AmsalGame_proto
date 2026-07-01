using UnityEngine;

// 권총 파밍 스폰 지점. Player가 트리거 반경에 들어오면 자동 획득.
// CLAUDE.md: A/B 사이트 각 1자루, 총 2자루 제한 — 라운드 리셋 시스템은 미구현이라 재스폰 로직은 없음(추후 과제).
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public class WeaponPickup : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        var weapons = other.GetComponent<RangedWeaponController>();
        if (weapons == null || weapons.HasPistol) return;

        weapons.GrantPistol();
        gameObject.SetActive(false);
    }
}
