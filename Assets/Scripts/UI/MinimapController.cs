using UnityEngine;
using UnityEngine.UI;

// 좌상단 미니맵 — 전용 카메라(Minimap 레이어 전용)가 런타임 생성 RenderTexture에 평면도를 그리고,
// SoundEventSystem의 소리 이벤트를 실제 반경 크기의 원형 핑으로 시각화한다 (CLAUDE.md: 사운드 핑).
// 적 위치는 의도적으로 표시하지 않는다 (정보전 컨셉) — 미니맵에서 얻는 정보는 소리와 자기 노출뿐.
// 씬 배선은 Phase1SceneSetup.SetupMinimap()이 담당.
public class MinimapController : MonoBehaviour
{
    [Header("카메라/렌더텍스처 (밸런싱 예정)")]
    [Tooltip("미니맵 렌더텍스처 해상도")]
    [SerializeField] private int rtResolution = 256;
    [Tooltip("미니맵에 보이는 반경(m) — 직교 카메라 orthographicSize")]
    [SerializeField] private float radarRange = 18f;
    [Tooltip("미니맵 카메라 높이")]
    [SerializeField] private float cameraHeight = 30f;

    [Header("사운드 핑 (밸런싱 예정)")]
    [Tooltip("핑 지속 시간(초) — 페이드아웃 포함")]
    [SerializeField] private float pingLifetime = 1f;
    [Tooltip("핑 반경 최소 클램프(m) — 작은 발소리도 보이게")]
    [SerializeField] private float pingRadiusMin = 1.5f;
    [Tooltip("핑 반경 최대 클램프(m) — 총성(반경 9999) 대응")]
    [SerializeField] private float pingRadiusMax = 25f;
    [Tooltip("이 반경 이상이면 전역 사운드(총성)로 간주해 경고색 사용")]
    [SerializeField] private float alarmRadiusThreshold = 50f;
    [SerializeField] private Color pingColor = new Color(1f, 1f, 0.6f, 0.6f);
    [SerializeField] private Color alarmPingColor = new Color(1f, 0.2f, 0.2f, 0.7f);

    [Header("존버 노출 링 (밸런싱 예정)")]
    [Tooltip("노출 상태 링 펄스 속도")]
    [SerializeField] private float exposurePulseSpeed = 4f;
    [SerializeField] private Color exposureRingColor = new Color(1f, 0.15f, 0.15f, 0.8f);

    [Header("원형 테두리 (R6풍)")]
    [Tooltip("미니맵 외곽 링 색상")]
    [SerializeField] private Color frameRingColor = new Color(0.85f, 0.88f, 0.95f, 0.55f);

    [Header("배선 (Phase1SceneSetup이 Bind로 주입)")]
    [SerializeField] private Camera minimapCamera;
    [SerializeField] private RawImage targetImage;
    [Tooltip("원형 마스크 Image — 스프라이트는 런타임에 절차 생성해 주입")]
    [SerializeField] private Image maskImage;
    [Tooltip("원형 외곽 링 Image — 스프라이트는 런타임에 절차 생성해 주입")]
    [SerializeField] private Image frameRingImage;
    [SerializeField] private Transform player;
    [SerializeField] private ExposureSystem exposure;
    [SerializeField] private GameStateManager gameState;
    [SerializeField] private GameObject exposureRing;

    // 핑은 벽 클론(y≈0~3)보다 위에 그려 소팅 문제를 원천 차단
    private const float PingHeight = 10f;
    private const string MinimapLayerName = "Minimap";

    private RenderTexture _rt;
    private Texture2D _discTexture;
    private Texture2D _ringTexture;
    private Texture2D _maskTexture;
    private Texture2D _frameRingTexture;
    private Sprite _maskSprite;
    private Sprite _frameRingSprite;
    private Material _ringMaterial;
    private int _minimapLayer;
    private bool _gameEnded;

    public void Bind(Camera cam, RawImage image, Image circleMask, Image circleFrame, Transform playerTransform,
        ExposureSystem exposureSystem, GameStateManager state, GameObject ring)
    {
        minimapCamera = cam;
        targetImage = image;
        maskImage = circleMask;
        frameRingImage = circleFrame;
        player = playerTransform;
        exposure = exposureSystem;
        gameState = state;
        exposureRing = ring;
    }

