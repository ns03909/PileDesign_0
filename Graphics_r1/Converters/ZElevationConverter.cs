using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    // Z (ローカル座標) と 絶対標高 の双方向変換コンバーター
    //   MultiBinding で [0]=Z, [1]=ReferenceAltitude を渡すと、
    //   画面には 標高 = Z + ReferenceAltitude を表示し、
    //   入力された 標高 は Z = 標高 - ReferenceAltitude に逆変換して保存する。
    public class ZElevationConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return 0.0;
            double z = values[0] is double z0 ? z0 : 0.0;
            double refAlt = values[1] is double r0 ? r0 : 0.0;
            return z + refAlt;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            double elevation = 0.0;
            if (value is double d) elevation = d;
            else if (value is string s && double.TryParse(s, NumberStyles.Any, culture, out var parsed)) elevation = parsed;
            else return [Binding.DoNothing, Binding.DoNothing];

            double refAlt = App.InputModel?.FundamentalInput?.ReferenceAltitude ?? 0.0;
            return [elevation - refAlt, Binding.DoNothing];
        }
    }
}
