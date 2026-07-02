using UnityEngine;

// 미니맵 사운드 핑 — 소리가 난 위치에 실제 소리 반경 크기의 원을 띄우고 잠깐 페이드아웃 후 자멸한다.
// CLAUDE.md: "핑 크기 = 소리 거리/크기 비례, 잠깐 떴다 사라지는 방식". Minimap 레이어에서만 렌더된다.
[RequireComponent(typeof(MeshRenderer))]
public class MinimapPing : MonoBehaviour
{
    private Material _material;
    private Color _baseColor;
    private float _lifetime;
    private float _timer;

    // radius = 실제 소리 반경(m). Quad 메시는 로컬 XY 평면이라 (90,0,0) 회전 후 스케일 (2r, 2r, 1)이 월드 XZ 원이 된다.
    public void Init(Vector3 position, float radius, Color color, float lifetime, Texture2D circleTexture, int minimapLayer)
    {
        gameObject.layer = minimapLayer;
        transform.position = position;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

        // Sprites/Default: 언릿+투명이 기본이라 URP/Unlit 투명 설정(블렌드 키워드)을 코드로 만질 필요가 없다
        _material = new Material(Shader.Find("Sprites/Default"))
        {
            mainTexture = circleTexture,
            color = color
        };

        var meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = _material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _baseColor = color;
        _lifetime = Mathf.Max(0.05f, lifetime);
        _timer = 0f;
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        float t = _timer / _lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        Color c = _baseColor;
        c.a = _baseColor.a * (1f - t);
        _material.color = c;
    }

    private void OnDestroy()
    {
        // 머티리얼 인스턴스만 정리 — 원형 텍스처는 MinimapController가 모든 핑에 공유하는 자원
        if (_material != null) Destroy(_material);
    }
}
