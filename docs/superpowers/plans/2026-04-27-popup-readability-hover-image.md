# Popup Readability + Card Image Hover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 팝업 가독성 개선(중복 카드 묶기, VBoxContainer) + 아이템 위에 마우스를 올리면 게임 이미지를 보여주는 툴팁 추가.

**Architecture:** `ImageCache`가 텍스처 로드/캐시, `HoverLabel`이 Label에 마우스 이벤트 추가, `ImageTooltip`이 이미지를 렌더링. `CardNodePool`이 Label→HoverLabel로 교체되고, `PlayerDetailPopup`이 FlowContainer→VBoxContainer + 중복 묶기로 레이아웃 개선.

**Tech Stack:** Godot 4 C#, System.IO, System.Text.RegularExpressions, Image.GetRegion (atlas crop)

---

## 파일 구조

| 파일 | 작업 | 역할 |
|------|------|------|
| `src/ImageCache.cs` | 신규 | 텍스처 로드 + 캐시, .tres 파싱, snake_case 변환 |
| `src/UI/ImageTooltip.cs` | 신규 | 마우스 오버 이미지 툴팁 패널 |
| `src/UI/HoverLabel.cs` | 신규 | 마우스 이벤트 있는 Label 노드 |
| `src/UI/CardNodePool.cs` | 수정 | Label→HoverLabel, Acquire 시그니처 변경 |
| `src/UI/PlayerDetailPopup.cs` | 수정 | FlowContainer→VBoxContainer, 중복 묶기, Initialize() |
| `src/ModSettings.cs` | 수정 | ImageBasePath 설정 추가 |
| `ModStart.cs` | 수정 | ImageCache + ImageTooltip 생성 및 연결 |

---

## Task 1: ModSettings에 ImageBasePath 추가

**Files:**
- Modify: `src/ModSettings.cs`

- [ ] **Step 1: ImageBasePath 프로퍼티 추가**

`src/ModSettings.cs`에서 `PollIntervalSeconds` 프로퍼티 바로 아래에 추가:

```csharp
public float PollIntervalSeconds { get; private set; } = 1.5f;
public string ImageBasePath { get; private set; } =
    @"C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2";
```

- [ ] **Step 2: Load()에 읽기 추가**

`settings.PollIntervalSeconds = ...` 라인 바로 뒤에:

```csharp
settings.PollIntervalSeconds = (float)cfg.GetValue("performance", "poll_interval_seconds", 1.5f);
settings.ImageBasePath = (string)cfg.GetValue("assets", "image_base_path",
    @"C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2");
```

- [ ] **Step 3: 빌드 확인**

```bash
cd "C:\AI_Folder\ClaudeCode\SlaytheSpire2\testmode1"
dotnet build testmode1.csproj
```
Expected: 경고 0개, 오류 0개

---

## Task 2: ImageCache.cs 생성

**Files:**
- Create: `src/ImageCache.cs`

- [ ] **Step 1: 파일 생성**

`src/ImageCache.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace TestMode1
{
    public class ImageCache
    {
        public enum ItemType { Card, Relic, Potion }

        public static ImageCache Instance { get; private set; }

        private readonly string _basePath;
        private readonly Dictionary<string, Texture2D> _texCache = new();
        private readonly Dictionary<string, Image> _atlasCache  = new();

        private static readonly string[] CardCharFolders =
        {
            "ironclad", "silent", "defect", "necrobinder", "regent",
            "colorless", "curse", "status", "event", "token", "quest"
        };

        public ImageCache(string basePath)
        {
            _basePath = basePath;
            Instance  = this;
        }

        public Texture2D GetTexture(string displayName, ItemType type)
        {
            var key = $"{type}:{displayName}";
            if (_texCache.TryGetValue(key, out var cached)) return cached;

            var tex = type switch
            {
                ItemType.Relic  => LoadDirect(Path.Combine("images", "relics",  ToSnakeCase(displayName) + ".png")),
                ItemType.Potion => LoadDirect(Path.Combine("images", "potions", ToSnakeCase(displayName) + ".png")),
                ItemType.Card   => LoadCard(displayName),
                _               => null
            };

            if (tex != null) _texCache[key] = tex;
            return tex;
        }

        // "Bash" → "bash", "Ashen Strike" → "ashen_strike"
        public static string ToSnakeCase(string name) =>
            Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

        // ── 내부 로드 메서드 ─────────────────────────────────────

        private Texture2D LoadDirect(string relativePath)
        {
            var full = Path.Combine(_basePath, relativePath);
            if (!File.Exists(full)) return null;
            var img = Image.LoadFromFile(full);
            return img != null ? ImageTexture.CreateFromImage(img) : null;
        }

        private Texture2D LoadCard(string displayName)
        {
            var snake = ToSnakeCase(displayName);
            var spritesRoot = Path.Combine(_basePath, "images", "atlases", "card_atlas.sprites");

            foreach (var ch in CardCharFolders)
            {
                var tresPath = Path.Combine(spritesRoot, ch, snake + ".tres");
                if (!File.Exists(tresPath)) continue;

                var content = File.ReadAllText(tresPath);
                if (!ParseAtlasRegion(content, out var atlasFile, out var region)) continue;

                var atlasPath = Path.Combine(_basePath, "images", "atlases", atlasFile);
                var atlas     = GetOrLoadAtlas(atlasPath);
                if (atlas == null) continue;

                var cropped = atlas.GetRegion(region);
                return ImageTexture.CreateFromImage(cropped);
            }
            return null;
        }

        private Image GetOrLoadAtlas(string path)
        {
            if (_atlasCache.TryGetValue(path, out var cached)) return cached;
            if (!File.Exists(path)) return null;
            var img = Image.LoadFromFile(path);
            if (img != null) _atlasCache[path] = img;
            return img;
        }

        // path="res://images/atlases/card_atlas_1.png" + region = Rect2(1, 385, 250, 190)
        private static bool ParseAtlasRegion(string content, out string atlasFile, out Rect2I region)
        {
            atlasFile = null;
            region    = default;

            var pathMatch = Regex.Match(content, @"path=""res://images/atlases/([^""]+)""");
            if (!pathMatch.Success) return false;
            atlasFile = pathMatch.Groups[1].Value;

            var rectMatch = Regex.Match(content, @"region\s*=\s*Rect2\(\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\s*\)");
            if (!rectMatch.Success) return false;
            region = new Rect2I(
                int.Parse(rectMatch.Groups[1].Value),
                int.Parse(rectMatch.Groups[2].Value),
                int.Parse(rectMatch.Groups[3].Value),
                int.Parse(rectMatch.Groups[4].Value));
            return true;
        }
    }
}
```

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build testmode1.csproj
```
Expected: 경고 0개, 오류 0개

---

## Task 3: ImageTooltip.cs 생성

**Files:**
- Create: `src/UI/ImageTooltip.cs`

- [ ] **Step 1: 파일 생성**

`src/UI/ImageTooltip.cs`:

```csharp
using Godot;

