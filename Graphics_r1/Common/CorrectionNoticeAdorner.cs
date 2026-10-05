using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PileDesign.Common
{
    /// <summary>
    /// 入力欄の直下に「入力を補正しました」を出す飾り (<see cref="NumericInput"/> が使う)。
    ///
    /// 補正した値は正しい値なので、検証エラー (赤) にはしない。
    /// エラーにすると、OK を押せるかを Validation.GetHasError で決めている画面が通らなくなる。
    /// 下の欄に重なるので、次に入力を始めたら消す。
    /// </summary>
    internal sealed class CorrectionNoticeAdorner : Adorner
    {
        private readonly Border _box;

        public CorrectionNoticeAdorner(UIElement adorned, string message) : base(adorned)
        {
            IsHitTestVisible = false;
            _box = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC2, 0x6A, 0x00)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = message,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x42, 0x00)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 320,
                },
            };
            AddVisualChild(_box);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _box;

        protected override Size MeasureOverride(Size constraint)
        {
            _box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _box.Arrange(new Rect(new Point(0, AdornedElement.RenderSize.Height + 1), _box.DesiredSize));
            return finalSize;
        }
    }
}
