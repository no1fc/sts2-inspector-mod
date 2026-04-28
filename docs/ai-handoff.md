# STS2 Inspector Mod — AI Handoff Document

> 작성일: 2026-04-28  
> 목적: 다른 AI가 컨텍스트 없이도 이 프로젝트를 즉시 이어받아 작업할 수 있도록 전체 코드·구조·의사결정을 기록

---

## 1. 프로젝트 개요

**Slay the Spire 2** 게임에 로드되는 Godot 4 C# 모드.  
게임 중 플레이어(자신 또는 멀티플레이 상대)의 **덱·패·유물·포션을 실시간으로 조회**하는 관전용 인스펙터 UI를 제공한다.

| 항목 | 값 |
|------|-----|
| 플랫폼 | Godot 4 + C# (.NET 9.0) |
| 게임 API | Reflection (비공개 멤버 포함, 게임 DLL `sts2.dll` 직접 참조) |
| 빌드 | `dotnet build testmode1.csproj` |
| DLL 출력 | `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\testmode1\` (빌드 시 자동 복사) |
| 이미지 에셋 | `C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2\` |

---

## 2. 파일 구조

```
testmode1/
├── ModStart.cs                  # 모드 진입점 (ModInitializer + InspectorBootstrapper)
├── testmode1.csproj
├── mod_manifest.json
├── src/
│   ├── ModSettings.cs           # ConfigFile 기반 설정 로드/저장
│   ├── PlayerSnapshot.cs        # 폴링 결과 데이터 컨테이너
│   ├── PlayerDataPoller.cs      # Reflection 폴링 엔진
│   ├── ImageCache.cs            # 이미지 로드·캐시 (atlas 파싱)
│   └── UI/
│       ├── OverlayPanel.cs      # 항상 표시되는 플레이어 목록 패널
│       ├── PlayerDetailPopup.cs # 플레이어 상세 팝업 (탭 UI)
│       ├── CardNodePool.cs      # HoverLabel 오브젝트 풀
│       ├── HoverLabel.cs        # 마우스 오버 시 툴팁 Label
│       └── ImageTooltip.cs      # 이미지 + 스탯 + 설명 툴팁 패널
└── docs/
    ├── project-summary.md       # 한국어 요약
    └── ai-handoff.md            # 이 문서
```

---

## 3. 아키텍처 & 데이터 흐름

```
ModStart.ModInit()
  └─ InspectorBootstrapper._Ready()
       ├─ ImageCache (싱글톤)
       ├─ ImageTooltip → CanvasLayer (ZIndex=100)
       ├─ OverlayPanel → CanvasLayer
       ├─ PlayerDetailPopup → CanvasLayer
       │    └─ Initialize(tooltip) → CardNodePool(80개 HoverLabel)
       └─ PlayerDataPoller
            ├─ PlayerUpdated → OverlayPanel.UpdatePlayer(snap)
            │               → PlayerDetailPopup.RefreshIfShowing(snap)
            └─ PlayerRemoved → OverlayPanel.RemovePlayer(id)
  OverlayPanel.PlayerRowClicked(id)
       └─ PlayerDetailPopup.ShowPlayer(poller.GetLastSnapshot(id))
```

```
PlayerDataPoller (1.5초 Timer 폴링)
  → TakeSnapshot(playerId)
  → BuildSnapshot() [Reflection]
  → PlayerSnapshot {
      DeckCardIds, HandCardIds, RelicIds, PotionIds  // 표시명 (한국어)
      NameToDescription   // 표시명 → BBCode 제거된 설명 텍스트
      NameToImageKey      // 표시명 → 영어 snake_case 이미지 키 (예: "타격" → "card_strike_defect")
      NameToStats         // 표시명 → "코스트: 1  피해: 6" 형식 스탯 문자열
    }
  → PlayerDetailPopup.RefreshContent()
  → PopulateTab() → CardNodePool.Acquire(display, imgKey, type, page, desc, stats)
  → HoverLabel.Configure(text, imgKey, type, tooltip, desc, stats)
  → [마우스 오버] → ImageCache.GetTexture(imgKey, type)
                  → ImageTooltip.ShowAt(texture, name, desc, stats, pos)
```

---

## 4. 핵심 게임 API Reflection 경로 (덤프로 확인된 사실)

```
RunManager.Instance
  └─ .State  (private, BindingFlags.NonPublic | Instance)
       ├─ as IPlayerCollection  (멀티플레이)
       │    └─ .Players (IEnumerable)
       │         └─ player  [MegaCrit.Sts2.Core.Entities.Players.Player]
       │              ├─ .NetId (UInt64)           → 플레이어 고유 ID
       │              ├─ .Character (CharacterModel)
       │              │    └─ .Title (LocString)   → 캐릭터 표시명
       │              ├─ .Deck (CardPile)           → 덱 카드 묶음
       │              ├─ .Relics (IReadOnlyList<RelicModel>)
       │              ├─ .Potions (IEnumerable<PotionModel>)  [비어있는 슬롯 제외됨]
       │              └─ .Piles (IEnumerable<CardPile>)       → 모든 카드 더미 배열
       │                   └─ CardPile
       │                        ├─ .Type (PileType) = "Deck"/"Hand"/"Discard"/...
       │                        ├─ .Cards (IReadOnlyList<CardModel>)
       │                        ├─ .IsEmpty (Boolean)
       │                        └─ .IsCombatPile (Boolean)  ← 전투 중 생성된 더미 여부
       └─ .Player / .CurrentPlayer / .LocalPlayer  (솔로 fallback)
