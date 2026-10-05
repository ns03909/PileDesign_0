using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 解析結果の選択肢に出る記号 (Mh・UH・RX など) を「記号 名前 [単位]」の表示に変換する。
    ///
    /// 選択肢が記号だけだと、何を選んでいるのか・数字の単位が何かがその場で分からなかった。
    /// 値は記号のまま (表示や描画の側が記号の文字列で比べている) で、見せ方だけを変える。
    /// 詳しい定義は <see cref="ResultSymbolDescriptionConverter"/> (ツールチップ) に置く。
    /// 単位は図・ツールチップの数字と同じ (力 kN・モーメント kN·m・変位 mm・回転角 rad)。
    /// </summary>
    public sealed class ResultSymbolLabelConverter : IValueConverter
    {
        private static readonly Dictionary<string, (string Name, string Unit)> Labels = new(StringComparer.Ordinal)
        {
            // --- 梁応力 (部材座標系) ---
            ["Fh"] = ("水平合成せん断力", "kN"),
            ["Mh"] = ("水平合成曲げモーメント", "kN·m"),
            ["Fx"] = ("軸力", "kN"),
            ["Fy"] = ("せん断力 (y 方向)", "kN"),
            ["Fz"] = ("せん断力 (z 方向)", "kN"),
            ["Mx"] = ("ねじりモーメント", "kN·m"),
            ["My"] = ("曲げモーメント (y 軸まわり)", "kN·m"),
            ["Mz"] = ("曲げモーメント (z 軸まわり)", "kN·m"),

            // --- 節点変位 (全体座標系) ---
            ["UH"] = ("水平合成変位", "mm"),
            ["U"] = ("3 方向の合成変位", "mm"),
            ["θH"] = ("水平軸まわりの合成回転角", "rad"),
            ["UX"] = ("X 方向の変位", "mm"),
            ["UY"] = ("Y 方向の変位", "mm"),
            ["UZ"] = ("鉛直変位", "mm"),
            ["θX"] = ("X 軸まわりの回転角", "rad"),
            ["θY"] = ("Y 軸まわりの回転角", "rad"),
            ["θZ"] = ("鉛直軸まわりの回転角", "rad"),

            // --- 地盤反力 (全体座標系) ---
            ["RH"] = ("水平合成地盤反力", "kN"),
            ["R"] = ("3 方向の合成地盤反力", "kN"),
            ["MH"] = ("水平軸まわりの合成モーメント反力", "kN·m"),
            ["RX"] = ("X 方向の地盤反力", "kN"),
            ["RY"] = ("Y 方向の地盤反力", "kN"),
            ["RZ"] = ("鉛直の地盤反力", "kN"),
            ["MX"] = ("X 軸まわりのモーメント反力", "kN·m"),
            ["MY"] = ("Y 軸まわりのモーメント反力", "kN·m"),
            ["MZ"] = ("鉛直軸まわりのモーメント反力", "kN·m"),
        };

        /// <summary>true なら閉じた枠用の「記号 [単位]」、false なら一覧用の「記号 名前 [単位]」。</summary>
        public bool Short { get; set; }

        /// <summary>一覧に出す表示。未登録の記号はそのまま返す。</summary>
        public static string Label(string? symbol, bool shortForm = false)
        {
            if (string.IsNullOrEmpty(symbol)) return "";
            if (!Labels.TryGetValue(symbol, out var l)) return symbol;
            return shortForm ? $"{symbol} [{l.Unit}]" : $"{symbol}　{l.Name}　[{l.Unit}]";
        }

        /// <summary>名前と単位を持つ記号の一覧 (テストが網羅性を検査するために使う)。</summary>
        public static IReadOnlyCollection<string> KnownSymbols => Labels.Keys;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Label(value as string, Short);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
