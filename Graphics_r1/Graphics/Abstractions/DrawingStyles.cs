using System.Windows.Media;

namespace PileDesign.Graphics.Abstractions
{
    /// <summary>
    /// 描画スタイル（線、塗りつぶし）
    /// </summary>
    public class DrawingStyle
    {
        public Color StrokeColor { get; set; } = Colors.Black;
        public double StrokeThickness { get; set; } = 1.0;
        public DoubleCollection? DashArray { get; set; }
        public Color? FillColor { get; set; }
        public double Opacity { get; set; } = 1.0;

        // よく使うスタイルのプリセット
        public static DrawingStyle Default => new();

        public static DrawingStyle Dashed(Color color, double thickness = 1.0) => new()
        {
            StrokeColor = color,
            StrokeThickness = thickness,
            DashArray = [4, 2]
        };

        public static DrawingStyle Solid(Color color, double thickness = 1.0) => new()
        {
            StrokeColor = color,
            StrokeThickness = thickness
        };

        public static DrawingStyle Filled(Color fillColor, Color? strokeColor = null, double strokeThickness = 0.5) => new()
        {
            FillColor = fillColor,
            StrokeColor = strokeColor ?? Colors.Black,
            StrokeThickness = strokeThickness
        };
    }

    /// <summary>
    /// テキストスタイル
    /// </summary>
    public class TextStyle
    {
        public Color Color { get; set; } = Colors.Black;
        public double FontSize { get; set; } = 12.0;
        public string FontFamily { get; set; } = "Meiryo";
        public HorizontalTextAlignment HorizontalAlignment { get; set; } = HorizontalTextAlignment.Left;
        public VerticalTextAlignment VerticalAlignment { get; set; } = VerticalTextAlignment.Top;
        public double Angle { get; set; } = 0.0;
        public double ScaleY { get; set; } = 1.0;

        public static TextStyle Default => new();

        public static TextStyle Centered(double fontSize = 12.0) => new()
        {
            FontSize = fontSize,
            HorizontalAlignment = HorizontalTextAlignment.Center,
            VerticalAlignment = VerticalTextAlignment.Center
        };
    }

    /// <summary>
    /// 水平方向のテキスト配置
    /// </summary>
    public enum HorizontalTextAlignment
    {
        Left,   // L
        Center, // C
        Right   // R
    }

    /// <summary>
    /// 垂直方向のテキスト配置
    /// </summary>
    public enum VerticalTextAlignment
    {
        Top,    // T
        Center, // C
        Bottom  // B
    }
}
