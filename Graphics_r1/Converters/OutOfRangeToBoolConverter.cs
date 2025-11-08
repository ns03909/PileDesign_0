using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    // values: [0]=value, [1]=lowerBound, [2]=upperBound
    public sealed class OutOfRangeToBoolConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3) return false;

            if (TryToDouble(values[0], out var v)
                && TryToDouble(values[1], out var lower)
                && TryToDouble(values[2], out var upper))
            {
                // SoilPile未設定などは赤にしない（falseを返す）
                return v > upper || v < lower;
            }

            return false;
        }

        private static bool TryToDouble(object o, out double d)
        {
            if (o is double dv) { d = dv; return true; }
            if (o is float fv) { d = fv; return true; }
            if (o is IConvertible)
            {
                try { d = System.Convert.ToDouble(o); return true; }
                catch { /* fallthrough */ }
            }
            d = 0;
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}