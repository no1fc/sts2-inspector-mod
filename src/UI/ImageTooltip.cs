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

            // 피해 / 방어 / 코스트 수치 표시 (이미지 아래, 설명 위)
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
