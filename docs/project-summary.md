# STS2 인스펙터 모드 (testmode1) — 개발 요약

> 최종 업데이트: 2026-04-27

---

## 프로젝트 개요

STS2(Slay the Spire 2) 게임 내에서 **다른 플레이어(또는 자신)의 덱·패·유물·포션을 실시간으로 확인**할 수 있는 관전용 인스펙터 모드.

- **플랫폼**: Godot 4 + C# (.NET 9.0)
- **모드 진입점**: `ModStart.cs` → `InspectorBootstrapper._Ready()`
- **게임 API 접근 방식**: Reflection (게임 DLL `sts2.dll`을 직접 참조, 런타임에 비공개 멤버 접근)
- **빌드 명령**: `dotnet build testmode1.csproj`
- **DLL 자동 복사 대상**: `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\testmode1\`

---

## 파일 구조

```
testmode1/
├── ModStart.cs                  # 모드 초기화 진입점, UI 노드 생성 및 연결
├── testmode1.csproj             # 프로젝트 정의, sts2.dll 참조
├── mod_manifest.json            # 모드 메타데이터
├── src/
│   ├── ModSettings.cs           # 설정 로드/저장 (ConfigFile, user://)
│   ├── PlayerSnapshot.cs        # 폴링 결과 데이터 컨테이너
│   ├── PlayerDataPoller.cs      # 게임 API Reflection 폴링 엔진
│   ├── ImageCache.cs            # 이미지 로드·캐시 (atlas 파싱)
│   └── UI/
│       ├── OverlayPanel.cs      # 플레이어 목록 오버레이 패널
│       ├── PlayerDetailPopup.cs # 플레이어 상세 팝업 (탭 UI)
│       ├── CardNodePool.cs      # HoverLabel 오브젝트 풀
│       ├── HoverLabel.cs        # 마우스 오버 시 툴팁 표시 Label
│       └── ImageTooltip.cs      # 이미지 + 설명 툴팁 패널
└── docs/
    └── project-summary.md       # 이 문서
```

---

## 아키텍처 개요

```
ModStart
  └─ InspectorBootstrapper._Ready()
       ├─ ImageCache 생성 (싱글톤)
       ├─ ImageTooltip 생성 → CanvasLayer에 추가
       ├─ OverlayPanel 생성 → CanvasLayer에 추가
       ├─ PlayerDetailPopup 생성 → Initialize(tooltip)
       └─ PlayerDataPoller 생성
            ├─ PlayerUpdated → OverlayPanel.UpdatePlayer()
            │                → PlayerDetailPopup.RefreshIfShowing()
            ├─ PlayerRemoved → OverlayPanel.RemovePlayer()
            └─ OverlayPanel.PlayerRowClicked → PlayerDetailPopup.ShowPlayer()
```

### 데이터 흐름

```
PlayerDataPoller (1.5초 폴링)
  → TakeSnapshot() [Reflection]
  → PlayerSnapshot {
      DeckCardIds, HandCardIds, RelicIds, PotionIds   // 표시명 (한국어 가능)
      NameToDescription                                // 표시명 → 효과 설명
      NameToImageKey                                   // 표시명 → 이미지 키 (영어)
    }
  → PlayerDetailPopup.RefreshContent()
  → PopulateTab() → CardNodePool.Acquire(display, imgKey, type, page, desc)
  → HoverLabel.Configure(displayText, imageKey, type, tooltip, description)
  → [마우스 오버] → ImageCache.GetTexture(imageKey, type)
                  → ImageTooltip.ShowAt(texture, name, description, pos)
```

---

## 주요 컴포넌트 설명

### ModSettings (`src/ModSettings.cs`)

설정 파일 경로: `user://sts2_inspector_settings.cfg`

| 설정 키 | 타입 | 기본값 | 설명 |
|---------|------|--------|------|
| `display.show_deck` | bool | true | 덱 탭 표시 여부 |
| `display.show_hand` | bool | true | 패 탭 표시 여부 |
| `display.show_relics` | bool | true | 유물 탭 표시 여부 |
| `display.show_potions` | bool | true | 포션 탭 표시 여부 |
| `display.panel_x/y` | float | 1600/300 | 오버레이 패널 위치 |
| `performance.poll_interval_seconds` | float | 1.5 | 폴링 주기 |
| `assets.image_base_path` | string | `C:\AI_Folder\...\data_sts2` | 이미지 에셋 루트 |

---

### PlayerDataPoller (`src/PlayerDataPoller.cs`)

