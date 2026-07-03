using System;
using UnityEngine;

// 미니맵 사운드 핑 — 소리가 난 위치에 실제 소리 반경 크기의 원을 띄우고 잠깐 페이드아웃 후 풀로 복귀한다.
// CLAUDE.md: "핑 크기 = 소리 거리/크기 비례, 잠깐 떴다 사라지는 방식". Minimap 레이어에서만 렌더된다.
// 생성/파괴를 반복하지 않도록 MinimapController가 풀링한다 (Setup 1회 → Show 반복).
[RequireComponent(typeof(MeshRenderer))]
public class MinimapPing : MonoBehaviour
{
    private Material _material;
    private Color _baseColor;
    private float _lifetime;
    private float _timer;
    private Action<MinimapPing> _onFinished;

    // 풀 생성 시 1회 — 머티리얼/레이어/회전 등 공유 설정.
    // Quad 메시는 로컬 XY 평면이라 (90,0,0) 회전 후 스케일 (2r, 2r, 1)이 월드 XZ 원이 된다.
    // Sprites/Default: 언릿+투명이 기본이라 URP/Unlit 투명 설정(블렌드 키워드)을 코드로 만질 필요가 없다.
    public void Setup(Texture2D circleTexture, int minimapLayer, Shader spriteShader, Action<MinimapPing> onFinished)
    {
        gameObject.layer = minimapLayer;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _onFinished = onFinished;

        _material = new Material(spriteShader) { mainTexture = circleTexture };
        var meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = _material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // radius = 실제 소리 반경(m)
    public void Show(Vector3 position, float radius, Color color, float lifetime)
    {
        transform.position = position;
        transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
        _baseColor = color;
        _material.color = color;
        _lifetime = Mathf.Max(0.05f, lifetime);
        _timer = 0f;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        float t = _timer / _lifetime;
        if (t >= 1f)
        {
            gameObject.SetActive(false);
            _onFinished?.Invoke(this); // 풀 복귀
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
