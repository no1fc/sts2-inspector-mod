using System.Collections.Generic;
using Godot;

namespace TestMode1.UI
{
    public partial class OverlayPanel : PanelContainer
    {
        [Signal]
        public delegate void PlayerRowClickedEventHandler(string playerId);

        private VBoxContainer _playerRows;
        private CheckButton _btnDeck, _btnHand, _btnRelics, _btnPotions;
        private bool _collapsed = false;

        private readonly Dictionary<string, HBoxContainer> _rowsByPlayerId = new();
        private readonly Dictionary<string, Label[]> _countLabelsByPlayerId = new();

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

            var btnCollapse = new Button { Text = "─" };
            btnCollapse.Pressed += OnCollapsePressed;
            toggleBar.AddChild(btnCollapse);

            _btnDeck    = MakeToggle("덱",   settings.ShowDeck,    () => ModSettings.Instance.SetToggle("deck",    _btnDeck.ButtonPressed));
            _btnHand    = MakeToggle("패",   settings.ShowHand,    () => ModSettings.Instance.SetToggle("hand",    _btnHand.ButtonPressed));
            _btnRelics  = MakeToggle("유물", settings.ShowRelics,  () => ModSettings.Instance.SetToggle("relics",  _btnRelics.ButtonPressed));
            _btnPotions = MakeToggle("포션", settings.ShowPotions, () => ModSettings.Instance.SetToggle("potions", _btnPotions.ButtonPressed));

            toggleBar.AddChild(_btnDeck);
            toggleBar.AddChild(_btnHand);
            toggleBar.AddChild(_btnRelics);
            toggleBar.AddChild(_btnPotions);

            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, 400),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            root.AddChild(scroll);

            _playerRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill };
            scroll.AddChild(_playerRows);
        }

        public void UpdatePlayer(PlayerSnapshot snap)
        {
            if (!_rowsByPlayerId.TryGetValue(snap.PlayerId, out var row))
                row = CreateRow(snap.PlayerId, snap.PlayerName);

            RefreshRow(snap.PlayerId, snap);
        }

        public void RemovePlayer(string playerId)
        {
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

        private HBoxContainer CreateRow(string playerId, string playerName)
        {
            var row = new HBoxContainer();

            // 플레이어 이름을 버튼으로 만들어 클릭 처리
            var nameBtn = new Button
            {
                Text = playerName,
                Flat = true,
                CustomMinimumSize = new Vector2(100, 0),
                Alignment = HorizontalAlignment.Left
            };
            nameBtn.Pressed += () => EmitSignal(SignalName.PlayerRowClicked, playerId);

            var deckLbl   = new Label { CustomMinimumSize = new Vector2(50, 0) };
            var relicLbl  = new Label { CustomMinimumSize = new Vector2(50, 0) };
            var potionLbl = new Label { CustomMinimumSize = new Vector2(50, 0) };

            row.AddChild(nameBtn);
            row.AddChild(deckLbl);
            row.AddChild(relicLbl);
            row.AddChild(potionLbl);

            _playerRows.AddChild(row);
            _rowsByPlayerId[playerId] = row;
            _countLabelsByPlayerId[playerId] = new[] { deckLbl, relicLbl, potionLbl };

            return row;
        }

        private void RefreshRow(string playerId, PlayerSnapshot snap)
        {
            if (!_countLabelsByPlayerId.TryGetValue(playerId, out var labels)) return;
            var s = ModSettings.Instance;
            labels[0].Text    = s.ShowDeck    ? $"덱:{snap.DeckCount}"   : "";
            labels[1].Text    = s.ShowRelics  ? $"유:{snap.RelicCount}"  : "";
            labels[2].Text    = s.ShowPotions ? $"포:{snap.PotionCount}" : "";
            labels[0].Visible = s.ShowDeck;
            labels[1].Visible = s.ShowRelics;
            labels[2].Visible = s.ShowPotions;
        }

        private void OnCollapsePressed()
        {
            _collapsed = !_collapsed;
            _playerRows.Visible = !_collapsed;
        }

        private static CheckButton MakeToggle(string text, bool initial, System.Action onToggle)
        {
            var btn = new CheckButton { Text = text, ButtonPressed = initial };
            btn.Toggled += _ => onToggle();
            return btn;
        }
    }
}
