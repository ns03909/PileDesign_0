using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 要素分割を細かくしたとき、変位だけでなく<b>断面力・ばねの反力・検定比</b>も一定の値に近づくこと。
    ///
    /// <para><see cref="AnalysisOutputInvariantTests.RefiningTheMesh_ConvergesToAFixedAnswer"/> は線形域の最大変位だけを見ていた。
    /// 変位は積分された量なので分割に鈍い。曲げモーメントは節点の値を拾い、検定比はそれを耐力で割るので、
    /// 要素長に依存してはいけない量 (ばねの面積の按分・断面力の拾い方・非線形の判定) が依存していれば、こちらに先に出る。</para>
    ///
    /// <para>細分化は解析の要素を 2 等分する (<see cref="RefineElements"/>)。分割していない状態からの変化は
    /// 既定の分割が粗いために大きい (2026-09-10 の実測で 10〜27%) ので比べない。</para>
    /// </summary>
    [TestClass]
    public class MeshConvergenceTests
    {
        /// <summary>
        /// 解析の要素を 1 つずつ 2 等分する (<paramref name="times"/> 回)。<b>すべての要素が同じ割合で細かくなる。</b>
        ///
        /// <para>杭区間を 2 等分する形 (<see cref="AnalysisOutputInvariantTests.RefinePileSegments"/>) は、要素の境が
        /// 杭区間の境と土層の境の両方で決まるので、土層の境で切られた要素 (杭頭のすぐ下の 2 m など) が段を進めても
        /// 割れないことがある。そうすると「細かくした」はずの段どうしで杭頭の要素が同じ長さのまま残り、
        /// 次の段で急に割れて値が跳ぶ。収束を見る網には向かない。ここは画面の「杭要素分割」が節点を足すのと同じ形で、
        /// 要素の中点に節点を足す。</para>
        /// </summary>
        internal static int RefineElements(PileDesign.Models.InputData.InputModel model, int times)
        {
            int added = 0;
            foreach (var sp in model.ElementDivision?.SoilPiles ?? [])
            {
                var zs = sp.ZDataItems.Select(z => z.Z).ToList();
                for (int t = 0; t < times; t++)
                    zs = zs.Zip(zs.Skip(1), (a, b) => new[] { a, (a + b) / 2 }).SelectMany(x => x).Append(zs[^1]).ToList();
                added += zs.Count - sp.ZDataItems.Count;
                sp.ZDataItems = new System.Collections.ObjectModel.ObservableCollection<PileDesign.Models.InputData.PileZDataItem>(zs.Select(z =>
                {
                    var item = new PileDesign.Models.InputData.PileZDataItem { Z = z, GroundInput = sp.GroundInput };
                    item.SetSoilDisplacement();
                    return item;
                }));
                sp.UpdateProperties();
            }
            return added;
        }

        /// <summary>
        /// 線形域: ケースごとの最大変位・杭の最大曲げモーメントが、細かくするほど一定の値に近づく。
        /// 1→2 段の差より 2→3 段の差が小さく (収束している)、2→3 段の差が許容以内であること。
        /// 地盤ばねの反力は比べない。ばね 1 つの反力は受け持つ長さに比例するので、要素を割れば半分になるのが正しい。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_4", "PileExample3_4")]
        [DataRow("ExampleK8", "PileExampleK8")]
        public void Linear_SectionForcesConverge(string ground, string pile)
        {
            ConvergenceSnapshot Run(int refinements) => HeadlessHorizontalRunner.RunExample(ground, pile,
                AnalysisOutputInvariantTests.LinearOptions(m => RefineElements(m, refinements)));
            ConvergenceSnapshot[] runs;
            try { runs = [Run(1), Run(2), Run(3)]; }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }
            Assert.IsTrue(runs.All(r => r.Cases.Count == runs[0].Cases.Count), $"[{ground}] ケース数が違います");

            var quantities = new (string Name, Func<CaseRecord, double> Get, double Tolerance)[]
            {
                ("最大変位", c => c.MaxAbsHorizDisp, 0.03),
                ("杭の最大曲げモーメント", c => c.MaxAbsPileMoment ?? 0, 0.03),
            };
            var problems = new List<string>();
            int compared = 0;
            for (int i = 0; i < runs[0].Cases.Count; i++)
                foreach (var (name, get, tol) in quantities)
                {
                    double v1 = get(runs[0].Cases[i]), v2 = get(runs[1].Cases[i]), v3 = get(runs[2].Cases[i]);
                    if (Math.Abs(v3) < 1e-9) continue;
                    compared++;
                    double d12 = Math.Abs(v2 - v1) / Math.Abs(v3), d23 = Math.Abs(v3 - v2) / Math.Abs(v3);
                    Console.WriteLine($"{ground} {runs[0].Cases[i].CaseKey} {name}: {v1:E4} → {v2:E4} → {v3:E4} (差 {d12:P2} → {d23:P2})");
                    if (d23 > tol) problems.Add($"{runs[0].Cases[i].CaseKey} の{name}: 2 段 {v2:E4} / 3 段 {v3:E4} (差 {d23:P2}、許容 {tol:P0})");
                    else if (d23 > 1e-3 && d23 > 0.75 * d12) problems.Add($"{runs[0].Cases[i].CaseKey} の{name}: 差が縮まりません ({d12:P2} → {d23:P2})");
                }
            TestSource.AssertScanned(compared, 3, $"{ground} で比べた量");
            Assert.AreEqual(0, problems.Count,
                $"[{ground}] 要素分割を細かくしても断面力が収束しません:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// 非線形のまま (利用者が解く設定): 杭体の曲げ・せん断の検定比が 2 段と 3 段で揃う。
        /// 非線形の判定 (ひび割れ・降伏) が要素長に依存していると、検定比が分割で跳ぶ。
        ///
        /// <para>1 段目までは杭頭付近の要素がまだ長く、計算例8 で曲げの検定比が 15% 動く (2026-10-02 の実測)。
        /// 2 段と 3 段では曲げが 2% 程度、せん断 (安全限界) が 5〜6% (計算例9 1.050 → 0.988、計算例8 0.730 → 0.693) 動く。
        /// せん断は曲げモーメントの勾配で、要素端の値を拾うので分割に敏感。許容は曲げ 5%・せん断 8%。</para>
        ///
        /// <para><b>杭頭回転角 (杭頭固定の場所打ち杭) は比べない。</b>計算例8 の安全限界では、細かくするたびに
        /// 0.71 → 1.11 → 0.90 → 1.23 (荷重の段数を 2 倍にしても同じ) と行き来し、収束しない。
        /// 大部分は解法の経路への依存だった (杭頭ばねの回転の最大値に反復途中の行き過ぎが残り、収束点が除荷の線に乗る)。
        /// 2026-10-02 に直した (<see cref="PileHeadSpringPathIndependenceTests"/>) あとも、要素分割で 0.54 → 0.72 → 0.64 → 0.77 → 0.72
        /// と 1 割ほど動く (曲げの ±2% が降伏後の小さな勾配で約 5 倍になる)。この網の許容 (5%) に収まらないので比べず、
        /// 該当する検定に「降伏後」と示している (<see cref="PileHeadBeyondYieldTests"/>)。</para>
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("ExampleK8", "PileExampleK8")]
        public void Nonlinear_EvaluationRatiosConverge(string ground, string pile)
        {
            EvaluationResult Evaluate(int refinements)
            {
                var vm = HeadlessHorizontalRunner.RunExampleForViewModel(ground, pile, new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4,
                    Level2Steps = 8,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    UseLineSearch = true,
                    Parallelism = 1,
                    Customize = m => RefineElements(m, refinements),
                });
                vm.ApplyConcreteModelOptions();
                return EvaluationService.BuildEvaluationResult(vm, factored: true);
            }

            EvaluationResult once, twice;
            try
            {
                once = Evaluate(2);
                twice = Evaluate(3);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }

            static string Key(EvaluationItem i) => $"{i.Category} | {i.LimitName} | L{i.Level} {i.LoadCaseName} | {i.LoadCombinationName} | {i.LiquefactionLabel}";
            // 杭の組ごとに最大の組を出す項目は、分割で組が入れ替わりうる。検定・荷重条件ごとの最大で比べる
            static bool Compared(EvaluationItem i) => i.IsJudged && !i.Category.StartsWith("杭頭回転角", StringComparison.Ordinal);
            Assert.IsTrue(once.Items.Any(i => i.IsJudged && i.Category.StartsWith("杭頭回転角", StringComparison.Ordinal)) || ground != "ExampleK8",
                "比べない検定 (杭頭回転角) の名前が変わっています。除外が空振りしていないか確かめてください");
            var a = once.Items.Where(Compared).GroupBy(Key).ToDictionary(g => g.Key, g => g.Max(i => i.Ratio));
            var b = twice.Items.Where(Compared).GroupBy(Key).ToDictionary(g => g.Key, g => g.Max(i => i.Ratio));
            TestSource.AssertScanned(a.Count, 4, $"{ground} の検定の組");

            var problems = new List<string>();
            foreach (var (key, ra) in a)
            {
                if (!b.TryGetValue(key, out double rb)) { problems.Add($"3 段で無くなった: {key}"); continue; }
                double rel = Math.Abs(rb - ra) / Math.Max(Math.Abs(ra), 0.05);
                Console.WriteLine($"{ground} {key}: {ra:F4} → {rb:F4} ({rel:P2})");
                double tol = key.StartsWith("杭体せん断", StringComparison.Ordinal) ? 0.08 : 0.05;
                if (rel > tol) problems.Add($"{key}: 検定比 {ra:F3} → {rb:F3} (差 {rel:P1}、許容 {tol:P0})");
            }
            Assert.AreEqual(0, problems.Count,
                $"[{ground}] 要素分割を細かくすると検定比が動きます:\n  " + string.Join("\n  ", problems.Take(30)));
        }
    }
}
