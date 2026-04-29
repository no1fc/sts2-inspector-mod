using System.Collections.Generic;
using Godot;

namespace TestMode1.UI
{
    public partial class OverlayPanel : PanelContainer
    {
        [Signal]
        public delegate void PlayerRowClickedEventHandler(string playerId);

        [Signal]
        public delegate void ToggleChangedEventHandler();

        private VBoxContainer _playerRows;
        private ScrollContainer _scroll;
        private Button _btnCollapse;
        private CheckButton _btnDeck, _btnHand, _btnRelics, _btnPotions, _btnBuffs;
        private bool _collapsed = false;

        private readonly Dictionary<string, HBoxContainer> _rowsByPlayerId = new();
        private readonly Dictionary<string, Label[]> _countLabelsByPlayerId = new();
        private readonly Dictionary<string, PlayerSnapshot> _lastSnaps = new();

        public override void _Ready()
        {
            var settings = ModSettings.Instance;

            Position = new Vector2(settings.PanelX, settings.PanelY);
            MouseFilter = MouseFilterEnum.Stop;

            var root = new VBoxContainer();
            AddChild(root);

            // Toggle bar
            var toggleBar = new HBoxContainer();
            root.AddChild(toggleBar);

            _btnCollapse = new Button { Text = "─" };
            _btnCollapse.Pressed += OnCollapsePressed;
            toggleBar.AddChild(_btnCollapse);

            _btnDeck    = MakeToggle("덱",   settings.ShowDeck,    () => { ModSettings.Instance.SetToggle("deck",    _btnDeck.ButtonPressed);    RefreshAllRows(); });
            _btnHand    = MakeToggle("패",   settings.ShowHand,    () => { ModSettings.Instance.SetToggle("hand",    _btnHand.ButtonPressed);    RefreshAllRows(); });
            _btnRelics  = MakeToggle("유물", settings.ShowRelics,  () => { ModSettings.Instance.SetToggle("relics",  _btnRelics.ButtonPressed);  RefreshAllRows(); });
            _btnPotions = MakeToggle("포션", settings.ShowPotions, () => { ModSettings.Instance.SetToggle("potions", _btnPotions.ButtonPressed); RefreshAllRows(); });

            _btnBuffs = MakeToggle("버프", settings.ShowBuffs,
                () => { ModSettings.Instance.SetToggle("buffs", _btnBuffs.ButtonPressed); RefreshAllRows(); });

            toggleBar.AddChild(_btnDeck);
            toggleBar.AddChild(_btnHand);
            toggleBar.AddChild(_btnRelics);
            toggleBar.AddChild(_btnPotions);
            toggleBar.AddChild(_btnBuffs);

            _scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, 400),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            root.AddChild(_scroll);

            _playerRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill };
            _scroll.AddChild(_playerRows);
        }

        public void UpdatePlayer(PlayerSnapshot snap)
        {
            _lastSnaps[snap.PlayerId] = snap;
            if (!_rowsByPlayerId.ContainsKey(snap.PlayerId))
                CreateRow(snap);
            RefreshRow(snap.PlayerId, snap);
        }

        public void RemovePlayer(string playerId)
        {
            _lastSnaps.Remove(playerId);
            if (!_rowsByPlayerId.TryGetValue(playerId, out var row)) return;
            row.QueueFree();
            _rowsByPlayerId.Remove(playerId);
            _countLabelsByPlayerId.Remove(playerId);
        }

        // Drag support
        private bool _dragging;

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _dragging = true;
                }
                else if (_dragging)
                {
                    _dragging = false;
                    ModSettings.Instance.SetPanelPosition(Position.X, Position.Y);
                }
            }
            else if (ev is InputEventMouseMotion mm && _dragging)
            {
                Position += mm.Relative;
            }
        }

        private HBoxContainer CreateRow(PlayerSnapshot snap)
        {
            var row = new HBoxContainer();

            var displayText = snap.CharacterName.Length > 0
                ? $"{snap.PlayerName}\n{snap.CharacterName}"
                : snap.PlayerName;

            var nameBtn = new Button
            {
                Text = displayText,
                Flat = true,
                CustomMinimumSize = new Vector2(120, 0),
                Alignment = HorizontalAlignment.Left
            };
            nameBtn.Pressed += () => EmitSignal(SignalName.PlayerRowClicked, snap.PlayerId);

            var deckLbl   = new Label { CustomMinimumSize = new Vector2(50, 0) };
            var relicLbl  = new Label { CustomMinimumSize = new Vector2(50, 0) };
            var potionLbl = new Label { CustomMinimumSize = new Vector2(50, 0) };
            var buffLbl   = new Label { CustomMinimumSize = new Vector2(50, 0) };

            row.AddChild(nameBtn);
            row.AddChild(deckLbl);
            row.AddChild(relicLbl);
            row.AddChild(potionLbl);
            row.AddChild(buffLbl);

            _playerRows.AddChild(row);
            _rowsByPlayerId[snap.PlayerId] = row;
            _countLabelsByPlayerId[snap.PlayerId] = new[] { deckLbl, relicLbl, potionLbl, buffLbl };

            return row;
        }

        private void RefreshRow(string playerId, PlayerSnapshot snap)
        {
            if (!_countLabelsByPlayerId.TryGetValue(playerId, out var labels)) return;
            var s = ModSettings.Instance;
            labels[0].Text    = s.ShowDeck    ? $"덱:{snap.DeckCount}"   : "";
            labels[1].Text    = s.ShowRelics  ? $"유:{snap.RelicCount}"  : "";
            labels[2].Text    = s.ShowPotions ? $"포:{snap.PotionCount}" : "";
            labels[3].Text    = s.ShowBuffs   ? $"버:{snap.BuffCount}"   : "";
            labels[0].Visible = s.ShowDeck;
            labels[1].Visible = s.ShowRelics;
            labels[2].Visible = s.ShowPotions;
            labels[3].Visible = s.ShowBuffs;
        }

        private void RefreshAllRows()
        {
            foreach (var (id, snap) in _lastSnaps)
                RefreshRow(id, snap);
            EmitSignal(SignalName.ToggleChanged);
        }

        private void OnCollapsePressed()
        {
            _collapsed = !_collapsed;
            _scroll.Visible = !_collapsed;
            _btnCollapse.Text = _collapsed ? "+" : "─";
            ResetSize();
        }

        private static CheckButton MakeToggle(string text, bool initial, System.Action onToggle)
        {
            var btn = new CheckButton { Text = text, ButtonPressed = initial };
            btn.Toggled += _ => onToggle();
            return btn;
        }
    }
}
