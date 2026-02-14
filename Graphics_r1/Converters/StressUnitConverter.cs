using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 応力単位変換コンバーター
    /// 内部値: kN/m² → 表示値: N/mm²
    /// 変換係数: 1 kN/m² = 0.001 N/mm²
    /// </summary>
    public class StressUnitConverter : IValueConverter
    {
        // kN/m² から N/mm² への変換係数
        private const double KNPerM2_To_NPerMM2 = 0.001;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double kNPerM2)
            {
                // kN/m² → N/mm²
                double nPerMM2 = kNPerM2 * KNPerM2_To_NPerMM2;
                return nPerMM2;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double nPerMM2)
            {
                // N/mm² → kN/m²
                double kNPerM2 = nPerMM2 / KNPerM2_To_NPerMM2;
                return kNPerM2;
            }
            else if (value is string str && double.TryParse(str, out double nPerMM2Parsed))
            {
                // N/mm² → kN/m²
                double kNPerM2 = nPerMM2Parsed / KNPerM2_To_NPerMM2;
                return kNPerM2;
            }
            return value;
        }
    }
}
