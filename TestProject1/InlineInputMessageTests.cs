using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Converters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 入力欄の直下に、エラーと補正の知らせを出す。
    /// 以前は赤縁とツールチップだけ (変換エラーは英語)、範囲外や空欄は黙って補正していた。
    /// </summary>
    [TestClass]
    public class InlineInputMessageTests
    {
        public sealed class Source
        {
            public double Value { get; set; } = 5;
        }

        private static void Run(System.Action action)
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("STA スレッドが 60 秒以内に完了しなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
        }

        private static TextBox Bound(Source source, double? min, double? max)
        {
            var tb = new TextBox();
            tb.SetBinding(TextBox.TextProperty, new Binding(nameof(Source.Value)) { Source = source, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            NumericInput.SetMode(tb, NumericInputMode.Decimal);
            if (min != null) NumericInput.SetMin(tb, min);
            if (max != null) NumericInput.SetMax(tb, max);
            return tb;
        }

        private static void LoseFocus(TextBox tb) => tb.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

        /// <summary>範囲外を補正したら知らせを出し、次に入力を始めたら消す。補正した値はエラーにしない。</summary>
        [TestMethod]
        public void Clamping_LeavesANotice_UntilTheNextEdit()
        {
            var source = new Source();
            string? notice = null, afterEdit = "未設定", stillThere = null;
            bool hasError = true;
            Run(() =>
            {
                var tb = Bound(source, 1, 10);
                tb.Text = "100";
                LoseFocus(tb);
                notice = NumericInput.GetCorrectionNotice(tb);
                hasError = Validation.GetHasError(tb);
                // 値の書き戻しで文字が整え直されただけでは消さない (StringFormat で 5000 → 5,000 など)
                tb.Text = "10.0";
                stillThere = NumericInput.GetCorrectionNotice(tb);
                // 打鍵したら消す
                tb.RaiseEvent(new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current, tb, "7"))
                { RoutedEvent = UIElement.PreviewTextInputEvent });
                afterEdit = NumericInput.GetCorrectionNotice(tb);
            });
            Assert.AreEqual("入力範囲 (1 〜 10) の外なので 10 にしました (入力 100)。", notice);
            Assert.IsFalse(hasError, "補正した値は正しいので、エラー扱いにしない (OK の可否を HasError で決めている画面がある)");
            Assert.IsNotNull(stillThere, "文字が整え直されただけで知らせが消えています");
            Assert.IsNull(afterEdit, "入力を始めても知らせが残っています");
        }

        [TestMethod]
        public void Blank_IsFilledAndSaysSo()
        {
            var source = new Source();
            string? notice = null;
            Run(() =>
            {
                var tb = Bound(source, 1, null);
                tb.Text = "";
                LoseFocus(tb);
                notice = NumericInput.GetCorrectionNotice(tb);
            });
            Assert.AreEqual(1.0, source.Value);
            Assert.AreEqual("空欄だったので 1 にしました。", notice);
        }

        [TestMethod]
        public void InRangeValue_LeavesNoNotice()
        {
            var source = new Source();
            string? notice = "未設定";
            Run(() =>
            {
                var tb = Bound(source, 1, 10);
                tb.Text = "3";
                LoseFocus(tb);
                notice = NumericInput.GetCorrectionNotice(tb);
            });
            Assert.AreEqual(3.0, source.Value);
            Assert.IsNull(notice);
        }

        /// <summary>数値に読めない文字列は、WPF の英語の文ではなく日本語で言う。範囲の規則などの文はそのまま。</summary>
        [TestMethod]
        public void ConversionError_IsSaidInJapanese()
        {
            var source = new Source();
            string? message = null, ruleMessage = null;
            Run(() =>
            {
                var tb = new TextBox();
                tb.SetBinding(TextBox.TextProperty, new Binding(nameof(Source.Value)) { Source = source, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
                tb.Text = "abc";
                tb.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                var errors = Validation.GetErrors(tb);
                Assert.AreEqual(1, errors.Count, "前提: 変換エラーになる");
                message = ValidationErrorMessageConverter.Describe(errors[0]);

                var rule = new ValidationError(new RangeValidationRule(), tb.GetBindingExpression(TextBox.TextProperty)!, "値は 1 以上 10 以下でなければなりません。", null);
                ruleMessage = ValidationErrorMessageConverter.Describe(rule);
            });
            Assert.AreEqual(ValidationErrorMessageConverter.NotANumber, message);
            Assert.AreEqual("値は 1 以上 10 以下でなければなりません。", ruleMessage);
        }

        /// <summary>
        /// 共通のスタイル (数値欄はすべてこれを基にする) が、文を欄の直下に出すテンプレートを使う。
        /// 自前のスタイルで数値欄を作った画面にも NumericInput が同じテンプレートを付ける。
        /// </summary>
        [TestMethod]
        public void TheSharedStyle_ShowsTheMessageUnderTheField()
        {
            bool styleUsesTemplate = false, autoApplied = false, kept = false;
            Run(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var template = Application.Current.TryFindResource("InlineValidationErrorTemplate") as ControlTemplate;
                Assert.IsNotNull(template, "InlineValidationErrorTemplate がありません");

                var styled = new TextBox { Style = (Style)Application.Current.FindResource("DecimalTextBoxStyle") };
                styleUsesTemplate = ReferenceEquals(Validation.GetErrorTemplate(styled), template);

                var plain = new TextBox();
                NumericInput.SetMode(plain, NumericInputMode.Integer);
                plain.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                autoApplied = ReferenceEquals(Validation.GetErrorTemplate(plain), template);

                var own = new ControlTemplate();
                var custom = new TextBox();
                Validation.SetErrorTemplate(custom, own);
                NumericInput.SetMode(custom, NumericInputMode.Integer);
                custom.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                kept = ReferenceEquals(Validation.GetErrorTemplate(custom), own);
            });
            Assert.IsTrue(styleUsesTemplate, "DecimalTextBoxStyle (ValidatedTextBoxStyle) がテンプレートを使っていません");
            Assert.IsTrue(autoApplied, "自前のスタイルの数値欄にテンプレートが付いていません");
            Assert.IsTrue(kept, "自前のテンプレートを上書きしています");

            string fundamental = TestSource.Read("Graphics_r1", "Views", "FundamentalWindow.xaml");
            Assert.AreEqual(7, System.Text.RegularExpressions.Regex.Matches(fundamental, "Style=\"\\{StaticResource ValidatedTextBoxStyle\\}\"").Count,
                "基本設定の限界値・許容沈下量・β の欄に、エラーの文を出すスタイルが付いていません");
        }
    }
}
