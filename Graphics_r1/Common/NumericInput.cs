using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    ///   - フォーカスが外れる直前 (PreviewLostKeyboardFocus) に、空欄なら Min (or 0) に、
    ///     範囲外なら Min/Max に欄の文字を直す。値の書き戻しは通常どおりバインディングに任せる。
    ///     補正したことを欄の直下に出す (<see cref="CorrectionNoticeProperty"/>)
    ///   - LostFocus でも同じ補正をする (キーボードのフォーカスを経ずに外れた場合の網。こちらは書き戻しまで行う)
    ///   - 検証エラーの文を欄の直下に出すテンプレート (InlineValidationErrorTemplate) を、
    ///     欄が自前のテンプレートを持たなければ付ける
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

        // --- 補正の知らせ -------------------------------------------------------
        // 以前は範囲外や空欄を黙って補正しており、入れた値が変わったことに気付けなかった。
        // 補正後の値は正しいので検証エラーにはせず、欄の直下に知らせだけを出す (CorrectionNoticeAdorner)。
        public static readonly DependencyProperty CorrectionNoticeProperty = DependencyProperty.RegisterAttached(
            "CorrectionNotice", typeof(string), typeof(NumericInput), new PropertyMetadata(null, OnCorrectionNoticeChanged));

        public static string? GetCorrectionNotice(DependencyObject d) => (string?)d.GetValue(CorrectionNoticeProperty);
        private static void SetCorrectionNotice(DependencyObject d, string? value) => d.SetValue(CorrectionNoticeProperty, value);

        private static readonly DependencyProperty NoticeAdornerProperty = DependencyProperty.RegisterAttached(
            "NoticeAdorner", typeof(CorrectionNoticeAdorner), typeof(NumericInput), new PropertyMetadata(null));

        /// <summary>
        /// いま知らせを出している欄。知らせは同時に 1 つだけにする
        /// (隣り合う欄を続けて直すと、知らせどうしが重なって読めなかった)。
        /// </summary>
        private static WeakReference<TextBox>? _noticed;

        /// <summary>出ている知らせを消す (窓の Undo で値を戻したときなど、知らせが古くなったとき)。</summary>
        public static void ClearAllCorrectionNotices()
        {
            if (_noticed != null && _noticed.TryGetTarget(out var tb)) SetCorrectionNotice(tb, null);
            _noticed = null;
        }

        private static void OnCorrectionNoticeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;
            if (e.NewValue is string)
            {
                if (_noticed != null && _noticed.TryGetTarget(out var previous) && !ReferenceEquals(previous, tb))
                    SetCorrectionNotice(previous, null);
                _noticed = new WeakReference<TextBox>(tb);
            }
            if (tb.GetValue(NoticeAdornerProperty) is CorrectionNoticeAdorner old)
            {
                AdornerLayer.GetAdornerLayer(tb)?.Remove(old);
                tb.ClearValue(NoticeAdornerProperty);
            }
            if (e.NewValue is string text && AdornerLayer.GetAdornerLayer(tb) is { } layer)
            {
                var adorner = new CorrectionNoticeAdorner(tb, text);
                layer.Add(adorner);
                tb.SetValue(NoticeAdornerProperty, adorner);
            }
        }

        /// <summary>補正の知らせの文。</summary>
        internal static string BuildCorrectionNotice(string entered, string corrected, object? min, object? max)
        {
            if (string.IsNullOrEmpty(entered) || entered is "-" or ".")
                return $"空欄だったので {corrected} にしました。";
            string range = DescribeRange(min, max)?.Replace("入力範囲: ", "") ?? "";
            return $"入力範囲 ({range}) の外なので {corrected} にしました (入力 {entered})。";
        }

        /// <summary>
        /// 次に入力を始めたら知らせを消す (下の欄に重なるので出しっぱなしにしない)。
        /// 文字の変化 (TextChanged) では消さない。値を書き戻すと StringFormat で「5000」が「5,000」に
        /// 書き直され、それだけで知らせが消えてしまう。打鍵・削除・貼り付けだけを入力とみなす。
        /// </summary>
        private static void ClearNoticeOnEdit(TextBox tb)
        {
            if (GetCorrectionNotice(tb) != null) SetCorrectionNotice(tb, null);
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox tb && e.Key is Key.Back or Key.Delete) ClearNoticeOnEdit(tb);
        }

        private static void WriteCorrection(TextBox tb, string corrected, string entered, bool writeBack)
        {
            tb.Text = corrected;
            if (writeBack) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SetCorrectionNotice(tb, BuildCorrectionNotice(entered, corrected, GetMin(tb), GetMax(tb)));
        }

        /// <summary>
        /// 検証エラーの文を欄の直下に出すテンプレートを付ける。
        /// スタイルや XAML で自前のテンプレートを指定している欄には触らない。
        /// </summary>
        private static void ApplyInlineErrorTemplate(TextBox tb)
        {
            if (DependencyPropertyHelper.GetValueSource(tb, Validation.ErrorTemplateProperty).BaseValueSource != BaseValueSource.Default)
                return;
            if (Application.Current?.TryFindResource("InlineValidationErrorTemplate") is ControlTemplate template)
                Validation.SetErrorTemplate(tb, template);
        }

        // --- 内部 -------------------------------------------------------------

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            // 既存ハンドラを必ず外してから付け直す (重複防止)
            tb.PreviewTextInput -= OnPreviewTextInput;
            tb.PreviewLostKeyboardFocus -= OnPreviewLostKeyboardFocus;
            DataObject.RemovePastingHandler(tb, OnPasting);
            tb.LostFocus -= OnLostFocus;
            tb.PreviewKeyDown -= OnPreviewKeyDown;

            tb.Loaded -= OnLoaded;

            if ((NumericInputMode)e.NewValue != NumericInputMode.None)
            {
                tb.PreviewTextInput += OnPreviewTextInput;
                tb.PreviewLostKeyboardFocus += OnPreviewLostKeyboardFocus;
                DataObject.AddPastingHandler(tb, OnPasting);
                tb.LostFocus += OnLostFocus;
                tb.PreviewKeyDown += OnPreviewKeyDown;

                // 入力範囲の案内は Loaded で行う。
                // Min / Max は XAML の属性順で Mode より後に来るので、
                // ここで読むとまだ null のことがある。
                tb.Loaded += OnLoaded;
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            ApplyRangeToolTip(tb);
            ApplyInlineErrorTemplate(tb);
        }

        /// <summary>
        /// 入力範囲をツールチップに出す。
        ///
        /// Min / Max は 82 箇所で指定されているが画面には出ておらず、
        /// 範囲外を入れてクランプされて初めて制限に気付く状態だった。
        ///
        /// 既存のツールチップがある場合:
        ///   ・単純な文字列なら範囲を 1 行追記する
        ///   ・それ以外 (TextBlock / Grid などのリッチな内容) は触らない
        /// </summary>
        private static void ApplyRangeToolTip(TextBox tb)
        {
            string? range = DescribeRange(GetMin(tb), GetMax(tb));
            if (range == null) return;

            switch (tb.ToolTip)
            {
                case null:
                    tb.ToolTip = range;
                    break;

                case string existing:
                    if (!existing.Contains(range, StringComparison.Ordinal))
                        tb.ToolTip = existing + Environment.NewLine + range;
                    break;

                    // リッチなツールチップは構造を崩さないよう触らない
            }
        }

        /// <summary>入力範囲の説明。Min / Max どちらも無ければ null。</summary>
        internal static string? DescribeRange(object? min, object? max)
        {
            double? lo = ResolveBound(min);
            double? hi = ResolveBound(max);

            if (lo.HasValue && hi.HasValue) return $"入力範囲: {Format(lo.Value)} 〜 {Format(hi.Value)}";
            if (lo.HasValue) return $"入力範囲: {Format(lo.Value)} 以上";
            if (hi.HasValue) return $"入力範囲: {Format(hi.Value)} 以下";
            return null;
        }

        private static string Format(double v) =>
            v == Math.Floor(v) && Math.Abs(v) < 1e15
                ? ((long)v).ToString(CultureInfo.InvariantCulture)
                : v.ToString("G6", CultureInfo.InvariantCulture);

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb) return;
            ClearNoticeOnEdit(tb);
            string prospective = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                        .Insert(tb.SelectionStart, e.Text);
            e.Handled = !IsValid(prospective, GetMode(tb));
        }

        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox tb) { e.CancelCommand(); return; }
            ClearNoticeOnEdit(tb);
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

        /// <summary>
        /// フォーカスが外れる直前に欄の文字を直す。
        ///
        /// 以前は LostFocus だけで直していたが、画面の LostFocus の処理 (値の書き戻しと描き直し) が先に走ると
        ///   ・描き直しが直す前の値で行われ、直したあとは描き直されない (杭断面: 管厚 9999 で断面積が負のまま)
        ///   ・StringFormat=N0 の欄は書き戻しで文字が「99,999」になり、読めずに直さないまま通る (外径 99999 が入る)
        /// となっていた。ここで直せば、書き戻しも描き直しも直した値で行われる。
        /// 値は書き戻さない (画面がこの時点で取る Undo の控えに、直した値が入らないように)。
        /// </summary>
        private static void OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb && ReferenceEquals(e.OldFocus, tb)) Correct(tb, writeBack: false);
        }

        private static void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) Correct(tb, writeBack: true);
        }

        private static void Correct(TextBox tb, bool writeBack)
        {
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
                WriteCorrection(tb, isInt
                    ? ((long)Math.Round(fallback)).ToString(CultureInfo.InvariantCulture)
                    : fallback.ToString(CultureInfo.InvariantCulture), text, writeBack);
                return;
            }

            // NumberStyles.Float は AllowExponent を含むため Scientific 表記もパース可能。
            // 書き戻しのあとは StringFormat=N0 で「99,999」になっていることがあるので桁区切りも読む
            if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v))
                return;

            double clamped = v;
            if (min.HasValue && clamped < min.Value) clamped = min.Value;
            if (max.HasValue && clamped > max.Value) clamped = max.Value;

            // クランプが発生した場合のみ書き戻す (無駄な変更通知を避ける)
            if (Math.Abs(clamped - v) > 1e-12)
            {
                WriteCorrection(tb, isInt
                    ? ((long)Math.Round(clamped)).ToString(CultureInfo.InvariantCulture)
                    : clamped.ToString(CultureInfo.InvariantCulture), text, writeBack);
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
