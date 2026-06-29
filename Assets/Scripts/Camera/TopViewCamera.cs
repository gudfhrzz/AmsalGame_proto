using UnityEngine;

public class TopViewCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [Tooltip("플레이어 기준 카메라 오프셋. Y=높이, Z=뒤로 당김(기울기)")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 14f, -3f);
    [SerializeField] private float smoothSpeed = 8f;

    [Header("Angle")]
    [Tooltip("75°=약간 기울어진 탑뷰, 90°=완전 수직 탑뷰")]
    [SerializeField] private Vector3 eulerAngles = new Vector3(75f, 0f, 0f);

    private void Start()
    {
        transform.rotation = Quaternion.Euler(eulerAngles);
        if (target != null)
            transform.position = target.position + offset;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        transform.rotation = Quaternion.Euler(eulerAngles);
    }
#endif
}
