using Godot;

namespace TestMode1.UI
{
    public partial class PopupResizer : Control
    {
        private readonly PlayerDetailPopup _popup;
        private bool    _resizing;
        private Vector2 _resizeStart;
        private Vector2 _sizeAtResize;

        private const float HandleSize = 20f;

        public PopupResizer(PlayerDetailPopup popup)
        {
            _popup            = popup;
            MouseFilter       = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.Fdiagsize;
            CustomMinimumSize = new Vector2(HandleSize, HandleSize);
            Size              = new Vector2(HandleSize, HandleSize);
        }

        public override void _Process(double _)
        {
            Visible = _popup.Visible;
            if (_popup.Visible)
                Position = _popup.Position + _popup.Size - new Vector2(HandleSize, HandleSize);
        }

        public override void _Draw()
        {
            var color = InspectorTheme.Border;
            float w   = Size.X;
            float h   = Size.Y;
            for (int i = 1; i <= 3; i++)
            {
                float off = i * 4.5f;
                DrawLine(new Vector2(w - 2f, h - off), new Vector2(w - off, h - 2f), color, 1.5f);
            }
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _resizing     = true;
                    _resizeStart  = GetViewport().GetMousePosition();
                    _sizeAtResize = _popup.Size;
                }
                else
                {
                    _resizing = false;
                }
            }
            else if (ev is InputEventMouseMotion && _resizing)
            {
                var delta   = GetViewport().GetMousePosition() - _resizeStart;
                var newSize = new Vector2(
                    Mathf.Max(_sizeAtResize.X + delta.X, PlayerDetailPopup.MinW),
                    Mathf.Max(_sizeAtResize.Y + delta.Y, PlayerDetailPopup.MinH));
                _popup.SetPopupSize(newSize);
            }
        }
    }
}
