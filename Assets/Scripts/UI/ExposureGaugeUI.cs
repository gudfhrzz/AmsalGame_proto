using UnityEngine;
using UnityEngine.UI;

// 존버 방지 HUD — 우상단 경고 게이지 + 노출 시 화면 경고 오버레이
public class ExposureGaugeUI : MonoBehaviour
{
    [SerializeField] private ExposureSystem source;
    [SerializeField] private Image gaugeFill;
    [SerializeField] private Text countdownText;
    [SerializeField] private Image warningOverlay;

    public void Bind(ExposureSystem exposure, Image gauge, Text countdown, Image overlay)
    {
        source = exposure;
        gaugeFill = gauge;
        countdownText = countdown;
        warningOverlay = overlay;
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