    private void Start()
    {
        _minimapLayer = LayerMask.NameToLayer(MinimapLayerName);
        _discTexture = CreateDiscTexture(64);
        _ringTexture = CreateRingTexture(64);

        if (minimapCamera != null)
        {
            _rt = new RenderTexture(rtResolution, rtResolution, 16);
            minimapCamera.targetTexture = _rt;
            minimapCamera.orthographicSize = radarRange;
            if (targetImage != null) targetImage.texture = _rt;
        }

        // 원형 마스크/외곽 링 스프라이트 주입 (R6풍 원형 미니맵) — UI Mask의 스텐실이 이 알파 경계를 따라간다
        if (maskImage != null)
        {
            _maskTexture = CreateSolidDiscTexture(256);
            _maskSprite = Sprite.Create(_maskTexture, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f));
            maskImage.sprite = _maskSprite;
        }
        if (frameRingImage != null)
        {
            _frameRingTexture = CreateThinRingTexture(256);
            _frameRingSprite = Sprite.Create(_frameRingTexture, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f));
            frameRingImage.sprite = _frameRingSprite;
            frameRingImage.color = frameRingColor;
        }

        if (exposureRing != null)
        {
            var ringRenderer = exposureRing.GetComponent<MeshRenderer>();
            if (ringRenderer != null)
            {
                _ringMaterial = new Material(Shader.Find("Sprites/Default"))
                {
                    mainTexture = _ringTexture,
                    color = exposureRingColor
                };
                ringRenderer.sharedMaterial = _ringMaterial;
                ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            exposureRing.SetActive(false);
        }

        if (SoundEventSystem.Instance != null)
            SoundEventSystem.Instance.OnSoundEmitted += HandleSound;
        if (exposure != null)
        {
            exposure.OnExposureStart += HandleExposureStart;
            exposure.OnExposureEnd += HandleExposureEnd;
        }
        if (gameState != null)
            gameState.OnGameEnded += HandleGameEnded;
    }

    private void OnDestroy()
    {
        if (SoundEventSystem.Instance != null)
            SoundEventSystem.Instance.OnSoundEmitted -= HandleSound;
        if (exposure != null)
        {
            exposure.OnExposureStart -= HandleExposureStart;
            exposure.OnExposureEnd -= HandleExposureEnd;
        }
        if (gameState != null)
            gameState.OnGameEnded -= HandleGameEnded;

        // 카메라가 해제된 RT에 그리지 않도록 targetTexture부터 끊는다
        if (minimapCamera != null) minimapCamera.targetTexture = null;
        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
        }
        if (_discTexture != null) Destroy(_discTexture);
        if (_ringTexture != null) Destroy(_ringTexture);
        if (_maskSprite != null) Destroy(_maskSprite);
        if (_frameRingSprite != null) Destroy(_frameRingSprite);
        if (_maskTexture != null) Destroy(_maskTexture);
        if (_frameRingTexture != null) Destroy(_frameRingTexture);
        if (_ringMaterial != null) Destroy(_ringMaterial);
    }

    private void Update()
    {
        // 노출 링 펄스 (활성 상태에서만)
        if (_ringMaterial != null && exposureRing != null && exposureRing.activeSelf)
        {
            Color c = exposureRingColor;
            c.a = exposureRingColor.a * (0.55f + 0.45f * Mathf.Sin(Time.time * exposurePulseSpeed));
            _ringMaterial.color = c;
        }
    }

    private void LateUpdate()
    {
        // 북쪽 고정(회전 없음) 플레이어 추적 — 카메라 회전은 씬 배선 시 (90,0,0)으로 설정됨
        if (minimapCamera != null && player != null)
            minimapCamera.transform.position = new Vector3(player.position.x, cameraHeight, player.position.z);
    }

    private void HandleSound(SoundEvent e)
    {
        // 게임 종료 후 게이트 — 승패 확정 이후의 소리는 정보 가치가 없으므로 핑을 그리지 않는다
        // (사망 시 유령 핑의 근본 원인이던 IsMoving 잔존은 PlayerController.OnDisable에서 해결됨 — 이중 안전장치)
        if (_gameEnded) return;

        bool isAlarm = e.Radius >= alarmRadiusThreshold;
        float radius = Mathf.Clamp(e.Radius, pingRadiusMin, pingRadiusMax);

        var pingGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pingGO.name = "MinimapPing";
        Destroy(pingGO.GetComponent<Collider>());

        var ping = pingGO.AddComponent<MinimapPing>();
        ping.Init(
            new Vector3(e.Position.x, PingHeight, e.Position.z),
            radius,
            isAlarm ? alarmPingColor : pingColor,
            pingLifetime,
            _discTexture,
            _minimapLayer
        );
    }

    private void HandleExposureStart()
    {
        if (_gameEnded) return;
        if (exposureRing != null) exposureRing.SetActive(true);
    }

    private void HandleExposureEnd()
    {
        if (exposureRing != null) exposureRing.SetActive(false);
    }

    private void HandleGameEnded(GameStateManager.Result result)
    {
        _gameEnded = true;
        if (exposureRing != null) exposureRing.SetActive(false);
    }

    // ── 절차적 텍스처 (1회 생성, 모든 핑이 공유) ─────

    // 가장자리가 부드러운 원판 — 소리 도달 범위 표현
    private static Texture2D CreateDiscTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        FillRadial(tex, size, r => Mathf.Clamp01((1f - r) / 0.15f));
        return tex;
    }

    // 도넛 모양 링 — 노출 상태 표시
    private static Texture2D CreateRingTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        FillRadial(tex, size, r => Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.15f));
        return tex;
    }

    // 경계만 살짝 안티앨리어싱된 꽉 찬 원판 — UI 원형 마스크용
    private static Texture2D CreateSolidDiscTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        FillRadial(tex, size, r => Mathf.Clamp01((1f - r) / 0.015f));
        return tex;
    }

    // 얇은 외곽 링 — 미니맵 원형 테두리용
    private static Texture2D CreateThinRingTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        FillRadial(tex, size, r => Mathf.Clamp01(1f - Mathf.Abs(r - 0.98f) / 0.02f));
        return tex;
    }

    private static void FillRadial(Texture2D tex, int size, System.Func<float, float> alphaByRadius)
    {
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alphaByRadius(r)));
            }
        }
        tex.Apply();
    }
}
