using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// bool プロパティ ⇔ RadioButton ペアの安全なバインディング用コンバータ。
    /// Convert: プロパティ値 == ConverterParameter ("True"/"False") のとき IsChecked=true。
    /// ConvertBack: チェックされたときのみ ConverterParameter を書き戻し、
    /// アンチェック時は Binding.DoNothing を返す（ペアの相方が書くため何もしない）。
    ///
    /// 背景: BooleanNegationConverter による「正値 TwoWay + 負値 TwoWay」のラジオペアは、
    /// アンチェック時にも互いに逆値を書き戻すため、RadioButton の GroupName static テーブルを
    /// 介してウィンドウ再表示時に相互解除の無限ループ → StackOverflowException を起こした
    /// (2026-08-11 docx 出力設定ウィンドウ「まとめ方」で実発生。共有 VM プロパティ +
    /// 再生成ウィンドウの組合せで発火)。IntEqualsConverter と同じ
    /// 「チェック遷移のみが書く」規約に統一することでループを構造的に不可能にする。
    /// </summary>
    public class BoolEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!bool.TryParse(parameter?.ToString(), out var param)) return DependencyProperty.UnsetValue;
            return value is bool v && v == param;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!bool.TryParse(parameter?.ToString(), out var param)) return Binding.DoNothing;
            if (value is bool b && b) return param;
            return Binding.DoNothing; // アンチェックでは書き戻さない
        }
    }
}
