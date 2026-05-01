using PileDesign.FEM;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Views
{
    /// <summary>
    /// 解析結果ダッシュボード (E.18)。
    /// 現在の InputModel / AnaModel / VerticalBeamCaseResults の状態をカード形式で
    /// 一画面に集約表示する。実行中アプリの「全体像把握」が目的。
    ///
    /// 解析中の数値ではなく、解析完了後に呼び出す前提のスナップショット表示。
    /// 「再計算」ボタンで開いたまま現在の状態を再読込できる。
    /// </summary>
    public partial class ResultDashboardWindow : Window
    {
        private readonly MainWindowViewModel _vm;

        public ResultDashboardWindow(MainWindowViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            InitializeComponent();
            Loaded += (_, __) => Refresh();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            LastUpdateText.Text = $"更新: {DateTime.Now:HH:mm:ss}";

            UpdateModelSection();
            UpdateHorizontalSection();
            UpdateSettlementSection();
            UpdateVerticalBeamSection();
            UpdateFileSection();
        }

        // ---- 解析モデル ----------------------------------------------------
        private void UpdateModelSection()
        {
            var im = _vm.CurrentInputModel;
            var am = _vm.CurrentModel;

            PileCountText.Text = im?.PileLayoutItems is { } piles ? $"{piles.Count} 本" : "—";
            InputNodeCountText.Text = im?.InputNodes is { } inodes ? $"{inodes.Count} 個" : "—";
            FemNodeCountText.Text = am?.Nodes is { } nodes ? $"{nodes.Count:N0} 個" : "—";
            FemBeamCountText.Text = am?.Beams is { } beams ? $"{beams.Count:N0} 本" : "—";
        }

        // ---- 水平解析 ------------------------------------------------------
        private void UpdateHorizontalSection()
        {
            var am = _vm.CurrentModel;
            var steps = am?.AnalysisStepResults;
            bool done = _vm.IsHorizontalAnalysisDone;

            HorizontalStatusText.Text = done ? "✓ 完了" : (steps?.Count > 0 ? "△ 実施済 (未確定)" : "未実施");
            HorizontalStatusText.Style = done
                ? (Style)FindResource("DashOkStyle")
                : (Style)FindResource("DashMutedStyle");

            if (steps == null || steps.Count == 0)
            {
                HorizontalCaseCountText.Text = "—";
                HorizontalStepCountText.Text = "—";
                HorizontalAvgIterText.Text = "—";
                HorizontalMaxIterText.Text = "—";
                HorizontalWorstResidualText.Text = "—";
                HorizontalMaxDispText.Text = "—";
                return;
            }

            // ケース数 = (LoadCase, LoadCombination, IsLiquefaction) の組合せ数
            int caseCount = steps
                .Select(r => (r.LoadCase?.Level ?? 0, r.LoadCase?.No ?? 0,
                              r.LoadCombination?.No ?? 0, r.IsLiquefaction))
                .Distinct()
                .Count();

            HorizontalCaseCountText.Text = $"{caseCount}";
            HorizontalStepCountText.Text = $"{steps.Count:N0}";

            double avgIter = steps.Average(r => r.Iteration);
            int maxIter = steps.Max(r => r.Iteration);
            double worstRes = steps.Max(r => r.ResidualValue);

            HorizontalAvgIterText.Text = $"{avgIter:F1}";
            HorizontalMaxIterText.Text = $"{maxIter}";
            HorizontalWorstResidualText.Text = worstRes.ToString("E2");
            HorizontalWorstResidualText.Style = worstRes > 1.0E-3
                ? (Style)FindResource("DashWarnStyle")
                : (Style)FindResource("DashValueStyle");

            // 代表変位 (m): 累積変位の絶対値最大
            double maxAbs = 0;
            if (am?.Nodes != null)
            {
                foreach (var n in am.Nodes)
                {
                    var d = n.CumulativeDisp;
                    if (d == null) continue;
                    double a = Math.Max(Math.Abs(d.Ux), Math.Max(Math.Abs(d.Uy), Math.Abs(d.Uz)));
                    if (a > maxAbs) maxAbs = a;
                }
            }
            HorizontalMaxDispText.Text = maxAbs > 0 ? $"{maxAbs:F4}" : "—";
        }

        // ---- 沈下解析 ------------------------------------------------------
        private void UpdateSettlementSection()
        {
            bool done = _vm.IsVerticalAnalysisDone;
            SettlementStatusText.Text = done ? "✓ 完了" : "未実施";
            SettlementStatusText.Style = done
                ? (Style)FindResource("DashOkStyle")
                : (Style)FindResource("DashMutedStyle");
        }

        // ---- 基礎梁考慮鉛直解析 --------------------------------------------
        private void UpdateVerticalBeamSection()
        {
            bool done = _vm.IsVerticalBeamAnalysisDone;
            VerticalBeamStatusText.Text = done ? "✓ 完了" : "未実施";
            VerticalBeamStatusText.Style = done
                ? (Style)FindResource("DashOkStyle")
                : (Style)FindResource("DashMutedStyle");

            int caseCount = _vm.VerticalBeamCaseResults?.Count ?? 0;
            VerticalBeamCaseCountText.Text = caseCount > 0 ? $"{caseCount}" : "—";
        }

        // ---- ファイル ------------------------------------------------------
        private void UpdateFileSection()
        {
            CurrentFileText.Text = string.IsNullOrEmpty(_vm.CurrentFilePath)
                ? "(新規)"
                : Path.GetFileName(_vm.CurrentFilePath);
            CurrentFileText.ToolTip = _vm.CurrentFilePath;

            LastAnalysisText.Text = _vm.LastAnalysisTime is { } t
                ? t.ToString("yyyy-MM-dd HH:mm:ss")
                : "—";
        }
    }
}
