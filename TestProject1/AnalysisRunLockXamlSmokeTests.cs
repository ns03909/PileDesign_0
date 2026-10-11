using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 解析の窓で、解析の設定を変える欄が実行中は触れなくなること (2026-10-11)。
    ///
    /// <para>以前は実行中も触れ、解析は設定をケースごと・ステップごと・反復ごとに読み直していたので、
    /// 途中で変えると以降だけ別の設定で解かれた (<see cref="RunSettingsSnapshotTests"/>)。
    /// 解析側は開始時の写しだけを読むように直したうえで、欄も実行中は無効にする。</para>
    ///
    /// <para>窓の中で設定のプロパティに結び付いた入力欄をすべて拾い、どれも実行中は無効になる祖先
    /// (<c>IsAnalysisRunning</c> で無効にする枠) の中にあることを見る。あとから欄を足して枠の外に置くと落ちる。
    /// XAML の StaticResource の誤り (コンバーターのキー) もここで踏む。</para>
    /// </summary>
    [TestClass]
    public class AnalysisRunLockXamlSmokeTests
    {
        private static readonly DependencyProperty[] InputProperties =
        [
            TextBox.TextProperty, ToggleButton.IsCheckedProperty, Selector.SelectedItemProperty,
        ];

        private static void PumpDispatcher()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        /// <summary>論理ツリーをたどって、設定のプロパティに結び付いた入力欄を集める。</summary>
        private static List<(FrameworkElement Control, string Path)> SettingControls(DependencyObject root, ISet<string> settings)
        {
            var found = new List<(FrameworkElement, string)>();
            var stack = new Stack<object>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                if (stack.Pop() is not DependencyObject d) continue;
                if (d is FrameworkElement fe)
                {
                    foreach (var dp in InputProperties)
                    {
                        if (BindingOperations.GetBinding(fe, dp) is { } b && b.Path?.Path is { } path && settings.Contains(path))
                            found.Add((fe, path));
                    }
                }
                foreach (var child in LogicalTreeHelper.GetChildren(d)) stack.Push(child);
            }
            return found;
        }

        /// <summary>自分か論理ツリー上の祖先のどれかが無効か (無効は子へ伝わる)。</summary>
        private static bool IsDisabledByAncestorOrSelf(DependencyObject d)
        {
            for (var cur = d; cur != null; cur = LogicalTreeHelper.GetParent(cur))
                if (cur is UIElement ui && !ui.IsEnabled) return true;
            return false;
        }

        private static void AssertSettingsLockWhileRunning(
            string name, Func<(Window Window, Action<bool> SetRunning)> factory, string[] settings, int minControls, params string[] panels)
        {
            string? failure = null;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var (window, setRunning) = factory();
                try
                {
                    PumpDispatcher();
                    var controls = SettingControls(window, new HashSet<string>(settings));
                    if (controls.Count < minControls)
                    {
                        failure = $"設定の欄が {controls.Count} 個しか見つかりません (下限 {minControls})。検査の当て先を見直すこと";
                        return;
                    }
                    foreach (var p in panels)
                    {
                        if (window.FindName(p) is not UIElement panel) { failure = $"枠 {p} が見つかりません"; return; }
                        if (!panel.IsEnabled) { failure = $"解析していないのに枠 {p} が無効です"; return; }
                    }

                    setRunning(true);
                    PumpDispatcher();
                    var unlocked = controls.Where(c => !IsDisabledByAncestorOrSelf(c.Control)).Select(c => c.Path).Distinct().ToList();
                    if (unlocked.Count > 0)
                    {
                        failure = $"解析の実行中も触れる設定の欄があります: {string.Join(", ", unlocked)}";
                        return;
                    }

                    setRunning(false);
                    PumpDispatcher();
                    foreach (var p in panels)
                        if (window.FindName(p) is UIElement panel && !panel.IsEnabled)
                            failure = $"解析を終えても枠 {p} が無効のままです";
                }
                finally { window.Close(); }
            }, out bool timedOut);

            if (timedOut) { Assert.Inconclusive($"{name} の XAML パースが 60 秒以内に完了しなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{name}: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            if (failure != null) Assert.Fail($"{name}: {failure}");
        }

        [TestMethod]
        public void HorizontalCalculationWindow_LocksTheRunSettingsWhileRunning()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (inputModel == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            AssertSettingsLockWhileRunning("HorizontalCalculationWindow", () =>
            {
                var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = inputModel };
                var vm = new PileDesign.ViewModels.HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };
                var window = new PileDesign.Views.HorizontalCalculationWindow { DataContext = vm };
                return (window, running => vm.IsAnalysisRunning = running);
            },
            [
                "ConnectionMode", "UseAnalysisAxialForce", "UsePsSpringAtPileTip", "IsVLAnalysisEnabled", "RestrainFoundationTorsion",
                "LiquefactionOption", "Level1CalculationStepsCount", "Level2CalculationStepsCount",
                "UseModifiedNewtonRaphson", "FullNRIterations", "SelectedConvergenceMethod", "RelaxationFactor", "SkipIteration",
                "MaxCaseDegreeOfParallelism", "StopONMaxDisplacement", "MaxAllowedDisplacement",
            ],
            minControls: 20, "LoadSettingsPanel", "ConvergenceSettingsPanel");
        }

        [TestMethod]
        public void VerticalBeamCalculationWindow_LocksTheRunSettingsWhileRunning()
        {
            AssertSettingsLockWhileRunning("VerticalBeamCalculationWindow", () =>
            {
                var vm = new PileDesign.ViewModels.VerticalBeamCalculationViewModel(new PileDesign.ViewModels.MainWindowViewModel());
                var window = new PileDesign.Views.VerticalBeamCalculationWindow { DataContext = vm };
                return (window, running => vm.IsAnalysisRunning = running);
            },
            ["AnalyzeVL", "AnalyzeLevel1", "AnalyzeLevel2", "LoadStepsCount", "MaxIterations", "ConvergenceTolerance", "RelaxationFactor"],
            minControls: 7, "RunSettingsPanel");
        }

        [TestMethod]
        public void GroupSettlementWithBeamWindow_LocksTheRunSettingsWhileRunning()
        {
            AssertSettingsLockWhileRunning("GroupSettlementWithBeamWindow", () =>
            {
                var vm = new PileDesign.ViewModels.GroupSettlementWithBeamCalculationViewModel(new PileDesign.ViewModels.MainWindowViewModel());
                var window = new PileDesign.Views.GroupSettlementWithBeamWindow { DataContext = vm };
                return (window, running => vm.IsAnalysisRunning = running);
            },
            ["LoadSource", "AnalyzeVL", "AnalyzeLevel1", "AnalyzeLevel2", "MaxIterations", "ConvergenceTolerance", "KMin", "KMax"],
            minControls: 8, "RunSettingsPanel");
        }
    }
}
