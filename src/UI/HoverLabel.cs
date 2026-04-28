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
