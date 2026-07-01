using UnityEngine;
using UnityEngine.UI;

// 암살 가능한 적 머리 위에 아이콘을 띄운다. Screen Space - Overlay 캔버스 하위에 배치되어야 한다.
[RequireComponent(typeof(RectTransform), typeof(Image))]
public class AssassinationIndicatorUI : MonoBehaviour
{
    [SerializeField] private AssassinationSystem source;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private float worldHeightOffset = 2.2f;

    private RectTransform _rect;
    private Image _image;

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _image = GetComponent<Image>();
    }

    public void Bind(AssassinationSystem assassination, Camera cam, RectTransform canvas)
    {
        source = assassination;
        worldCamera = cam;
        canvasRect = canvas;
    }

    private void LateUpdate()
    {
        if (source == null || worldCamera == null || canvasRect == null) return;

        bool visible = source.CanAssassinate && source.CurrentTarget != null;
        if (!visible)
        {
            if (_image.enabled) _image.enabled = false;
            return;
        }

        Vector3 worldPos = source.CurrentTarget.position + Vector3.up * worldHeightOffset;
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);

        if (screenPos.z < 0f)
        {
            _image.enabled = false;
            return;
        }

        _image.enabled = true;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out Vector2 localPoint))
            _rect.anchoredPosition = localPoint;
    }
}
