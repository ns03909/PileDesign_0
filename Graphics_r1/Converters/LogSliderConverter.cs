using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    public class LogSliderConverter : IValueConverter
    {
        // min/maxはSliderのMinimum/Maximumに合わせて調整
        public double LogMin { get; set; } = 1;
        public double LogMax { get; set; } = 1000;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ViewModel→Slider（対数→線形）
            if (value is double d)
            {
                double min = LogMin;
                double max = LogMax;
                double logMin = Math.Log10(min);
                double logMax = Math.Log10(max);
                double logValue = Math.Log10(Math.Max(d, min));
                // 線形スケールに変換
                return (logValue - logMin) / (logMax - logMin) * (max - min) + min;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Slider→ViewModel（線形→対数）
            if (value is double d)
            {
                double min = LogMin;
                double max = LogMax;
                double logMin = Math.Log10(min);
                double logMax = Math.Log10(max);
                // 逆変換
                double ratio = (d - min) / (max - min);
                double logValue = logMin + ratio * (logMax - logMin);
                return Math.Pow(10, logValue);
            }
            return value;
        }
    }
}