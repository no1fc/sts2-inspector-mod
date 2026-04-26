# STS2 멀티플레이어 인스펙터 모드 설계

**날짜:** 2026-04-26  
**프로젝트:** testmode1 (Slay the Spire 2 Mod)  
**목표:** 멀티플레이어 세션에서 다른 플레이어의 덱/패/유물/포션을 확인할 수 있는 최적화된 UI 모드

---

## 1. 개요

기존 유사 모드의 문제점(카드 목록 열 때마다 UI 노드 생성/삭제로 인한 버벅임)을 해결한다.  
핵심 전략: **폴링 + dirty 감지 + 노드 풀링**

- 주기적으로 게임 상태를 읽되, 변경이 없으면 UI를 건드리지 않는다.
- 접속 중인 플레이어 수만큼만 처리한다 (최대 4명, 동적).
- UI 노드는 미리 생성해두고 재활용하여 팝업 열기 비용을 제거한다.

---

## 2. 아키텍처

```
ModStart.cs
├── PlayerDataPoller       ← 폴링 루프 + dirty 감지
│   └── PlayerSnapshot     ← 플레이어 1명 상태 스냅샷
├── UI/
│   ├── OverlayPanel       ← 화면 고정 요약 패널
│   ├── PlayerDetailPopup  ← 플레이어 클릭 시 상세 팝업
│   └── CardNodePool       ← 노드 재활용 풀
└── ModSettings            ← 토글/폴링 설정 저장
```

### 데이터 흐름

```
[PlayerDataPoller] --poll_interval_ms--> 게임 상태 읽기
    ↓
현재 플레이어 목록 확인
    ├── 신규 플레이어 → PlayerSnapshot 추가 + OverlayPanel 행 추가
    ├── 퇴장 플레이어 → PlayerSnapshot 제거 + OverlayPanel 행 제거
    └── 기존 플레이어 → hash 비교
            ├── 같음 → 아무것도 안함
            └── 다름 → Snapshot 갱신 + 해당 플레이어 UI만 갱신
```

---

## 3. 컴포넌트 상세

### PlayerSnapshot

플레이어 1명의 상태를 담는 데이터 클래스.

```csharp
class PlayerSnapshot {
    string PlayerId;
    string PlayerName;
    List<string> DeckCardIds;   // 덱 카드 ID 목록
    List<string> HandCardIds;   // 현재 패 카드 ID 목록
    List<string> RelicIds;      // 유물 ID 목록
    List<string> PotionIds;     // 포션 ID 목록
    string Hash;                // dirty 감지용 해시
}
```

**Hash 계산:** `DeckCardIds + HandCardIds + RelicIds + PotionIds`를 연결한 문자열의 해시값. 변경 감지에만 사용하므로 단순 문자열 비교로 충분.

### PlayerDataPoller

- Godot `_Process` 또는 타이머로 `poll_interval_ms`마다 실행
- 게임에서 현재 세션 플레이어 목록을 읽어 `PlayerSnapshot` 딕셔너리를 동적으로 관리
- dirty 플레이어 발생 시 이벤트 발행 → UI 레이어가 구독하여 업데이트

### CardNodePool

- 팝업에서 카드/유물/포션을 표시하는 노드를 **미리 최대 수량** 생성
- 팝업 열기 = 풀에서 노드 가져와 내용만 교체 (instantiate 없음)
- 팝업 닫기 = 노드를 풀에 반환 (free 없음)

---

## 4. UI

### OverlayPanel (고정 패널)

- 화면 우측 고정, 드래그로 위치 변경 가능
- 마지막 위치 설정 파일에 저장
- `[─]` 버튼으로 전체 접기/펼치기
- 상단 토글 버튼: `[덱]` `[패]` `[유물]` `[포션]` (각각 독립 on/off)

```
┌─────────────────────────┐
│ [덱✓] [패✓] [유물✓] [포션✓] │
├─────────────────────────┤
│ 👤 Player1   🃏12 💎3 🧪2 │
│ 👤 Player2   🃏8  💎5 🧪1 │
│ 👤 Player3   🃏15 💎2 🧪0 │
└─────────────────────────┘
```

- 각 행 클릭 → PlayerDetailPopup 오픈
- 토글 OFF된 항목의 수치는 해당 행에서 숨겨짐

### PlayerDetailPopup (상세 팝업)

- OverlayPanel 옆에 표시
- 동시에 하나만 열림 (다른 플레이어 클릭 시 내용 교체)
- 탭 구성: `[덱 (N)]` `[패 (N)]` `[유물]` `[포션]`
- 토글 OFF된 항목은 탭 자체 숨김

```
┌──────────────────────────────┐
│ Player1                   [X]│
├──────────────────────────────┤
│ [덱 (12)] [패 (3)] [유물] [포션] │
├──────────────────────────────┤
│ Strike  Strike  Defend       │
│ Defend  Bash    Pommel Strike│
└──────────────────────────────┘
```

---

## 5. 설정

파일 위치: `user://sts2_inspector_settings.cfg`

```ini
[display]
show_deck    = true
show_hand    = true
show_relics  = true
show_potions = true
panel_position_x = 1600
panel_position_y = 300

[performance]
poll_interval_ms = 1500
```

- 토글 변경 시 즉시 저장
- 패널 위치는 드래그 종료 시 저장

---

## 6. 파일 구조

```
testmode1/
├── ModStart.cs
├── src/
│   ├── PlayerSnapshot.cs
│   ├── PlayerDataPoller.cs
│   ├── ModSettings.cs
│   └── UI/
│       ├── OverlayPanel.cs
│       ├── PlayerDetailPopup.cs
│       └── CardNodePool.cs
└── mod_manifest.json
```

---

## 7. 최적화 포인트 요약

| 문제 | 해결 |
|------|------|
| 열 때마다 노드 생성 | CardNodePool로 재활용 |
| 변화 없어도 UI 갱신 | hash dirty 감지로 스킵 |
| 항상 4명 처리 | 접속 중인 플레이어 수만 처리 |
| 모든 플레이어 동시 갱신 | dirty 플레이어만 선택 갱신 |