```

### 손패(Hand) 접근
- `Piles` 배열에서 `Type == "Hand"` 인 `CardPile`을 찾아야 함
- 손패 `CardPile`은 **전투 중에만 존재** (`IsCombatPile = True`)
- 전투 밖에서는 `FindHandPile()`이 `null` 반환 → `HandCardIds`는 빈 리스트

### 카드 모델 (CardModel, 예: StrikeDefect)
| 프로퍼티 | 타입 | 용도 |
|---|---|---|
| `Title` | `String` | 한국어 카드명 ("타격") — 직접 사용 가능 |
| `Description` | `LocString` | 설명 — LocString 해석 불가 (미해결) |
| `CanonicalEnergyCost` | `Int32` | 코스트 (0=무료, -1=X코스트) |
| `CanonicalVars` | `IEnumerable<DynamicVar>` | 피해/방어 변수 목록 — **전투 중에만 non-null** |
| `CanonicalInstance` | `CardModel` | 항상 존재하는 템플릿 인스턴스 (전투 밖 fallback용) |
| `Key` | (ModelId) | 내부 ID (예: `CARD.STRIKE_DEFECT`) |

**DynamicVar** (`MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar`):
| 프로퍼티 | 타입 | 값 예시 |
|---|---|---|
| `Name` | `String` | `"Damage"` / `"Block"` |
| `IntValue` | `Int32` | `6` |
| `BaseValue` | `Decimal` | `6` |

### 유물/포션 모델
| 프로퍼티 | 타입 | 용도 |
|---|---|---|
| `Title` | `LocString` | 해석 불가 → **`HoverTip.Title` 사용** |
| `HoverTip` | `struct (IHoverTip)` | `Title`과 `Description`이 이미 한국어 평문 |
| `IconBaseName` | `String` | 이미지 파일명 기반 (예: `"cracked_core"`) |

**HoverTip 접근:**
```csharp
var hoverTip = item.GetType().GetProperty("HoverTip")?.GetValue(item);
var title = hoverTip.GetType().GetProperty("Title")?.GetValue(hoverTip)?.ToString();   // "부서진 핵"
var desc  = hoverTip.GetType().GetProperty("Description")?.GetValue(hoverTip)?.ToString(); // BBCode 포함
```

### LocString 처리
`MegaCrit.Sts2.Core.Localization.LocString`은 `.ToString()`이 타입명을 반환한다.  
`.Value` / `.Text` / `.Str` / `.LocalizedText` / `.Localized` / `.Current` 시도해도 모두 null — **현재 해석 불가**.

---

## 5. 완전한 소스 코드

### ModStart.cs

```csharp
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using TestMode1;
using TestMode1.UI;

[ModInitializer("ModInit")]
public static class ModStart
{
    public static void ModInit()
    {
        GD.Print("[Inspector] Initializing...");
        TestMode1.ModSettings.Load();
        var harmony = new Harmony("testmode1.inspector");
        harmony.PatchAll();
        var bootstrapper = new InspectorBootstrapper();
        (Engine.GetMainLoop() as SceneTree)?.Root.CallDeferred("add_child", bootstrapper);
        GD.Print("[Inspector] Initialized.");
    }
}

public partial class InspectorBootstrapper : Node
{
    public override void _Ready()
    {
        var canvas = new CanvasLayer { Layer = 128 };
        AddChild(canvas);

        _ = new ImageCache(TestMode1.ModSettings.Instance.ImageBasePath);

        var tooltip = new ImageTooltip();
        canvas.AddChild(tooltip);

        var overlay = new OverlayPanel();
        var popup   = new PlayerDetailPopup();
        canvas.AddChild(overlay);
        canvas.AddChild(popup);
        popup.Initialize(tooltip);
        popup.Position = new Vector2(600f, 200f);

        var poller = new PlayerDataPoller();
        AddChild(poller);

        poller.PlayerUpdated += snap =>
        {
            overlay.UpdatePlayer(snap);
            popup.RefreshIfShowing(snap);
        };
        poller.PlayerRemoved += id => overlay.RemovePlayer(id);
        overlay.PlayerRowClicked += id =>
        {
            var snap = poller.GetLastSnapshot(id);
            if (snap != null) popup.ShowPlayer(snap);
        };

        GD.Print("[Inspector] UI ready.");
    }
}
```

---

### src/PlayerSnapshot.cs

```csharp
using System.Collections.Generic;

namespace TestMode1
{
    public class PlayerSnapshot
    {
        public string PlayerId   { get; set; }
        public string PlayerName { get; set; }

        public List<string> DeckCardIds { get; set; } = new();
        public List<string> HandCardIds { get; set; } = new();
        public List<string> RelicIds    { get; set; } = new();
        public List<string> PotionIds   { get; set; } = new();

        public int DeckCount   => DeckCardIds.Count;
        public int HandCount   => HandCardIds.Count;
        public int RelicCount  => RelicIds.Count;
        public int PotionCount => PotionIds.Count;

        public Dictionary<string, string> NameToDescription { get; set; } = new();
        public Dictionary<string, string> NameToImageKey    { get; set; } = new();
        public Dictionary<string, string> NameToStats       { get; set; } = new();

        public string Hash { get; private set; } = "";

        public void ComputeHash()
        {
            Hash = string.Join(",", DeckCardIds) + "|" +
                   string.Join(",", HandCardIds) + "|" +
                   string.Join(",", RelicIds)    + "|" +
                   string.Join(",", PotionIds);
        }

