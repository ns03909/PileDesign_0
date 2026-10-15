using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    /// <summary>工程の状態。帯の色と記号を決める。</summary>
    public enum WorkflowStepStatus
    {
        /// <summary>済み</summary>
        Done,
        /// <summary>済んだが、そのあとの入力の変更でやり直しが要る</summary>
        NeedsRedo,
        /// <summary>次にすること (前の工程が済んでいる)</summary>
        Next,
        /// <summary>まだ (前の工程が済んでいない)</summary>
        NotYet,
    }

    /// <summary>工程の帯の 1 つ。押すと <see cref="Command"/> でその画面を開く。</summary>
    public sealed record WorkflowStep(string Name, string StateText, WorkflowStepStatus Status, string ToolTip, ICommand? Command)
    {
        public string Mark => Status switch
        {
            WorkflowStepStatus.Done => "✓",
            WorkflowStepStatus.NeedsRedo => "!",
            WorkflowStepStatus.Next => "▶",
            _ => "・",
        };
    }

    /// <summary>入力の検査の件数 (「入力の診断」と同じ検査)。解析を止めるものと、注意 (結果に影響しうる) の数。</summary>
    public readonly record struct InputCheckCounts(int Blockers, int Warnings);

    /// <summary>
    /// MainWindowViewModel partial: 工程の帯 (入力 → 杭要素分割 → 解析 → 検定 → 計算書)。
    ///
    /// 初めて使うときに「次に何をすればよいか」が分からないので、工程と完了の状態を 1 本の帯で見せ、
    /// 押すとその工程の画面を開く。状態は既存の印 (杭の本数・杭要素分割済み・水平解析済み・入力の変更で古くなったか) から決める。
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>ダッシュボードを開く (メイン画面が設定する。ダッシュボードはメイン画面が持つ)。</summary>
        public Action? OpenResultDashboardAction { get; set; }

        [RelayCommand]
        private void OpenResultDashboard() => OpenResultDashboardAction?.Invoke();

        /// <summary>工程の帯。工程の印が変わったときに作り直して知らせる (<see cref="OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs)"/>)。</summary>
        public IReadOnlyList<WorkflowStep> WorkflowSteps => BuildWorkflowSteps(
            CurrentInputModel?.PileLayoutItems?.Count ?? 0, IsElementSplit, IsHorizontalAnalysisDone, IsHorizontalResultStale,
            OpenDiagnosticListCommand, OpenElementDivisionWindowCommand, OpenLateralLoadAnalysisWindowCommand,
            OpenResultDashboardCommand, OpenDocxOutputWindowCommand, _workflowInputCheck);

        // ── 入力の検査の件数 (工程の帯の「入力」) ─────────────────────────────

        private InputCheckCounts? _workflowInputCheck;
        private System.Windows.Threading.DispatcherTimer? _workflowInputCheckTimer;

        /// <summary>
        /// 入力を編集したら、少し待ってから入力の検査を数え直す (打つたびに検査を回さない)。
        /// 以前の帯は杭が 1 本でもあれば「入力」を済みにしており、地盤・荷重などが足りなくても次の工程へ進むように見えた。
        /// </summary>
        internal void RequestWorkflowInputCheck()
        {
            if (System.Windows.Application.Current == null) return;   // 画面の無い実行 (テスト) では数えない
            Debounce(ref _workflowInputCheckTimer, 500, RunWorkflowInputCheck);
        }

        /// <summary>入力の検査 (「入力の診断」と同じ) を数え、工程の帯を作り直す。</summary>
        internal void RunWorkflowInputCheck()
        {
            var input = CurrentInputModel;
            InputCheckCounts? counts = null;
            if (input != null && (input.PileLayoutItems?.Count ?? 0) > 0)
            {
                try
                {
                    int blockers = Services.CheckInputData.CollectAnalysisBlockers(input).Count;
                    int warnings = Services.CheckInputData.CollectInputWarningDiagnostics(input).Count;
                    counts = new InputCheckCounts(blockers, warnings);
                }
                catch (Exception ex)
                {
                    // 数えられなければ件数を出さない (帯は杭の本数だけで決める。以前と同じ)
                    Serilog.Log.Warning(ex, "[工程の帯] 入力の検査を数えられませんでした");
                }
            }
            if (_workflowInputCheck == counts) return;
            _workflowInputCheck = counts;
            OnPropertyChanged(nameof(WorkflowSteps));
        }

        /// <summary>
        /// 工程の状態を決める (純粋関数)。次にすることは、まだ済んでいない最初の工程。
        /// <paramref name="inputCheck"/> は入力の検査の件数 (まだ数えていなければ null)。解析を止める入力が残っていれば
        /// 「入力」は済みにせず「!」で件数を示す。注意だけなら済みにして件数を添える。
        /// </summary>
        internal static IReadOnlyList<WorkflowStep> BuildWorkflowSteps(
            int pileCount, bool elementSplit, bool analyzed, bool analysisStale,
            ICommand? input, ICommand? split, ICommand? analysis, ICommand? evaluation, ICommand? report,
            InputCheckCounts? inputCheck = null)
        {
            int blockers = pileCount > 0 ? inputCheck?.Blockers ?? 0 : 0;
            int warnings = pileCount > 0 ? inputCheck?.Warnings ?? 0 : 0;
            bool inputDone = pileCount > 0 && blockers == 0;
            string inputState = pileCount == 0 ? "杭が未入力"
                : blockers > 0 ? $"杭 {pileCount} 本・解析を止める {blockers} 件"
                : warnings > 0 ? $"杭 {pileCount} 本・注意 {warnings} 件"
                : $"杭 {pileCount} 本";
            string inputTip = "地盤・杭体・杭配置・荷重を入力します。押すと「入力の診断」を開き、足りない・おかしい入力を一覧します";
            if (blockers > 0) inputTip = $"解析を止める入力が {blockers} 件あります。" + inputTip;
            else if (warnings > 0) inputTip = $"結果に影響しうる注意が {warnings} 件あります。" + inputTip;
            var steps = new List<WorkflowStep>
            {
                new("入力", inputState,
                    inputDone ? WorkflowStepStatus.Done : pileCount > 0 ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Next,
                    inputTip, input),
                new("杭要素分割", elementSplit ? "済み" : "未",
                    elementSplit ? WorkflowStepStatus.Done : inputDone ? WorkflowStepStatus.Next : WorkflowStepStatus.NotYet,
                    "杭を解析の要素に分けます (F4)。押すと杭要素分割の画面を開きます", split),
                new("解析", !analyzed ? "未" : analysisStale ? "やり直しが必要" : "済み",
                    !analyzed ? (elementSplit ? WorkflowStepStatus.Next : WorkflowStepStatus.NotYet)
                        : analysisStale ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Done,
                    analysisStale
                        ? "解析のあとに入力が変更されています。押すと水平解析の画面を開きます (F5)"
                        : "水平解析を実行します。押すと水平解析の画面を開きます (F5)", analysis),
                new("検定", analyzed ? (analysisStale ? "入力変更前の結果" : "結果あり") : "解析後",
                    !analyzed ? WorkflowStepStatus.NotYet : analysisStale ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Done,
                    "検定の結果 (OK / NG・支配ケース・杭ごとの検定比) を確かめます。押すとダッシュボードを開きます", evaluation),
                new("計算書", analyzed ? (analysisStale ? "入力変更前の結果" : "出力できます") : "解析後",
                    !analyzed ? WorkflowStepStatus.NotYet : analysisStale ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Next,
                    "計算書 (Word) を出力します。押すと計算書の出力の画面を開きます", report),
            };
            return steps;
        }
    }
}
