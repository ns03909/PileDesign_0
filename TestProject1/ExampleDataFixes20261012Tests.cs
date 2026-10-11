using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 同梱の計算例のデータの直し (2026-10-12)。入力の検査を足したら見えてきたもの。
    /// </summary>
    [TestClass]
    public class ExampleDataFixes20261012Tests
    {
        /// <summary>
        /// キャプテンパイル工法 (AIJ2018) の地盤には土質点 (N 値の一覧) が無く (既定の 1 点だけ)、杭先端の平均 N 値が 0、
        /// 先端支持力が 0 で計算されていた。各層の N 値から土質点を作った。
        /// </summary>
        [TestMethod]
        public void CaptainPileExample_HasATipNValue()
        {
            var (model, _) = IntegrationTests.BuildExampleInputModel("ExampleCPT2018", "PileExampleCPT2018");
            if (model == null) { Assert.Inconclusive("例題を組めません"); return; }
            Assert.IsTrue(model.GroundsInput[0].GroundMassesData.Count >= 30, "土質点が足りません");
            foreach (var sp in model.ElementDivision.SoilPiles)
            {
                Assert.IsTrue(sp.PileToeNValue >= 45, $"杭体 {sp.PileBodyNo}: 杭先端の平均 N 値が {sp.PileToeNValue} です (支持層は N = 45〜60)");
                Assert.IsTrue(sp.Qpu > 0, $"杭体 {sp.PileBodyNo}: 先端の極限支持力度が 0 です");
            }
        }

        /// <summary>
        /// 杭間隔比 R/B = 10 は「群杭の影響を考えない」つもりの値だったが、密な砂 (φ が約 33° を超える層) では後方杭の κ が
        /// 上限 3 に届かず、わずかに低減が掛かっていた。考えないことを表す 0 にした (例題 5・9・10 は配置に合わせた値なので残す)。
        /// </summary>
        [TestMethod]
        public void NoExampleUsesTenAsTheNoGroupEffectSpacingRatio()
        {
            var files = Directory.GetFiles(TestSource.ExamplesDir(), "PileExample*.json");
            TestSource.AssertScanned(files.Length, 10, "杭の計算例");
            var withTen = files.Where(f => Regex.IsMatch(File.ReadAllText(f), "\"pileSpacingFactor\":\\s*10(?![\\d.])"))
                .Select(Path.GetFileName).ToList();
            Assert.AreEqual(0, withTen.Count, "R/B = 10 の計算例が残っています: " + string.Join(", ", withTen));
            Assert.AreEqual(0.0, new PileDesign.Models.InputData.PileLayoutItemDto().PileSpacingFactor,
                "計算例に書かれていない R/B の既定が 0 (群杭の影響を考えない) ではありません");
        }

        /// <summary>
        /// 全杭が ξ = 1・R/B = 0 なら、注意は「群杭の影響を考えずに計算します」の 1 行にまとめる (以前は杭ごとに「未入力です」を 1 行ずつ)。
        /// </summary>
        [TestMethod]
        public void TheNoGroupEffectNotice_IsOneLine()
        {
            var (model, _) = IntegrationTests.BuildExampleInputModel("Example3_4", "PileExample3_4");
            if (model == null) { Assert.Inconclusive("例題を組めません"); return; }
            var notices = CheckInputData.CollectInputWarnings(model).Where(m => m.Contains("R/B", System.StringComparison.Ordinal)).ToList();
            Assert.AreEqual(1, notices.Count, "R/B の注意が 1 行にまとまっていません:\n" + string.Join("\n", notices));
            StringAssert.Contains(notices[0], "群杭の影響を考えずに");

            // 一部の杭だけ R/B = 0 なら、その杭の番号を挙げる
            model.PileLayoutItems[0].PileSpacingFactor = 3.0;
            notices = CheckInputData.CollectInputWarnings(model).Where(m => m.Contains("R/B が 0", System.StringComparison.Ordinal)).ToList();
            Assert.AreEqual(1, notices.Count);
            StringAssert.Contains(notices[0], "杭 No.");
        }
    }
}