        public bool IsDirtyComparedTo(PlayerSnapshot other) =>
            other == null || Hash != other.Hash;
    }
}
```

---

### src/PlayerDataPoller.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace TestMode1
{
    public partial class PlayerDataPoller : Node
    {
        public event Action<PlayerSnapshot> PlayerUpdated;
        public event Action<string> PlayerRemoved;

        private readonly Dictionary<string, PlayerSnapshot> _lastSnapshots = new();
        private string _localPlayerId;
        private bool _loggedCollType;

        public PlayerSnapshot GetLastSnapshot(string playerId) =>
            _lastSnapshots.TryGetValue(playerId, out var s) ? s : null;

        public override void _Ready()
        {
            var timer = new Timer
            {
                WaitTime = ModSettings.Instance.PollIntervalSeconds,
                Autostart = true
            };
            timer.Timeout += OnPollTick;
            AddChild(timer);
        }

        private void OnPollTick()
        {
            try
            {
                if (!IsRunActive()) return;

                var currentIds = GetCurrentPlayerIds();
                GD.Print($"[Inspector] Poll: {currentIds.Count} remote player(s) found");

                foreach (var id in _lastSnapshots.Keys.Except(currentIds).ToList())
                {
                    _lastSnapshots.Remove(id);
                    PlayerRemoved?.Invoke(id);
                }

                foreach (var id in currentIds)
                {
                    var snap = TakeSnapshot(id);
                    if (snap == null) continue;
                    snap.ComputeHash();

                    if (!_lastSnapshots.TryGetValue(id, out var last) || snap.IsDirtyComparedTo(last))
                    {
                        _lastSnapshots[id] = snap;
                        PlayerUpdated?.Invoke(snap);
                    }
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[Inspector] Poll error: {e.Message}\n{e.StackTrace}");
            }
        }

        private static readonly System.Reflection.PropertyInfo _stateProp =
            typeof(RunManager).GetProperty("State",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

        private static IPlayerCollection GetPlayerCollection()
        {
            var rm = RunManager.Instance;
            if (rm == null) return null;
            return _stateProp?.GetValue(rm) as IPlayerCollection;
        }

        private bool IsRunActive()
        {
            try
            {
                var rm = RunManager.Instance;
                if (rm == null) return false;

                var coll = GetPlayerCollection();
                if (coll != null)
                {
                    if (!_loggedCollType)
                    {
                        GD.Print($"[Inspector] Active — state type: {coll.GetType().FullName}");
                        _loggedCollType = true;
                    }
                    return true;
                }

                if (GetSinglePlayerFallback() != null)
                {
                    if (!_loggedCollType)
                    {
                        var state = _stateProp?.GetValue(rm);
                        GD.Print($"[Inspector] Active (solo fallback) — state type: {state?.GetType().FullName}");
                        _loggedCollType = true;
                    }
                    return true;
                }

                return false;
            }
            catch { return false; }
        }

        private List<string> GetCurrentPlayerIds()
        {
            try
            {
                var coll = GetPlayerCollection();
                if (coll != null)
                {
                    var ids = EnumeratePlayers(coll)
                        .Select(GetPlayerId)
                        .Where(id => id != null)
                        .ToList();
                    GD.Print($"[Inspector] Player IDs: [{string.Join(", ", ids)}]");
                    return ids;
                }

                var player = GetSinglePlayerFallback();
                if (player != null)
                {
                    _localPlayerId ??= ResolveLocalPlayerId();
                    var id = GetPlayerId(player) ?? _localPlayerId ?? "local";
                    GD.Print($"[Inspector] Player IDs (solo fallback): [{id}]");
                    return new List<string> { id };
                }

                return new();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[Inspector] GetCurrentPlayerIds: {e.Message}");
                return new();
            }
        }

        private PlayerSnapshot TakeSnapshot(string playerId)
        {
            try
            {
                var coll = GetPlayerCollection();
                if (coll != null)
                {
                    foreach (var player in EnumeratePlayers(coll))
                    {
                        if (GetPlayerId(player) == playerId)
                            return BuildSnapshot(playerId, player);
                    }
                    return null;
                }

                var solo = GetSinglePlayerFallback();
                if (solo != null)
                    return BuildSnapshot(playerId, solo);

                return null;
            }
            catch (Exception e)
            {
                GD.PrintErr($"[Inspector] TakeSnapshot({playerId}): {e.Message}");
                return null;
            }
        }

        private static object GetSinglePlayerFallback()
        {
            try
            {
                var state = _stateProp?.GetValue(RunManager.Instance);
                if (state == null) return null;
                var t = state.GetType();
                return t.GetProperty("Player")?.GetValue(state)
                    ?? t.GetProperty("CurrentPlayer")?.GetValue(state)
                    ?? t.GetProperty("LocalPlayer")?.GetValue(state);
            }
            catch { return null; }
        }

        private static IEnumerable<object> EnumeratePlayers(IPlayerCollection coll)
        {
            var prop = coll.GetType().GetProperty("Players")
                    ?? typeof(IPlayerCollection).GetProperty("Players");

            if (prop == null)
            {
                GD.PrintErr($"[Inspector] Players 프로퍼티를 찾을 수 없음 (type={coll.GetType().FullName})");
                yield break;
            }

            if (prop.GetValue(coll) is System.Collections.IEnumerable list)
                foreach (var p in list) yield return p;
        }

        private static string GetPlayerId(object player) =>
            player?.GetType().GetProperty("NetId")?.GetValue(player)?.ToString();

        private static string ResolveLocalPlayerId()
        {
            try
            {
                var lobby = RunManager.Instance.RunLobby;
                if (lobby == null) return null;
                var localPlayer = lobby.GetType().GetProperty("LocalPlayer")?.GetValue(lobby);
                return localPlayer?.GetType().GetProperty("NetId")?.GetValue(localPlayer)?.ToString();
            }
            catch { return null; }
        }

        private static PlayerSnapshot BuildSnapshot(string playerId, object player)
        {
            var t = player.GetType();
            var snap = new PlayerSnapshot { PlayerId = playerId };

            var character = t.GetProperty("Character")?.GetValue(player);
            if (character != null)
            {
                var ct = character.GetType();
                snap.PlayerName = ResolveLocString(ct.GetProperty("Title")?.GetValue(character))
                               ?? ResolveLocString(ct.GetProperty("Name")?.GetValue(character))
                               ?? playerId;
            }
            else
            {
                snap.PlayerName = playerId;
            }

            var deck    = t.GetProperty("Deck")?.GetValue(player);
            var relics  = t.GetProperty("Relics")?.GetValue(player);
            var potions = t.GetProperty("Potions")?.GetValue(player);
            var hand    = FindHandPile(t.GetProperty("Piles")?.GetValue(player));

            snap.DeckCardIds = ExtractTitles(deck);
            snap.RelicIds    = ExtractTitles(relics);
            snap.PotionIds   = ExtractTitles(potions);
            snap.HandCardIds = ExtractTitles(hand);

            snap.NameToDescription = new Dictionary<string, string>();
            ExtractDescriptions(deck,    snap.NameToDescription);
            ExtractDescriptions(relics,  snap.NameToDescription);
            ExtractDescriptions(potions, snap.NameToDescription);
            ExtractDescriptions(hand,    snap.NameToDescription);

            snap.NameToImageKey = new Dictionary<string, string>();
            ExtractImageKeys(deck,    snap.NameToImageKey);
            ExtractImageKeys(relics,  snap.NameToImageKey);
            ExtractImageKeys(potions, snap.NameToImageKey);
            ExtractImageKeys(hand,    snap.NameToImageKey);

            snap.NameToStats = new Dictionary<string, string>();
            ExtractStats(deck,    snap.NameToStats);
            ExtractStats(relics,  snap.NameToStats);
            ExtractStats(potions, snap.NameToStats);
            ExtractStats(hand,    snap.NameToStats);

            GD.Print($"[Inspector] Snap [{playerId}] name={snap.PlayerName} deck={snap.DeckCount} relics={snap.RelicCount} potions={snap.PotionCount} hand={snap.HandCount}");
            return snap;
        }

        // 카드: Title (String) 직접 사용. 유물/포션: HoverTip.Title (한국어 평문)
        private static string ResolveDisplayName(object item)
        {
            var t = item.GetType();
            var titleVal = t.GetProperty("Title")?.GetValue(item);
            if (titleVal is string s && !string.IsNullOrEmpty(s))
                return s;

            var hoverTip = t.GetProperty("HoverTip")?.GetValue(item);
            if (hoverTip != null)
            {
                var htTitle = hoverTip.GetType().GetProperty("Title")?.GetValue(hoverTip)?.ToString();
                if (!string.IsNullOrEmpty(htTitle)) return htTitle;
            }

            return ResolveLocString(titleVal)
                ?? t.GetProperty("Name")?.GetValue(item)?.ToString()
                ?? t.Name;
        }

        private static List<string> ExtractTitles(object collection)
        {
            var list = new List<string>();
            if (collection == null) return list;
            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;
                list.Add(ResolveDisplayName(item));
            }
            return list;
        }

        private static void ExtractImageKeys(object collection, Dictionary<string, string> dict)
        {
            if (collection == null) return;
            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;
                var t = item.GetType();
                var displayName = ResolveDisplayName(item);
                if (dict.ContainsKey(displayName)) continue;

                var rawKey = t.GetProperty("Key")?.GetValue(item)?.ToString()
                          ?? t.GetProperty("CardModelId")?.GetValue(item)?.ToString()
                          ?? t.GetProperty("Id")?.GetValue(item)?.ToString();
                if (rawKey == null) continue;

                var snakeKey = ImageCache.ToSnakeCase(rawKey);
                if (!string.IsNullOrEmpty(snakeKey))
                {
                    dict[displayName] = snakeKey;
                    GD.Print($"[Inspector] ImageKey: {displayName} → {snakeKey}");
                }
            }
        }

        private static void ExtractDescriptions(object collection, Dictionary<string, string> dict)
        {
            if (collection == null) return;
            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;
                var t = item.GetType();
                var name = ResolveDisplayName(item);
                if (dict.ContainsKey(name)) continue;

                // 1순위: HoverTip.Description (유물/포션 — 한국어 평문 + BBCode 확인됨)
                var hoverTip = t.GetProperty("HoverTip")?.GetValue(item);
                if (hoverTip != null)
                {
                    var htDesc = hoverTip.GetType().GetProperty("Description")?.GetValue(hoverTip)?.ToString();
                    if (!string.IsNullOrEmpty(htDesc))
                    {
                        dict[name] = StripRichText(htDesc);
                        continue;
                    }
                }

                // 2순위: LocString 체인 (카드 — 현재 해석 실패)
                object descObj = t.GetProperty("DynamicDescription")?.GetValue(item)
                              ?? t.GetProperty("Description")?.GetValue(item)
                              ?? t.GetProperty("RawDescription")?.GetValue(item)
                              ?? t.GetProperty("CardDescription")?.GetValue(item);

                if (descObj == null)
                {
                    try { descObj = t.GetMethod("GetDescription")?.Invoke(item, null); }
                    catch { }
                }

                if (descObj == null)
                {
                    var sub = t.GetProperty("CardData")?.GetValue(item)
                           ?? t.GetProperty("Data")?.GetValue(item)
                           ?? t.GetProperty("Model")?.GetValue(item);
                    if (sub != null)
                    {
                        var st = sub.GetType();
                        descObj = st.GetProperty("Description")?.GetValue(sub)
                                ?? st.GetProperty("RawDescription")?.GetValue(sub);
                    }
                }

                var desc = ResolveLocString(descObj) ?? "";
                if (!string.IsNullOrEmpty(desc))
                    dict[name] = StripRichText(desc);
            }
        }

        private static void ExtractStats(object collection, Dictionary<string, string> dict)
        {
            if (collection == null) return;
            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;
                var name = ResolveDisplayName(item);
                if (dict.ContainsKey(name)) continue;

                var stats = BuildStatsString(item);
                if (!string.IsNullOrEmpty(stats))
                {
                    dict[name] = stats;
                    GD.Print($"[Inspector] Stats: {name} -> {stats}");
                }
            }
        }

        private static string BuildStatsString(object item)
        {
            if (item == null) return null;
            var t = item.GetType();

            int? damage = null, block = null, cost = null;

            cost ??= TryGetIntProp(item, "CanonicalEnergyCost", "BaseCost", "BaseEnergyCost", "EnergyCost", "Cost");

            // CanonicalVars: 전투 중에만 non-null. 전투 밖에서는 CanonicalInstance fallback 시도
            var canonVarsSource = t.GetProperty("CanonicalVars")?.GetValue(item);
            if (!(canonVarsSource is System.Collections.IEnumerable))
            {
                var ci = t.GetProperty("CanonicalInstance")?.GetValue(item);
                if (ci != null)
                    canonVarsSource = ci.GetType().GetProperty("CanonicalVars")?.GetValue(ci);
            }

            if (canonVarsSource is System.Collections.IEnumerable canonVars)
            {
                foreach (var v in canonVars)
                {
                    if (v == null) continue;
                    var vt  = v.GetType();
                    var key = vt.GetProperty("Name")?.GetValue(v)?.ToString()
                           ?? vt.GetProperty("Key")?.GetValue(v)?.ToString()
                           ?? vt.GetProperty("Id")?.GetValue(v)?.ToString();
                    var val = TryGetIntProp(v, "IntValue", "BaseValue", "CanonicalValue", "Value", "CurrentValue");
                    if (key == null || val == null) continue;
                    switch (key.ToUpperInvariant())
                    {
                        case "D":
                        case "DAMAGE": damage = val; break;
                        case "B":
                        case "BLOCK":  block  = val; break;
                    }
                }
            }

            // fallback: 직접 프로퍼티 / 메서드 / 서브 오브젝트 / 효과 컬렉션
            damage ??= TryGetIntProp(item, "BaseDamage", "DamageAmount", "Damage");
            block  ??= TryGetIntProp(item, "BaseBlock",  "BlockAmount",  "Block");
            damage ??= TryInvokeIntMethod(item, "GetBaseDamage", "GetDamage");
            block  ??= TryInvokeIntMethod(item, "GetBaseBlock",  "GetBlock");

            var sub = t.GetProperty("CardData")?.GetValue(item)
                   ?? t.GetProperty("Data")?.GetValue(item)
                   ?? t.GetProperty("Model")?.GetValue(item);
            if (sub != null)
            {
                damage ??= TryGetIntProp(sub, "BaseDamage", "DamageAmount", "Damage");
                block  ??= TryGetIntProp(sub, "BaseBlock",  "BlockAmount",  "Block");
                cost   ??= TryGetIntProp(sub, "CanonicalEnergyCost", "BaseCost", "EnergyCost", "Cost");
            }

            var effects = t.GetProperty("Effects")?.GetValue(item)
                       ?? t.GetProperty("CardEffects")?.GetValue(item)
                       ?? t.GetProperty("Actions")?.GetValue(item);
            if (effects is System.Collections.IEnumerable effectList)
            {
                foreach (var eff in effectList)
                {
                    if (eff == null) continue;
                    damage ??= TryGetIntProp(eff, "BaseDamage", "DamageAmount", "Damage", "Amount");
                    block  ??= TryGetIntProp(eff, "BaseBlock",  "BlockAmount",  "Block",  "Amount");
                }
            }

            var parts = new System.Text.StringBuilder();
            if (cost.HasValue && cost.Value != 0)
            {
                var costStr = cost.Value == -1 ? "X" : cost.Value.ToString();
                parts.Append($"코스트: {costStr}  ");
            }
            if (damage.HasValue && damage.Value > 0) parts.Append($"피해: {damage.Value}  ");
            if (block.HasValue  && block.Value  > 0) parts.Append($"방어: {block.Value}");
            return parts.Length > 0 ? parts.ToString().Trim() : null;
        }

        // Piles[] 배열에서 Type="Hand" CardPile 반환 (전투 중에만 존재)
        private static object FindHandPile(object pilesRaw)
        {
            if (pilesRaw is not System.Collections.IEnumerable pilesEnum) return null;
            foreach (var pile in pilesEnum)
            {
                if (pile == null) continue;
                var pileType = pile.GetType().GetProperty("Type")?.GetValue(pile)?.ToString();
                if (pileType == "Hand") return pile;
            }
            return null;
        }

        // CardPile 등 wrapper 타입에서 Cards 프로퍼티를 통해 IEnumerable 반환
        private static System.Collections.IEnumerable UnwrapCollection(object collection)
        {
            if (collection is System.Collections.IEnumerable direct)
                return direct;

            var ct    = collection.GetType();
            var inner = ct.GetProperty("Cards")?.GetValue(collection)
                     ?? ct.GetProperty("Items")?.GetValue(collection)
                     ?? ct.GetProperty("All")?.GetValue(collection)
                     ?? ct.GetProperty("CardModels")?.GetValue(collection);
            return inner as System.Collections.IEnumerable ?? Enumerable.Empty<object>();
        }

        private static int? TryGetIntProp(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var val = t.GetProperty(n,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)?.GetValue(obj);
                if (val is int i) return i;
                if (val != null && int.TryParse(val.ToString(), out var parsed)) return parsed;
            }
            return null;
        }

        private static int? TryInvokeIntMethod(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                try
                {
                    var m = t.GetMethod(n,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (m == null) continue;
                    var result = m.Invoke(obj, null);
                    if (result is int i) return i;
                    if (result != null && int.TryParse(result.ToString(), out var parsed)) return parsed;
                }
                catch { }
            }
            return null;
        }

        private static string StripRichText(string text) =>
            Regex.Replace(text, @"\[[^\]]+\]", "").Trim();

        private static string ResolveLocString(object obj)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            if (t.Namespace?.Contains("Localization") == true || t.Name.Contains("LocString"))
            {
                return t.GetProperty("Value")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Text")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Str")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("LocalizedText")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Localized")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Current")?.GetValue(obj)?.ToString();
            }
            return obj.ToString();
        }
    }
}
```