namespace TestMode1.UI
{
    public partial class ImageTooltip : Control
    {
        private TextureRect _image;
        private Label       _fallback;

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

            _fallback = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            vbox.AddChild(_fallback);
        }

        public void ShowAt(Texture2D texture, string name, Vector2 pos)
        {
            _image.Texture    = texture;
            _fallback.Text    = texture != null ? "" : $"[이미지 없음]\n{name}";
            _fallback.Visible = texture == null;
            Visible           = true;
            Position          = ClampToScreen(pos);
        }

        public new void Hide() => Visible = false;

        private Vector2 ClampToScreen(Vector2 pos)
        {
            var vp = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
            return new Vector2(
                Mathf.Clamp(pos.X, 0, vp.X - 270),
                Mathf.Clamp(pos.Y, 0, vp.Y - 220));
        }
    }
}
```

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build testmode1.csproj
```
Expected: 경고 0개, 오류 0개

---

## Task 4: HoverLabel.cs 생성

**Files:**
- Create: `src/UI/HoverLabel.cs`

- [ ] **Step 1: 파일 생성**

`src/UI/HoverLabel.cs`:

```csharp
using Godot;

namespace TestMode1.UI
{
    public partial class HoverLabel : Label
    {
        private ImageTooltip         _tooltip;
        private string               _imageKey;
        private ImageCache.ItemType  _itemType;

        public override void _Ready()
        {
            MouseFilter   = MouseFilterEnum.Stop;
            MouseEntered += OnEnter;
            MouseExited  += OnExit;
        }

        // 풀에서 꺼낼 때마다 호출해서 데이터 갱신
        public void Configure(string displayText, string imageKey,
                              ImageCache.ItemType type, ImageTooltip tooltip)
        {
            Text      = displayText;
            _imageKey = imageKey;
            _itemType = type;
            _tooltip  = tooltip;
        }

        private void OnEnter()
        {
            var tex = ImageCache.Instance?.GetTexture(_imageKey, _itemType);
            var pos = GetViewport().GetMousePosition() + new Vector2(16, -210);
            _tooltip?.ShowAt(tex, _imageKey, pos);
        }

        private void OnExit() => _tooltip?.Hide();
    }
}
```

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build testmode1.csproj
```
Expected: 경고 0개, 오류 0개

---

## Task 5: CardNodePool — Label → HoverLabel

**Files:**
- Modify: `src/UI/CardNodePool.cs`

- [ ] **Step 1: 파일 전체 교체**

`src/UI/CardNodePool.cs` 전체를 다음으로 교체:

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
                                  ImageCache.ItemType itemType, Node target)
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
            node.Configure(displayText, imageKey, itemType, _tooltip);
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

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build testmode1.csproj
```
Expected: 오류 없음 (PlayerDetailPopup에서 Acquire 시그니처 불일치 빌드 오류 발생 예정 → 다음 Task에서 수정)

---

## Task 6: PlayerDetailPopup — 레이아웃 개선 + HoverLabel 연결

**Files:**
- Modify: `src/UI/PlayerDetailPopup.cs`

