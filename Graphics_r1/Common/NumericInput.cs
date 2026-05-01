using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Common
{
    public enum NumericInputMode
    {
        /// <summary>未指定 (フィルタなし)。</summary>
        None,
        /// <summary>0,1,2,...</summary>
        Integer,
        /// <summary>0.5, 1.25 等。小数点 1 個まで。</summary>
        Decimal,
        /// <summary>-3, 0, 5 等。先頭に '-' を許容。</summary>
        SignedInteger,
        /// <summary>-3.14, 1.0 等。先頭 '-' + 小数点 1 個。</summary>
        SignedDecimal,
        /// <summary>1.0E-5, -2.5e+10 等。指数表記 (E/e + 任意の符号 + 数字)。</summary>
        ScientificDecimal,
    }

    /// <summary>
    /// TextBox の入力を数値のみに制限する添付プロパティ。
    ///
    /// 機能:
    ///   - キーボード入力 (PreviewTextInput) を文字種でフィルタ
    ///   - クリップボード貼り付け (DataObject.Pasting) も同様にフィルタ
    ///   - LostFocus 時に空欄なら Min (or 0) に補正、範囲外なら Min/Max にクランプして
    ///     バインドソースを更新
    ///
    /// 使い方 (XAML):
    ///   <TextBox Text="{Binding Foo}"
    ///            common:NumericInput.Mode="Integer"
    ///            common:NumericInput.Min="1" common:NumericInput.Max="64"/>
    ///
    /// あるいは Style 経由 (Styles.xaml の IntegerTextBoxStyle / DecimalTextBoxStyle) を推奨。
    /// </summary>
    public static class NumericInput
    {
        // --- Mode --------------------------------------------------------------
        public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
            "Mode", typeof(NumericInputMode), typeof(NumericInput),
            new PropertyMetadata(NumericInputMode.None, OnModeChanged));

        public static void SetMode(DependencyObject d, NumericInputMode value) => d.SetValue(ModeProperty, value);
        public static NumericInputMode GetMode(DependencyObject d) => (NumericInputMode)d.GetValue(ModeProperty);

        // --- Min / Max (任意) -------------------------------------------------
        // double? を使うと WPF の DependencyProperty では Box が必要なので object? で扱う。
        public static readonly DependencyProperty MinProperty = DependencyProperty.RegisterAttached(
            "Min", typeof(object), typeof(NumericInput), new PropertyMetadata(null));

        public static readonly DependencyProperty MaxProperty = DependencyProperty.RegisterAttached(
            "Max", typeof(object), typeof(NumericInput), new PropertyMetadata(null));

        public static void SetMin(DependencyObject d, object? value) => d.SetValue(MinProperty, value);
        public static object? GetMin(DependencyObject d) => d.GetValue(MinProperty);
        public static void SetMax(DependencyObject d, object? value) => d.SetValue(MaxProperty, value);
        public static object? GetMax(DependencyObject d) => d.GetValue(MaxProperty);

        private static double? ResolveBound(object? raw)
        {
            if (raw == null) return null;
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }

        // --- 内部 -------------------------------------------------------------

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            // 既存ハンドラを必ず外してから付け直す (重複防止)
            tb.PreviewTextInput -= OnPreviewTextInput;
            DataObject.RemovePastingHandler(tb, OnPasting);
            tb.LostFocus -= OnLostFocus;

            if ((NumericInputMode)e.NewValue != NumericInputMode.None)
            {
                tb.PreviewTextInput += OnPreviewTextInput;
                DataObject.AddPastingHandler(tb, OnPasting);
                tb.LostFocus += OnLostFocus;
            }
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb) return;
            string prospective = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                        .Insert(tb.SelectionStart, e.Text);
            e.Handled = !IsValid(prospective, GetMode(tb));
        }

        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox tb) { e.CancelCommand(); return; }
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text, true))
            {
                e.CancelCommand();
                return;
            }
            var pasted = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            string prospective = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                        .Insert(tb.SelectionStart, pasted);
            if (!IsValid(prospective, GetMode(tb)))
                e.CancelCommand();
        }

        private static void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            var mode = GetMode(tb);
            if (mode == NumericInputMode.None) return;

            var min = ResolveBound(GetMin(tb));
            var max = ResolveBound(GetMax(tb));
            bool isInt = mode is NumericInputMode.Integer or NumericInputMode.SignedInteger;

            string text = tb.Text;

            // 空欄 / "-" 単独 / "." 単独 / "1E" 等の編集途中 → Min (or 0) に戻す
            if (string.IsNullOrEmpty(text) || text == "-" || text == ".")
            {
                double fallback = min ?? 0;
                tb.Text = isInt
                    ? ((long)Math.Round(fallback)).ToString(CultureInfo.InvariantCulture)
                    : fallback.ToString(CultureInfo.InvariantCulture);
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                return;
            }

            // NumberStyles.Float は AllowExponent を含むため Scientific 表記もパース可能
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return;

            double clamped = v;
            if (min.HasValue && clamped < min.Value) clamped = min.Value;
            if (max.HasValue && clamped > max.Value) clamped = max.Value;

            // クランプが発生した場合のみ書き戻す (無駄な変更通知を避ける)
            if (Math.Abs(clamped - v) > 1e-12)
            {
                tb.Text = isInt
                    ? ((long)Math.Round(clamped)).ToString(CultureInfo.InvariantCulture)
                    : clamped.ToString(CultureInfo.InvariantCulture);
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
        }

        /// <summary>
        /// 入力途中の文字列が「数値として続行可能」か判定。
        /// "" や "-" や "1." や "1E" や "1E-" などの編集途中も valid として返す。
        /// </summary>
        private static bool IsValid(string text, NumericInputMode mode)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (mode == NumericInputMode.None) return true;

            bool allowSign = mode is NumericInputMode.SignedInteger
                                  or NumericInputMode.SignedDecimal
                                  or NumericInputMode.ScientificDecimal;
            bool allowDecimal = mode is NumericInputMode.Decimal
                                     or NumericInputMode.SignedDecimal
                                     or NumericInputMode.ScientificDecimal;
            bool allowExponent = mode is NumericInputMode.ScientificDecimal;

            int start = 0;
            if (text[0] == '-')
            {
                if (!allowSign) return false;
                start = 1;
                if (text.Length == 1) return true;
            }

            bool seenDot = false;
            bool seenE = false;
            bool sawSignAfterE = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9') continue;
                if (c == '.' && allowDecimal && !seenDot && !seenE) { seenDot = true; continue; }
                if ((c == 'E' || c == 'e') && allowExponent && !seenE && i > start) { seenE = true; continue; }
                if ((c == '+' || c == '-') && seenE && !sawSignAfterE)
                {
                    // 'E' の直後にしか符号は出せない
                    if (i > 0 && (text[i - 1] == 'E' || text[i - 1] == 'e'))
                    {
                        sawSignAfterE = true;
                        continue;
                    }
                    return false;
                }
                return false;
            }
            return true;
        }
    }
}
