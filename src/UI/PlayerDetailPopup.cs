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
