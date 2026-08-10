using PileDesign.Common;
using PileDesign.Models.InputData;
using System;

namespace TestProject1
{
    /// <summary>
    /// 計算フォールバック可視化（CalcFallbackTracker）の検証。
    /// 断面計算の「例外→既定値 0 で代替」が無音で流れず、カウント・サマリーに載ることを保証する。
    /// </summary>
    [TestClass]
    public class CalcFallbackTrackerTests
    {
        [TestInitialize]
        public void Init() => CalcFallbackTracker.Reset();

        [TestCleanup]
        public void Cleanup() => CalcFallbackTracker.Reset();

        [TestMethod]
        public void Report_CountsAndSummary()
        {
            Assert.AreEqual(0, CalcFallbackTracker.TotalCount);

            CalcFallbackTracker.Report("テスト発生源A", new InvalidOperationException("dummy"), "detail");
            CalcFallbackTracker.Report("テスト発生源A");
            CalcFallbackTracker.Report("テスト発生源B");

            Assert.AreEqual(3, CalcFallbackTracker.TotalCount);
            string summary = CalcFallbackTracker.BuildSummary();
            StringAssert.Contains(summary, "テスト発生源A: 2 回");
            StringAssert.Contains(summary, "テスト発生源B: 1 回");

            CalcFallbackTracker.Reset();
            Assert.AreEqual(0, CalcFallbackTracker.TotalCount);
            Assert.AreEqual(string.Empty, CalcFallbackTracker.BuildSummary());
        }

        /// <summary>断面積分の例外（→(0,0) 代替）がトラッカーに記録される。</summary>
        [TestMethod]
        public void SectionIntegration_ExceptionFallback_IsReported()
        {
            var section = new CircularSolidSection(1000.0);

            // material = null で内部の GetStress が NullReferenceException → catch → (0,0) 代替
            var (n, m) = section.GetForceAndMoment(MaterialLaw.Bilinear, null, 0.001, 1e-6);

            Assert.AreEqual(0.0, n, 1e-12, "フォールバック時は N=0");
            Assert.AreEqual(0.0, m, 1e-12, "フォールバック時は M=0");
            Assert.IsTrue(CalcFallbackTracker.TotalCount >= 1, "断面積分フォールバックが記録されていない");
            StringAssert.Contains(CalcFallbackTracker.BuildSummary(), "断面積分");
        }

        /// <summary>
        /// M-φ が算定できない断面の線形弾性代替がトラッカーに記録される。
        /// 鋼管杭で板厚 0（TryCreateSteelPipeSection が null）とし、確実に失敗パスを踏ませる。
        /// </summary>
        [TestMethod]
        public void MPhiLinearFallback_IsReported()
        {
            var section = new PileSection
            {
                PileBodyType = "鋼管杭",
                PileSectionType = "鋼管部",
                PipeGrade = "SKK400",
                PipeDia = 800.0,
                PipeTs = 0.0,      // 板厚 0 → 断面が構築できず線形フォールバック
                PileDiameter = 800.0,
            };

            var (phis, moments) = section.GetMPhiRelationship(1000.0);

            // 線形弾性フォールバック（2 点折線）が返り、記録が残る
            Assert.AreEqual(2, phis.Count, "線形フォールバックは 2 点のはず");
            Assert.IsTrue(CalcFallbackTracker.TotalCount >= 1, "線形 M-φ 代替が記録されていない");
            StringAssert.Contains(CalcFallbackTracker.BuildSummary(), "M-φ の算定（→線形弾性で代替）");
        }

        /// <summary>正常な断面計算ではフォールバックが記録されない（誤検知しない）。</summary>
        [TestMethod]
        public void NormalCalculation_DoesNotReport()
        {
            var section = new PileSection
            {
                PileBodyType = "場所打ち鉄筋コンクリート杭",
                PileSectionType = "鉄筋コンクリート部",
                ConcreteOutDia = 1500.0,
                ConcreteFc = 27.0,
                ConcreteGsi = 1.0,
                MainBarNum = 30,
                MainBarSize = "D29",
                MainBarSpec = "SD390",
                MainBarDr = 200.0,
                HoopSize = "D13",
                HoopSpacing = 150.0,
                HoopSpec = "SD295",
                HoopCenterCover = 150.0,
                PileDiameter = 1500.0,
            };

            CalcFallbackTracker.Reset();
            var (phis, moments) = section.GetMPhiRelationship(1000.0);

            Assert.IsTrue(phis.Count >= 3, "正常系の M-φ が折線になっていない");
            Assert.AreEqual(0, CalcFallbackTracker.TotalCount,
                $"正常計算でフォールバックが記録された: {CalcFallbackTracker.BuildSummary()}");
        }
    }
}
