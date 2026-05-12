using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PileDesign.Common
{
    /// <summary>
    /// 「LN-M」「ΔLN-M」形式の荷重ケースラベル (N=Level 1/2, M=1..4) を TextBlock に対して
    /// Attached Property で集約適用するヘルパ。OTM 表など、同じパターンが繰り返される箇所の
    /// XAML を簡潔にするために導入。
    ///
    /// 設定する内容:
    ///   - Text: そのまま "L1-1" 等
    ///   - HorizontalAlignment / TextAlignment: Center
    ///   - Foreground: Level 1 → NikkenGreenBrush, Level 2 → NikkenPaleRedBrush
    ///   - ToolTip: CurrentInputModel.LoadCasesInput.LoadCasesLevel{N}[M-1].LoadName を binding
    ///
    /// 使い方:
    ///   <code>&lt;TextBlock common:LoadCaseCodeLabel.Code="L1-1" Grid.Row="0" Grid.Column="1"/&gt;</code>
    ///
    /// 制約:
    ///   - 親の DataContext が MainWindowViewModel である必要あり (CurrentInputModel パスのため)
    ///   - DataGrid 列ヘッダのように DataContext が失われる文脈では使用不可
    /// </summary>
    public static class LoadCaseCodeLabel
    {
        private static readonly Regex _codeRegex = new(
            @"^(?<delta>Δ?)L(?<level>[12])-(?<index>[1-4])$",
            RegexOptions.Compiled);

        public static readonly DependencyProperty CodeProperty = DependencyProperty.RegisterAttached(
            "Code", typeof(string), typeof(LoadCaseCodeLabel),
            new PropertyMetadata(null, OnCodeChanged));

        public static string GetCode(DependencyObject obj) => (string)obj.GetValue(CodeProperty);
        public static void SetCode(DependencyObject obj, string value) => obj.SetValue(CodeProperty, value);

        private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb || e.NewValue is not string code) return;
            var m = _codeRegex.Match(code);
            if (!m.Success) return;

            int level = int.Parse(m.Groups["level"].Value);
            int index = int.Parse(m.Groups["index"].Value);

            tb.Text = code;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.TextAlignment = TextAlignment.Center;
            // Brush は StaticResource 経由で参照 (App.xaml 等で定義済み)
            tb.SetResourceReference(TextBlock.ForegroundProperty,
                level == 1 ? "NikkenGreenBrush" : "NikkenPaleRedBrush");

            string path = $"CurrentInputModel.LoadCasesInput.LoadCases{(level == 1 ? "Level1" : "Level2")}[{index - 1}].LoadName";
            var binding = new Binding(path) { FallbackValue = code };
            BindingOperations.SetBinding(tb, TextBlock.ToolTipProperty, binding);
        }
    }
}
