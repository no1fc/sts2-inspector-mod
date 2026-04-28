using Godot;

namespace TestMode1.UI
{
    public partial class HoverLabel : VBoxContainer
    {
        private Label               _nameLabel;
        private ImageTooltip        _tooltip;
        private string              _imageKey;
        private ImageCache.ItemType _itemType;
        private string              _description;
        private string              _stats;

        // CardNodePool이 n.Text = "" 로 초기화하므로 Label 호환 프로퍼티 유지
        public string Text
        {
            get => _nameLabel?.Text ?? "";
            set { if (_nameLabel != null) _nameLabel.Text = value; }
        }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseEntered += OnEnter;
            MouseExited  += OnExit;

            _nameLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_nameLabel);
        }

        public void Configure(string displayText, string imageKey,
                              ImageCache.ItemType type, ImageTooltip tooltip,
                              string description = "", string stats = "")
        {
            _nameLabel.Text = displayText;
            _imageKey       = imageKey;
            _itemType       = type;
            _tooltip        = tooltip;
            _description    = description;
            _stats          = stats;
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
