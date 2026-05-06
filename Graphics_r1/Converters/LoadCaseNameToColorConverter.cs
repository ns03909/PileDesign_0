using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PileDesign.Models.InputData;

namespace PileDesign.Converters
{
    /// <summary>
    /// 荷重ケース名 + AllLoadCases から Foreground色を返す MultiValueConverter
    /// VL0, VLadd → スカイブルー
    /// VL → ディープブルー
    /// Level==1 → 緑 (#238966)
    /// Level==2 → 桃赤 (#E95541)
    /// </summary>
    public class LoadCaseNameToColorConverter : IMultiValueConverter
    {
        private static readonly Brush SkyBlueBrush = new SolidColorBrush(Color.FromRgb(0x62, 0xB0, 0xE2));
        private static readonly Brush DeepBlueBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x71, 0xAD));
        private static readonly Brush Level1Brush = new SolidColorBrush(Color.FromRgb(0x23, 0x89, 0x66));
        private static readonly Brush Level2Brush = new SolidColorBrush(Color.FromRgb(0xE9, 0x55, 0x41));
        private static readonly Brush DefaultBrush = Brushes.Black;

        static LoadCaseNameToColorConverter()
        {
            SkyBlueBrush.Freeze();
            DeepBlueBrush.Freeze();
            Level1Brush.Freeze();
            Level2Brush.Freeze();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not string name)
                return DefaultBrush;

            // 常荷重ケースは名前で判定
            if (name == "VL0" || name == "VLadd")
                return SkyBlueBrush;
            if (name == "VL")
                return DeepBlueBrush;

            // 地震荷重ケースはLoadCase.Levelで判定
            if (values[1] is IEnumerable loadCases)
            {
                foreach (var item in loadCases)
                {
                    if (item is LoadCase lc && lc.LoadName == name)
                    {
                        return lc.Level switch
                        {
                            1 => Level1Brush,
                            2 => Level2Brush,
                            _ => DefaultBrush
                        };
                    }
                }
            }

            return DefaultBrush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }
}
