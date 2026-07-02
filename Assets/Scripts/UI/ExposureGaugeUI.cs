using UnityEngine;
using UnityEngine.UI;

// 존버 방지 HUD — 우상단 경고 게이지 + 노출 시 화면 경고 오버레이
public class ExposureGaugeUI : MonoBehaviour
{
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
        if (source == null) return;
        source.OnExposureStart += HandleExposureStart;
        source.OnExposureEnd += HandleExposureEnd;
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

        if (gaugeFill != null)
        {
            gaugeFill.gameObject.SetActive(showGauge);
            gaugeFill.fillAmount = source.WarningProgress01;
        }

        if (countdownText != null)
            countdownText.gameObject.SetActive(showGauge);

        if (gaugeBackground != null)
            gaugeBackground.gameObject.SetActive(showGauge);
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