**폴링 방식**: `Timer` 노드 → `OnPollTick()` → `TakeSnapshot()` → `PlayerUpdated` 이벤트 발생

**게임 API 접근 경로**:
```
RunManager.Instance
  └─ .State (Reflection: private property)
       ├─ as IPlayerCollection → .Players → 복수 플레이어 목록
       └─ .Player / .CurrentPlayer / .LocalPlayer → 솔로 폴백
```

**스냅샷 빌드 Reflection 경로**:
```
player
  ├─ .Character.Title → PlayerName (LocString 처리)
  ├─ .Deck → DeckCardIds
  ├─ .Relics → RelicIds
  ├─ .Potions → PotionIds
  └─ .Piles.Hand → HandCardIds
```

**컬렉션 언래핑**: 덱은 직접 IEnumerable이 아닌 `DeckModel` 래퍼일 수 있음
→ `Cards` / `Items` / `All` / `CardModels` 프로퍼티 순서로 시도

**LocString 처리**: `MegaCrit.Sts2.Core.Localization` 네임스페이스 감지 후
→ `.Value` → `.Text` → `.Str` 순으로 실제 문자열 추출

---

### ImageCache (`src/ImageCache.cs`)

이미지 에셋 경로 구조:
```
data_sts2/
├─ images/
│   ├─ relics/{name}.png           (256×256 개별 PNG)
│   ├─ potions/{name}.png          (개별 PNG)
│   └─ atlases/
│       ├─ card_atlas_0/1/2.png    (대형 atlas PNG, 13~19 MB)
│       └─ card_atlas.sprites/
│           ├─ ironclad/{name}.tres
│           ├─ defect/{name}.tres
│           ├─ silent/{name}.tres
│           └─ ... (13개 캐릭터 폴더)
```

**키 → 파일명 변환 규칙**:

| 타입 | 게임 내 키 | 파일명 |
|------|-----------|--------|
| Card | `card_strike_defect` | `defect/strike_defect.tres` |
| Card | `card_zap` | `defect/zap.tres` |
| Card | `card_bash` | `ironclad/bash.tres` |
| Relic | `relic_cracked_core` | `relics/cracked_core.png` |
| Potion | `potion_fire_potion` | `potions/fire_potion.png` |

→ **규칙**: 타입 접두사(`card_`, `relic_`, `potion_`) 제거 후 파일명으로 사용

**.tres 파싱**:
```
[ext_resource path="res://images/atlases/card_atlas_1.png" ...]
[resource]
region = Rect2(3277, 1, 250, 190)
```
→ Regex로 atlas 파일명과 Rect2I 추출 → `Image.GetRegion(Rect2I)` 크롭

**이미지 로딩**: `Image.LoadFromFile()` 미사용 (Windows 절대 경로 문제)
→ `File.ReadAllBytes()` + `img.LoadPngFromBuffer()` 사용

---

### ImageTooltip (`src/UI/ImageTooltip.cs`)

마우스 호버 시 카드/유물 이미지와 설명을 표시하는 패널.

- `ZIndex = 100` (모든 UI 위에 표시)
- `MouseFilter = Ignore` (클릭 이벤트 통과)
- 이미지: `TextureRect` 250×190
- 설명: `Label` (word wrap, 최대 5줄, BBCode 태그 제거 후 표시)
- `ShowAt(texture, name, description, pos)` — 화면 경계 클램프 포함

---

### HoverLabel (`src/UI/HoverLabel.cs`)

`Label`을 상속하는 partial class. `_Ready()`에서 신호 연결 후 재사용 시 `Configure()`로 데이터 갱신.

```csharp
Configure(displayText, imageKey, itemType, tooltip, description)
// MouseEntered → ImageCache.GetTexture(imageKey, type) → tooltip.ShowAt(...)
// MouseExited  → tooltip.Hide()
```

---

### CardNodePool (`src/UI/CardNodePool.cs`)

`HoverLabel` 노드 재사용 풀. `AddChild/QueueFree` 대신 `Reparent()` 방식.

- `_storage`: Visible=false 컨테이너 (미사용 노드 보관)
- `Acquire(displayText, imageKey, itemType, target, description)` → 풀에서 꺼내 `target`에 Reparent
- `ReleaseAll()` → 모든 사용 중 노드를 다시 `_storage`로 Reparent

---

### OverlayPanel (`src/UI/OverlayPanel.cs`)

항상 화면에 표시되는 플레이어 목록 패널.

