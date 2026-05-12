using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 矩形荷重の LinkedPileNo (連結杭 No) 表示用コンバーター。
    /// 0 以下 (未連結) は "—" として表示し、1 以上はそのまま数値表示。
    /// </summary>
    public class LinkedPileNoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int n && n > 0) return n.ToString();
            return "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
