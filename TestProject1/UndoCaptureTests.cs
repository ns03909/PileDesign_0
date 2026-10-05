using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Common.Undo;
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
    /// 元に戻すための控えを、窓の段でまとめて取る仕組み (UndoCapture) と、同じ状態を積まない規則。
    ///
    /// 以前は控えを欄・コンボボックス・表ごとに手で付けていて、付け忘れた操作は Ctrl+Z で戻らず
    /// (杭断面の鋼管寸法、杭体の根入れ、杭頭部のコンボボックス、地盤のラジオボタンなど)、
    /// フォーカスが移るたびに何も変わっていなくても控えを積んで、押しても何も起きない「元に戻す」が混ざった。
    /// </summary>
    [TestClass]
    public class UndoCaptureTests
    {
        private sealed class Box { public int Value { get; set; } }

        [TestMethod]
        public void TheSameStateIsNotSavedTwice_AndRedoSurvivesANoOpSave()
        {
            var m = new UndoManager { StateFingerprint = UndoCapture.Fingerprint };
            m.SaveState(new Box { Value = 1 });
            m.SaveState(new Box { Value = 1 });
            Assert.AreEqual(1, m.History.Count, "同じ状態を 2 回積んでいます");

            m.SaveState(new Box { Value = 2 });
            m.Undo();
            Assert.AreEqual(1, ((Box)m.CurrentState!).Value);
            m.SaveState(new Box { Value = 1 });   // 戻したあとにフォーカスを移しただけ
            Assert.IsTrue(m.CanRedo, "何も変えていないのに、やり直せる手が消えました");
            m.Redo();
            Assert.AreEqual(2, ((Box)m.CurrentState!).Value);
        }

        /// <summary>控え方式で元に戻す画面は、すべて窓の段で控えを取る (付け忘れの網)。</summary>
        [TestMethod]
        public void EverySnapshotUndoWindow_UsesTheWindowLevelCapture()
        {
            var hosts = typeof(ISnapshotUndoHost).Assembly.GetTypes()
                .Where(t => typeof(ISnapshotUndoHost).IsAssignableFrom(t) && !t.IsInterface).Select(t => t.Name).ToList();
            TestSource.AssertScanned(hosts.Count, 7, "控え方式で元に戻す ViewModel");

            var windows = new Dictionary<string, string>
            {
                ["PileSectionViewModel"] = "PileSectionWindow.xaml",
                ["PileBodyViewModel"] = "PileBodyWindow.xaml",
                ["PileTopViewModel"] = "PileTopWindow.xaml",
                ["GroundLayerViewModel"] = "GroundWindow.xaml",
                ["SettlementViewModel"] = "SettlementWindow.xaml",
                ["ElementDivisionViewModel"] = "ElementDivisionWindow.xaml",
                ["LoadCaseViewModel"] = "LoadCaseWindow.xaml",
            };
            var missing = new List<string>();
            foreach (var host in hosts)
            {
                if (!windows.TryGetValue(host, out var xamlName)) { missing.Add($"{host}: 画面の対応がこのテストにありません"); continue; }
                string xaml = TestSource.Read("Graphics_r1", "Views", xamlName);
                if (!xaml.Contains("common:UndoCapture.Enabled=\"True\"")) missing.Add(xamlName);
            }
            Assert.AreEqual(0, missing.Count, "窓の段で控えを取っていない画面: " + string.Join(", ", missing));
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

        private static void Click(UIElement target)
            => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });

        private static void Enter(UIElement target)
            => target.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, (IInputElement)target)
            { RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent });

        /// <summary>
        /// 杭断面ウィンドウで、コンボボックス (主筋の呼び径) をマウスで変えても Ctrl+Z で戻る。
        /// 欄を渡り歩くだけ (何も変えない) では、元に戻す手が増えない。
        /// </summary>
        [TestMethod]
        public void PileSectionWindow_ComboChange_IsUndone_AndFocusMovesAddNoSteps()
        {
            string? afterUndo = null, before = null;
            int stepsBefore = -1, stepsAfterWandering = -1;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var input = new InputModel { FundamentalInput = new FundamentalInput() };
                var main = new MainWindowViewModel { CurrentInputModel = input };
                input.AttachViewModel(main);
                var section = new PileSection
                {
                    PileBodyType = PileDesign.Constants.PileTypeNames.InsituRc,
                    PileSectionType = PileDesign.Constants.PileTypeNames.RcSection,
                    ConcreteOutDia = 1500, ConcreteFc = 27, ConcreteGsi = 1.0, MainBarNum = 30, MainBarSize = "D29", MainBarSpec = "SD390",
                    MainBarCenterCover = 200, HoopSize = "D13", HoopSpacing = 150, HoopSpec = "SD295", HoopCenterCover = 150, PileDiameter = 1500,
                };
                var window = new PileDesign.Views.PileSectionWindow(main, section, 1, 1)
                { ShowActivated = false, WindowState = WindowState.Normal, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var vm = (PileSectionViewModel)window.DataContext;
                    UndoCapture.Take(window);   // 開いた直後の基準 (実機は Loaded のあと)
                    var fields = Descendants(window).OfType<TextBox>().Where(t => !t.IsReadOnly).Take(5).ToList();

                    stepsBefore = vm._undoManager.History.Count;
                    foreach (var f in fields) { Enter(f); Click(f); }
                    stepsAfterWandering = vm._undoManager.History.Count;

                    before = vm.PileSection.MainBarSize;
                    var combo = Descendants(window).OfType<ComboBox>().First();
                    Click(combo);                         // マウスで開く直前に控え
                    vm.PileSection.MainBarSize = "D35";   // 選んだ結果
                    Assert.IsTrue(UndoKeys.TryRoute(window, Key.Z, ModifierKeys.Control, null));
                    afterUndo = vm.PileSection.MainBarSize;
                }
                finally { window.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual(stepsBefore, stepsAfterWandering, "何も変えずに欄を渡り歩いただけで、元に戻す手が増えています");
            Assert.AreEqual(before, afterUndo, "コンボボックスの変更が Ctrl+Z で戻りません");
        }
    }
}
