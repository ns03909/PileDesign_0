using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// SC 杭の安全限界せん断は、軸力が鋼管だけの降伏軸力 sNy 以上で Qu = 0 になる。
    /// sNy は Q-N 曲線の範囲 (軸力比 0.50、分母にコンクリートを含む) よりずっと手前なので、
    /// 範囲の<b>中</b>で耐力が 0 になる。そこでは検定を NG (検定比 ∞) とする (2026-11-23 利用者の判断)。
    ///
    /// 以前は範囲の外と同じ「検定不能」で、理由も「軸力が曲線の範囲の外」と事実と違っていた。
    /// 検定比を NaN のままにすると降順の並べ替えで最後に回り、NG なのに支配ケースから漏れる。
    /// </summary>
    [TestClass]
    public class ScShearZeroCapacityTests
    {
        [TestMethod]
        public void ItemIsNgWhenTheCapacityIsZeroInsideTheCurve()
        {
            var item = new EvaluationItem { Kind = EvaluationKind.PileSectionShear, Response = 50, Limit = 0, IsOk = !(50.0 > 0.0) };
            Assert.IsFalse(item.IsOk);
            Assert.IsTrue(item.IsJudged);
            Assert.AreEqual(double.PositiveInfinity, item.Ratio, "耐力 0 で応答があれば検定比は ∞");
            Assert.IsTrue(double.IsNaN(new EvaluationItem { Response = 0, Limit = 0, IsOk = true }.Ratio), "応答も 0 なら比は決まらない");
        }

        [TestMethod]
        public void UltimateShearBeyondThePipeYieldIsNgAndGoverns()
        {
            var options = new HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = 4,
                Level2Steps = 16,
                UseLineSearch = true,
                Parallelism = 1,
            };
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example3_1", "PileExample3_1", options);
            }
            catch (System.InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題の解析ができない環境");
                return;
            }

            var scCalcs = vm.ResultInputModel!.ElementDivision!.SoilPiles!
                .SelectMany(sp => sp.PileBodySegments ?? [])
                .Select(seg => seg.PileSection)
                .Where(s => s != null && s.PileSectionType == PileTypeNames.Sc)
                .Distinct()
                .Select(s => s!.CreateSectionCalculator())
                .OfType<SCSection>()
                .ToList();
            Assert.IsTrue(scCalcs.Count > 0, "前提: 設計例集3.1 に SC 杭の断面があること");

            // すべての SC 断面で sNy を超え、曲線の範囲 (軸力比 0.50) には収まる軸力
            double sNyMax = scCalcs.Max(c => c.PrecastSteelPipe.As * c.PrecastSteelPipe.Fys) / 1000;
            double rangeMax = scCalcs.Min(c => c.ShearNMaxUltimate) / 1000;
            double n = 1.05 * sNyMax;
            Assert.IsTrue(n < rangeMax, $"前提: sNy ({sNyMax:0.0} kN) が曲線の範囲の圧縮側 ({rangeMax:0.0} kN) より手前");

            foreach (var pile in vm.ResultInputModel.PileLayoutItems!)
                for (int k = 0; k < pile.AxialForceLevel2s.Count; k++)
                    pile.AxialForceLevel2s[k] = n;

            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
            var zero = result.Items.Where(i => i.Kind == EvaluationKind.PileSectionShear && i.Limit == 0).ToList();
            Assert.IsTrue(zero.Count > 0, "耐力 0 のせん断の検定項目が作られていません (検定不能に落としている?)");
            Assert.IsTrue(zero.All(i => i.LimitName == "安全限界"), "耐力 0 になるのは安全限界だけのはず");
            Assert.IsTrue(zero.Where(i => i.Response > 0).All(i => i.IsJudged && !i.IsOk && double.IsPositiveInfinity(i.Ratio)),
                "耐力 0 で応答があるのに NG (検定比 ∞) になっていません");
            Assert.IsTrue(zero.All(i => i.Basis!.Any(b => b.Item == "耐力 0")), "検定の根拠に耐力 0 の理由がありません");
            Assert.IsTrue(double.IsPositiveInfinity(result.Governing!.Ratio), "耐力 0 の NG が支配ケースになっていません");
        }
    }
}
