using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Phase 1 스크립트(AIController, AssassinationSystem, CombatController, ExposureSystem 등)를
// 씬에 자동으로 배선해서 Play 모드에서 실제로 동작하게 만드는 Editor 전용 도구.
// CLAUDE.md 로드맵 기준 "기본 맵"의 남은 배선 작업(적 배치, NavMesh, 레이어) + CQC/존버 방지 HUD까지 처리한다.
public static class Phase1SceneSetup
{
    private const string PlayerLayerName = "Player";
    private const string EnemyLayerName = "Enemy";
    private const string MinimapLayerName = "Minimap";
    private const int EnemyCount = 2;

    [MenuItem("Tools/AmsalGame/Phase 1 씬 자동 구성")]
    public static void Run()
    {
        if (!EditorUtility.DisplayDialog(
                "Phase 1 씬 자동 구성",
                "다음 작업을 수행합니다:\n" +
                "- Player/Enemy 레이어 추가\n" +
                "- Player에 SoundEmitter, AssassinationSystem, CombatController, PlayerCombatInput, ExposureSystem, RangedWeaponController, TeamMember/ClusterPenaltySystem 부착\n" +
                "- SoundEventSystem 배치\n" +
                "- 테스트용 Enemy 2기 배치 (AIController, CombatController)\n" +
                "- NavMesh 베이크\n" +
                "- A/B 사이트(권총 파밍 지점) 배치\n" +
                "- Player용 시야 SpotLight + 특수부대풍 암전 (태양광/환경광 소등, FOV 차폐)\n" +
                "- GameStateManager(승패 판정) 배치\n" +
                "- HUD Canvas 생성 (암살 인디케이터, 존버 방지 게이지, 승패 배너)\n" +
                "- 좌상단 미니맵 (전용 카메라 + 사운드 핑 + 노출 링)\n\n" +
                "프로젝트 설정(레이어)이 변경됩니다. 계속할까요?",
                "실행", "취소"))
        {
            return;
        }

        var log = new StringBuilder();

        EnsureLayers(log);

        var player = SetupPlayer(log);
        SetupSoundEventSystem(log);
        var enemies = SetupEnemies(log, player);
        SetupFacingMarkers(log, enemies);
        BakeNavMesh(log);
        SnapToNavMesh(log, enemies);
        WireLayerMasks(log, player, enemies);
        SetupSites(log, player);
        SetupVisionLight(log, player);

        if (player != null)
        {
            var canvas = SetupHud(log, player);
            SetupGameState(log, player, canvas);
            SetupMinimap(log, player, canvas);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[Phase1SceneSetup] 완료:\n" + log);
        EditorUtility.DisplayDialog("Phase 1 씬 자동 구성", "완료되었습니다. Console 로그를 확인하세요.\nCtrl+S로 씬을 저장해 주세요.", "확인");
    }

    // ── 레이어 ─────────────────────────────────────

    private static void EnsureLayers(StringBuilder log)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layersProp = tagManager.FindProperty("layers");

        AddLayerIfMissing(layersProp, PlayerLayerName, log);
        AddLayerIfMissing(layersProp, EnemyLayerName, log);
        AddLayerIfMissing(layersProp, MinimapLayerName, log);

        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    private static void AddLayerIfMissing(SerializedProperty layersProp, string layerName, StringBuilder log)
    {
        for (int i = 0; i < layersProp.arraySize; i++)
        {
            if (layersProp.GetArrayElementAtIndex(i).stringValue == layerName)
            {
                log.AppendLine($"- 레이어 '{layerName}' 이미 존재 (스킵)");
                return;
            }
        }

        // 0~7은 Unity 내장 레이어, 사용자 레이어는 8번부터 빈 슬롯에 등록
        for (int i = 8; i < layersProp.arraySize; i++)
        {
            var slot = layersProp.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                log.AppendLine($"- 레이어 '{layerName}' 추가 (slot {i})");
                return;
            }
        }

        log.AppendLine($"- 레이어 '{layerName}' 추가 실패: 빈 슬롯 없음");
    }

    // ── 플레이어 ───────────────────────────────────

    private static PlayerController SetupPlayer(StringBuilder log)
    {
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player == null)
        {
            log.AppendLine("- PlayerController를 찾을 수 없음 (Player 설정 스킵)");
            return null;
        }

        player.tag = "Player";

        int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
        if (playerLayer >= 0) player.gameObject.layer = playerLayer;

        if (player.GetComponent<SoundEmitter>() == null)
        {
            player.gameObject.AddComponent<SoundEmitter>();
            log.AppendLine("- Player에 SoundEmitter 추가");
        }

        if (player.GetComponent<AssassinationSystem>() == null)
        {
            player.gameObject.AddComponent<AssassinationSystem>();
            log.AppendLine("- Player에 AssassinationSystem 추가");
        }

        // CombatController가 Health를 RequireComponent로 자동 부착 (Player는 지금까지 Health가 없었음)
        if (player.GetComponent<CombatController>() == null)
        {
            player.gameObject.AddComponent<CombatController>();
            log.AppendLine("- Player에 CombatController(+Health) 추가");
        }

        if (player.GetComponent<PlayerCombatInput>() == null)
        {
            player.gameObject.AddComponent<PlayerCombatInput>();
            log.AppendLine("- Player에 PlayerCombatInput 추가 (LMB 공격/암살, RMB 막기/패링, F 그랩)");
        }

        if (player.GetComponent<ExposureSystem>() == null)
        {
            player.gameObject.AddComponent<ExposureSystem>();
            log.AppendLine("- Player에 ExposureSystem 추가 (존버 방지)");
        }

        if (player.GetComponent<RangedWeaponController>() == null)
        {
            player.gameObject.AddComponent<RangedWeaponController>();
            log.AppendLine("- Player에 RangedWeaponController 추가 (Q 칼 던지기, R 회수, MMB 권총 발사)");
        }

        if (player.GetComponent<ClusterPenaltySystem>() == null)
        {
            // ClusterPenaltySystem이 TeamMember를 RequireComponent로 자동 부착
            player.gameObject.AddComponent<ClusterPenaltySystem>();
            log.AppendLine("- Player에 TeamMember/ClusterPenaltySystem 추가 (군집 페널티)");
        }

