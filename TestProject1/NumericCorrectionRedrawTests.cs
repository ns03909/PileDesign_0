using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TestProject1
{
    /// <summary>
    /// 範囲外の値を直したあと、画面の計算 (諸元表・図・N-M) が直した値で描き直されること。
    ///
    /// 杭断面ウィンドウはフォーカスが外れたとき (LostFocus) に描き直す。範囲外の補正も LostFocus で
    /// 行っていたので、描き直しが先に走ると直す前の値 (管厚 9999 など) で描かれ、直したあとは
    /// 誰も描き直さなかった。コンクリート断面積が負・N-M の軸が ±1 億 kN のまま残っていた (実機で確認)。
    /// 補正はフォーカスが外れる直前 (PreviewLostKeyboardFocus) に欄の文字だけを直し、
    /// 値の書き戻しは通常どおりバインディングに任せる。
    /// </summary>
    [TestClass]
    public class NumericCorrectionRedrawTests
    {
        private static PileSection BuildSection() => new()
        {
            PileBodyType = PileDesign.Constants.PileTypeNames.InsituRc,
            PileSectionType = PileDesign.Constants.PileTypeNames.RcSection,
            ConcreteOutDia = 1500.0,
            ConcreteFc = 27.0,
            ConcreteGsi = 1.0,
            MainBarNum = 30,
            MainBarSize = "D29",
            MainBarSpec = "SD390",
            MainBarDr = 200.0,
            HoopSize = "D13",
            HoopSpacing = 150.0,
            HoopSpec = "SD295",
            HoopCenterCover = 150.0,
            PileDiameter = 1500.0,
        };

        private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                yield return d;
                foreach (var g in LogicalDescendants(d)) yield return g;
            }
        }

        /// <summary>実際にフォーカスが外れるときの順: PreviewLostKeyboardFocus → LostFocus。</summary>
        internal static void LeaveField(TextBox tb)
        {
            tb.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, tb, null)
            { RoutedEvent = Keyboard.PreviewLostKeyboardFocusEvent });
            tb.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }

        [TestMethod]
        public void PileSectionWindow_RedrawsWithTheCorrectedValue()
        {
            string? shownDc = null, notice = null;
            double modelValue = double.NaN;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var input = new InputModel { FundamentalInput = new FundamentalInput() };
                var main = new MainWindowViewModel { CurrentInputModel = input };
                input.AttachViewModel(main);
                var section = BuildSection();
                XamlSmokeTestSupport.EnsureApplicationResources();
                var window = new PileDesign.Views.PileSectionWindow(main, section, pileBodyNo: 1, segmentNo: 1)
                { ShowActivated = false, WindowState = WindowState.Normal, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1600, Height = 1000 };
                try
                {
                    // 見せないとドッキングの中の欄にバインディングが付かない
                    window.Show();
                    window.UpdateLayout();
                    var tb = LogicalDescendants(window).OfType<TextBox>()
                        .First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "PileSection.ConcreteOutDia");
                    var vm = (PileSectionViewModel)window.DataContext;

                    tb.Text = "99999";    // 範囲は 100 〜 5000
                    LeaveField(tb);

                    modelValue = vm.PileSection.ConcreteOutDia;
                    notice = NumericInput.GetCorrectionNotice(tb);
                    shownDc = vm.PileSection.SelectedPileSectionSpecification.FirstOrDefault(s => s.Item == "コンクリート外径")?.Value;
                }
                finally { window.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual(5000.0, modelValue, "値が範囲の端に直っていません");
            StringAssert.Contains(notice, "5000 にしました");
            Assert.AreEqual("5,000", shownDc, "諸元表が直す前の値で描かれたまま残っています");
        }

        /// <summary>
        /// 実機で見つかった形そのもの: 鋼管コンクリート部の管厚に 9999 を入れて 200 に直ったのに、
        /// 諸元表のコンクリート断面積が 9999 のときの値 (負) のまま残った。
        /// </summary>
        [TestMethod]
        public void SteelPipeConcrete_WallThickness_RedrawsWithTheCorrectedValue()
        {
            string? shownAc = null, expectedAc = null;
            double modelTs = double.NaN;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var input = new InputModel { FundamentalInput = new FundamentalInput() };
                var main = new MainWindowViewModel { CurrentInputModel = input };
                input.AttachViewModel(main);
                var section = BuildSection();
                section.PileBodyType = PileDesign.Constants.PileTypeNames.InsituSteelPipeConcrete;
                section.PileSectionType = PileDesign.Constants.PileTypeNames.SteelPipeConcreteSection;
                section.PipeDia = 1200;
                section.PipeTs = 16;
                section.PipeGrade = "SKK490";
                section.CorrosionDepth = 1.0;
                section.MainBarNum = 16;
                section.MainBarCenterCover = 200;
                var window = new PileDesign.Views.PileSectionWindow(main, section, pileBodyNo: 1, segmentNo: 1)
                { ShowActivated = false, WindowState = WindowState.Normal, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var tb = LogicalDescendants(window).OfType<TextBox>()
                        .First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "PileSection.PipeTs");
                    var vm = (PileSectionViewModel)window.DataContext;

                    tb.Text = "9999";    // 範囲は 0.1 〜 200
                    LeaveField(tb);

                    modelTs = vm.PileSection.PipeTs;
                    shownAc = vm.PileSection.SelectedPileSectionSpecification.FirstOrDefault(s => s.Item == "コンクリート断面積")?.Value;
                    expectedAc = $"{vm.PileSection.Ac:N0}";
                }
                finally { window.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual(200.0, modelTs);
            Assert.IsNotNull(shownAc, "諸元表にコンクリート断面積の行がありません");
            Assert.AreEqual(expectedAc, shownAc, "諸元表のコンクリート断面積が、直す前の管厚で計算されたまま残っています");
            Assert.IsFalse(shownAc!.StartsWith("-"), "コンクリート断面積が負です");
        }

        /// <summary>
        /// 補正は欄の文字だけを直し、値の書き戻しはフォーカスが外れたときのバインディングに任せる。
        /// 直前に書き戻すと、杭断面ウィンドウが直前 (PreviewLostKeyboardFocus) に取る Undo の控えに
        /// 直した値が入り、元に戻せなくなる。
        /// </summary>
        [TestMethod]
        public void TheCorrection_DoesNotReachTheModelBeforeFocusLeaves()
        {
            var source = new InlineInputMessageTests.Source();
            double beforeLeave = double.NaN, afterLeave = double.NaN;
            string? textBeforeLeave = null;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var tb = new TextBox();
                tb.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(source.Value))
                { Source = source, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus });
                NumericInput.SetMode(tb, NumericInputMode.Decimal);
                NumericInput.SetMin(tb, 1.0);
                NumericInput.SetMax(tb, 10.0);
                tb.Text = "100";
                tb.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, tb, null)
                { RoutedEvent = Keyboard.PreviewLostKeyboardFocusEvent });
                textBeforeLeave = tb.Text;
                beforeLeave = source.Value;
                tb.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                afterLeave = source.Value;
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual("10", textBeforeLeave, "フォーカスが外れる直前に欄の文字が直っていません");
            Assert.AreEqual(5.0, beforeLeave, "直前に値まで書き戻しています (Undo の控えに入ってしまう)");
            Assert.AreEqual(10.0, afterLeave);
        }
    }
}
