using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 解析結果を (荷重ケース, 荷重組合せ, 液状化, ステップ) で引く索引 (<see cref="ResultLookup{T}"/>)。
    ///
    /// 以前は同じ照合が 4 通りに書かれていた。<c>Beam.GetBeamResult</c> / <c>Node.GetNodeResult</c> /
    /// <c>AnaModel.GetAnalysisLastStep</c> と計算書の索引は、指定した液状化の状態に結果が無いと
    /// <b>黙って逆の状態の結果を返し</b>、検定 (FirstOrDefault) は「結果なし」とした。
    /// そのため画面の切替が「液状化」のままキャンバス・グラフに非液状化の値が出て、検定・計算書と食い違った。
    /// また最終ステップを求めるたびに全ステップの結果を走査していた。
    /// </summary>
    [TestClass]
    public class ResultIndexTests
    {
        private static AnaModel? _model;
        private static string? _skipReason;

        /// <summary>計算例9 を液状化の考慮・非考慮の両方で解析したモデル (テストの間で共有。書き換えるテストは複製を使う)。</summary>
        private static AnaModel Model()
        {
            if (_model != null) return _model;
            if (_skipReason != null) Assert.Inconclusive(_skipReason);
            try
            {
                var vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4, Level2Steps = 8, UseLineSearch = true, Parallelism = 2,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Both,
                });
                _model = vm.CurrentModel!;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                _skipReason = "例題ファイルなし";
                Assert.Inconclusive(_skipReason);
            }
            Assert.IsTrue(_model!.AnalysisStepResults.Any(r => r.IsLiquefaction) && _model.AnalysisStepResults.Any(r => !r.IsLiquefaction),
                "(前提) 液状化の考慮・非考慮の両方の結果があること");
            return _model;
        }

        // ── 以前の線形走査 (逆の液状化へのすり替えは除く) を、比較の基準としてここに写す ──

        private static bool Same(ICaseStepResult r, LoadCase? lc, LoadCombination? comb, bool liq)
            => LoadCase.IsSameCase(lc, r.LoadCase) && LoadCombination.IsSameCombination(comb, r.LoadCombination) && r.IsLiquefaction == liq;

        private static T? LinearAt<T>(IList<T>? list, LoadCase? lc, LoadCombination? comb, bool liq, int step) where T : class, ICaseStepResult
            => list?.FirstOrDefault(r => Same(r, lc, comb, liq) && r.Step == step);

        private static T? LinearLatest<T>(IList<T>? list, LoadCase? lc, LoadCombination? comb, bool liq) where T : class, ICaseStepResult
            => list?.Where(r => Same(r, lc, comb, liq)).OrderByDescending(r => r.Step).FirstOrDefault();

        private static T? LinearMatching<T>(IList<T>? list, LoadCase? lc, LoadCombination? comb, bool liq, int step) where T : class, ICaseStepResult
            => list?.FirstOrDefault(r => r.IsLiquefaction == liq && r.Step == step
                && (lc == null || LoadCase.IsSameCase(r.LoadCase, lc))
                && (comb == null || LoadCombination.IsSameCombination(r.LoadCombination, comb)));

        private static int LinearLastStep(AnaModel m, LoadCase lc, LoadCombination comb, bool liq)
            => m.AnalysisStepResults.Where(r => Same(r, lc, comb, liq)).Select(r => r.Step).DefaultIfEmpty(AnaModel.NoAnalysisStep).Max();

        /// <summary>解析した荷重条件 (液状化の両方の状態) に、存在しない荷重条件を 1 つ足したもの。</summary>
        private static List<(LoadCase Lc, LoadCombination Comb, bool Liq)> Conditions(AnaModel m)
        {
            var cases = m.AnalysisStepResults
                .GroupBy(r => (r.LoadCase.Level, r.LoadCase.No, r.LoadCombination.No))
                .Select(g => (g.First().LoadCase, g.First().LoadCombination))
                .ToList();
            var list = new List<(LoadCase, LoadCombination, bool)>();
            foreach (var (lc, comb) in cases)
            {
                list.Add((lc, comb, false));
                list.Add((lc, comb, true));
            }
            list.Add((new LoadCase { Level = 1, No = 99 }, cases[0].LoadCombination, false));
            return list;
        }

        /// <summary>
        /// 索引は、解析した計算例のすべての梁・節点・ばね・荷重条件・ステップについて、以前の線形走査と同じ行を返すこと
        /// (同じインスタンス)。最終ステップ・その持ち主にある最新・「すべて」を含む照合も比べる。
        /// </summary>
        [TestMethod]
        public void IndexReturnsTheSameResultAsTheLinearScanForEveryOwnerCaseAndStep()
        {
            var m = Model();
            var conditions = Conditions(m);
            int maxStep = m.AnalysisStepResults.Max(r => r.Step);
            int compared = 0;

            void Check<T>(string what, IList<T>? list, ResultLookup<T> lookup) where T : class, ICaseStepResult
            {
                foreach (var (lc, comb, liq) in conditions)
                {
                    for (int step = -1; step <= maxStep + 1; step++)
                    {
                        Assert.AreSame(LinearAt(list, lc, comb, liq, step), lookup.At(lc, comb, liq, step),
                            $"{what}: L{lc.Level}-{lc.No} 組合せ{comb.No} 液状化={liq} ステップ{step}");
                        compared++;
                    }
                    Assert.AreSame(LinearLatest(list, lc, comb, liq), lookup.Latest(lc, comb, liq), $"{what}: 最新 L{lc.Level}-{lc.No}");
                    Assert.AreSame(LinearMatching(list, null, comb, liq, maxStep), lookup.Matching(null, comb, liq, maxStep), $"{what}: 荷重ケース=すべて");
                    Assert.AreSame(LinearMatching(list, lc, null, liq, 1), lookup.Matching(lc, null, liq, 1), $"{what}: 組合せ=すべて");
                    compared += 3;
                }
                Assert.IsNull(lookup.At(null, conditions[0].Comb, false, 0), $"{what}: 荷重ケースが null なら一致しない (IsSameCase と同じ)");
            }

            Check("ステップ", m.AnalysisStepResults, m.StepIndex);
            foreach (var n in m.Nodes) Check($"節点 {n.Name}", n.NodeResults, n.ResultLookup);
            foreach (var b in m.Beams) Check($"梁 {b.Name}", b.BeamResults, b.ResultLookup);
            foreach (var s in m.HorizontalSoilSprings ?? []) Check($"ばね {s.Name}", s.HorizontalSpringResults, s.ResultLookup);
            foreach (var r in m.RotationalSprings ?? []) Check($"回転ばね {r.Name}", r.RotationalSpringResults, r.ResultLookup);

            // 公開の入口 (最終ステップ・step = -1) も同じ
            foreach (var (lc, comb, liq) in conditions)
            {
                int last = LinearLastStep(m, lc, comb, liq);
                Assert.AreEqual(last, m.GetAnalysisLastStep(lc, comb, liq));
                Assert.AreSame(LinearLatest(m.AnalysisStepResults, lc, comb, liq), m.GetAnalysisLastStepResult(lc, comb, liq));
                foreach (var b in m.Beams)
                    Assert.AreSame(LinearAt(b.BeamResults, lc, comb, liq, last), b.GetBeamResult(m, lc, comb, liq));
                foreach (var n in m.Nodes)
                    Assert.AreSame(LinearAt(n.NodeResults, lc, comb, liq, last), n.GetNodeResult(m, lc, comb, liq));
            }

            TestSource.AssertScanned(m.Beams.Count(b => b.BeamResults.Count > 0), 10, "結果のある梁");
            TestSource.AssertScanned(compared, 10_000, "照合した組");
        }

        /// <summary>
        /// 指定した液状化の状態に結果が無ければ null を返し、逆の状態の結果にすり替えないこと。
        /// 逆の状態を見せたいときは <see cref="AnaModel.ResolveLiquefactionWithResults"/> で明示的に切り替え、切り替えたことが分かること。
        /// </summary>
        [TestMethod]
        public void LiquefiedLookupDoesNotSilentlyReturnTheNonLiquefiedResult()
        {
            var m = Model().DeepCopy();
            var s = m.AnalysisStepResults.First(r => r.LoadCase.Level >= 1 && !r.IsLiquefaction);
            var (lc, comb) = (s.LoadCase, s.LoadCombination);
            Assert.IsTrue(m.GetAnalysisLastStep(lc, comb, true) >= 0, "(前提) 液状化を考慮した結果もあること");

            m.RemoveCaseResults(lc, comb, true);   // 非液状化の結果だけが残る

            var beam = m.Beams.First(b => b.BeamResults.Count > 0);
            var node = m.Nodes.First(n => n.NodeResults.Count > 0);
            Assert.IsTrue(m.GetAnalysisLastStep(lc, comb, false) >= 0 && beam.GetBeamResult(m, lc, comb, false) != null,
                "(前提) 非液状化の結果は残っていること");

            Assert.IsTrue(m.GetAnalysisLastStep(lc, comb, true) < 0, "液状化の最終ステップを求めたのに、非液状化の最終ステップが返りました");
            Assert.IsNull(m.GetAnalysisLastStepResult(lc, comb, true), "液状化の最終ステップの結果に、非液状化のものが返りました");
            Assert.IsNull(beam.GetBeamResult(m, lc, comb, true), "液状化の梁の結果に、非液状化のものが返りました");
            Assert.IsNull(node.GetNodeResult(m, lc, comb, true), "液状化の節点の結果に、非液状化のものが返りました");
            Assert.IsNull(beam.ResultLookup.Latest(lc, comb, true));

            // 明示的に切り替える入口: 逆の状態と、切り替えた印を返す
            var (liq, switched) = m.ResolveLiquefactionWithResults(lc, comb, true);
            Assert.IsFalse(liq, "結果のある状態 (非液状化) を返すこと");
            Assert.IsTrue(switched, "切り替えたことが分かること (画面に表示するため)");
            Assert.AreSame(beam.GetBeamResult(m, lc, comb, false), beam.GetBeamResult(m, lc, comb, liq));
            Assert.AreEqual((false, false), m.ResolveLiquefactionWithResults(lc, comb, false), "結果のある状態を指定したら切り替えない");
            Assert.AreEqual((false, true), m.ResolveLiquefactionWithResults(lc, null, true), "組合せ=すべて でも同じ");
        }

        /// <summary>
        /// 一覧の中ほどの行を差し替えても (件数・先頭・末尾は同じ)、次に引いたときに新しい行が見えること。
        /// 以前の索引は (件数, 先頭, 末尾) だけを照合していたので、差し替える前の行を返した (2026-10-14 のレビュー)。
        /// いまは一覧 (<see cref="ResultList{T}"/>) の変更が版を上げ、索引は版で作り直す。索引を捨てる呼び出しは要らない。
        /// </summary>
        [TestMethod]
        public void ReplacingAMiddleRow_IsSeenWithoutInvalidating()
        {
            var lc = new LoadCase { Level = 1, No = 1 };
            var comb = new LoadCombination(1, 1, 1, 1);
            var node = new Node();
            for (int step = 0; step < 3; step++)
                node.NodeResults.Add(new NodeResult { LoadCase = lc, LoadCombination = comb, Step = step });
            var before = node.ResultLookup.At(lc, comb, false, 1);
            Assert.AreSame(node.NodeResults[1], before, "(前提) 索引を作った");

            var replacement = new NodeResult { LoadCase = lc, LoadCombination = comb, Step = 1 };
            node.NodeResults[1] = replacement;   // 件数・先頭・末尾はそのまま
            Assert.AreSame(replacement, node.ResultLookup.At(lc, comb, false, 1), "中ほどで差し替えた行が見えず、差し替える前の行を返しています");

            node.NodeResults.Insert(1, new NodeResult { LoadCase = lc, LoadCombination = comb, Step = 5 });
            Assert.AreEqual(5, node.ResultLookup.Latest(lc, comb, false)!.Step, "中ほどに足した行が見えません");
            node.NodeResults.RemoveAll(r => r.Step == 5);
            Assert.AreEqual(2, node.ResultLookup.Latest(lc, comb, false)!.Step, "消した行が残っています");

            // 保存の形 (配列) は変わらない
            string json = System.Text.Json.JsonSerializer.Serialize(node.NodeResults);
            StringAssert.StartsWith(json, "[", "結果の一覧が配列として保存されません");
            var back = System.Text.Json.JsonSerializer.Deserialize<ResultList<NodeResult>>(json)!;
            Assert.AreEqual(node.NodeResults.Count, back.Count);
        }

        /// <summary>結果を足す・消す・差し替えると、次に引いたときに索引が作り直されること。</summary>
        [TestMethod]
        public void LookupSeesResultsAppendedRemovedOrReplacedAfterTheIndexWasBuilt()
        {
            var m = Model().DeepCopy();
            var beam = m.Beams.First(b => b.BeamResults.Count > 0);
            var node = m.Nodes.First(n => n.NodeResults.Count > 0);
            var comb = m.AnalysisStepResults[0].LoadCombination;
            var newCase = new LoadCase { Level = 1, No = 77 };

            // 索引を作らせる
            Assert.IsTrue(m.GetAnalysisLastStep(newCase, comb, false) < 0);
            Assert.IsNull(beam.GetBeamResult(m, newCase, comb, false));

            // 1. 足す (解析の取り込みと同じく、一覧の末尾に追加する)
            var caseModel = m.DeepCopy();
            int snapSteps = caseModel.AnalysisStepResults.Count;
            var snapNodes = caseModel.Nodes.Select(n => n.NodeResults.Count).ToArray();
            var snapBeams = caseModel.Beams.Select(b => b.BeamResults.Count).ToArray();
            var snapSprings = caseModel.HorizontalSoilSprings.Select(s => s.HorizontalSpringResults.Count).ToArray();
            for (int step = 0; step <= 2; step++)
            {
                caseModel.AnalysisStepResults.Add(new AnalysisStepResult(newCase, comb, false, step, 1, 0.0));
                foreach (var b in caseModel.Beams) b.BeamResults.Add(new BeamResult { LoadCase = newCase, LoadCombination = comb, Step = step });
                foreach (var n in caseModel.Nodes) n.NodeResults.Add(new NodeResult { LoadCase = newCase, LoadCombination = comb, Step = step });
            }
            AnaModel.AppendCaseResultsToMain(m, caseModel, snapSteps, snapNodes, snapBeams, snapSprings, null);

            Assert.AreEqual(2, m.GetAnalysisLastStep(newCase, comb, false), "取り込んだ荷重条件の最終ステップが見えません");
            var added = beam.GetBeamResult(m, newCase, comb, false);
            Assert.IsNotNull(added, "取り込んだ梁の結果が見えません");
            Assert.AreEqual(2, added!.Step);
            Assert.IsNotNull(node.GetNodeResult(m, newCase, comb, false, 1));

            // 2. 書き換える側が索引を捨て忘れても、一覧の件数・末尾が変われば作り直す
            var replacement = new BeamResult { LoadCase = newCase, LoadCombination = comb, Step = 2 };
            beam.BeamResults.RemoveAt(beam.BeamResults.Count - 1);
            beam.BeamResults.Add(replacement);   // 件数は同じ・末尾が別のインスタンス
            Assert.AreSame(replacement, beam.ResultLookup.At(newCase, comb, false, 2), "差し替えた行が見えません");

            beam.BeamResults = [.. beam.BeamResults];   // 一覧ごと差し替え
            Assert.AreSame(replacement, beam.ResultLookup.At(newCase, comb, false, 2));

            // 3. 消す
            m.RemoveCaseResults(newCase, comb, false);
            Assert.IsTrue(m.GetAnalysisLastStep(newCase, comb, false) < 0, "消した荷重条件の最終ステップが残っています");
            Assert.IsNull(beam.GetBeamResult(m, newCase, comb, false, 1), "消した梁の結果が残っています");

            // 4. 全消去
            m.ClearAllAnalysisResults();
            var any = Model().AnalysisStepResults[0];
            Assert.IsTrue(m.GetAnalysisLastStep(any.LoadCase, any.LoadCombination, any.IsLiquefaction) < 0);
            Assert.IsNull(node.GetNodeResult(m, any.LoadCase, any.LoadCombination, any.IsLiquefaction));
        }

        /// <summary>
        /// 結果を荷重条件で照合する処理を、索引の外で書き直していないこと。
        /// 以前は 4 通りに書かれ、液状化の扱い (逆の状態へのすり替えの有無) が場所ごとに違っていた。
        /// </summary>
        [TestMethod]
        public void NoFileReimplementsTheCaseKeyMatchingOutsideTheIndex()
        {
            var offenders = new List<string>();
            int scanned = 0;
            int routed = 0;
            var identity = new Regex(@"IsSameCase\(|IsSameCombination\(|LoadCombination\?\.No\s*==");
            var liquefaction = new Regex(@"\.IsLiquefaction\s*==|==\s*\w+\.IsLiquefaction\b|IsLiquefaction\s*!=");
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (Path.GetFileName(file) == "ResultIndex.cs") continue;   // 照合はここにだけ書く
                scanned++;
                var lines = File.ReadAllLines(file);
                routed += lines.Count(l => l.Contains("ResultLookup.") || l.Contains("StepIndex."));
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!identity.IsMatch(lines[i])) continue;
                    // 同じ式 (前後 4 行) で液状化も比べていれば、結果の照合を書き直している
                    int from = Math.Max(0, i - 4), to = Math.Min(lines.Length - 1, i + 4);
                    for (int j = from; j <= to; j++)
                        if (liquefaction.IsMatch(lines[j])) { offenders.Add($"{Path.GetFileName(file)}:{i + 1}"); break; }
                }
                // 計算書・検定にあった、荷重ケースを文字列にした照合用の鍵
                if (lines.Any(l => Regex.IsMatch(l, @"\bstring\??\s+CaseKeyOf\(")))
                    offenders.Add($"{Path.GetFileName(file)}: CaseKeyOf");
            }
            TestSource.AssertScanned(scanned, 300, "本体のソース");
            TestSource.AssertScanned(routed, 20, "索引を通して結果を引いている箇所");
            Assert.AreEqual(0, offenders.Count,
                "結果を荷重条件で照合する処理を索引 (FEM/ResultIndex.cs の ResultLookup) の外で書いています。"
                + "Beam.ResultLookup などを使ってください: " + string.Join(", ", offenders.Distinct()));
        }

        /// <summary>
        /// キャンバスの再描画と同じ引き方 (全梁・全節点を step = -1 で) の所要時間を、以前の線形走査と比べて出力する。
        /// 判定はしない (計測の記録)。
        /// </summary>
        [TestMethod]
        public void ReportCanvasRedrawLookupCost()
        {
            var m = Model();
            var s = m.AnalysisStepResults.Last();
            var (lc, comb, liq) = (s.LoadCase, s.LoadCombination, s.IsLiquefaction);

            int LegacyLastStep() => m.AnalysisStepResults
                .Where(r => Same(r, lc, comb, liq)).Select(r => r.Step).ToList().DefaultIfEmpty(AnaModel.NoAnalysisStep).Max();

            const int redraws = 20;
            var sw = Stopwatch.StartNew();
            int hitsOld = 0;
            for (int k = 0; k < redraws; k++)
            {
                foreach (var b in m.Beams) if (LinearAt(b.BeamResults, lc, comb, liq, LegacyLastStep()) != null) hitsOld++;
                foreach (var n in m.Nodes) if (LinearAt(n.NodeResults, lc, comb, liq, LegacyLastStep()) != null) hitsOld++;
            }
            var old = sw.Elapsed;

            sw.Restart();
            int hitsNew = 0;
            for (int k = 0; k < redraws; k++)
            {
                foreach (var b in m.Beams) if (b.GetBeamResult(m, lc, comb, liq) != null) hitsNew++;
                foreach (var n in m.Nodes) if (n.GetNodeResult(m, lc, comb, liq) != null) hitsNew++;
            }
            var now = sw.Elapsed;

            Assert.AreEqual(hitsOld, hitsNew);
            Console.WriteLine($"再描画 {redraws} 回 (梁 {m.Beams.Count}・節点 {m.Nodes.Count}・ステップ結果 {m.AnalysisStepResults.Count}): "
                + $"線形走査 {old.TotalMilliseconds:F1} ms → 索引 {now.TotalMilliseconds:F1} ms");
        }
    }
}
