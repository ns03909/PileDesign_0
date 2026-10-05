using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TestProject1
{
    /// <summary>
    /// 入力欄にフォーカスがあっても、窓の Ctrl+Z / Ctrl+Y が効くこと (UndoKeys)。
    ///
    /// TextBox は取り消すものが無くても Ctrl+Z を処理済みにするので、窓の KeyBinding は欄にフォーカスが
    /// ある間は届かなかった。杭断面ウィンドウで「Ctrl+Z で元に戻らない」と実機で報告があった。
    /// あわせて、杭断面ウィンドウの鋼管の外径・管厚・腐食深さは Undo の控えを取っていなかった。
    /// </summary>
    [TestClass]
    public class UndoKeysTests
    {
        private sealed class Counter : ICommand
        {
            public int Count;
            public bool Enabled = true;
            public event System.EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => Enabled;
            public void Execute(object? parameter) => Count++;
        }

        private static void Run(System.Action action)
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
        }

        public sealed class Source { public double Value { get; set; } = 5; }

        [TestMethod]
        public void CtrlZ_InAFieldWithNothingTyped_GoesToTheWindowsUndo()
        {
            int undo = -1, redo = -1;
            bool routed = false, typingKept = true, otherKey = true;
            Run(() =>
            {
                var u = new Counter(); var r = new Counter();
                var w = new Window();
                w.InputBindings.Add(new KeyBinding(u, Key.Z, ModifierKeys.Control));
                w.InputBindings.Add(new KeyBinding(r, Key.Y, ModifierKeys.Control));
                var tb = new TextBox();
                tb.SetBinding(TextBox.TextProperty, new Binding(nameof(Source.Value)) { Source = new Source(), UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });

                routed = UndoKeys.TryRoute(w, Key.Z, ModifierKeys.Control, tb);
                UndoKeys.TryRoute(w, Key.Y, ModifierKeys.Control, tb);

                // 打ちかけ (書き戻す前) で欄に取り消せる打鍵があれば、欄の文字の取り消しに任せる
                tb.Text = "12";
                tb.Text = "123";
                typingKept = !tb.CanUndo || !UndoKeys.TryRoute(w, Key.Z, ModifierKeys.Control, tb);

                otherKey = UndoKeys.TryRoute(w, Key.Z, ModifierKeys.Control | ModifierKeys.Shift, tb);
                undo = u.Count; redo = r.Count;
            });
            Assert.IsTrue(routed, "欄にフォーカスがあると窓の Undo に届きません");
            Assert.AreEqual(1, undo, "打ちかけの欄の Ctrl+Z まで窓の Undo に取っています");
            Assert.AreEqual(1, redo);
            Assert.IsTrue(typingKept, "打ちかけの欄の取り消しを奪っています");
            Assert.IsFalse(otherKey, "Ctrl+Shift+Z まで受けています");
        }

        /// <summary>実機で報告された形: 管厚 16 → 9999 (200 に補正) のあと Ctrl+Z で 16 に戻る。知らせの文も消える。</summary>
        [TestMethod]
        public void PileSectionWindow_CtrlZ_RestoresTheWallThickness()
        {
            double afterEdit = double.NaN, afterUndo = double.NaN, diaAfterUndo = double.NaN;
            string? noticeAfterUndo = "未設定";
            Run(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var input = new InputModel { FundamentalInput = new FundamentalInput() };
                var main = new MainWindowViewModel { CurrentInputModel = input };
                input.AttachViewModel(main);
                var section = new PileSection
                {
                    PileBodyType = PileDesign.Constants.PileTypeNames.InsituSteelPipeConcrete,
                    PileSectionType = PileDesign.Constants.PileTypeNames.SteelPipeConcreteSection,
                    PipeDia = 1200, PipeTs = 16, PipeGrade = "SKK490", CorrosionDepth = 1.0,
                    ConcreteFc = 27, ConcreteGsi = 1.0, MainBarNum = 16, MainBarSize = "D29", MainBarSpec = "SD390", MainBarCenterCover = 200,
                    HoopSize = "D13", HoopSpacing = 150, HoopSpec = "SD295", HoopCenterCover = 150,
                };
                var window = new PileDesign.Views.PileSectionWindow(main, section, pileBodyNo: 1, segmentNo: 1)
                { ShowActivated = false, WindowState = WindowState.Normal, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var vm = (PileSectionViewModel)window.DataContext;
                    TextBox Field(string path) => Descendants(window).OfType<TextBox>()
                        .First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == path);

                    var dia = Field("PileSection.PipeDia");
                    dia.Text = "1500";
                    NumericCorrectionRedrawTests.LeaveField(dia);

                    var ts = Field("PileSection.PipeTs");
                    ts.Text = "9999";
                    NumericCorrectionRedrawTests.LeaveField(ts);
                    afterEdit = vm.PileSection.PipeTs;

                    Assert.IsTrue(UndoKeys.TryRoute(window, Key.Z, ModifierKeys.Control, ts), "窓の Undo に届きません");
                    afterUndo = vm.PileSection.PipeTs;
                    noticeAfterUndo = NumericInput.GetCorrectionNotice(ts);

                    UndoKeys.TryRoute(window, Key.Z, ModifierKeys.Control, ts);
                    diaAfterUndo = vm.PileSection.PipeDia;
                }
                finally { window.Close(); }
            });
            Assert.AreEqual(200.0, afterEdit, "前提: 200 に補正される");
            Assert.AreEqual(16.0, afterUndo, "Ctrl+Z で管厚が元に戻りません");
            Assert.IsNull(noticeAfterUndo, "戻したあとも「200 にしました」が残っています");
            Assert.AreEqual(1200.0, diaAfterUndo, "2 回目の Ctrl+Z で外径が元に戻りません (外径の編集の控えが無い)");
        }

        /// <summary>Ctrl+Z を KeyBinding で持つ画面は、欄からも届くようにしてあること。</summary>
        [TestMethod]
        public void EveryWindowWithAnUndoKeyBinding_RoutesFromTextBoxes()
        {
            // 個別に PreviewKeyDown で受けている画面と、メイン画面 (キーの扱いが別) は除く
            var handledElsewhere = new HashSet<string> { "ChangWindow.xaml", "GroundWindow.xaml", "MainWindow.xaml" };
            var dir = TestSource.Dir("Graphics_r1", "Views");
            var missing = new List<string>();
            int scanned = 0;
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.xaml"))
            {
                string name = System.IO.Path.GetFileName(file);
                string xaml = System.IO.File.ReadAllText(file);
                if (!xaml.Contains("<KeyBinding Key=\"Z\" Modifiers=\"Control\"")) continue;
                scanned++;
                if (handledElsewhere.Contains(name)) continue;
                if (!xaml.Contains("common:UndoKeys.RouteFromTextBoxes=\"True\"")) missing.Add(name);
            }
            TestSource.AssertScanned(scanned, 10, "Ctrl+Z の KeyBinding を持つ画面");
            Assert.AreEqual(0, missing.Count, "欄にフォーカスがあると Ctrl+Z が届かない画面: " + string.Join(", ", missing));

            string section = TestSource.Read("Graphics_r1", "Views", "PileSectionWindow.xaml");
            Assert.IsFalse(section.Contains("TextBox_PreviewLostKeyboardFocus"), "Undo の控えは窓でまとめて取る (欄ごとに付けると付け忘れる)");
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                yield return d;
                foreach (var g in Descendants(d)) yield return g;
            }
        }
    }
}