        log.AppendLine($"- Player 태그/레이어 설정 완료 ({player.name})");
        return player;
    }

    // ── 사운드 시스템 ──────────────────────────────

    private static void SetupSoundEventSystem(StringBuilder log)
    {
        if (Object.FindFirstObjectByType<SoundEventSystem>() != null)
        {
            log.AppendLine("- SoundEventSystem 이미 존재 (스킵)");
            return;
        }

        var go = new GameObject("SoundEventSystem");
        go.AddComponent<SoundEventSystem>();
        log.AppendLine("- SoundEventSystem 생성");
    }

    // ── 적 AI ──────────────────────────────────────

    private static AIController[] SetupEnemies(StringBuilder log, PlayerController player)
    {
        var enemies = new AIController[EnemyCount];
        int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
        Vector3 basePos = player != null ? player.transform.position : Vector3.zero;
        Vector3[] offsets = { new Vector3(4f, 0f, 4f), new Vector3(-4f, 0f, 4f), new Vector3(4f, 0f, -4f), new Vector3(-4f, 0f, -4f) };

        for (int i = 0; i < EnemyCount; i++)
        {
            string name = $"Enemy_{i + 1:00}";
            var existing = GameObject.Find(name);
            if (existing != null)
            {
                // RequireComponent는 이미 존재하는 컴포넌트에는 소급 적용되지 않으므로,
                // 이전 버전 스크립트로 생성된 개체엔 CombatController가 없을 수 있어 직접 보강한다.
                if (existing.GetComponent<CombatController>() == null)
                {
                    existing.AddComponent<CombatController>();
                    log.AppendLine($"- {name} 이미 존재, CombatController 보강 추가");
                }
                else
                {
                    log.AppendLine($"- {name} 이미 존재 (스킵)");
                }
                enemies[i] = existing.GetComponent<AIController>();
                continue;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.position = basePos + offsets[i % offsets.Length];
            if (enemyLayer >= 0) go.layer = enemyLayer;

            // RequireComponent 체인으로 NavMeshAgent, Health, CombatController까지 자동 부착
            var ai = go.AddComponent<AIController>();
            enemies[i] = ai;
            log.AppendLine($"- {name} 생성 (AIController + CombatController)");
        }

        return enemies;
    }

    // 적의 앞/뒤를 시각적으로 구분하기 위한 임시 마커. 캡슐의 자식이라 회전에 따라 같이 돈다.
    // 빨강 = 정면(인식당함), 초록 = 후방(암살 가능 120도 구간, AssassinationSystem.backConeAngle과 대략 일치)
    private static void SetupFacingMarkers(StringBuilder log, AIController[] enemies)
    {
        int created = 0;
        foreach (var ai in enemies)
        {
            if (ai == null) continue;
            if (ai.transform.Find("FrontMarker") != null) continue; // 이미 붙어있으면 스킵

            CreateFacingMarker(ai.transform, "FrontMarker", new Vector3(0f, 0.7f, 0.55f), Color.red);
            CreateFacingMarker(ai.transform, "BackMarker", new Vector3(0f, 0.7f, -0.55f), Color.green);
            created++;
        }

        if (created > 0) log.AppendLine($"- 적 {created}기에 앞/뒤 색상 마커 추가 (빨강=정면, 초록=후방)");
        else log.AppendLine("- 앞/뒤 색상 마커 이미 존재 (스킵)");
    }

    private static void CreateFacingMarker(Transform parent, string name, Vector3 localPos, Color color)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;
        Object.DestroyImmediate(marker.GetComponent<Collider>()); // 판정에 영향 없는 순수 시각 표시용
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPos;
        marker.transform.localScale = Vector3.one * 0.3f;
        TintMarker(marker.GetComponent<Renderer>(), color);
    }

    // ── NavMesh ────────────────────────────────────

    private static void BakeNavMesh(StringBuilder log)
    {
        var navGO = GameObject.Find("NavMeshSurface");
        if (navGO == null) navGO = new GameObject("NavMeshSurface");

        var surface = navGO.GetComponent<NavMeshSurface>();
        if (surface == null) surface = navGO.AddComponent<NavMeshSurface>();

        // Default 레이어(맵 블록아웃)만 지오메트리로 수집 → Player/Enemy 캡슐이 구멍을 만들지 않도록 함
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = LayerMask.GetMask("Default");
        surface.BuildNavMesh();

        log.AppendLine("- NavMesh 베이크 완료 (Default 레이어 기준)");
    }

    private static void SnapToNavMesh(StringBuilder log, AIController[] enemies)
    {
        foreach (var ai in enemies)
        {
            if (ai == null) continue;

            if (NavMesh.SamplePosition(ai.transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                ai.transform.position = hit.position;
            }
            else
            {
                log.AppendLine($"- 경고: {ai.name}을 NavMesh 위로 스냅하지 못함 (수동으로 위치 조정 필요)");
            }
        }
    }

    // ── 레이어마스크 배선 ──────────────────────────

    private static void WireLayerMasks(StringBuilder log, PlayerController player, AIController[] enemies)
    {
        int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
        int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
        int defaultLayer = LayerMask.NameToLayer("Default");

        if (player != null)
        {
            var assassination = player.GetComponent<AssassinationSystem>();
            if (assassination != null && enemyLayer >= 0)
            {
                var so = new SerializedObject(assassination);
                so.FindProperty("enemyLayer").intValue = 1 << enemyLayer;
                so.ApplyModifiedProperties();
            }

            var playerCombat = player.GetComponent<CombatController>();
            if (playerCombat != null && enemyLayer >= 0)
            {
                var so = new SerializedObject(playerCombat);
                so.FindProperty("opponentLayer").intValue = 1 << enemyLayer;
                so.ApplyModifiedProperties();
            }

            var ranged = player.GetComponent<RangedWeaponController>();
            if (ranged != null && enemyLayer >= 0 && defaultLayer >= 0)
            {
                var so = new SerializedObject(ranged);
                so.FindProperty("enemyLayer").intValue = 1 << enemyLayer;
                so.FindProperty("pistolHitMask").intValue = (1 << enemyLayer) | (1 << defaultLayer);
                so.ApplyModifiedProperties();
            }
            log.AppendLine("- AssassinationSystem/CombatController/RangedWeaponController 레이어마스크 → Enemy");
        }

        foreach (var ai in enemies)
        {
            if (ai == null) continue;

            var so = new SerializedObject(ai);
            so.FindProperty("obstacleLayer").intValue = 1 << defaultLayer;
            so.ApplyModifiedProperties();

            var aiCombat = ai.GetComponent<CombatController>();
            if (aiCombat != null && playerLayer >= 0)
            {
                var combatSo = new SerializedObject(aiCombat);
                combatSo.FindProperty("opponentLayer").intValue = 1 << playerLayer;
                combatSo.ApplyModifiedProperties();
            }
        }
        log.AppendLine("- AIController.obstacleLayer → Default, Enemy CombatController.opponentLayer → Player (전체 적용)");
    }

    // ── A/B 사이트 (권총 파밍) ──────────────────────

    private static void SetupSites(StringBuilder log, PlayerController player)
    {
        if (player == null) return;

        Vector3 basePos = player.transform.position;
        CreateSite(log, "SiteA", basePos + new Vector3(6f, 0f, -6f), new Color(0.2f, 0.5f, 1f));
        CreateSite(log, "SiteB", basePos + new Vector3(-6f, 0f, -6f), new Color(1f, 0.6f, 0.1f));
    }

    private static void CreateSite(StringBuilder log, string siteName, Vector3 desiredPos, Color markerColor)
    {
        var existingSite = GameObject.Find(siteName);
        if (existingSite != null)
        {
            // 이전 버전 스크립트가 renderer.material로 마커 색을 칠해 에디터 모드 머티리얼이 새어나갔을 수 있어 보정
            var existingMarker = existingSite.transform.Find("Marker");
            if (existingMarker != null) TintMarker(existingMarker.GetComponent<Renderer>(), markerColor);
            log.AppendLine($"- {siteName} 이미 존재 (스킵, 마커 머티리얼 보정)");
            return;
        }

        Vector3 pos = NavMesh.SamplePosition(desiredPos, out NavMeshHit hit, 15f, NavMesh.AllAreas) ? hit.position : desiredPos;

        var siteGO = new GameObject(siteName);
        siteGO.transform.position = pos;

        // 바닥 마커 (시각 식별용, 충돌 없음)
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Marker";
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(siteGO.transform, false);
        marker.transform.localScale = new Vector3(2.5f, 0.02f, 2.5f);
        marker.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        TintMarker(marker.GetComponent<Renderer>(), markerColor);

        // 권총 파밍 포인트
        var pickupGO = new GameObject("PistolSpawn", typeof(SphereCollider), typeof(Rigidbody), typeof(WeaponPickup));
        pickupGO.transform.SetParent(siteGO.transform, false);
        pickupGO.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        pickupGO.GetComponent<SphereCollider>().radius = 1f;

        var pistolVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pistolVisual.name = "PistolVisual";
        Object.DestroyImmediate(pistolVisual.GetComponent<Collider>());
        pistolVisual.transform.SetParent(pickupGO.transform, false);
        pistolVisual.transform.localScale = new Vector3(0.15f, 0.15f, 0.4f);

        log.AppendLine($"- {siteName} 생성 (권총 파밍 지점 포함, {pos})");
    }

    // 새 Material을 직접 만들어 sharedMaterial에 할당 — Enemy 등 다른 CreatePrimitive 오브젝트와 색이 얽히지 않게 하면서도,
    // 에디터 모드에서 renderer.material을 호출해 인스턴스가 새어나가는(leak) 경고를 피한다
    private static void TintMarker(Renderer renderer, Color color)
    {
        var mat = new Material(renderer.sharedMaterial) { color = color };
        renderer.sharedMaterial = mat;
    }

    // ── FOV (시야 SpotLight + 특수부대풍 암전) ────────────────────────

    private static void SetupVisionLight(StringBuilder log, PlayerController player)
    {
        if (player == null) return;
        ApplyTacticalLighting(log, player);
    }

    // 단독 메뉴 — 이미 배선된 씬에도 재실행으로 조명 수치를 덮어쓸 수 있는 튜닝 노브.
    // 컨셉: 맵 전체가 거의 완전한 어둠(밀실) + 플레이어 손전등(좁은 원뿔)만이 유일한 시야.
    [MenuItem("Tools/AmsalGame/특수부대 조명 적용 (제한 시야 강화)")]
    public static void RunTacticalLighting()
    {
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("특수부대 조명", "PlayerController를 찾을 수 없습니다. Phase 1 씬 자동 구성을 먼저 실행하세요.", "확인");
            return;
        }

        var log = new StringBuilder();
        ApplyTacticalLighting(log, player);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[Phase1SceneSetup] 특수부대 조명 적용:\n" + log);
        EditorUtility.DisplayDialog("특수부대 조명", "적용되었습니다. Console 로그를 확인하고 Ctrl+S로 씬을 저장해 주세요.", "확인");
    }

    // 항상 값을 덮어쓴다 (skip-if-exists 아님) — 수치 바꾸고 메뉴 재실행하면 그대로 반영되는 idempotent 튜닝 방식
    private static void ApplyTacticalLighting(StringBuilder log, PlayerController player)
    {
        // 1) 태양광: 사실상 소등 — 손전등 밖은 실루엣도 간신히 보이는 수준
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.intensity = 0.04f;
            l.color = new Color(0.75f, 0.8f, 1f); // 희미한 냉색 잔광 (달빛/비상등 느낌)
            log.AppendLine("- Directional Light 0.04 (사실상 암전)");
            break;
        }

        // 2) 환경광: 스카이박스 기여 차단 — Flat 모드 + 은은한 저조도.
        //    시야 밖은 마스크가 완전 검정으로 덮고 차폐도 레이캐스트 기반이라, 조명은 이제 순수 연출 —
        //    환경광은 시야 안(특히 손전등이 안 닿는 원형 영역)의 기본 가독성 담당.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.11f, 0.12f, 0.14f);
        log.AppendLine("- Ambient Flat (0.11, 0.12, 0.14) — 시야 안 기본 가독성용 저조도");

        // 3) 메인 카메라 배경: 스카이박스 대신 검정 — 맵 바깥이 밝게 뚫려 보이는 것 방지
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.008f, 0.01f, 0.016f);
            log.AppendLine("- 메인 카메라 배경 SolidColor(거의 검정)");
        }

        // 4) 시야 SpotLight (손전등): 기존 100°/14m에서 대폭 축소 — 어두워진 만큼 밝기는 올림
        var visionT = player.transform.Find("PlayerVisionLight");
        if (visionT == null)
        {
            var visionGO = new GameObject("PlayerVisionLight", typeof(Light));
            visionGO.transform.SetParent(player.transform, false);
            visionT = visionGO.transform;
            log.AppendLine("- PlayerVisionLight(SpotLight) 생성");
        }
        visionT.localPosition = new Vector3(0f, 1.2f, 0f);
        visionT.localRotation = Quaternion.Euler(25f, 0f, 0f);

        var vision = visionT.GetComponent<Light>();
        vision.type = LightType.Spot;
        vision.spotAngle = 70f;
        vision.innerSpotAngle = 38f;
        // 차폐가 레이캐스트 기반이 되면서 손전등은 순수 연출 — 부채꼴 시야(13.75m) 안이
        // 그럴듯하게 밝기만 하면 된다 (마스크 판정과 무관)
        vision.range = 20f;
        vision.intensity = 12f;
        vision.color = new Color(0.88f, 0.92f, 1f); // 냉백색 — 전술 라이트 톤
        vision.shadows = LightShadows.Soft;
        log.AppendLine("- PlayerVisionLight 70°/20m/강도12 — 부채꼴 시야 연출용 (시야 판정과는 무관)");

        // 5) 근접 PointLight: 손전등 원뿔 밖이라도 발밑 반경 ~3.5m는 희미하게 —
        //    본인 캡슐/바로 옆 벽조차 안 보이면 조작 자체가 불가능해지는 것 방지 (시야 정보는 거의 없음)
        var proxT = player.transform.Find("PlayerProximityLight");
        if (proxT == null)
        {
            var proxGO = new GameObject("PlayerProximityLight", typeof(Light));
            proxGO.transform.SetParent(player.transform, false);
            proxT = proxGO.transform;
            log.AppendLine("- PlayerProximityLight(PointLight) 생성");
        }
        proxT.localPosition = new Vector3(0f, 1.5f, 0f);

        var prox = proxT.GetComponent<Light>();
        prox.type = LightType.Point;
        prox.range = 6f;
        prox.intensity = 2f;
        prox.color = new Color(0.7f, 0.75f, 0.85f);
        prox.shadows = LightShadows.None;
        log.AppendLine("- PlayerProximityLight 반경6m/강도2 — 원형 시야(마스크 _ClearRadius 5.5m) 안을 밝히는 조명");

        // 6) 시야 마스크: 손전등 밖(=어두운 픽셀)을 완전 암전 처리하는 화면 후처리 + 플레이어 주변 원형 시야
        SetupVisionMask(log, player);
    }

    // 시야 마스크 배선 — VisionMask.mat을 만들어(없으면) 메인 카메라의 VisionMaskOverlay에 주입.
    // 다른 조명 수치처럼 머티리얼 값도 항상 덮어쓴다 (메뉴 재실행 = 튜닝 반영).
    private const string VisionMaskMaterialPath = "Assets/Materials/VisionMask.mat";

    private static void SetupVisionMask(StringBuilder log, PlayerController player)
    {
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            log.AppendLine("- 시야 마스크 실패: 메인 카메라 없음");
            return;
        }

        var shader = Shader.Find("AmsalGame/VisionMask");
        if (shader == null)
        {
            log.AppendLine("- 시야 마스크 실패: AmsalGame/VisionMask 셰이더를 찾을 수 없음 (Console에서 셰이더 컴파일 에러 확인)");
            return;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(VisionMaskMaterialPath);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, VisionMaskMaterialPath);
            log.AppendLine($"- {VisionMaskMaterialPath} 생성");
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }

        // 머티리얼 수치 덮어쓰기 — 부채꼴 범위/각도는 VisionMaskOverlay의 레이캐스트도 같은 값을 읽는 단일 출처
        mat.SetFloat("_ClearRadius", 5.5f);           // 플레이어 주변 원형 정상 시야 반경(m) — 고정, 장애물 무시
        mat.SetFloat("_ClearFeather", 1.5f);
        mat.SetFloat("_SectorRange", 13.75f);         // 전방 부채꼴 길이 = 원형 반경의 2.5배 (유저 지정 비율 — 반경 바꾸면 같이 조정)
        mat.SetFloat("_SectorRangeFeather", 2f);
        mat.SetFloat("_SectorHalfAngleDeg", 33f);     // 손전등 반각 35°보다 약간 안쪽 — 빛이 부채꼴을 다 덮게
        mat.SetFloat("_SectorAngleFeatherDeg", 6f);
        mat.SetFloat("_OcclusionFeather", 0.35f);     // 장애물 차폐 경계 부드러움(m)
        mat.SetFloat("_HiddenBrightness", 0f);        // 완전 암전 — 실루엣도 안 보임
        EditorUtility.SetDirty(mat);

        var overlay = mainCam.GetComponent<VisionMaskOverlay>();
        if (overlay == null)
        {
            overlay = mainCam.gameObject.AddComponent<VisionMaskOverlay>();
            log.AppendLine("- 메인 카메라에 VisionMaskOverlay 부착");
        }
        overlay.Bind(mat, player.transform);
        EditorUtility.SetDirty(overlay);

        // 오파크/깊이 텍스처 보장 — PC_RPAsset은 둘 다 이미 켜져 있지만 RP 에셋 교체에 대비해 안전장치
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset rpAsset)
        {
            if (!rpAsset.supportsCameraOpaqueTexture)
            {
                rpAsset.supportsCameraOpaqueTexture = true;
                EditorUtility.SetDirty(rpAsset);
                log.AppendLine("- URP Opaque Texture 활성화 (시야 마스크 필수 조건)");
            }
            if (!rpAsset.supportsCameraDepthTexture)
            {
                rpAsset.supportsCameraDepthTexture = true;
                EditorUtility.SetDirty(rpAsset);
                log.AppendLine("- URP Depth Texture 활성화 (원형 시야 월드 좌표 복원에 필수)");
            }
        }

        log.AppendLine("- 시야 마스크 적용: 주변 원형 5.5m(고정) + 전방 부채꼴 13.75m/66°(레이캐스트 차폐)만 보이고 나머지 완전 암전");
    }

    // ── 승리/패배 ────────────────────────────────────

    private static void SetupGameState(StringBuilder log, PlayerController player, GameObject canvasGO)
    {
        if (player == null) return;

        var existing = Object.FindFirstObjectByType<GameStateManager>();
        if (existing == null)
        {
            var go = new GameObject("GameStateManager");
            existing = go.AddComponent<GameStateManager>();
            existing.Bind(player.GetComponent<Health>());
            log.AppendLine("- GameStateManager 생성 (모든 Enemy 처치=승리, Player 사망=패배)");
        }
        else
        {
            log.AppendLine("- GameStateManager 이미 존재 (스킵)");
        }

        if (canvasGO == null || canvasGO.GetComponentInChildren<GameResultUI>() != null) return;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var textGO = new GameObject("ResultText", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(canvasGO.transform, false);
        var rect = (RectTransform)textGO.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = textGO.GetComponent<Text>();
        text.font = font;
        text.fontSize = 72;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = "";
        text.raycastTarget = false;
        textGO.SetActive(false);

        canvasGO.AddComponent<GameResultUI>().Bind(existing, text);
        log.AppendLine("- 승패 결과 배너 UI 생성");
    }

    // ── HUD ────────────────────────────────────────

    private static GameObject SetupHud(StringBuilder log, PlayerController player)
    {
        var canvasGO = GameObject.Find("HUDCanvas");
        if (canvasGO == null)
        {
            canvasGO = new GameObject("HUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            log.AppendLine("- HUDCanvas 생성");
        }
        else
        {
            log.AppendLine("- HUDCanvas 이미 존재 (존버 게이지만 재구성)");
        }
        var canvasRect = (RectTransform)canvasGO.transform;

        var assassination = player.GetComponent<AssassinationSystem>();
        var exposure = player.GetComponent<ExposureSystem>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 암살 인디케이터 (적 후방 표시) — 이미 있으면 유지
        if (canvasGO.transform.Find("AssassinationIndicator") == null)
        {
            var iconGO = new GameObject("AssassinationIndicator", typeof(RectTransform), typeof(Image), typeof(AssassinationIndicatorUI));
            iconGO.transform.SetParent(canvasGO.transform, false);
            var iconRect = (RectTransform)iconGO.transform;
            iconRect.sizeDelta = new Vector2(28f, 28f);
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.color = Color.red;
            iconImage.raycastTarget = false;
            iconImage.enabled = false;
            iconGO.GetComponent<AssassinationIndicatorUI>().Bind(assassination, Camera.main, canvasRect);
            log.AppendLine("- 암살 인디케이터 생성");
        }

        // 존버 게이지 클러스터 (우상단, R6풍) — 스타일 갱신이 가능하도록 delete-and-rebuild
        foreach (var oldName in new[] { "ExposureGaugeBackground", "ExposureGauge", "ExposureCountdownText", "WarningOverlay" })
        {
            var old = canvasGO.transform.Find(oldName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // R6풍 어두운 슬롯 배경 — 게이지보다 사방 4px 크게, 경고 중에만 게이지와 함께 표시 (ExposureGaugeUI가 토글)
        var gaugeBgGO = new GameObject("ExposureGaugeBackground", typeof(RectTransform), typeof(Image));
        gaugeBgGO.transform.SetParent(canvasGO.transform, false);
        var gaugeBgRect = (RectTransform)gaugeBgGO.transform;
        gaugeBgRect.anchorMin = gaugeBgRect.anchorMax = new Vector2(1f, 1f);
        gaugeBgRect.pivot = new Vector2(1f, 1f);
        gaugeBgRect.anchoredPosition = new Vector2(-26f, -26f);
        gaugeBgRect.sizeDelta = new Vector2(168f, 28f);
        var gaugeBgImage = gaugeBgGO.GetComponent<Image>();
        gaugeBgImage.color = new Color(0.08f, 0.09f, 0.10f, 0.85f);
        gaugeBgImage.raycastTarget = false;
        gaugeBgGO.SetActive(false);

        // 존버 방지 게이지 (R6풍 옐로 필)
        var gaugeGO = new GameObject("ExposureGauge", typeof(RectTransform), typeof(Image));
        gaugeGO.transform.SetParent(canvasGO.transform, false);
        var gaugeRect = (RectTransform)gaugeGO.transform;
        gaugeRect.anchorMin = gaugeRect.anchorMax = new Vector2(1f, 1f);
        gaugeRect.pivot = new Vector2(1f, 1f);
        gaugeRect.anchoredPosition = new Vector2(-30f, -30f);
        gaugeRect.sizeDelta = new Vector2(160f, 20f);
        var gaugeImage = gaugeGO.GetComponent<Image>();
        gaugeImage.color = new Color(1f, 0.76f, 0.03f);
        gaugeImage.type = Image.Type.Filled;
        gaugeImage.fillMethod = Image.FillMethod.Horizontal;
        gaugeImage.raycastTarget = false;
        gaugeGO.SetActive(false);

        // 노출 카운트다운 텍스트
        var textGO = new GameObject("ExposureCountdownText", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(canvasGO.transform, false);
        var textRect = (RectTransform)textGO.transform;
        textRect.anchorMin = textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(1f, 1f);
        textRect.anchoredPosition = new Vector2(-30f, -58f);
        textRect.sizeDelta = new Vector2(160f, 24f);
        var countdownText = textGO.GetComponent<Text>();
        countdownText.font = font;
        countdownText.fontSize = 16;
        countdownText.alignment = TextAnchor.MiddleRight;
        countdownText.color = new Color(1f, 0.76f, 0.03f);
        countdownText.text = "노출 경고!";
        countdownText.raycastTarget = false;
        textGO.SetActive(false);

        // 화면 전체 경고 오버레이 (노출 상태일 때만 활성화)
        var overlayGO = new GameObject("WarningOverlay", typeof(RectTransform), typeof(Image));
        overlayGO.transform.SetParent(canvasGO.transform, false);
        var overlayRect = (RectTransform)overlayGO.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImage = overlayGO.GetComponent<Image>();
        overlayImage.color = new Color(1f, 0f, 0f, 0.25f);
        overlayImage.raycastTarget = false;
        overlayImage.enabled = false;

        var gaugeUI = canvasGO.GetComponent<ExposureGaugeUI>();
        if (gaugeUI == null) gaugeUI = canvasGO.AddComponent<ExposureGaugeUI>();
        gaugeUI.Bind(exposure, gaugeImage, countdownText, overlayImage, gaugeBgImage);

        log.AppendLine("- 존버 게이지 재구성 (R6풍 슬롯 배경 + 옐로 필 + 카운트다운 + 경고 오버레이)");
        return canvasGO;
    }

    // ── 미니맵 ──────────────────────────────────────
    // 씬 조명이 의도적으로 어두워서(태양광 감광) 실제 지오메트리를 그대로 찍으면 안 보인다.
    // 전용 카메라가 Minimap 레이어의 언릿 클론만 렌더 → 조명과 무관한 고대비 평면도.

    private static void SetupMinimap(StringBuilder log, PlayerController player, GameObject canvasGO)
    {
        int minimapLayer = LayerMask.NameToLayer(MinimapLayerName);
        if (minimapLayer < 0)
        {
            log.AppendLine("- 미니맵 실패: Minimap 레이어 없음");
            return;
        }

        // 메인 카메라에서 Minimap 레이어 제외 (클론/마커가 게임 화면에 보이지 않게)
        var mainCam = Camera.main;
        if (mainCam != null && (mainCam.cullingMask & (1 << minimapLayer)) != 0)
        {
            mainCam.cullingMask &= ~(1 << minimapLayer);
            log.AppendLine("- 메인 카메라 cullingMask에서 Minimap 레이어 제외");
        }

        // 미니맵 전용 카메라 (MainCamera 태그 금지 — AssassinationIndicatorUI가 Camera.main에 바인딩됨)
        var camGO = GameObject.Find("MinimapCamera");
        if (camGO == null)
        {
            camGO = new GameObject("MinimapCamera", typeof(Camera));
            log.AppendLine("- MinimapCamera 생성");
        }
        var cam = camGO.GetComponent<Camera>();
        cam.orthographic = true;
        cam.transform.SetPositionAndRotation(
            player.transform.position + Vector3.up * 30f,
            Quaternion.Euler(90f, 0f, 0f));
        cam.cullingMask = 1 << minimapLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 50f;

        // URP: RP 에셋이 Depth/Opaque Texture를 전역 활성화하고 있어 오버라이드하지 않으면
        // 미니맵 카메라가 매 프레임 불필요한 depth prepass + opaque copy를 수행한다
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderType = CameraRenderType.Base;
        camData.renderPostProcessing = false;
        camData.renderShadows = false;
        camData.volumeLayerMask = 0;
        camData.requiresDepthOption = CameraOverrideOption.Off;
        camData.requiresColorOption = CameraOverrideOption.Off;

        // 좌상단 원형 미니맵 UI (R6풍) — 이전(사각) 구조가 있으면 지우고 재구성 (delete-and-rebuild)
        // 원형 마스크/외곽 링 스프라이트는 MinimapController가 런타임에 절차 생성해 주입한다
        RawImage rawImage = null;
        Image maskImage = null;
        Image frameRingImage = null;
        if (canvasGO != null)
        {
            foreach (var oldName in new[] { "MinimapBorder", "MinimapImage", "MinimapRoot" })
            {
                var old = canvasGO.transform.Find(oldName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            var rootGO = new GameObject("MinimapRoot", typeof(RectTransform));
            rootGO.transform.SetParent(canvasGO.transform, false);
            var rootRect = (RectTransform)rootGO.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(18f, -18f);
            rootRect.sizeDelta = new Vector2(270f, 270f);

            var maskGO = new GameObject("MinimapMask", typeof(RectTransform), typeof(Image), typeof(Mask));
            maskGO.transform.SetParent(rootGO.transform, false);
            StretchToParent((RectTransform)maskGO.transform);
            maskImage = maskGO.GetComponent<Image>();
            maskImage.raycastTarget = false;
            maskGO.GetComponent<Mask>().showMaskGraphic = false; // 스텐실만 사용

            var imageGO = new GameObject("MinimapImage", typeof(RectTransform), typeof(RawImage));
            imageGO.transform.SetParent(maskGO.transform, false);
            StretchToParent((RectTransform)imageGO.transform);
            rawImage = imageGO.GetComponent<RawImage>();
            rawImage.raycastTarget = false;

            var frameGO = new GameObject("MinimapRing", typeof(RectTransform), typeof(Image));
            frameGO.transform.SetParent(rootGO.transform, false);
            StretchToParent((RectTransform)frameGO.transform);
            frameRingImage = frameGO.GetComponent<Image>();
            frameRingImage.raycastTarget = false;

            log.AppendLine("- 원형 미니맵 UI 생성 (마스크 + 외곽 링, 좌상단)");
        }

        // 플레이어 마커 — Player 자식이라 요(yaw) 회전 = 바라보는 방향
        var markerRoot = player.transform.Find("MinimapPlayerMarker");
        if (markerRoot == null)
        {
            var markerGO = new GameObject("MinimapPlayerMarker");
            markerGO.transform.SetParent(player.transform, false);
            markerGO.transform.localPosition = new Vector3(0f, 12f, 0f);
            markerGO.layer = minimapLayer;

            var markerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                color = new Color(0.3f, 1f, 0.9f)
            };
            CreateMinimapMarkerPart(markerGO.transform, "Body", Vector3.zero, new Vector3(1.3f, 0.05f, 1.3f), markerMat, minimapLayer);
            CreateMinimapMarkerPart(markerGO.transform, "Nose", new Vector3(0f, 0f, 0.95f), new Vector3(0.45f, 0.05f, 0.9f), markerMat, minimapLayer);
            log.AppendLine("- 미니맵 플레이어 마커 생성");
        }

        // 존버 노출 링 — 비활성 시작, MinimapController가 노출 이벤트로 토글 + 펄스
        var ringTr = player.transform.Find("MinimapExposureRing");
        GameObject ringGO;
        if (ringTr == null)
        {
            ringGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGO.name = "MinimapExposureRing";
            Object.DestroyImmediate(ringGO.GetComponent<Collider>());
            ringGO.transform.SetParent(player.transform, false);
            ringGO.transform.localPosition = new Vector3(0f, 11f, 0f);
            ringGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringGO.transform.localScale = new Vector3(5f, 5f, 1f);
            ringGO.layer = minimapLayer;
            ringGO.SetActive(false);
            log.AppendLine("- 미니맵 노출 링 생성 (비활성)");
        }
        else
        {
            ringGO = ringTr.gameObject;
        }

        RebuildMinimapGeometry(log);

        // 컨트롤러 부착 + 배선
        var controller = camGO.GetComponent<MinimapController>();
        if (controller == null) controller = camGO.AddComponent<MinimapController>();
        controller.Bind(
            cam,
            rawImage,
            maskImage,
            frameRingImage,
            player.transform,
            player.GetComponent<ExposureSystem>(),
            Object.FindFirstObjectByType<GameStateManager>(),
            ringGO);
        log.AppendLine("- MinimapController 배선 완료");
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void CreateMinimapMarkerPart(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat, int layer)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        part.layer = layer;
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 맵 지오메트리(Default 레이어 + MeshRenderer + Collider)의 언릿 클론을 Minimap 레이어에 생성.
    // skip-if-exists가 아니라 delete-and-rebuild — 맵 확장/미로화 후 구버전 미니맵이 남지 않도록.
    private static void RebuildMinimapGeometry(StringBuilder log)
    {
        int minimapLayer = LayerMask.NameToLayer(MinimapLayerName);
        if (minimapLayer < 0) return;

        var existingRoot = GameObject.Find("MinimapGeometry");
        if (existingRoot != null) Object.DestroyImmediate(existingRoot);

        var root = new GameObject("MinimapGeometry");
        int defaultLayer = LayerMask.NameToLayer("Default");

        var wallMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.72f, 0.72f, 0.78f) };
        var floorMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.15f, 0.15f, 0.19f) };

        int count = 0;
        foreach (var src in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            var go = src.gameObject;
            if (go.layer != defaultLayer) continue;
            if (go.GetComponent<Collider>() == null) continue;             // 콜라이더 없는 장식(마커 구슬 등) 제외
            if (go.GetComponentInParent<WeaponPickup>() != null) continue; // 파밍 오브젝트 제외

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            // Instantiate 금지 — ProBuilderMesh/콜라이더/자식이 딸려오므로 렌더링에 필요한 것만 새로 구성.
            // 콜라이더도 복사하지 않는다 (원본과 겹친 콜라이더는 CharacterController 접촉 중복 유발).
            var clone = new GameObject("MinimapClone_" + go.name, typeof(MeshFilter), typeof(MeshRenderer));
            clone.layer = minimapLayer;
            clone.transform.SetParent(root.transform, true);
            clone.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
            clone.transform.localScale = go.transform.lossyScale;
            clone.GetComponent<MeshFilter>().sharedMesh = mf.sharedMesh;

            var cloneRenderer = clone.GetComponent<MeshRenderer>();
            // 바닥/벽 구분은 이름 기반 — ProBuilder 메시의 bounds는 신뢰 불가 (CLAUDE.md 기록 참고)
            cloneRenderer.sharedMaterial = go.name.Contains("바닥") ? floorMat : wallMat;
            cloneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            count++;
        }

        log.AppendLine($"- MinimapGeometry 재생성 ({count}개 클론)");
    }

    // 맵 확장/미로화 후 호출 — 미니맵이 설정된 씬에서만 클론을 갱신한다
    private static void RefreshMinimapGeometryIfPresent(StringBuilder log)
    {
        if (GameObject.Find("MinimapCamera") == null) return;
        RebuildMinimapGeometry(log);
    }

    // ── 맵 확장 (여러 방 + 복도) ─────────────────────
    // 기존 "바닥"/"외벽*" 오브젝트의 실제 월드 좌표를 런타임에 읽어서 그 기준으로 새 방을 붙인다.
    // 기존 벽 2개(서로 다른 방향)를 제거하고 문이 있는 벽으로 교체한 뒤, 복도 + 새 방을 생성한다.

    private const float DoorWidth = 3f;
    private const float WallThickness = 0.4f;
    private const float CorridorLength = 5f;
    private const float NewRoomSize = 7f;
    private static readonly string[] OriginalWallNames = { "외벽", "외벽2", "외벽3", "외벽4" };

    [MenuItem("Tools/AmsalGame/맵 확장 (방 2개 + 복도 추가)")]
    public static void ExpandMap()
    {
        if (!EditorUtility.DisplayDialog(
                "맵 확장",
                "기존 벽 2개(서로 다른 방향)를 제거하고 문이 있는 벽으로 교체한 뒤,\n" +
                "복도로 연결된 새 방 2개(Room B, Room C)를 추가하고 NavMesh를 다시 베이크합니다.\n" +
                "방 크기는 Player 위치에서 사방으로 Raycast를 쏴서 실측합니다.\n\n" +
                "기존 벽 오브젝트가 삭제됩니다 (원본은 git 커밋 7e92426에 남아있어 복구 가능).\n" +
                "이미 생성된 MapExpansion이 있으면 먼저 삭제하고 다시 만듭니다(재시도 가능).\n계속할까요?",
                "실행", "취소"))
        {
            return;
        }

        var log = new StringBuilder();
        SetupMapExpansion(log);
        BakeNavMesh(log);
        RefreshMinimapGeometryIfPresent(log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Phase1SceneSetup] 맵 확장 완료:\n" + log);
        EditorUtility.DisplayDialog("맵 확장", "완료되었습니다. Console 로그를 확인하세요.\nCtrl+S로 씬을 저장해 주세요.", "확인");
    }

    private static void SetupMapExpansion(StringBuilder log)
    {
        var existingRoot = GameObject.Find("MapExpansion");
        if (existingRoot != null)
        {
            Object.DestroyImmediate(existingRoot);
            log.AppendLine("- 기존 MapExpansion 삭제 (재생성)");
        }
        Physics.SyncTransforms(); // DestroyImmediate 직후 물리 씬이 갱신되도록 강제 (같은 호출 내 Raycast 오탐 방지)

        var player = Object.FindFirstObjectByType<PlayerController>();
        var floorGO = GameObject.Find("바닥");
        var floorRenderer = floorGO != null ? floorGO.GetComponent<Renderer>() : null;
        if (floorRenderer == null || player == null)
        {
            log.AppendLine("- 맵 확장 실패: '바닥' 또는 Player를 찾을 수 없음");
            return;
        }

        // bounds.size는 메시에 남아있는 이상 지오메트리 때문에 부풀려질 수 있어 신뢰하지 않는다.
        // bounds.max.y(표면 높이)는 비교적 안전. 실제 방 크기는 Raycast로 콜라이더까지 거리를 재서 구한다.
        float floorY = floorRenderer.bounds.max.y;
        Vector3 anchor = player.transform.position;
        Vector3 rayOrigin = new Vector3(anchor.x, floorY + 1f, anchor.z);

        int defaultLayer = LayerMask.NameToLayer("Default");
        int mask = defaultLayer >= 0 ? 1 << defaultLayer : ~0;
        const float fallbackHalfExtent = 8f;

        float halfX = MeasureHalfExtent(rayOrigin, Vector3.right, mask, fallbackHalfExtent, log, "X");
        float halfZ = MeasureHalfExtent(rayOrigin, Vector3.forward, mask, fallbackHalfExtent, log, "Z");

        Vector3 roomCenter = new Vector3(anchor.x, floorY, anchor.z);
        float roomWidth = halfX * 2f;
        float roomDepth = halfZ * 2f;
        log.AppendLine($"- 방 크기 실측: {roomWidth:F1} x {roomDepth:F1} (중심 {roomCenter})");

        float wallHeight = 3f;
        foreach (var n in OriginalWallNames)
        {
            var w = GameObject.Find(n);
            var r = w != null ? w.GetComponent<Renderer>() : null;
            // 높이는 폭/깊이보다 왜곡될 여지가 적지만, 방어적으로 상식적인 범위만 신뢰
            if (r != null && r.bounds.size.y > 0.5f && r.bounds.size.y < 10f) { wallHeight = r.bounds.size.y; break; }
        }

        var root = new GameObject("MapExpansion");

        var wallPosX = FindWallFacing(roomCenter, Vector3.right);
        var wallPosZ = FindWallFacing(roomCenter, Vector3.forward);

        BuildExpansion(root.transform, "B", wallPosX, roomCenter, Vector3.right, roomWidth, roomDepth, floorY, wallHeight, log);
        BuildExpansion(root.transform, "C", wallPosZ, roomCenter, Vector3.forward, roomWidth, roomDepth, floorY, wallHeight, log);
    }

    // Player 위치에서 direction/역방향으로 Raycast해서 방의 절반 크기를 구한다.
    // 양쪽 다 유효하면 더 짧은 쪽(안전한 쪽) 채택, 한쪽만 유효하면(반대쪽 벽이 이미 삭제된 경우 등) 그 값을 대칭으로 사용, 둘 다 실패하면 기본값.
    private static float MeasureHalfExtent(Vector3 origin, Vector3 direction, int mask, float fallback, StringBuilder log, string axisLabel)
    {
        float pos = RaycastDistance(origin, direction, mask, out string posHit);
        float neg = RaycastDistance(origin, -direction, mask, out string negHit);
        log.AppendLine($"- {axisLabel}축 진단: +방향 {(pos > 0 ? $"{pos:F1}m ({posHit})" : "미스")}, -방향 {(neg > 0 ? $"{neg:F1}m ({negHit})" : "미스")}");

        bool posOk = pos > 1.5f && pos < 40f;
        bool negOk = neg > 1.5f && neg < 40f;

        if (posOk && negOk) return Mathf.Min(pos, neg);
        if (negOk)
        {
            log.AppendLine($"- {axisLabel}축 +방향 Raycast 실패(벽이 이미 없을 수 있음) → -방향 실측값({neg:F1}m)을 대칭으로 사용");
            return neg;
        }
        if (posOk)
        {
            log.AppendLine($"- {axisLabel}축 -방향 Raycast 실패 → +방향 실측값({pos:F1}m)을 대칭으로 사용");
            return pos;
        }

        log.AppendLine($"- {axisLabel}축 양쪽 Raycast 실패 → 기본값({fallback}m) 사용, 위치가 부정확할 수 있으니 확인 필요");
        return fallback;
    }

    private static float RaycastDistance(Vector3 origin, Vector3 dir, int mask, out string hitName)
    {
        if (Physics.Raycast(origin, dir, out RaycastHit hit, 60f, mask))
        {
            hitName = hit.collider.name;
            return hit.distance;
        }
        hitName = null;
        return -1f;
    }

    private static GameObject FindWallFacing(Vector3 roomCenter, Vector3 direction)
    {
        GameObject best = null;
        float bestDot = 0.5f; // 방향과 충분히 일치할 때만 채택 (60도 이내)

        foreach (var n in OriginalWallNames)
        {
            var go = GameObject.Find(n);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) continue;

            Vector3 offset = r.bounds.center - roomCenter;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.01f) continue;

            float dot = Vector3.Dot(offset.normalized, direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = go;
            }
        }
        return best;
    }

    private static void BuildExpansion(Transform parent, string roomId, GameObject oldWall, Vector3 roomCenter,
        Vector3 direction, float roomWidth, float roomDepth, float floorY, float wallHeight, StringBuilder log)
    {
        Vector3 perpendicular = direction == Vector3.right ? Vector3.forward : Vector3.right;
        float roomExtentAlongDir = (direction == Vector3.right ? roomWidth : roomDepth) * 0.5f;
        float wallSpan = direction == Vector3.right ? roomDepth : roomWidth; // 기존 벽의 길이(문을 낼 축)

        Vector3 doorwayCenter = roomCenter + direction * roomExtentAlongDir;

        if (oldWall != null) Object.DestroyImmediate(oldWall);

        // 기존 벽 자리를 문이 있는 벽 두 조각으로 교체
        float doorSegLen = (wallSpan - DoorWidth) * 0.5f;
        if (doorSegLen > 0.2f)
        {
            Vector3 segOffset = perpendicular * (DoorWidth * 0.5f + doorSegLen * 0.5f);
            CreateWallSegment(parent, $"{roomId}_문벽1", doorwayCenter + segOffset, perpendicular, doorSegLen, wallHeight, floorY);
            CreateWallSegment(parent, $"{roomId}_문벽2", doorwayCenter - segOffset, perpendicular, doorSegLen, wallHeight, floorY);
        }

        // 복도
        Vector3 corridorCenter = doorwayCenter + direction * (CorridorLength * 0.5f);
        CreateFloorSegment(parent, $"{roomId}_복도바닥", corridorCenter, direction, CorridorLength, DoorWidth, floorY);
        Vector3 corridorWallOffset = perpendicular * (DoorWidth * 0.5f);
        CreateWallSegment(parent, $"{roomId}_복도벽1", corridorCenter + corridorWallOffset, direction, CorridorLength, wallHeight, floorY);
        CreateWallSegment(parent, $"{roomId}_복도벽2", corridorCenter - corridorWallOffset, direction, CorridorLength, wallHeight, floorY);

        // 새 방
        Vector3 newRoomCenter = doorwayCenter + direction * (CorridorLength + NewRoomSize * 0.5f);
        CreateFloorSegment(parent, $"{roomId}_바닥", newRoomCenter, direction, NewRoomSize, NewRoomSize, floorY);

        Vector3 farWallCenter = newRoomCenter + direction * (NewRoomSize * 0.5f);
        CreateWallSegment(parent, $"{roomId}_먼벽", farWallCenter, perpendicular, NewRoomSize, wallHeight, floorY);

        // 복도 쪽 벽에도 문
        Vector3 nearWallCenter = newRoomCenter - direction * (NewRoomSize * 0.5f);
        float nearSegLen = (NewRoomSize - DoorWidth) * 0.5f;
        if (nearSegLen > 0.2f)
        {
            Vector3 segOffset2 = perpendicular * (DoorWidth * 0.5f + nearSegLen * 0.5f);
            CreateWallSegment(parent, $"{roomId}_근접벽1", nearWallCenter + segOffset2, perpendicular, nearSegLen, wallHeight, floorY);
            CreateWallSegment(parent, $"{roomId}_근접벽2", nearWallCenter - segOffset2, perpendicular, nearSegLen, wallHeight, floorY);
        }

        Vector3 sideWallCenter1 = newRoomCenter + perpendicular * (NewRoomSize * 0.5f);
        Vector3 sideWallCenter2 = newRoomCenter - perpendicular * (NewRoomSize * 0.5f);
        CreateWallSegment(parent, $"{roomId}_측벽1", sideWallCenter1, direction, NewRoomSize, wallHeight, floorY);
        CreateWallSegment(parent, $"{roomId}_측벽2", sideWallCenter2, direction, NewRoomSize, wallHeight, floorY);

        log.AppendLine($"- Room {roomId} + 복도 생성 (방향 {direction}, 새 방 중심 {newRoomCenter})");
    }

    private static void CreateWallSegment(Transform parent, string name, Vector3 center, Vector3 lengthDir, float length, float height, float floorY)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, true);

        Vector3 scale = new Vector3(WallThickness, height, WallThickness);
        if (Mathf.Abs(lengthDir.x) > 0.5f) scale.x = length;
        else scale.z = length;
        go.transform.localScale = scale;
        go.transform.position = new Vector3(center.x, floorY + height * 0.5f, center.z);
    }

    private static void CreateFloorSegment(Transform parent, string name, Vector3 center, Vector3 dirA, float sizeAlongA, float sizeAlongB, float floorY)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, true);

        const float floorThickness = 0.2f;
        Vector3 scale = new Vector3(0f, floorThickness, 0f);
        if (Mathf.Abs(dirA.x) > 0.5f) { scale.x = sizeAlongA; scale.z = sizeAlongB; }
        else { scale.z = sizeAlongA; scale.x = sizeAlongB; }
        go.transform.localScale = scale;
        go.transform.position = new Vector3(center.x, floorY - floorThickness * 0.5f, center.z);
    }

    // ── 맵 미로화 (격자 기반, 우회로 있는 미로) ──────
    // 기존 큰 방을 걷어내고 격자 미로로 재구성한다. "바닥"은 이미 방 전체를 덮고 있어 그대로 재사용,
    // 외벽/장애물만 지우고 그 위에 Recursive Backtracker(DFS)로 미로 골격을 만든 뒤,
    // 브레이딩(스패닝 트리에 없는 인접 칸도 확률적으로 개방)으로 우회로/루프를 추가한다.

    private const int MazeGridSize = 4;
    private const float MazeDoorWidth = 2.5f;
    private const float MazeBraidChance = 0.4f;
    private static readonly string[] ObstacleNames = { "장애물1", "장애물2", "장애물3", "장애물4" };

    [MenuItem("Tools/AmsalGame/맵 미로화 (기존 방 전체 재구성)")]
    public static void BuildMaze()
    {
        if (!EditorUtility.DisplayDialog(
                "맵 미로화",
                $"기존 외벽 4개, 장애물 4개, (있다면) MapExpansion/이전 미로를 전부 삭제하고\n" +
                $"{MazeGridSize}x{MazeGridSize} 격자 미로(우회로 포함)를 새로 생성합니다.\n" +
                "Player/Enemy/Site 위치도 서로 다른 칸으로 재배치됩니다.\n\n" +
                "기존 벽/장애물이 삭제됩니다 (원본은 git 커밋 7e92426에 남아있어 복구 가능).\n계속할까요?",
                "실행", "취소"))
        {
            return;
        }

        var log = new StringBuilder();
        SetupMaze(log);
        BakeNavMesh(log);
        RefreshMinimapGeometryIfPresent(log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Phase1SceneSetup] 맵 미로화 완료:\n" + log);
        EditorUtility.DisplayDialog("맵 미로화", "완료되었습니다. Console 로그를 확인하세요.\nCtrl+S로 씬을 저장해 주세요.", "확인");
    }

    private static void SetupMaze(StringBuilder log)
    {
        var player = Object.FindFirstObjectByType<PlayerController>();
        var floorGO = GameObject.Find("바닥");
        var floorRenderer = floorGO != null ? floorGO.GetComponent<Renderer>() : null;
        if (floorRenderer == null || player == null)
        {
            log.AppendLine("- 맵 미로화 실패: '바닥' 또는 Player를 찾을 수 없음");
            return;
        }

        // 벽을 지우기 전에 먼저 실측한다 (지운 뒤 재면 Raycast가 더 먼 곳을 잘못 잡을 수 있음)
        float floorY = floorRenderer.bounds.max.y;
        Vector3 anchor = player.transform.position;
        Vector3 rayOrigin = new Vector3(anchor.x, floorY + 1f, anchor.z);
        int defaultLayer = LayerMask.NameToLayer("Default");
        int mask = defaultLayer >= 0 ? 1 << defaultLayer : ~0;

        float halfX = MeasureHalfExtent(rayOrigin, Vector3.right, mask, 8f, log, "X");
        float halfZ = MeasureHalfExtent(rayOrigin, Vector3.forward, mask, 8f, log, "Z");
        Vector3 roomCenter = new Vector3(anchor.x, floorY, anchor.z);
        float roomWidth = halfX * 2f;
        float roomDepth = halfZ * 2f;
        log.AppendLine($"- 방 크기 실측: {roomWidth:F1} x {roomDepth:F1} (중심 {roomCenter})");

        float wallHeight = 3f;
        foreach (var n in OriginalWallNames)
        {
            var w = GameObject.Find(n);
            var r = w != null ? w.GetComponent<Renderer>() : null;
            if (r != null && r.bounds.size.y > 0.5f && r.bounds.size.y < 10f) { wallHeight = r.bounds.size.y; break; }
        }

        // 정리: 이전 확장/미로, 기존 외벽/장애물 삭제
        var oldExpansion = GameObject.Find("MapExpansion");
        if (oldExpansion != null) Object.DestroyImmediate(oldExpansion);
        var oldMaze = GameObject.Find("Maze");
        if (oldMaze != null) Object.DestroyImmediate(oldMaze);
        foreach (var n in OriginalWallNames)
        {
            var w = GameObject.Find(n);
            if (w != null) Object.DestroyImmediate(w);
        }
        foreach (var n in ObstacleNames)
        {
            var o = GameObject.Find(n);
            if (o != null) Object.DestroyImmediate(o);
        }
        Physics.SyncTransforms();
        log.AppendLine("- 기존 외벽/장애물/이전 확장 구조 삭제");

        // 미로 그래프 생성 (스패닝 트리 + 브레이딩)
        var openRight = new bool[MazeGridSize, MazeGridSize];
        var openUp = new bool[MazeGridSize, MazeGridSize];
        GenerateMazeGraph(openRight, openUp, log);

        // 벽 생성
        var root = new GameObject("Maze");
        float cellSizeX = roomWidth / MazeGridSize;
        float cellSizeZ = roomDepth / MazeGridSize;
        Vector3 roomMin = roomCenter - new Vector3(roomWidth * 0.5f, 0f, roomDepth * 0.5f);

        for (int x = 0; x < MazeGridSize; x++)
        {
            for (int z = 0; z < MazeGridSize; z++)
            {
                Vector3 cellCenter = roomMin + new Vector3((x + 0.5f) * cellSizeX, 0f, (z + 0.5f) * cellSizeZ);

                // 오른쪽 경계 (x+1 칸과 공유, 마지막 열이면 바깥 경계라 항상 막힘)
                bool rightDoor = x < MazeGridSize - 1 && openRight[x, z];
                Vector3 rightWallCenter = cellCenter + Vector3.right * (cellSizeX * 0.5f);
                BuildWallOrDoor(root.transform, $"벽_{x}_{z}_E", rightWallCenter, Vector3.forward, cellSizeZ, rightDoor, wallHeight, floorY);

                // 위쪽 경계 (z+1 칸과 공유)
                bool upDoor = z < MazeGridSize - 1 && openUp[x, z];
                Vector3 upWallCenter = cellCenter + Vector3.forward * (cellSizeZ * 0.5f);
                BuildWallOrDoor(root.transform, $"벽_{x}_{z}_N", upWallCenter, Vector3.right, cellSizeX, upDoor, wallHeight, floorY);

                // 바깥쪽 왼쪽/아래쪽 경계는 첫 열/첫 행에서만 (안쪽 경계는 이웃 칸의 오른쪽/위쪽 벽이 이미 담당)
                if (x == 0)
                {
                    Vector3 leftWallCenter = cellCenter - Vector3.right * (cellSizeX * 0.5f);
                    BuildWallOrDoor(root.transform, $"벽_{x}_{z}_W", leftWallCenter, Vector3.forward, cellSizeZ, false, wallHeight, floorY);
                }
                if (z == 0)
                {
                    Vector3 downWallCenter = cellCenter - Vector3.forward * (cellSizeZ * 0.5f);
                    BuildWallOrDoor(root.transform, $"벽_{x}_{z}_S", downWallCenter, Vector3.right, cellSizeX, false, wallHeight, floorY);
                }
            }
        }

        log.AppendLine($"- 미로 생성 완료 ({MazeGridSize}x{MazeGridSize} 칸, 칸 크기 {cellSizeX:F1}x{cellSizeZ:F1})");

        // Player/Enemy/Site를 서로 다른 칸으로 재배치 (겹치지 않게 코너+중앙에 분산)
        Vector3 CellCenter(int cx, int cz) => roomMin + new Vector3((cx + 0.5f) * cellSizeX, floorY, (cz + 0.5f) * cellSizeZ);

        Vector3 playerCell = CellCenter(MazeGridSize / 2, MazeGridSize / 2);
        player.transform.position = new Vector3(playerCell.x, anchor.y, playerCell.z);
        log.AppendLine($"- Player를 중앙 칸으로 재배치");

        RepositionIfExists("Enemy_01", CellCenter(MazeGridSize - 1, MazeGridSize - 1), log);
        RepositionIfExists("Enemy_02", CellCenter(0, MazeGridSize - 1), log);
        RepositionIfExists("SiteA", CellCenter(MazeGridSize - 1, 0), log);
        RepositionIfExists("SiteB", CellCenter(0, 0), log);
    }

    private static void RepositionIfExists(string name, Vector3 worldPos, StringBuilder log)
    {
        var go = GameObject.Find(name);
        if (go == null) return;

        go.transform.position = new Vector3(worldPos.x, go.transform.position.y, worldPos.z);
        log.AppendLine($"- {name}을(를) 새 칸으로 재배치");
    }

    // Recursive Backtracker(DFS)로 모든 칸을 잇는 스패닝 트리를 만든 뒤,
    // 브레이딩으로 스패닝 트리에 없는 인접 칸 연결을 확률적으로 추가 개방해 루프(우회로)를 만든다.
    private static void GenerateMazeGraph(bool[,] openRight, bool[,] openUp, StringBuilder log)
    {
        int size = MazeGridSize;
        var visited = new bool[size, size];
        var stack = new Stack<Vector2Int>();
        var start = new Vector2Int(Random.Range(0, size), Random.Range(0, size));
        visited[start.x, start.y] = true;
        stack.Push(start);
        int treeEdges = 0;

        while (stack.Count > 0)
        {
            var cur = stack.Peek();
            var candidates = new List<Vector2Int>();
            if (cur.x + 1 < size && !visited[cur.x + 1, cur.y]) candidates.Add(new Vector2Int(cur.x + 1, cur.y));
            if (cur.x - 1 >= 0 && !visited[cur.x - 1, cur.y]) candidates.Add(new Vector2Int(cur.x - 1, cur.y));
            if (cur.y + 1 < size && !visited[cur.x, cur.y + 1]) candidates.Add(new Vector2Int(cur.x, cur.y + 1));
            if (cur.y - 1 >= 0 && !visited[cur.x, cur.y - 1]) candidates.Add(new Vector2Int(cur.x, cur.y - 1));

            if (candidates.Count == 0)
            {
                stack.Pop();
                continue;
            }

            var next = candidates[Random.Range(0, candidates.Count)];
            OpenEdge(openRight, openUp, cur, next);
            visited[next.x, next.y] = true;
            stack.Push(next);
            treeEdges++;
        }

        int braided = 0;
        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                if (x < size - 1 && !openRight[x, z] && Random.value < MazeBraidChance) { openRight[x, z] = true; braided++; }
                if (z < size - 1 && !openUp[x, z] && Random.value < MazeBraidChance) { openUp[x, z] = true; braided++; }
            }
        }

        log.AppendLine($"- 미로 그래프: 스패닝 트리 {treeEdges}개 연결 + 브레이딩(우회로) {braided}개 추가");
    }

    private static void OpenEdge(bool[,] openRight, bool[,] openUp, Vector2Int a, Vector2Int b)
    {
        if (b.x == a.x + 1) openRight[a.x, a.y] = true;
        else if (b.x == a.x - 1) openRight[b.x, b.y] = true;
        else if (b.y == a.y + 1) openUp[a.x, a.y] = true;
        else if (b.y == a.y - 1) openUp[b.x, b.y] = true;
    }

    private static void BuildWallOrDoor(Transform parent, string name, Vector3 wallCenter, Vector3 spanDir, float span, bool hasDoor, float wallHeight, float floorY)
    {
        if (!hasDoor)
        {
            CreateWallSegment(parent, name, wallCenter, spanDir, span, wallHeight, floorY);
            return;
        }

        float segLen = (span - MazeDoorWidth) * 0.5f;
        if (segLen <= 0.2f) return; // 칸이 문 폭보다 작으면 벽 없이 완전 개방

        Vector3 segOffset = spanDir * (MazeDoorWidth * 0.5f + segLen * 0.5f);
        CreateWallSegment(parent, name + "_1", wallCenter + segOffset, spanDir, segLen, wallHeight, floorY);
        CreateWallSegment(parent, name + "_2", wallCenter - segOffset, spanDir, segLen, wallHeight, floorY);
    }
}
