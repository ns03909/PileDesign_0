using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.PileLibrary;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// PC リングのせん断 (キャプテンパイル工法・キャプリングパイル工法)。
    /// 耐力は工法協会のマニュアルの表の値 (PCRingShearTable)。式 2.2.2 の Ty・Tb1・Tb2・Tc・d・Xn の算定法が
    /// マニュアルに無く、設計例も表で PC リングを選んでいるので、式からは求めない (2026-11-25 利用者と確認)。
    /// </summary>
    [TestClass]
    public class PCRingShearTests
    {
        /// <summary>表はカタログの PC リングと 1 対 1 (選べるのに耐力が無いリングを作らない)。</summary>
        [TestMethod]
        public void EveryCatalogRingHasShearCapacities()
        {
            var captain = new CaptainPile(24.0, 22669.0);
            var captainNames = captain.PCRings.Select(r => r.Name!).Distinct().ToList();
            TestSource.AssertScanned(captainNames.Count, 69, "キャプテンパイルの PC リング");
            foreach (var name in captainNames)
                Assert.IsNotNull(PCRingShearTable.Find(PCRingShearTable.CaptainFile, name), name);

            var capring = new CapringPile(22669.0);
            TestSource.AssertScanned(capring.PCRings.Count, 36, "キャプリングの PC リング");
            foreach (var r in capring.PCRings)
                Assert.IsNotNull(PCRingShearTable.Find(PCRingShearTable.CapringFile, r.Name), r.Name);
        }

        /// <summary>
        /// マニュアルの表の値 (頁の画像・PDF の文字と照合した代表値)。
        /// キャプテンパイル 第 6 版 表 2.2.4・2.2.5、キャプリング 2023 年版 2.2(4)。
        /// </summary>
        [TestMethod]
        public void TheTableValuesAreThoseOfTheManuals()
        {
            var c = PCRingShearTable.Find(PCRingShearTable.CaptainFile, "800-N")!;
            Assert.AreEqual((320.0, 640.0, 317.0, 770.0, 1240.0, 1080.0, 1550.0), (c.LongQa, c.ShortQa, c.RaU, c.Rb1U, c.Rb2U, c.UltNoFriction, c.UltFrictionMax));
            // 設計例 3.5 (p072) の φ2000 の短期許容せん断力 N 2130・S1 2840・S2 3690
            Assert.AreEqual(2130.0, PCRingShearTable.Find(PCRingShearTable.CaptainFile, "2000-N")!.ShortQa);
            Assert.AreEqual(2840.0, PCRingShearTable.Find(PCRingShearTable.CaptainFile, "2000-S1")!.ShortQa);
            Assert.AreEqual(3690.0, PCRingShearTable.Find(PCRingShearTable.CaptainFile, "2000-S2")!.ShortQa);
            var last = PCRingShearTable.Find(PCRingShearTable.CaptainFile, "3000-S2")!;
            Assert.AreEqual((3930.0, 7870.0, 8880.0, 18760.0), (last.LongQa, last.ShortQa, last.UltNoFriction, last.UltFrictionMax));

            // キャプリングの設計例 3.7.1 の 800N: sQr = 720 kN (表 3-9)
            var r = PCRingShearTable.Find(PCRingShearTable.CapringFile, "800N")!;
            Assert.AreEqual((360.0, 720.0, 910.0, 560.0, 1060.0, 960.0, 1470.0), (r.LongQa, r.ShortQa, r.RaU, r.Rb1U, r.Rb2U, r.UltNoFriction, r.UltFrictionMax));
            Assert.AreEqual(3190.0, PCRingShearTable.Find(PCRingShearTable.CapringFile, "1200S2")!.UltFrictionMax);
        }

        /// <summary>
        /// 終局の摩擦: 見込まなければ摩擦無視の値。見込めば圧縮軸力の 0.1 倍を足し、摩擦考慮上限で頭打ち。引張では足さない。
        /// </summary>
        [TestMethod]
        public void TheFrictionIsAddedOnlyInCompression_UpToTheCap()
        {
            var c = PCRingShearTable.Find(PCRingShearTable.CaptainFile, "800-N")!;   // 1080 / 1550
            Assert.AreEqual(1080.0, c.Ultimate(false, 3000.0));
            Assert.AreEqual(1080.0 + 0.1 * 2000.0, c.Ultimate(true, 2000.0), 1e-9);
            Assert.AreEqual(1550.0, c.Ultimate(true, 10000.0), "上限で頭打ち");
            Assert.AreEqual(1080.0, c.Ultimate(true, -500.0), "引張では摩擦を足さない");
        }

        /// <summary>選んだ PC リングの耐力が杭頭諸元 (画面・計算書) に出る。</summary>
        [TestMethod]
        public void TheSpecsShowTheCapacities()
        {
            var cp = new CapringPile(22669.0);
            var ring = cp.PCRings.First(x => x.Name == "800N");
            cp.PCRing = ring; cp.D = ring.D; cp.Update();
            var specs = cp.GetCombinedSpecs();
            Assert.AreEqual("720", specs.Single(s => s.Mark == "sQa").Value);
            Assert.AreEqual("960", specs.Single(s => s.Mark == "uQr").Value);
            cp.UltimateShearConsidersFriction = true;
            StringAssert.Contains(cp.GetCombinedSpecs().Single(s => s.Item.StartsWith("終局で")).Value, "上限 1,470");
        }

        /// <summary>
        /// 例題で、PC リングのせん断の項目がキャプテンパイル・キャプリングの杭ごと・荷重条件ごとに出て、
        /// 応答は杭頭の要素の i 端の |Q|、限界は限界状態に応じた表の値 (損傷限界 → 短期、安全限界 → 終局 (摩擦無視))。
        /// </summary>
        [TestMethod]
        [Timeout(600000)]
        [DataRow("ExampleCAP3_7", "PileExampleCAP3_7", "キャプリングパイル工法")]
        [DataRow("ExampleCPT2018", "PileExampleCPT2018", "キャプテンパイル工法")]
        public void TheExampleChecksThePCRingShearOfEveryPile(string ground, string pile, string method)
        {
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel(ground, pile, new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4,
                    Level2Steps = 8,
                    Parallelism = 1,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }
            vm.ApplyConcreteModelOptions();
            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
            var input = vm.ResultInputModel!;

            var rings = result.Items.Where(i => i.Kind == EvaluationKind.PCRingShear).ToList();
            var piles = input.PileLayoutItems.Where(p => input.PileBodies[p.PileBodyNo - 1].PileTopType == method).ToList();
            TestSource.AssertScanned(piles.Count, 1, $"{method}の杭");
            Assert.IsFalse(rings.Any(i => i.IsUnavailable), string.Join("\n", rings.Where(i => i.IsUnavailable).Select(i => i.UnavailableReason)));
            foreach (var p in piles)
            {
                var mine = rings.Where(i => i.PileNo == p.PileNo).ToList();
                Assert.IsTrue(mine.Any(i => i.Level == 1) && mine.Any(i => i.Level == 2), $"杭 No.{p.PileNo} にレベル1・レベル2 の項目がありません (項目 {rings.Count} 件、検定の項目のレベル: {string.Join(",", result.Items.Select(i => i.Level).Distinct())}、杭: {string.Join(",", rings.Select(i => i.PileNo).Distinct())})");

                var body = p.SoilPileAt(input)!.PileBodyInput;
                var capacity = method == "キャプテンパイル工法" ? body.PileTop.CaptainPile!.ShearCapacity! : body.PileTop.CapringPile!.ShearCapacity!;
                foreach (var item in mine)
                {
                    double expected = item.LimitName switch
                    {
                        "使用限界" => capacity.LongQa,
                        "損傷限界" => capacity.ShortQa,
                        _ => capacity.UltNoFriction,
                    };
                    Assert.AreEqual(expected, item.Limit, 1e-9, $"{item.Category} {item.LoadCaseName}");
                }
            }
            // 応答は杭頭の要素の i 端の |Q|
            var any = rings.First(i => i.Level == 1);
            var pileItem = input.PileLayoutItems.Single(p => p.PileNo == any.PileNo);
            var head = EvaluationService.PileHeadBeam(pileItem)!;
            // 検定はケースごとに最後のステップ
            var step = vm.CurrentModel!.AnalysisStepResults
                .Where(s => s.LoadCase?.No == any.LoadCaseNo && s.LoadCombination?.No == any.LoadCombinationNo
                            && s.IsLiquefaction == any.IsLiquefaction && s.LoadCase?.Level == 1)
                .OrderByDescending(s => s.Step).First();
            var f = head.ResultLookup.AtStepOf(step)!.CumulativeForce!;
            Assert.AreEqual(Math.Sqrt(f.Fyi * f.Fyi + f.Fzi * f.Fzi), any.Response, 1e-6);

            string text = EvaluationService.BuildEvaluationText(vm, factored: true, displayFilter: 2);
            StringAssert.Contains(text, $"PC リングのせん断 ({method}");
        }
    }
}
