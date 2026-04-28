using SkiaSharp;
using System.Windows.Media;

namespace PileDesign.Common
{
    public static class NikkenDrawingColors
    {
        public static readonly System.Drawing.Color SkyBlue = System.Drawing.Color.FromArgb(98, 176, 226); // sky blue #62B0E2
        public static readonly System.Drawing.Color PaleRed = System.Drawing.Color.FromArgb(233, 85, 65); // pale red #E95541
        public static readonly System.Drawing.Color Red = System.Drawing.Color.FromArgb(216, 37, 49); // red #D82531
        public static readonly System.Drawing.Color DeepBlue = System.Drawing.Color.FromArgb(50, 113, 173); // deep blue #3271AD
        public static readonly System.Drawing.Color Yellow = System.Drawing.Color.FromArgb(247, 181, 21); // yellow #F7B515
        public static readonly System.Drawing.Color Green = System.Drawing.Color.FromArgb(35, 137, 102); // green #238966
    }

    public static class NikkenWindowsMediaColors
    {
        public static readonly Color SkyBlue = Color.FromRgb(98, 176, 226); // sky blue
        public static readonly Color PaleRed = Color.FromRgb(233, 85, 65); // pale red
        public static readonly Color Red = Color.FromRgb(216, 37, 49); // red
        public static readonly Color DeepBlue = Color.FromRgb(50, 113, 173); // deep blue
        public static readonly Color Yellow = Color.FromRgb(247, 181, 21); // yellow
        public static readonly Color Green = Color.FromRgb(35, 137, 102); // green
    }

    public static class NikkenBrush
    {
        public static readonly SolidColorBrush SkyBlue = Freeze(new(NikkenWindowsMediaColors.SkyBlue));
        public static readonly SolidColorBrush PaleRed = Freeze(new(NikkenWindowsMediaColors.PaleRed));
        public static readonly SolidColorBrush Red = Freeze(new(NikkenWindowsMediaColors.Red));
        public static readonly SolidColorBrush DeepBlue = Freeze(new(NikkenWindowsMediaColors.DeepBlue));
        public static readonly SolidColorBrush Yellow = Freeze(new(NikkenWindowsMediaColors.Yellow));
        public static readonly SolidColorBrush Green = Freeze(new(NikkenWindowsMediaColors.Green));

        private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }
    }

    public static class NikkenSKColor
    {
        public static readonly SKColor SkyBlue = new(98, 176, 226);
        public static readonly SKColor PaleRed = new(233, 85, 65);
        public static readonly SKColor Red = new(216, 37, 49);
        public static readonly SKColor DeepBlue = new(50, 113, 173);
        public static readonly SKColor Yellow = new(247, 181, 21);
        public static readonly SKColor Green = new(35, 137, 102);
        // 限界状態 (DeepBlue / Green / PaleRed) を予約色として温存するための
        // ライン系列専用カラー (限界色と意味的に衝突しない)
        public static readonly SKColor LineOrange = new(210, 105, 30);   // 焦茶/オレンジ
        public static readonly SKColor LineSlate = new(112, 128, 144);   // スレートグレー
    }
}