- 드래그로 위치 변경 → `MouseUp` 시 `ModSettings.SetPanelPosition()` 저장
- 토글 버튼: 덱/패/유물/포션 컬럼 표시 여부
- 접기 버튼(`─`): `_playerRows` Visible 토글
- 플레이어 이름 버튼 클릭 → `PlayerRowClicked` 신호 → `PlayerDetailPopup.ShowPlayer()`

---

### PlayerDetailPopup (`src/UI/PlayerDetailPopup.cs`)

플레이어 클릭 시 표시되는 상세 팝업.

- 탭 구성: 덱 / 패 / 유물 / 포션
- 각 탭: `ScrollContainer` + `VBoxContainer` (세로 나열)
- 중복 카드 묶기: `Strike ×3` 형식, 알파벳 정렬
- `SelectFirstNonEmptyTab()`: 데이터 있는 첫 번째 탭 자동 선택
- 드래그 이동 지원

---

## 해결한 주요 문제들

### 1. 솔로 모드 지원
`RunManager.State`가 `IPlayerCollection`을 구현하지 않는 솔로 상태에서도 동작하도록 `GetSinglePlayerFallback()` 추가.

### 2. LocString 처리
카드/유물 이름이 `MegaCrit.Sts2.Core.Localization.LocString` 타입으로 반환될 때 `.ToString()`이 타입명을 반환하는 문제.
→ `ResolveLocString()` 헬퍼로 `.Value` / `.Text` / `.Str` 순 추출.

### 3. DeckModel 언래핑
`player.Deck`이 직접 `IEnumerable`이 아닌 `DeckModel` 래퍼 타입인 경우.
→ `Cards` / `Items` / `All` / `CardModels` 내부 프로퍼티 순차 탐색.

### 4. 이미지 로딩 실패
`Image.LoadFromFile(absolutePath)` 가 Godot 모드 컨텍스트에서 Windows 절대 경로를 처리하지 못함.
→ `File.ReadAllBytes()` + `img.LoadPngFromBuffer()` 로 교체.

### 5. 한국어 카드명 → 이미지 키 매핑 실패
`ToSnakeCase("타격")` = `""` (한국어 문자 전부 제거) → .tres 파일 탐색 실패.
→ 카드 오브젝트의 `Key` / `CardModelId` 프로퍼티(영어 내부 ID)를 별도 추출해 `NameToImageKey` 딕셔너리로 저장.

### 6. 이미지 키 접두사 불일치
내부 키가 `card_strike_defect`, `relic_cracked_core` 형식인데 파일명은 `strike_defect.tres`, `cracked_core.png`.
→ `GetTexture()` 호출 시 타입별 접두사(`card_`, `relic_`, `potion_`) 제거 후 파일명으로 사용.

---

## 빌드 및 디버그

```bash
# 빌드 (자동으로 mods 폴더에 DLL 복사)
cd "C:\AI_Folder\ClaudeCode\SlaytheSpire2\testmode1"
dotnet build testmode1.csproj

# 게임 로그 필터링 (주요 키워드)
[Inspector]   # 폴링, 스냅샷
[ImageCache]  # 이미지 로드 성공/실패
```

**이미지 로드 진단 로그 예시**:
```
[ImageCache] Init basePath=C:\AI_Folder\...\data_sts2
[Inspector] ImageKey: 타격 → card_strike_defect   ← 키 추출 성공
[ImageCache] Loading atlas: card_atlas_1.png        ← atlas 첫 로드
[ImageCache] No .tres for card: unknown_card        ← 파일 미존재
[ImageCache] Not found: ...relics/unknown.png       ← 유물 파일 미존재
```

---

## 외부 의존성

| 패키지 | 버전 | 용도 |
|--------|------|------|
| `Lib.Harmony` | 2.3.3 | Godot 게임 런타임 패치 (현재 직접 사용 안 함, 향후 확장용) |
| `sts2.dll` | (게임 내장) | `RunManager`, `IPlayerCollection` 등 게임 API |

---

## 이미지 에셋 위치

```
C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2\
├─ images/
│   ├─ relics/          # 319개 PNG (256×256)
│   ├─ potions/         # PNG
│   └─ atlases/
│       ├─ card_atlas_0.png  (19.2 MB)
│       ├─ card_atlas_1.png  (18.4 MB)
│       ├─ card_atlas_2.png  (13.3 MB)
│       └─ card_atlas.sprites/  # 874개 .tres (캐릭터별 폴더)
└─ localization/
    └─ eng/
        └─ cards.json    # 카드 이름/설명 (영어, 플레이스홀더 형식)
```