---

### src/UI/ImageTooltip.cs

```csharp
using Godot;

namespace TestMode1.UI
{
    public partial class ImageTooltip : Control
    {
        private TextureRect _image;
        private Label       _noImageLabel;
        private Label       _statsLabel;
        private Label       _descLabel;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            ZIndex      = 100;
            Visible     = false;

            var panel = new PanelContainer();
            AddChild(panel);

            var vbox = new VBoxContainer();
            panel.AddChild(vbox);

            _image = new TextureRect
            {
                CustomMinimumSize = new Vector2(250, 190),
                ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode       = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            vbox.AddChild(_image);

            _noImageLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Visible             = false
            };
            vbox.AddChild(_noImageLabel);

            _statsLabel = new Label
            {
                AutowrapMode        = TextServer.AutowrapMode.Off,
                CustomMinimumSize   = new Vector2(250, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Visible             = false
            };
            vbox.AddChild(_statsLabel);

            _descLabel = new Label
            {
                AutowrapMode      = TextServer.AutowrapMode.Word,
                CustomMinimumSize = new Vector2(250, 0),
                MaxLinesVisible   = 8,
                Visible           = false
            };
            vbox.AddChild(_descLabel);
        }

        public void ShowAt(Texture2D texture, string name, string description, string stats, Vector2 pos)
        {
            _image.Texture        = texture;
            _image.Visible        = texture != null;
            _noImageLabel.Text    = texture == null ? $"[이미지 없음]\n{name}" : "";
            _noImageLabel.Visible = texture == null;

            _statsLabel.Text    = stats ?? "";
            _statsLabel.Visible = !string.IsNullOrWhiteSpace(stats);

            _descLabel.Text    = description ?? "";
            _descLabel.Visible = !string.IsNullOrWhiteSpace(description);

            Visible  = true;
            Position = ClampToScreen(pos);
        }

        public new void Hide() => Visible = false;

        private Vector2 ClampToScreen(Vector2 pos)
        {
            var vp = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
            return new Vector2(
                Mathf.Clamp(pos.X, 0, vp.X - 270),
                Mathf.Clamp(pos.Y, 0, vp.Y - 420));
        }
    }
}
```

