using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 解析結果の全体の検査 (数値でない値)。結果を表示・保存・検定に渡す前に、解析の終わりで 1 回行う。
    ///
    /// <para>解析が最後まで走っても、変位・反力・断面力に NaN・無限大が入ることがある (特異に近い剛性、
    /// 範囲外の入力が残った区間など)。以前は結果の表・グラフ・検定がそれぞれ個別に受け取り、どこで何が
    /// 数値でないのかは利用者に分からなかった。ケース・要素・杭番号・量を挙げて知らせる。</para>
    /// </summary>
    public static class AnalysisResultValidator
    {
        /// <summary>数値でない値の 1 件。<see cref="PileNo"/> は杭の要素・節点のときだけ。</summary>
        public sealed record Finding(string CaseName, string Target, int? PileNo, string Quantity);

        /// <summary>
        /// 解析モデルの全結果 (節点の変位・荷重、要素の変位・断面力、地盤ばね・回転ばね) から数値でない値を探す。
        /// 1 つの節点・要素・ケースにつき 1 件にまとめ、<paramref name="limit"/> 件で打ち切る (件数は <paramref name="total"/>)。
        /// </summary>
        public static List<Finding> FindNonFinite(AnaModel? model, out int total, int limit = 100)
        {
            var findings = new List<Finding>();
            int count = 0;

            void Add(LoadCase? lc, LoadCombination? comb, bool liq, string target, int? pileNo, string quantity)
            {
                count++;
                if (findings.Count < limit)
                    findings.Add(new Finding(CaseName(lc, comb, liq), target, pileNo, quantity));
            }

            foreach (var node in model?.Nodes ?? [])
            {
                if (node?.NodeResults == null) continue;
                int? pileNo = AnalysisResultTableService.PileNoOfPileNode(node);
                foreach (var r in node.NodeResults)
                {
                    if (r == null) continue;
                    if (FirstNonFinite(r.CumulativeDisp == null ? [] : Enumerable.Range(0, 6).Select(r.CumulativeDisp.GetByIndex)) is { } d)
                        Add(r.LoadCase, r.LoadCombination, r.IsLiquefaction, $"節点 {node.Name}", pileNo, $"変位 ({d})");
                    else if (r.CumulativedLoad is { } l
                             && FirstNonFinite([l.Fx, l.Fy, l.Fz, l.Mx, l.My, l.Mz]) is { } f)
                        Add(r.LoadCase, r.LoadCombination, r.IsLiquefaction, $"節点 {node.Name}", pileNo, $"荷重 ({f})");
                }
            }

            foreach (var beam in model?.Beams ?? [])
            {
                if (beam?.BeamResults == null) continue;
                int? pileNo = AnalysisResultTableService.PileNoOfPileNode(beam.NodeI);
                foreach (var r in beam.BeamResults)
                {
                    if (r == null) continue;
                    if (FirstNonFinite(Forces(r.CumulativeForce)) is { } f)
                        Add(r.LoadCase, r.LoadCombination, r.IsLiquefaction, $"要素 {beam.Name}", pileNo, $"断面力 ({f})");
                    else if (FirstNonFinite(Disps(r.CumulativeDisp)) is { } d)
                        Add(r.LoadCase, r.LoadCombination, r.IsLiquefaction, $"要素 {beam.Name}", pileNo, $"変位 ({d})");
                }
            }

            foreach (var spring in model?.HorizontalSoilSprings ?? [])
            {
                if (spring?.HorizontalSpringResults == null) continue;
                int? pileNo = AnalysisResultTableService.PileNoOfPileNode(spring.NodeI)
                              ?? AnalysisResultTableService.PileNoOfPileNode(spring.NodeJ);
                foreach (var r in spring.HorizontalSpringResults)
                {
                    if (r == null) continue;
                    if (FirstNonFinite(Forces(r.CumulativeForce).Concat(Disps(r.CumulativeDisp))) is { } v)
                        Add(r.LoadCase, r.LoadCombination, r.IsLiquefaction, $"地盤ばね {spring.Name}", pileNo, $"反力・変位 ({v})");
                }
            }

            foreach (var spring in model?.RotationalSprings ?? [])
            {
                if (spring?.RotationalSpringResults == null) continue;
                int? pileNo = AnalysisResultTableService.PileNoOfPileNode(spring.NodeI)
                              ?? AnalysisResultTableService.PileNoOfPileNode(spring.NodeJ);
                foreach (var r in spring.RotationalSpringResults)
                {
                    if (r == null) continue;
                    if (FirstNonFinite(Forces(r.CumulativeForce).Concat(Disps(r.CumulativeDisp))) is { } v)
                        Add(r.LoadCase, r.LoadCombination, r.IsLiquefaction, $"杭頭の回転ばね {spring.Name}", pileNo, $"モーメント・回転 ({v})");
                }
            }

            total = count;
            return findings;
        }

        /// <summary>知らせる文 (見つからなければ null)。ケース・対象・量を挙げ、関係する杭を添える。</summary>
        public static string? Describe(IReadOnlyList<Finding> findings, int total)
        {
            if (findings.Count == 0) return null;
            var byCase = findings.GroupBy(f => f.CaseName).ToList();
            var lines = new List<string>
            {
                $"解析結果に数値でない値 (NaN・無限大) が {total} か所あります。この値は結果の表・グラフ・検定で正しく扱えません。",
            };
            foreach (var g in byCase.Take(5))
                lines.Add($"・{g.Key}: " + string.Join("、", g.Take(3).Select(f => $"{f.Target} の{f.Quantity}"))
                          + (g.Count() > 3 ? $" ほか {g.Count() - 3} か所" : ""));
            if (byCase.Count > 5) lines.Add($"・ほか {byCase.Count - 5} ケース");
            var piles = PileNos(findings);
            if (piles.Count > 0)
                lines.Add($"関係する杭: No.{string.Join(", ", piles.Take(10))}" + (piles.Count > 10 ? $" ほか {piles.Count - 10} 本" : "")
                          + " (メイン画面で選択しています)");
            lines.Add("入力 (断面・地盤・荷重) と解析の収束を確認してください。");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 見つかった値を問題 (段階は「解析」) にする。杭の要素・節点なら杭を、そうでなければケースを場所にする。
        /// 杭の選択・ログは文を解かずにこちらを使う。
        /// </summary>
        public static List<Common.Diagnostic> ToDiagnostics(IEnumerable<Finding> findings)
            => findings.Select(f => new Common.Diagnostic(Common.DiagnosticOrigin.Analysis,
                    f.PileNo is int no
                        ? Common.DiagnosticTarget.Pile(no) with { CaseName = f.CaseName }
                        : Common.DiagnosticTarget.LoadCase(f.CaseName),
                    $"{f.CaseName}: {f.Target} の{f.Quantity}が数値ではありません")).ToList();

        /// <summary>見つかった値に関係する杭の番号 (重複なし・出た順)。</summary>
        public static IReadOnlyList<int> PileNos(IEnumerable<Finding> findings)
            => findings.Where(f => f.PileNo.HasValue).Select(f => f.PileNo!.Value).Distinct().ToList();

        private static string CaseName(LoadCase? lc, LoadCombination? comb, bool liq)
        {
            string name = lc?.LoadName ?? "(荷重ケース不明)";
            if (comb != null && !string.IsNullOrEmpty(comb.Name)) name += $" / {comb.Name}";
            return liq ? name + " / 液状化有" : name;
        }

        private static IEnumerable<double> Forces(BeamForce? f) => f == null ? []
            : [f.Fxi, f.Fyi, f.Fzi, f.Mxi, f.Myi, f.Mzi, f.Fxj, f.Fyj, f.Fzj, f.Mxj, f.Myj, f.Mzj];

        private static IEnumerable<double> Disps(BeamDisp? d) => d == null ? []
            : [d.Dxi, d.Dyi, d.Dzi, d.Rxi, d.Ryi, d.Rzi, d.Dxj, d.Dyj, d.Dzj, d.Rxj, d.Ryj, d.Rzj];

        private static string? FirstNonFinite(IEnumerable<double> values)
        {
            foreach (double v in values)
                if (!double.IsFinite(v)) return double.IsNaN(v) ? "NaN" : "無限大";
            return null;
        }
    }
}
