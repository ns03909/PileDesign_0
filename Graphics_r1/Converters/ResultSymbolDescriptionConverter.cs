using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 解析結果の選択肢に出る記号 (Mh・UH・RX など) を日本語の説明に変換する。
    ///
    /// リボンの選択肢は表示幅の都合で記号だけになっており、
    /// 何を選んでいるのかがその場では分からない。ツールチップで補う。
    ///
    /// 定義はヘルプ「解析結果の記号 (凡例)」と揃えること。
    /// 梁応力は部材座標系、節点変位と地盤反力は全体座標系 (Z 上向き)。
    /// </summary>
    public sealed class ResultSymbolDescriptionConverter : IValueConverter
    {
        private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
        {
            // --- 梁応力 (部材座標系) ---
            ["Fh"] = "水平合成せん断力 √(Fy²+Fz²)",
            ["Mh"] = "水平合成曲げモーメント √(My²+Mz²)",
            ["Fx"] = "軸力 (部材軸方向。杭では杭軸方向)",
            ["Fy"] = "せん断力 (部材座標系 y 軸方向)",
            ["Fz"] = "せん断力 (部材座標系 z 軸方向)",
            ["Mx"] = "ねじりモーメント (部材軸まわり)",
            ["My"] = "曲げモーメント (部材座標系 y 軸まわり)",
            ["Mz"] = "曲げモーメント (部材座標系 z 軸まわり)",

            // --- 節点変位 (全体座標系) ---
            ["UH"] = "水平合成変位 √(UX²+UY²)",
            ["U"] = "3 方向の合成変位 √(UX²+UY²+UZ²)",
            ["θH"] = "水平軸まわりの合成回転角 √(θX²+θY²)",
            ["UX"] = "X 方向 (水平) の変位",
            ["UY"] = "Y 方向 (水平) の変位",
            ["UZ"] = "Z 方向 (鉛直) の変位。沈下は負",
            ["θX"] = "X 軸まわりの回転角",
            ["θY"] = "Y 軸まわりの回転角",
            ["θZ"] = "Z 軸 (鉛直軸) まわりの回転角",

            // --- 地盤反力 (全体座標系) ---
            ["RH"] = "水平合成地盤反力 √(RX²+RY²)",
            ["R"] = "3 方向の合成地盤反力 √(RX²+RY²+RZ²)",
            ["MH"] = "水平軸まわりの合成モーメント反力 √(MX²+MY²)",
            ["RX"] = "X 方向 (水平) の地盤反力",
            ["RY"] = "Y 方向 (水平) の地盤反力",
            ["RZ"] = "Z 方向 (鉛直) の地盤反力",
            ["MX"] = "X 軸まわりのモーメント反力。回転剛性を持つばね以外はほぼ 0",
            ["MY"] = "Y 軸まわりのモーメント反力。回転剛性を持つばね以外はほぼ 0",
            ["MZ"] = "Z 軸 (鉛直軸) まわりのモーメント反力",
        };

        /// <summary>記号に対応する説明。未登録なら null (ツールチップは出ない)。</summary>
        public static string? Describe(string? symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;
            return Descriptions.TryGetValue(symbol, out string? text) ? text : null;
        }

        /// <summary>説明を持つ記号の一覧 (テストが網羅性を検査するために使う)。</summary>
        public static IReadOnlyCollection<string> KnownSymbols => Descriptions.Keys;

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Describe(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
