using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TestMode1.UI
{
    public partial class PlayerDetailPopup : PanelContainer
    {
        public const float MinW = 280f;
        public const float MinH = 280f;

        private CardNodePool  _pool;
        private string        _currentPlayerId;
        private ImageTooltip  _tooltip;
        private PlayerSnapshot _lastSnap;

        private Label         _titleLabel;
        private TabContainer  _tabs;
        private VBoxContainer _deckPage, _handPage, _relicPage, _potionPage, _buffPage;

        private bool _dragging;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(MinW, MinH);
            Size              = new Vector2(420, 480);
            MouseFilter       = MouseFilterEnum.Stop;
            Visible           = false;

            AddThemeStyleboxOverride("panel",
                InspectorTheme.MakeBox(InspectorTheme.BgPanel, InspectorTheme.Border, 6, 10, 8));

            var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            AddChild(vbox);

            var titleBar = new HBoxContainer();
            vbox.AddChild(titleBar);

            _titleLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _titleLabel.AddThemeColorOverride("font_color", InspectorTheme.Gold);
            _titleLabel.AddThemeFontSizeOverride("font_size", 16);
            titleBar.AddChild(_titleLabel);

            var closeBtn = new Button { Text = "✕", Flat = true };
            closeBtn.AddThemeColorOverride("font_color",         InspectorTheme.TextDim);
            closeBtn.AddThemeColorOverride("font_color_hover",   InspectorTheme.CloseHover);
            closeBtn.AddThemeColorOverride("font_color_pressed", InspectorTheme.CloseHover);
            closeBtn.AddThemeStyleboxOverride("normal",  InspectorTheme.MakeTransparent());
            closeBtn.AddThemeStyleboxOverride("hover",   InspectorTheme.MakeTransparent());
            closeBtn.AddThemeStyleboxOverride("pressed", InspectorTheme.MakeTransparent());
            closeBtn.Pressed += () => Visible = false;
            titleBar.AddChild(closeBtn);

            _tabs = new TabContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 100)
            };
            vbox.AddChild(_tabs);

            var tabPanelStyle = new StyleBoxFlat { DrawCenter = false };
            tabPanelStyle.ContentMarginLeft   = 0;
            tabPanelStyle.ContentMarginRight  = 0;
            tabPanelStyle.ContentMarginTop    = 0;
            tabPanelStyle.ContentMarginBottom = 0;
            _tabs.AddThemeStyleboxOverride("panel", tabPanelStyle);

            (_deckPage,   var ds)  = MakePage("드로우 덱", BuildHeader(("카드명", true), ("개수",   false)));
            (_handPage,   var hs)  = MakePage("패",        BuildHeader(("카드명", true), ("코스트", false), ("피해", false), ("방어", false)));
            (_relicPage,  var rs)  = MakePage("유물",      BuildHeader(("유물명", true)));
            (_potionPage, var ps)  = MakePage("포션",      BuildHeader(("포션명", true)));
            (_buffPage,   var bfs) = MakePage("버프",      BuildHeader(("버프명", true), ("수치",   false)));

            _tabs.AddChild(ds);
            _tabs.AddChild(hs);
            _tabs.AddChild(rs);
            _tabs.AddChild(ps);
            _tabs.AddChild(bfs);
        }

        public void Initialize(ImageTooltip tooltip)
        {
            _tooltip = tooltip;
            _pool    = new CardNodePool(this, 80, tooltip);
        }

        public void ShowPlayer(PlayerSnapshot snap)
        {
            _lastSnap        = snap;
            _currentPlayerId = snap.PlayerId;
            _titleLabel.Text = snap.PlayerName;
            RefreshContent(snap, preserveTab: false);
            ClampToScreen();
            Visible = true;
        }

        public void RefreshIfShowing(PlayerSnapshot snap)
        {
            if (Visible && snap.PlayerId == _currentPlayerId)
            {
                _lastSnap = snap;
                RefreshContent(snap);
            }
        }

        public void ForceRefreshCurrent()
        {
            if (Visible && _lastSnap != null)
                RefreshContent(_lastSnap);
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

        public void SetPopupSize(Vector2 size)
        {
            Size = size;
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

        private void RefreshContent(PlayerSnapshot snap, bool preserveTab = true)
        {
            var savedTab = preserveTab ? _tabs.CurrentTab : -1;
            _pool?.ReleaseAll();
            if (_pool == null) return;

            var s       = ModSettings.Instance;
            var descs   = snap.NameToDescription;
            var imgKeys = snap.NameToImageKey;
            var stats   = snap.NameToStats;

            if (s.ShowDeck)    PopulateTab(_deckPage,   snap.DeckCardIds, "드로우 덱", 0, ImageCache.ItemType.Card,   descs, imgKeys, stats);
            if (s.ShowHand)
            {
                if (snap.HandCardEntries.Count > 0)
                    PopulateHandTab(_handPage, snap.HandCardEntries, "패", 1, ImageCache.ItemType.Card, descs, imgKeys, stats);
                else
                    PopulateTab(_handPage, snap.HandCardIds, "패", 1, ImageCache.ItemType.Card, descs, imgKeys, stats);
            }
            if (s.ShowRelics)  PopulateTab(_relicPage,  snap.RelicIds,  "유물", 2, ImageCache.ItemType.Relic,  descs, imgKeys, stats);
            if (s.ShowPotions) PopulateTab(_potionPage, snap.PotionIds, "포션", 3, ImageCache.ItemType.Potion, descs, imgKeys, stats);
            if (s.ShowBuffs)   PopulateBuffTab(_buffPage, snap.Buffs, "버프", 4);

            _tabs.SetTabHidden(0, !s.ShowDeck);
            _tabs.SetTabHidden(1, !s.ShowHand);
            _tabs.SetTabHidden(2, !s.ShowRelics);
            _tabs.SetTabHidden(3, !s.ShowPotions);
            _tabs.SetTabHidden(4, !s.ShowBuffs);

            if (savedTab >= 0 && savedTab < _tabs.GetTabCount() && !_tabs.IsTabHidden(savedTab))
                _tabs.CurrentTab = savedTab;
            else
                SelectFirstNonEmptyTab(snap);
        }

        private void PopulateHandTab(VBoxContainer page,
                                     List<HandCardEntry> entries,
                                     string title, int tabIdx,
                                     ImageCache.ItemType itemType,
                                     Dictionary<string, string> descriptions,
                                     Dictionary<string, string> imageKeys,
                                     Dictionary<string, string> statsMap)
        {
            foreach (var entry in entries)
            {
                var cost = entry.Cost.HasValue
                    ? (entry.Cost.Value == -1 ? "X" : entry.Cost.Value.ToString())
                    : "—";
                var dmg = (entry.EffectiveDamage.HasValue && entry.EffectiveDamage.Value > 0)
                    ? entry.EffectiveDamage.Value.ToString() : "—";
                var blk = (entry.EffectiveBlock.HasValue && entry.EffectiveBlock.Value > 0)
                    ? entry.EffectiveBlock.Value.ToString() : "—";

                var desc   = descriptions.GetValueOrDefault(entry.Name, "");
                var imgKey = imageKeys.GetValueOrDefault(entry.Name, ImageCache.ToSnakeCase(entry.Name));
                var st     = statsMap.GetValueOrDefault(entry.Name, "");
                _pool.AcquireRow(entry.Name, cost, dmg, blk, imgKey, itemType, page, desc, st);
            }
            _tabs.SetTabTitle(tabIdx, $"{title} ({entries.Count})");
        }

        private void PopulateBuffTab(VBoxContainer page, List<BuffEntry> buffs,
                                     string title, int tabIdx)
        {
            foreach (var buff in buffs)
            {
                var sign      = (!buff.IsDebuff && buff.Amount > 0) ? "+" : "";
                var amountStr = buff.Amount != 0 ? $"{sign}{buff.Amount}" : "";
                _pool.AcquireRow(buff.Name, amountStr, "", "", "", ImageCache.ItemType.Relic, page, "", "");
            }
            _tabs.SetTabTitle(tabIdx, $"{title} ({buffs.Count})");
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
                var countStr = count > 1 ? $"x{count}" : "";
                var desc     = descriptions.GetValueOrDefault(name, "");
                var imgKey   = imageKeys.GetValueOrDefault(name, ImageCache.ToSnakeCase(name));
                var st       = statsMap.GetValueOrDefault(name, "");
                _pool.AcquireRow(name, countStr, "", "", imgKey, itemType, page, desc, st);
            }

            _tabs.SetTabTitle(tabIdx, $"{title} ({ids.Count})");
        }

        private void SelectFirstNonEmptyTab(PlayerSnapshot snap)
        {
            int[] counts = { snap.DeckCount, snap.HandCount, snap.RelicCount, snap.PotionCount, snap.BuffCount };
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0 && !_tabs.IsTabHidden(i))
                {
                    _tabs.CurrentTab = i;
                    return;
                }
            }
        }

        private static HBoxContainer BuildHeader(params (string text, bool expand)[] cols)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize   = new Vector2(0, 28),
            };
            row.AddThemeConstantOverride("separation", 4);

            foreach (var (text, expand) in cols)
            {
                var lbl = new Label
                {
                    Text              = text,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                lbl.AddThemeColorOverride("font_color", InspectorTheme.TextDim);

                if (expand)
                {
                    lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                }
                else
                {
                    lbl.CustomMinimumSize   = new Vector2(InspectorTheme.ColSmall, 0);
                    lbl.HorizontalAlignment = HorizontalAlignment.Right;
                }
                row.AddChild(lbl);
            }
            return row;
        }

        private static (VBoxContainer vbox, ScrollContainer scroll) MakePage(string name, HBoxContainer header)
        {
            var scroll = new ScrollContainer
            {
                Name                 = name,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsHorizontal  = SizeFlags.ExpandFill,
                SizeFlagsVertical    = SizeFlags.ExpandFill
            };

            var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddChild(outer);

            outer.AddChild(header);

            var sepStyle = new StyleBoxFlat { BgColor = InspectorTheme.Separator };
            sepStyle.ContentMarginTop    = 1;
            sepStyle.ContentMarginBottom = 4;
            var sep = new HSeparator();
            sep.AddThemeStyleboxOverride("separator", sepStyle);
            outer.AddChild(sep);

            var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            vbox.AddThemeConstantOverride("separation", 6);
            outer.AddChild(vbox);

            return (vbox, scroll);
        }
    }
}
