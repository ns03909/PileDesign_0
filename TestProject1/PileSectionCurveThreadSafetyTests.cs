using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 断面の NM 曲線を並列に読んでも、値が割れないこと。
    ///
    /// 検定 (EvaluationService.CheckMPhiLimitForBeams) は杭要素ごとに Parallel.For で回り、
    /// 同じ断面の FactoredUltimateNM などを同時に叩く。ここが直列化されていないと:
    ///   ・キャッシュの器 (List,List)? が 24 バイトの構造体で書き込みがアトミックでなく、
    ///     「N は今回の計算・M は別の計算」という破れた値が読める
    ///   ・算出 (GetNMRaw) が純粋関数ではなく、UpdateSteelPipeAxialThresholds() で
    ///     断面の軸力閾値を書き換えてからその閾値でクリップするため、
    ///     同時に走ると互いの中間状態を掴んでクリップが狂う
    ///
    /// 実害: 計算書の「安全限界M」が 710.3 と 664.8 の間で間欠的に変わっていた
    /// (EvaluationTextGoldenTests の Example3_5/factored/filter2 が時々落ちる原因)。
    /// 解析値そのものは正しく、耐力側だけが壊れるので気づきにくい。
    /// </summary>
    [TestClass]
    public class PileSectionCurveThreadSafetyTests
    {
        private static double NearestM(List<double> ns, List<double> ms, double n)
        {
            if (ns == null || ms == null || ns.Count < 2) return double.NaN;
            double best = double.NaN, bestd = double.MaxValue;
            for (int i = 0; i < ns.Count; i++)
            {
                double d = Math.Abs(ns[i] - n);
                if (d < bestd) { bestd = d; best = ms[i]; }
            }
            return best;
        }

        [TestMethod]
        [Timeout(600000)]
        public void UltimateNM_IsStableUnderParallelReads()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example3_5", "PileExample3_5");
            if (input == null) { Assert.Inconclusive($"例題ファイルなしのためスキップ: {err}"); return; }

            var section = input.PileBodies?[0]?.PileBodySegments?[0]?.PileSection;
            if (section == null) { Assert.Inconclusive("断面が取得できません"); return; }

            // 直列で 1 回読んだ値を正とする
            var baseline = section.FactoredUltimateNM;
            double expectedM = NearestM(baseline.N, baseline.M, 671.0);
            int expectedCount = baseline.N.Count;
            Assert.IsTrue(expectedCount > 1 && double.IsFinite(expectedM), "基準となる曲線が取得できません");

            var observed = new ConcurrentBag<string>();
            const int trials = 60;

            for (int t = 0; t < trials; t++)
            {
                // 毎回キャッシュを捨てて「初回アクセスの競合」を作る
                section.InvalidateComputedCaches();

                Parallel.For(0, Environment.ProcessorCount * 4, _ =>
                {
                    var nm = section.FactoredUltimateNM;
                    observed.Add($"pts={nm.N?.Count ?? -1}/{nm.M?.Count ?? -1} M={NearestM(nm.N, nm.M, 671.0):F1}");
                });
            }

            string expected = $"pts={expectedCount}/{expectedCount} M={expectedM:F1}";
            var wrong = observed.Where(s => s != expected)
                                .GroupBy(s => s)
                                .Select(g => $"{g.Key} ×{g.Count()}")
                                .ToList();

            Assert.AreEqual(0, wrong.Count,
                $"並列読み出しで曲線が割れました (期待 {expected}, 観測 {observed.Count} 件)\n  "
                + string.Join("\n  ", wrong));
        }
    }
}
