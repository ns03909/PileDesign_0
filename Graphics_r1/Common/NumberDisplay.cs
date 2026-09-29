using System;
using System.Globalization;

namespace PileDesign.Common
{
    /// <summary>画面・諸元表の数値の表示の書き方。</summary>
    internal static class NumberDisplay
    {
        /// <summary>
        /// ヤング係数などの大きな値の表示。3 桁区切りの整数で出すが、0 でない 1 未満の値は小数まで出す。
        ///
        /// <para>整数に丸めると 0.44 N/mm² が「0」と表示され、0 を入れたように見えた (γ を下限の 0.1 にしたとき。
        /// 実際には 0 でないので解析前の検査も通っていた)。</para>
        /// </summary>
        internal static string Modulus(double value)
        {
            if (double.IsFinite(value) && value != 0 && Math.Abs(value) < 1)
                return value.ToString("G3", CultureInfo.CurrentCulture);   // 有効 3 桁 (0.0004 も 0 にしない)
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }
    }
}
