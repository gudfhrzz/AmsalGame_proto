# Changelog

AmsalGame_proto 개발 기록입니다.

---

## [0.1.0] — 2026-06-29 · Phase 1 기초 구현

### 추가된 시스템

#### 플레이어
- `PlayerController` — WASD 이동, Shift 걷기(발소리 최소화), CharacterController 기반
- `AgentData` (ScriptableObject) — 요원별 이동속도 / 발소리 반경 데이터 분리
  - AgentType: Offensive / Defensive / Neutral
  - 기본값: moveSpeed 3.5f, walkMultiplier 0.5f, moveSoundRadius 8m, walkSoundRadius 3m

#### 카메라
- `TopViewCamera` — 75° 탑뷰 스무스 팔로우 카메라

#### 사운드 이벤트
- `SoundEventSystem` — 싱글톤, `Action<SoundEvent>` 브로드캐스트
- `SoundEmitter` — 이동 중 0.3초 간격으로 발소리 이벤트 emit
  - 이동/걷기 상태에 따라 반경 자동 전환

#### AI
- `AIController` — NavMesh 기반 FSM
  - 상태: Idle → Patrol → Suspicious → Chase → Alert
  - FOV 탐지: 거리 8m, 각도 90°, 장애물 Raycast 차폐
  - 청각 탐지: 반경 15m, SoundEventSystem 구독
  - `TriggerAlert()` 공개 메서드 (동료 사망 알림용)
- `Health` — maxHp=3, TakeDamage, OnDeath 이벤트

#### 전투
- `AssassinationSystem` — F키 암살
  - 유효 거리 1.5m, 적 후방 120도 이내
  - AI가 Chase 상태면 암살 불가 (인식 여부 판정)

---

## [Unreleased] — Phase 1 진행 중

### 구현 예정
- 기본 맵 (ProBuilder, A/B 사이트)
- CQC 시스템 (공격 / 막기 / 패링 / 잡기)
- 존버 방지 시스템 (정지 시 위치 노출)
- FOV 시야 시각화 (플레이어 시야 차폐 렌더링)
- 원거리 무기 (칼 던지기 → 권총 파밍)
- 군집 페널티 시스템
- 승리 조건
