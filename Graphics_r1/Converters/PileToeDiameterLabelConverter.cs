using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// PileBodyInput.PileConstructionType を入力 UI ラベル文字列に変換する。
    ///   - 場所打ちコンクリート杭     → 「杭先端径d (mm)」
    ///   - 埋込み杭（プレボーリング/中掘り） → 「根固め部径d (mm)」
    ///   - 埋込み杭（Smart-MAGNUM）  → 「拡大根固め部径Den (mm)」
    ///   - 埋込み杭（Hybridニーディング） → 「根固め部径D3 (mm)」(設計拡径比 e から導出、読み取り専用)
    ///   - 打込み杭                  → 「杭先端径d (mm)」
    ///   - 回転貫入杭                → 「羽根径Dw (mm)」
    ///   - その他/未設定             → 「杭先端径d (mm)」
    /// 既存の PileBodyInput.PileToeDia フィールドを兼用するため、入力欄のラベルだけを工法に応じて切り替える。
    /// </summary>
    public class PileToeDiameterLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string type = value as string ?? string.Empty;
            return type switch
            {
                Constants.PileConstructionTypeNames.Rotary => "羽根径Dw (mm)",
                Constants.PileConstructionTypeNames.SmartMagnum => "拡大根固め部径Den (mm)",
                Constants.PileConstructionTypeNames.HybridKneading => "根固め部径D3 (mm) ※e×節部径D1",
                _ when Constants.PileConstructionTypeNames.IsPreboring(type) => "根固め部径d (mm)",
                Constants.PileConstructionTypeNames.Chubori => "根固め部径d (mm)",
                _ => "杭先端径d (mm)",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