- [ ] **Step 1: 파일 전체 교체**

`src/UI/PlayerDetailPopup.cs` 전체를 다음으로 교체:

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TestMode1.UI
{
    public partial class PlayerDetailPopup : PanelContainer
    {
        private CardNodePool _pool;
        private string       _currentPlayerId;
        private ImageTooltip _tooltip;

        private Label        _titleLabel;
        private TabContainer _tabs;
        private VBoxContainer _deckPage, _handPage, _relicPage, _potionPage;

        private bool _dragging;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(320, 420);
            MouseFilter       = MouseFilterEnum.Stop;
            Visible           = false;

            var vbox = new VBoxContainer();
            AddChild(vbox);

            // 타이틀 바
            var titleBar = new HBoxContainer();
            vbox.AddChild(titleBar);

            _titleLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            titleBar.AddChild(_titleLabel);

            var closeBtn = new Button { Text = "X" };
            closeBtn.Pressed += () => Visible = false;
            titleBar.AddChild(closeBtn);

            // 탭
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

        // ModStart에서 AddChild 후 호출
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

        // 드래그
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

            var s = ModSettings.Instance;
            if (s.ShowDeck)    PopulateTab(_deckPage,   snap.DeckCardIds, "덱",   0, ImageCache.ItemType.Card);
            if (s.ShowHand)    PopulateTab(_handPage,   snap.HandCardIds, "패",   1, ImageCache.ItemType.Card);
            if (s.ShowRelics)  PopulateTab(_relicPage,  snap.RelicIds,    "유물", 2, ImageCache.ItemType.Relic);
            if (s.ShowPotions) PopulateTab(_potionPage, snap.PotionIds,   "포션", 3, ImageCache.ItemType.Potion);

            _tabs.SetTabHidden(0, !s.ShowDeck);
            _tabs.SetTabHidden(1, !s.ShowHand);
            _tabs.SetTabHidden(2, !s.ShowRelics);
            _tabs.SetTabHidden(3, !s.ShowPotions);

            SelectFirstNonEmptyTab(snap);
        }

        private void PopulateTab(VBoxContainer page, List<string> ids,
                                  string title, int tabIdx, ImageCache.ItemType itemType)
        {
            // 중복 묶기: {name: count}, 알파벳 정렬
            var groups = ids
                .GroupBy(x => x)
                .OrderBy(g => g.Key)
                .Select(g => (name: g.Key, count: g.Count()));

            foreach (var (name, count) in groups)
            {
                var display = count > 1 ? $"{name}  ×{count}" : name;
                _pool.Acquire(display, name, itemType, page);
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

        // ScrollContainer 안에 VBoxContainer를 담아 탭 페이지로 반환
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

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build testmode1.csproj
```
Expected: ModStart.cs에서 오류 발생 예정 (Initialize 미호출) → 다음 Task에서 수정

---

## Task 7: ModStart — ImageCache + ImageTooltip 연결

**Files:**
- Modify: `ModStart.cs`

- [ ] **Step 1: InspectorBootstrapper._Ready() 수정**

`ModStart.cs`의 `InspectorBootstrapper._Ready()`에서 아래와 같이 수정:

```csharp
public override void _Ready()
{
    var canvas = new CanvasLayer { Layer = 128 };
    AddChild(canvas);

    // ImageCache 초기화 (ModSettings는 이미 로드됨)
    _ = new ImageCache(TestMode1.ModSettings.Instance.ImageBasePath);

    var tooltip = new ImageTooltip();
    canvas.AddChild(tooltip);

    var overlay = new OverlayPanel();
    var popup   = new PlayerDetailPopup();
    canvas.AddChild(overlay);
    canvas.AddChild(popup);
    popup.Initialize(tooltip);          // ← 추가

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
```

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build testmode1.csproj
```
Expected: 경고 0개, 오류 0개, DLL 모드 폴더 복사 완료

---

## Task 8: 최종 검증

- [ ] **게임 실행 후 확인 항목**

1. **팝업 레이아웃**: 카드 목록이 세로로 한 줄씩 표시되는지
2. **중복 묶기**: 같은 카드가 여러 장이면 `Strike  ×3` 형식으로 표시되는지
3. **첫 탭 자동 선택**: 팝업 열릴 때 데이터 있는 탭이 먼저 선택되는지
4. **유물 호버**: 마우스를 유물 이름 위에 올리면 256×256 이미지가 뜨는지
5. **포션 호버**: 마우스를 포션 이름 위에 올리면 256×256 이미지가 뜨는지
6. **카드 호버**: 마우스를 카드 이름 위에 올리면 250×190 카드 아트가 뜨는지
7. **이미지 없는 경우**: `[이미지 없음]\n{name}` 폴백 텍스트가 표시되는지

- [ ] **이미지 로드 실패 시 진단**

카드 이미지가 안 뜨면 Output 로그에 `[Inspector]` 관련 오류 없는지 확인.  
경로 문제라면 `ModSettings.Instance.ImageBasePath` 기본값 확인:
```
C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2
```
