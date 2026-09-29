using PileDesign.FEM;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 解析を最後まで走らせたときの、ケースごとの状態と全体の状態をそろえて知らせる。
    ///
    /// <para>以前は、未収束・物理的未収束のケースがあっても、処理の一部に失敗して続けたケースがあっても、
    /// 完了の知らせは「計算が終了しました」だけで、内訳は解析ログをさかのぼらないと分からなかった。
    /// 「一部に問題があったが全体は完了した」ことを、ケース名とともに完了の知らせに出す。</para>
    /// </summary>
    public static class AnalysisRunOutcome
    {
        /// <summary>
        /// 完了の知らせの文。<paramref name="needsAttention"/> は、収束していない・緩めて受理した・続けた失敗の
        /// どれかがあるとき true (知らせを警告として出す)。
        /// </summary>
        public static string Describe(IEnumerable<AnalysisStepResult>? steps, IReadOnlyCollection<string> notices, out bool needsAttention)
        {
            // ケースごとに最も悪いステップの状態 (途中が収束していなければ、その先は釣り合っていない状態の上に積み上がる)
            var cases = (steps ?? [])
                .Where(s => s != null)
                .GroupBy(s => AnaModel.CaseConvergenceKey(s.LoadCase, s.LoadCombination, s.IsLiquefaction))
                .Select(g => (Name: CaseName(g.First()), Status: g.Max(s => s.Status)))
                .ToList();

            var lines = new List<string> { "計算が終了しました。" };
            int relaxed = cases.Count(c => c.Status == StepStatus.ConvergedRelaxed);
            var unconverged = cases.Where(c => c.Status == StepStatus.Unconverged).Select(c => c.Name).ToList();
            var physical = cases.Where(c => c.Status == StepStatus.PhysicallyUnconverged).Select(c => c.Name).ToList();

            if (cases.Count > 0)
            {
                if (relaxed == 0 && unconverged.Count == 0 && physical.Count == 0)
                    lines.Add($"全 {cases.Count} ケースが収束しました。");
                else
                {
                    lines.Add($"{cases.Count} ケースのうち、収束 {cases.Count - relaxed - unconverged.Count - physical.Count}・"
                              + $"緩めた基準で受理 {relaxed}・未収束 {unconverged.Count}・物理的未収束 {physical.Count}。");
                    if (unconverged.Count > 0) lines.Add("未収束: " + List(unconverged));
                    if (physical.Count > 0) lines.Add("物理的未収束 (耐力を超えている可能性): " + List(physical));
                    if (unconverged.Count > 0 || physical.Count > 0)
                        lines.Add("収束していないケースの結果も登録します (表・グラフで見られます) が、"
                                  + "検定では「未収束」として OK にも NG にも数えません。");
                }
            }

            if (notices.Count > 0)
            {
                lines.Add("");
                lines.Add("解析は続けましたが、次の処理に失敗しています:");
                lines.AddRange(notices.Take(8).Select(n => "・" + n));
                if (notices.Count > 8) lines.Add($"…ほか {notices.Count - 8} 件 (解析ログを参照してください)");
            }

            needsAttention = relaxed > 0 || unconverged.Count > 0 || physical.Count > 0 || notices.Count > 0;
            return string.Join("\n", lines);
        }

        private static string CaseName(AnalysisStepResult s)
        {
            string name = s.LoadCase?.LoadName ?? "(荷重ケース不明)";
            if (s.LoadCombination != null && !string.IsNullOrEmpty(s.LoadCombination.Name)) name += $" / {s.LoadCombination.Name}";
            return s.IsLiquefaction ? name + " / 液状化有" : name;
        }

        private static string List(List<string> names)
            => string.Join("、", names.Take(5)) + (names.Count > 5 ? $" ほか {names.Count - 5} ケース" : "");
    }
}