---

### src/UI/HoverLabel.cs

```csharp
using Godot;

namespace TestMode1.UI
{
    public partial class HoverLabel : Label
    {
        private ImageTooltip        _tooltip;
        private string              _imageKey;
        private ImageCache.ItemType _itemType;
        private string              _description;
        private string              _stats;

        public override void _Ready()
        {
            MouseFilter   = MouseFilterEnum.Stop;
            MouseEntered += OnEnter;
            MouseExited  += OnExit;
        }

        public void Configure(string displayText, string imageKey,
                              ImageCache.ItemType type, ImageTooltip tooltip,
                              string description = "", string stats = "")
        {
            Text         = displayText;
            _imageKey    = imageKey;
            _itemType    = type;
            _tooltip     = tooltip;
            _description = description;
            _stats       = stats;
        }

        private void OnEnter()
        {
            var tex = ImageCache.Instance?.GetTexture(_imageKey, _itemType);
            var pos = GetViewport().GetMousePosition() + new Vector2(16, -210);
            _tooltip?.ShowAt(tex, _imageKey, _description, _stats, pos);
        }

        private void OnExit() => _tooltip?.Hide();
    }
}
```

---

### src/UI/CardNodePool.cs

```csharp
using System.Collections.Generic;
using Godot;

namespace TestMode1.UI
{
    public class CardNodePool
    {
        private readonly List<HoverLabel> _available = new();
        private readonly List<HoverLabel> _inUse     = new();
        private readonly Control          _storage;
        private readonly ImageTooltip     _tooltip;

        public CardNodePool(Node parent, int poolSize, ImageTooltip tooltip)
        {
            _tooltip = tooltip;
            _storage = new Control { Visible = false };
            parent.AddChild(_storage);

            for (int i = 0; i < poolSize; i++)
            {
                var lbl = new HoverLabel();
                _storage.AddChild(lbl);
                _available.Add(lbl);
            }
        }

        public HoverLabel Acquire(string displayText, string imageKey,
                                  ImageCache.ItemType itemType, Node target,
                                  string description = "", string stats = "")
        {
            HoverLabel node;
            if (_available.Count > 0)
            {
                node = _available[^1];
                _available.RemoveAt(_available.Count - 1);
            }
            else
            {
                node = new HoverLabel();
                _storage.AddChild(node);
            }

            node.Reparent(target);
            node.Configure(displayText, imageKey, itemType, _tooltip, description, stats);
            node.Visible = true;
            _inUse.Add(node);
            return node;
        }

        public void ReleaseAll()
        {
            foreach (var n in _inUse)
            {
                n.Reparent(_storage);
                n.Visible = false;
                n.Text    = "";
                _available.Add(n);
            }
            _inUse.Clear();
        }
    }
}
```

