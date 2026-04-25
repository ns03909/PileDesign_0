using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 複数の double を合計する MultiBinding 用 Converter。
    /// 主に「Z + ReferenceAltitude」で絶対標高を表示する用途で使う。
    /// </summary>
    public class SumConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null) return 0.0;
            double sum = 0.0;
            foreach (var v in values)
            {
                if (v is double d) sum += d;
                else if (v is float f) sum += f;
                else if (v is int i) sum += i;
                else if (v != null && double.TryParse(v.ToString(), NumberStyles.Any, culture, out double parsed))
                    sum += parsed;
            }
            return sum;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
