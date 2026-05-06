using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PileDesign.Converters
{
    /// <summary>
    /// LoadCase.Level (int) から Foreground色を返す IValueConverter
    /// Level==1 → 緑, Level==2 → 桃赤
    /// </summary>
    public class LoadCaseLevelToColorConverter : IValueConverter
    {
        private static readonly Brush Level1Brush = new SolidColorBrush(Color.FromRgb(0x23, 0x89, 0x66));
        private static readonly Brush Level2Brush = new SolidColorBrush(Color.FromRgb(0xE9, 0x55, 0x41));
        private static readonly Brush DefaultBrush = Brushes.Black;

        static LoadCaseLevelToColorConverter()
        {
            Level1Brush.Freeze();
            Level2Brush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                return level switch
                {
                    1 => Level1Brush,
                    2 => Level2Brush,
                    _ => DefaultBrush
                };
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
