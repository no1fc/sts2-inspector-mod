using Godot;

namespace TestMode1.UI
{
    public partial class HoverLabel : VBoxContainer
    {
        private readonly Label[] _cols = new Label[4];
        private HBoxContainer    _row;
        private ImageTooltip     _tooltip;
        private string           _imageKey;
        private ImageCache.ItemType _itemType;
        private string           _description;
        private string           _stats;
        private Color            _baseColor;

        // CardNodePool이 n.Text = ""로 초기화할 때 사용
        public string Text
        {
            get => _cols[0]?.Text ?? "";
            set { if (_cols[0] != null) _cols[0].Text = value; }
        }

        public override void _Ready()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            MouseFilter         = MouseFilterEnum.Stop;
            CustomMinimumSize   = new Vector2(0, 28);
            MouseEntered += OnEnter;
            MouseExited  += OnExit;

            _row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _row.AddThemeConstantOverride("separation", 4);
            AddChild(_row);

            // 열 0: 카드/아이템명 — 남은 너비를 채움
            _cols[0] = new Label
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment   = VerticalAlignment.Center,
                MouseFilter         = MouseFilterEnum.Ignore,
                ClipText            = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _row.AddChild(_cols[0]);

            // 열 1-3: 고정 너비 (코스트/피해/방어 또는 개수/수치)
            for (int i = 1; i < 4; i++)
            {
                _cols[i] = new Label
                {
                    CustomMinimumSize   = new Vector2(InspectorTheme.ColSmall, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Center,
                    MouseFilter         = MouseFilterEnum.Ignore,
                    Visible             = false,
                };
                _row.AddChild(_cols[i]);
            }
        }

        // 단일 열 (덱·유물·포션에서 사용)
        public void Configure(string displayText, string imageKey,
                              ImageCache.ItemType type, ImageTooltip tooltip,
                              string description = "", string stats = "")
        {
            ApplyMeta(imageKey, type, tooltip, description, stats);
            SetColumns(displayText, "", "", "");
        }

        // 다중 열 (패·버프·개수 표시에 사용)
        public void ConfigureRow(string c0, string c1, string c2, string c3,
                                  string imageKey, ImageCache.ItemType type, ImageTooltip tooltip,
                                  string description = "", string stats = "")
        {
            ApplyMeta(imageKey, type, tooltip, description, stats);
            SetColumns(c0, c1, c2, c3);
        }

        private void ApplyMeta(string imageKey, ImageCache.ItemType type, ImageTooltip tooltip,
                                string description, string stats)
        {
            _imageKey    = imageKey;
            _itemType    = type;
            _tooltip     = tooltip;
            _description = description;
            _stats       = stats;
            _baseColor   = InspectorTheme.ColorForType(type);
            foreach (var lbl in _cols)
                lbl?.AddThemeColorOverride("font_color", _baseColor);
        }

        private void SetColumns(string c0, string c1, string c2, string c3)
        {
            _cols[0].Text    = c0;
            _cols[1].Text    = c1; _cols[1].Visible = c1.Length > 0;
            _cols[2].Text    = c2; _cols[2].Visible = c2.Length > 0;
            _cols[3].Text    = c3; _cols[3].Visible = c3.Length > 0;
        }

        private void OnEnter()
        {
            foreach (var lbl in _cols)
                lbl?.AddThemeColorOverride("font_color", InspectorTheme.GoldHover);

            var tex = ImageCache.Instance?.GetTexture(_imageKey, _itemType);
            var pos = GetViewport().GetMousePosition() + new Vector2(16, -210);
            _tooltip?.ShowAt(tex, _imageKey, _description, _stats, pos);
        }

        private void OnExit()
        {
            foreach (var lbl in _cols)
                lbl?.AddThemeColorOverride("font_color", _baseColor);
            _tooltip?.Hide();
        }
    }
}
