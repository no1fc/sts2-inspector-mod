using Godot;

namespace TestMode1.UI
{
    internal static class InspectorTheme
    {
        // ── 색상 팔레트 (STS 다크 테마) ──────────────────────────────
        internal static readonly Color BgPanel    = new(0.10f, 0.08f, 0.05f, 0.92f);
        internal static readonly Color BgRow      = new(0.16f, 0.13f, 0.08f, 1.00f);
        internal static readonly Color Gold       = new(0.96f, 0.78f, 0.35f, 1.00f);
        internal static readonly Color GoldHover  = new(1.00f, 0.93f, 0.55f, 1.00f);
        internal static readonly Color TextMain   = new(0.95f, 0.92f, 0.88f, 1.00f);
        internal static readonly Color TextDim    = new(0.70f, 0.67f, 0.63f, 1.00f);
        internal static readonly Color ColDeck    = new(0.85f, 0.82f, 0.75f, 1.00f);
        internal static readonly Color ColRelic   = new(0.96f, 0.78f, 0.35f, 1.00f);
        internal static readonly Color ColPotion  = new(0.55f, 0.88f, 0.60f, 1.00f);
        internal static readonly Color Border     = new(0.45f, 0.35f, 0.18f, 1.00f);
        internal static readonly Color Separator  = new(0.35f, 0.28f, 0.15f, 0.80f);
        internal static readonly Color CloseHover = new(1.00f, 0.40f, 0.40f, 1.00f);

        // ── 레이아웃 상수 ──────────────────────────────────────────────
        internal const float ColSmall = 58f;   // 코스트/피해/방어/개수/수치 열 너비

        // ── StyleBox 헬퍼 ──────────────────────────────────────────────
        internal static StyleBoxFlat MakeBox(Color bg, Color? border = null,
                                              int radius = 4, int padH = 6, int padV = 4)
        {
            var box = new StyleBoxFlat
            {
                BgColor             = bg,
                ContentMarginLeft   = padH,
                ContentMarginRight  = padH,
                ContentMarginTop    = padV,
                ContentMarginBottom = padV,
            };
            box.SetCornerRadiusAll(radius);
            if (border.HasValue)
            {
                box.BorderColor = border.Value;
                box.SetBorderWidthAll(1);
            }
            return box;
        }

        internal static StyleBoxFlat MakeTransparent() =>
            new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), DrawCenter = false };

        internal static Color ColorForType(ImageCache.ItemType type) => type switch
        {
            ImageCache.ItemType.Relic  => ColRelic,
            ImageCache.ItemType.Potion => ColPotion,
            _                          => TextMain
        };
    }
}