---

### src/UI/PlayerDetailPopup.cs

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TestMode1.UI
{
    public partial class PlayerDetailPopup : PanelContainer
    {
        private CardNodePool  _pool;
        private string        _currentPlayerId;
        private ImageTooltip  _tooltip;

        private Label         _titleLabel;
        private TabContainer  _tabs;
        private VBoxContainer _deckPage, _handPage, _relicPage, _potionPage;

        private bool _dragging;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(320, 420);
            MouseFilter       = MouseFilterEnum.Stop;
            Visible           = false;

            var vbox = new VBoxContainer();
            AddChild(vbox);

            var titleBar = new HBoxContainer();
            vbox.AddChild(titleBar);

            _titleLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            titleBar.AddChild(_titleLabel);

            var closeBtn = new Button { Text = "X" };
            closeBtn.Pressed += () => Visible = false;
            titleBar.AddChild(closeBtn);

            _tabs = new TabContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 360)
            };
            vbox.AddChild(_tabs);

            (_deckPage,   var ds) = MakePage("덱");
            (_handPage,   var hs) = MakePage("패");
            (_relicPage,  var rs) = MakePage("유물");
            (_potionPage, var ps) = MakePage("포션");

            _tabs.AddChild(ds);
            _tabs.AddChild(hs);
            _tabs.AddChild(rs);
            _tabs.AddChild(ps);
        }

        public void Initialize(ImageTooltip tooltip)
        {
            _tooltip = tooltip;
            _pool    = new CardNodePool(this, 80, tooltip);
        }

        public void ShowPlayer(PlayerSnapshot snap)
        {
            _currentPlayerId = snap.PlayerId;
            _titleLabel.Text = snap.PlayerName;
            RefreshContent(snap);
            ClampToScreen();
            Visible = true;
        }

        public void RefreshIfShowing(PlayerSnapshot snap)
        {
            if (Visible && snap.PlayerId == _currentPlayerId)
                RefreshContent(snap);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) _dragging = true;
                else if (_dragging) _dragging = false;
            }
            else if (ev is InputEventMouseMotion mm && _dragging)
            {
                Position += mm.Relative;
            }
        }

        private void ClampToScreen()
        {
            var viewport = GetViewport();
            if (viewport == null) return;
            var vSize = viewport.GetVisibleRect().Size;
            var w = Mathf.Max(Size.X, CustomMinimumSize.X);
            var h = Mathf.Max(Size.Y, CustomMinimumSize.Y);
            Position = new Vector2(
                Mathf.Clamp(Position.X, 10, vSize.X - w - 10),
                Mathf.Clamp(Position.Y, 10, vSize.Y - h - 10));
        }

        private void RefreshContent(PlayerSnapshot snap)
        {
            _pool?.ReleaseAll();
            if (_pool == null) return;

            var s       = ModSettings.Instance;
            var descs   = snap.NameToDescription;
            var imgKeys = snap.NameToImageKey;
            var stats   = snap.NameToStats;

            if (s.ShowDeck)    PopulateTab(_deckPage,   snap.DeckCardIds, "덱",   0, ImageCache.ItemType.Card,   descs, imgKeys, stats);
            if (s.ShowHand)    PopulateTab(_handPage,   snap.HandCardIds, "패",   1, ImageCache.ItemType.Card,   descs, imgKeys, stats);
            if (s.ShowRelics)  PopulateTab(_relicPage,  snap.RelicIds,    "유물", 2, ImageCache.ItemType.Relic,  descs, imgKeys, stats);
            if (s.ShowPotions) PopulateTab(_potionPage, snap.PotionIds,   "포션", 3, ImageCache.ItemType.Potion, descs, imgKeys, stats);

            _tabs.SetTabHidden(0, !s.ShowDeck);
            _tabs.SetTabHidden(1, !s.ShowHand);
            _tabs.SetTabHidden(2, !s.ShowRelics);
            _tabs.SetTabHidden(3, !s.ShowPotions);

            SelectFirstNonEmptyTab(snap);
        }

        private void PopulateTab(VBoxContainer page, List<string> ids,
                                  string title, int tabIdx,
                                  ImageCache.ItemType itemType,
                                  Dictionary<string, string> descriptions,
                                  Dictionary<string, string> imageKeys,
                                  Dictionary<string, string> statsMap)
        {
            var groups = ids
                .GroupBy(x => x)
                .OrderBy(g => g.Key)
                .Select(g => (name: g.Key, count: g.Count()));

            foreach (var (name, count) in groups)
            {
                var display = count > 1 ? $"{name}  x{count}" : name;
                var desc    = descriptions.GetValueOrDefault(name, "");
                var imgKey  = imageKeys.GetValueOrDefault(name, ImageCache.ToSnakeCase(name));
                var st      = statsMap.GetValueOrDefault(name, "");
                _pool.Acquire(display, imgKey, itemType, page, desc, st);
            }

            _tabs.SetTabTitle(tabIdx, $"{title} ({ids.Count})");
        }

        private void SelectFirstNonEmptyTab(PlayerSnapshot snap)
        {
            int[] counts = { snap.DeckCount, snap.HandCount, snap.RelicCount, snap.PotionCount };
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0 && !_tabs.IsTabHidden(i))
                {
                    _tabs.CurrentTab = i;
                    return;
                }
            }
        }

        private static (VBoxContainer vbox, ScrollContainer scroll) MakePage(string name)
        {
            var scroll = new ScrollContainer
            {
                Name                 = name,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsVertical    = SizeFlags.ExpandFill
            };
            var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill };
            scroll.AddChild(vbox);
            return (vbox, scroll);
        }
    }
}
```

---

### src/ModSettings.cs / src/ImageCache.cs / src/UI/OverlayPanel.cs

변경 없음. 이전 ai-handoff.md의 코드와 동일.

---

## 6. 이미지 에셋 구조

```
C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2\
├─ images/
│   ├─ relics/              # PNG (256×256), 파일명 예: cracked_core.png
│   ├─ potions/             # PNG, 파일명 예: fire_potion.png
│   └─ atlases/
│       ├─ card_atlas_0.png
│       ├─ card_atlas_1.png
│       ├─ card_atlas_2.png
│       └─ card_atlas.sprites/   # .tres (캐릭터별 폴더)
│           ├─ ironclad/ ├─ defect/ ├─ silent/ └─ ...
└─ localization/eng/cards.json
```

---

## 7. 해결한 핵심 문제들

| 문제 | 원인 | 해결 |
|------|------|------|
| 유물/포션 이름이 영문 | `Title`이 `LocString` — 해석 불가 | `HoverTip.Title` (struct 필드, 한국어 평문) 사용 |
| 유물/포션 설명 없음 | `Description`이 `LocString` — 해석 불가 | `HoverTip.Description` 사용 (BBCode → `StripRichText`) |
| 카드 코스트 미표시 | `BaseCost` 등 프로퍼티 없음 | `CanonicalEnergyCost (Int32)` 직접 접근 |
| 손패 hand=0 | `Piles`가 `CardPile[]` 배열인데 `.Hand` 프로퍼티로 접근 시도 | `FindHandPile()` — `Piles[].Type == "Hand"` 필터링 |
| DeckModel unwrap | `player.Deck`이 직접 `IEnumerable` 아님 | `UnwrapCollection()` — `Cards`→`Items`→`All`→`CardModels` 탐색 |
| 이미지 로드 실패 | `Image.LoadFromFile()`이 Godot 모드에서 Windows 경로 불가 | `File.ReadAllBytes()` + `LoadPngFromBuffer()` |
| 한국어 이름 → 이미지 키 불가 | `ToSnakeCase("타격")` = `""` | `NameToImageKey` 딕셔너리 — 내부 `.Key` 프로퍼티에서 영어 ID 추출 |

---

## 8. 미해결 사항 (다음 세션 작업)

### 1. 피해/방어 스탯 미표시 (최우선)

**현상**: `[Inspector] Stats: 타격 -> 코스트: 1` — 피해/방어 없음

**원인 분석**:
- `CanonicalVars (IEnumerable<DynamicVar>)`는 **전투 중에만 non-null**
- 전투 밖에서 폴링 시 null → 코스트만 표시됨
- `CanonicalInstance` fallback 추가됨 (`BuildStatsString`에서) — **아직 미검증**

**확인 필요**:
- 전투 진입 후 `[Inspector] Stats: 타격 -> 코스트: 1  피해: 6` 출력되는지
- `CanonicalInstance.CanonicalVars`가 전투 밖에서도 non-null인지

**DynamicVar 구조 (덤프 확인됨)**:
```
Name (String)     = "Damage" 또는 "Block"
IntValue (Int32)  = 6
BaseValue (Decimal) = 6
```

### 2. 손패 카드 전투 진입 시 표시 여부 미검증

`FindHandPile()`이 `Piles[].Type == "Hand"` 로 찾는 로직 추가됨. 전투 진입 후 `hand=N` (N>0) 확인 필요.

### 3. 카드 설명 미표시

`Description (LocString)`은 현재 해석 불가. `.Value`/`.Text`/`.Str` 모두 null.  
STS2 LocString 해석 방법을 찾지 못함 — 향후 게임 업데이트나 다른 접근법 필요.

---

## 9. 빌드 & 배포

```bash
cd "C:\AI_Folder\ClaudeCode\SlaytheSpire2\testmode1"
dotnet build testmode1.csproj
# 성공: 경고 0, 오류 0
# 출력: testmode1.dll → C:\Program Files (x86)\Steam\...\mods\testmode1\
```

---

## 10. 진단 로그 키워드

```
[Inspector] Snap [1] name=디펙트 deck=15 relics=3 potions=0 hand=5  ← hand>0 이면 손패 성공
[Inspector] Stats: 타격 -> 코스트: 1  피해: 6                         ← 피해/방어 성공
[Inspector] Stats: 타격 -> 코스트: 1                                  ← 전투 밖 (피해/방어 없음)
[Inspector] ImageKey: 부서진 핵 → relic_cracked_core                  ← 유물 한국어 이름 성공
```
