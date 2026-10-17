using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 検討に要る解析 1 つの状態 (工程の帯の「解析」)。<c>Done</c> は結果を持っているか、
    /// <c>Stale</c> はその結果が入力の変更前のものか。
    /// </summary>
    public readonly record struct RequiredAnalysisState(string Name, bool Done, bool Stale)
    {
        public string StateText => !Done ? "未" : Stale ? "やり直し" : "済み";
    }

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
            OpenResultDashboardCommand, OpenDocxOutputWindowCommand, _workflowInputCheck,
            RequiredAnalyses, CurrentInputModel?.FundamentalInput?.SettlementDesignBasisName);

        /// <summary>
        /// 検討に要る解析とその状態 (工程の帯の「解析」)。水平解析と単杭沈下解析は常に、群杭沈下解析は
        /// 基本設定の「沈下検討の対象」が「単杭＋群杭沈下」のときだけ。
        /// 単杭沈下は基礎梁考慮の解析でも済み、群杭沈下は基礎梁なし・ありのどちらかで済み。
        /// 以前の帯は水平解析だけを見ており、沈下の解析が未実施・入力の変更前でも「解析 済み」と出ていた。
        /// </summary>
        internal IReadOnlyList<RequiredAnalysisState> RequiredAnalyses
        {
            get
            {
                var list = new List<RequiredAnalysisState>
                {
                    new("水平", IsHorizontalAnalysisDone, IsHorizontalResultStale),
                    new("単杭沈下", IsVerticalAnalysisDone || IsVerticalBeamAnalysisDone,
                        IsSettlementResultStale(SettlementAnalyses.SinglePile | SettlementAnalyses.VerticalBeam)),
                };
                if (CurrentInputModel?.FundamentalInput?.SettlementDesignIncludesGroup ?? true)
                    list.Add(new("群杭沈下", IsGroupPileSettlementAnalysisDone || HasGroupSettlementBeamAwareCases,
                        IsSettlementResultStale(SettlementAnalyses.Group | SettlementAnalyses.GroupWithBeam)));
                return list;
            }
        }

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
            InputCheckCounts? inputCheck = null, IReadOnlyList<RequiredAnalysisState>? analyses = null, string? settlementBasis = null)
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
            };
            if (analyses == null)
            {
                // 水平解析だけで決める (以前の帯。解析の一覧を渡さない呼び出し)
                steps.Add(new("解析", !analyzed ? "未" : analysisStale ? "やり直しが必要" : "済み",
                    !analyzed ? (elementSplit ? WorkflowStepStatus.Next : WorkflowStepStatus.NotYet)
                        : analysisStale ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Done,
                    analysisStale
                        ? "解析のあとに入力が変更されています。押すと水平解析の画面を開きます (F5)"
                        : "水平解析を実行します。押すと水平解析の画面を開きます (F5)", analysis));
                steps.Add(new("検定", analyzed ? (analysisStale ? "入力変更前の結果" : "結果あり") : "解析後",
                    !analyzed ? WorkflowStepStatus.NotYet : analysisStale ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Done,
                    "検定の結果 (OK / NG・支配ケース・杭ごとの検定比) を確かめます。押すとダッシュボードを開きます", evaluation));
                steps.Add(new("計算書", analyzed ? (analysisStale ? "入力変更前の結果" : "出力できます") : "解析後",
                    !analyzed ? WorkflowStepStatus.NotYet : analysisStale ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Next,
                    "計算書 (Word) を出力します。押すと計算書の出力の画面を開きます", report));
                return steps;
            }

            // 検討に要る解析ごとに状態を出す。帯の色は一番悪いもの (やり直し > 未 > 済み)
            bool anyDone = false, allDone = true, anyStale = false;
            foreach (var a in analyses)
            {
                anyDone |= a.Done;
                allDone &= a.Done;
                anyStale |= a.Done && a.Stale;
            }
            string analysisText = string.Join("・", analyses.Select(a => $"{a.Name} {a.StateText}"));
            string basis = string.IsNullOrEmpty(settlementBasis) ? "" : $"沈下検討の対象は「{settlementBasis}」です (基本設定)。";
            string analysisTip = "検討に要る解析: " + string.Join("、", analyses.Select(a => $"{a.Name}解析 {a.StateText}")) + "。" + basis
                + (anyStale ? "「やり直し」の解析は、そのあとに入力が変更されています。" : "")
                + "押すと水平解析の画面を開きます (F5)。沈下の解析はリボン「解析条件/解析」から実行します";
            steps.Add(new("解析", analysisText,
                anyStale ? WorkflowStepStatus.NeedsRedo
                    : allDone ? WorkflowStepStatus.Done
                    : elementSplit ? WorkflowStepStatus.Next : WorkflowStepStatus.NotYet,
                analysisTip, analysis));
            steps.Add(new("検定",
                !anyDone ? "解析後" : anyStale ? "入力変更前の結果あり" : allDone ? "結果あり" : "一部の解析のみ",
                !anyDone ? WorkflowStepStatus.NotYet : anyStale ? WorkflowStepStatus.NeedsRedo
                    : allDone ? WorkflowStepStatus.Done : WorkflowStepStatus.Next,
                "検定の結果 (OK / NG・支配ケース・杭ごとの検定比) を確かめます。押すとダッシュボードを開きます"
                    + (anyDone && !allDone ? "。未実施の解析の検定は、検定できない項目として出ます" : ""), evaluation));
            steps.Add(new("計算書",
                !anyDone ? "解析後" : anyStale ? "入力変更前の結果あり" : allDone ? "出力できます" : "未実施の解析あり",
                !anyDone ? WorkflowStepStatus.NotYet : anyStale ? WorkflowStepStatus.NeedsRedo : WorkflowStepStatus.Next,
                "計算書 (Word) を出力します。押すと計算書の出力の画面を開きます"
                    + (anyDone && !allDone ? "。未実施の解析の章は省かれます" : ""), report));
            return steps;
        }
    }
}
