# STS2 멀티플레이어 인스펙터

Slay the Spire 2 멀티플레이 중 다른 플레이어의 **덱, 패, 유물, 포션, 버프**를 실시간으로 확인할 수 있는 모드입니다.

> 영문 문서: [README.md](README.md)

## 기능

- **오버레이 패널** — 현재 함께 플레이 중인 플레이어 목록을 항상 표시; Steam 표시 이름과 선택한 캐릭터 클래스 표시
- **상세 팝업** — 플레이어 이름 클릭 시 탭 형식으로 상세 정보 표시 (드로우 덱 / 패 / 유물 / 포션 / 버프)
- **실시간 패 스탯** — 패 탭에서 각 카드의 현재 코스트, 피해, 방어력을 게임 엔진이 계산한 값으로 표시 (강도·민첩성 및 전투 중 보너스 반영)
- **이미지 툴팁** — 카드·유물·포션 위에 마우스를 올리면 아트와 설명 표시
- **드래그 이동** — 오버레이 패널과 상세 팝업을 화면 어디로든 이동 가능
- **접기 토글** — `─` 버튼 클릭 시 패널이 토글 바만 남도록 축소; `+` 클릭 시 다시 확장
- **F7 단축키** — 모드 UI 전체를 켜거나 끔
- **설정 저장** — 패널 위치와 표시 항목이 세션 간 유지
- 솔로 모드 지원 (싱글 플레이어 런에서도 동작)

## 요구 사항

| | |
|---|---|
| 게임 | Slay the Spire 2 (Steam) |
| 런타임 | .NET 9.0 |
| 빌드 도구 | Godot 4 + `dotnet` CLI |
| 이미지 에셋 | `data_sts2/` 폴더 (아래 참조) |

## 설치

1. 모드 빌드:
   ```bash
   dotnet build testmode1.csproj
   ```
   빌드 성공 시 DLL이 자동으로 복사됩니다:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\testmode1\
   ```

2. `ModSettings`에 설정된 경로(기본값: `C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2\`)에 이미지 에셋을 배치합니다:
   ```
   data_sts2/
   ├─ images/
   │   ├─ relics/          # 개별 PNG (256×256)
   │   ├─ potions/         # 개별 PNG
   │   └─ atlases/
   │       ├─ card_atlas_0.png
   │       ├─ card_atlas_1.png
   │       ├─ card_atlas_2.png
   │       └─ card_atlas.sprites/   # 캐릭터별 .tres 스프라이트 리전 파일
   ```

3. 게임을 실행하고 모드 메뉴에서 **STS2 Multiplayer Inspector**를 활성화합니다.

## 단축키

| 키 | 동작 |
|----|------|
| `F7` | 모드 UI 전체 표시 / 숨기기 |

## 설정

설정은 `user://sts2_inspector_settings.cfg`에 저장됩니다.

| 키 | 기본값 | 설명 |
|----|--------|------|
| `display.show_deck` | `true` | 드로우 덱 탭 표시 여부 |
| `display.show_hand` | `true` | 패 탭 표시 여부 |
| `display.show_relics` | `true` | 유물 탭 표시 여부 |
| `display.show_potions` | `true` | 포션 탭 표시 여부 |
| `display.show_buffs` | `true` | 버프 탭 표시 여부 |
| `display.panel_x/y` | `1600 / 300` | 오버레이 패널 위치 |
| `performance.poll_interval_seconds` | `1.5` | 게임 상태 폴링 간격 (초) |
| `assets.image_base_path` | *(로컬 경로)* | 이미지 에셋 루트 폴더 |

## 아키텍처

```
ModStart (InspectorBootstrapper._Ready)
  ├─ ImageCache          — 카드/유물/포션 텍스처를 디스크에서 로드
  ├─ ImageTooltip        — 호버 툴팁 (이미지 + 설명)
  ├─ OverlayPanel        — 항상 표시되는 플레이어 목록 (접기 지원)
  ├─ PlayerDetailPopup   — 플레이어별 탭 팝업 (드로우 덱/패/유물/포션/버프)
  └─ PlayerDataPoller    — Reflection으로 1.5초마다 게임 상태 폴링
       ├─ PlayerUpdated  → OverlayPanel + PlayerDetailPopup 갱신
       └─ PlayerRemoved  → OverlayPanel 정리
```

STS2는 공개 모딩 API를 제공하지 않으므로, `RunManager.Instance`에 Reflection으로 접근합니다.

### 패 카드 피해 값 해석 방식

카드는 두 가지 VarSet 패턴을 사용합니다:

| 패턴 | 예시 카드 | 키 구조 |
|------|----------|---------|
| 단순형 | 타격, 극적인 입장 | `Damage(BaseValue=6)` |
| 계산형 | 영혼 폭풍, 풀어놓기 | `CalculationBase(base=9)` + `CalculatedDamage` |

`CanonicalValue`(게임 엔진의 실시간 계산값)가 있으면 우선 사용하고, 없으면 `BaseValue`로 폴백합니다.
실시간 값을 사용할 때는 강도·민첩성이 이미 포함되어 있으므로 중복 적용하지 않습니다.

## 디버깅

게임 로그를 아래 접두사로 필터링합니다:

```
[Inspector]   # 폴링, 스냅샷, 패 카드 스탯
[ImageCache]  # 텍스처 로드 성공 / 실패
```

타입별 최초 1회 덤프 (세션당 한 번):
- `[Inspector] PlayerProps: ...` — 플레이어 객체 프로퍼티 (Steam 이름 필드 식별용)
- `[Inspector] VarSet '...': ...` — 카드 변수 키와 값

## 라이선스

MIT

## 작성자

SD
