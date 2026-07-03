using UnityEngine;
using UnityEngine.UI;

// 존버 방지 HUD — 우상단 경고 게이지 + 노출 시 가장자리 비네트 경고
// (플레이테스트: 화면 전체가 빨개지면 플레이 방해 → 중앙 투명 비네트 스프라이트를 런타임 절차 생성해 주입)
public class ExposureGaugeUI : MonoBehaviour
{
    private const int VignetteTextureSize = 256;
    [Tooltip("이 반경(0~1, 화면 중심 기준)부터 가장자리로 갈수록 빨개짐")]
    [SerializeField] private float vignetteInnerRadius = 0.55f;

    private Texture2D _vignetteTexture;
    private Sprite _vignetteSprite;
    private bool? _lastGaugeVisible; // SetActive를 상태 변화 시에만 호출 (매 프레임 중복 호출 방지)

    [SerializeField] private ExposureSystem source;
    [SerializeField] private Image gaugeFill;
    [SerializeField] private Text countdownText;
    [SerializeField] private Image warningOverlay;
    [SerializeField] private Image gaugeBackground; // R6풍 어두운 슬롯 — 경고 중에만 게이지와 함께 표시

    public void Bind(ExposureSystem exposure, Image gauge, Text countdown, Image overlay, Image background = null)
    {
        source = exposure;
        gaugeFill = gauge;
        countdownText = countdown;
        warningOverlay = overlay;
        gaugeBackground = background;
    }

    private void OnEnable()
    {
        EnsureVignetteSprite();
        if (source == null) return;
        source.OnExposureStart += HandleExposureStart;
        source.OnExposureEnd += HandleExposureEnd;
    }

    private void OnDestroy()
    {
        if (_vignetteSprite != null) Destroy(_vignetteSprite);
        if (_vignetteTexture != null) Destroy(_vignetteTexture);
    }

    // 중앙 투명 → 가장자리 불투명 방사형 텍스처 — 씬에 스프라이트 에셋을 두지 않고 런타임 생성
    // (미니맵의 절차 텍스처와 같은 패턴)
    private void EnsureVignetteSprite()
    {
        if (warningOverlay == null || _vignetteSprite != null) return;

        int size = VignetteTextureSize;
        _vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(vignetteInnerRadius, 1f, r));
                _vignetteTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        _vignetteTexture.Apply();

        _vignetteSprite = Sprite.Create(_vignetteTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        warningOverlay.sprite = _vignetteSprite;
    }

    private void OnDisable()
    {
        if (source == null) return;
        source.OnExposureStart -= HandleExposureStart;
        source.OnExposureEnd -= HandleExposureEnd;
    }

    private void Update()
    {
        if (source == null) return;

        bool showGauge = source.IsWarning;

        if (_lastGaugeVisible != showGauge)
        {
            _lastGaugeVisible = showGauge;
            if (gaugeFill != null) gaugeFill.gameObject.SetActive(showGauge);
            if (countdownText != null) countdownText.gameObject.SetActive(showGauge);
            if (gaugeBackground != null) gaugeBackground.gameObject.SetActive(showGauge);
        }

        if (showGauge && gaugeFill != null)
            gaugeFill.fillAmount = source.WarningProgress01;
    }

    private void HandleExposureStart()
    {
        if (warningOverlay != null) warningOverlay.enabled = true;
    }

    private void HandleExposureEnd()
    {
        if (warningOverlay != null) warningOverlay.enabled = false;
    }
}
