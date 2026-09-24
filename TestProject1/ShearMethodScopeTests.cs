using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 高強度せん断補強筋の工法の適用範囲の外では、せん断の検定を OK / NG として扱わないこと。
    ///
    /// 以前は杭径・Fc の範囲外を諸元表の注記に出すだけで、検定は通常どおり OK / NG を出していた。
    /// 引張軸力を適用外とする工法 (エムケーパイルリング785 の (3.2) 式) でも、損傷限界は軸力によらない値で検定していた。
    /// 今は項目に理由を付け、判定を「適用範囲外」として OK・NG のどちらにも数えない (未収束と同じ扱い)。
    /// </summary>
    [TestClass]
    public class ShearMethodScopeTests
    {
        private static PileSection RcSection(string method, double dia, double fc) => new()
        {
            PileBodyType = PileTypeNames.InsituRc,
            PileSectionType = PileTypeNames.RcSection,
            ConcreteOutDia = dia,
            PileDiameter = dia,
            ConcreteFc = fc,
            HoopMethod = method,
        };

        // ── 範囲の判定 ─────────────────────────────────────────────

        [TestMethod]
        public void MethodScopeIsCheckedByDiameterAndFc()
        {
            var mk = ShearReinforcementMethods.Get(ShearReinforcementMethods.MkPileRing785)!;
            Assert.IsNull(mk.OutOfScopeReason(1000, 30), "範囲内なのに範囲外になっています");
            StringAssert.Contains(mk.OutOfScopeReason(700, 30), "杭径");
            StringAssert.Contains(mk.OutOfScopeReason(2700, 30), "杭径");
            StringAssert.Contains(mk.OutOfScopeReason(1000, 50), "Fc");
            StringAssert.Contains(mk.OutOfScopeReason(1000, 21), "Fc");

            // ウルボンは杭径の規定が無い
            var ulbon = ShearReinforcementMethods.Get(ShearReinforcementMethods.UlbonSpiral)!;
            Assert.IsNull(ulbon.OutOfScopeReason(500, 30), "杭径の規定が無い工法で、杭径を範囲外としています");
        }

        /// <summary>
        /// 工法の式を使わない断面では、工法名が残っていても範囲外にしないこと。
        /// 工法の設定は断面の種類を変えても残るので、工法名だけを見ると既製杭などの検定まで止めてしまう。
        /// </summary>
        [TestMethod]
        public void OnlySectionsThatUseTheMethodAreChecked()
        {
            var s = RcSection(ShearReinforcementMethods.MkPileRing785, 700, 30);
            Assert.IsNotNull(s.HoopMethodOutOfScopeReason, "工法の断面で杭径が範囲外なのに、理由がありません");

            var standard = RcSection(ShearReinforcementMethods.Standard, 700, 30);
            Assert.IsNull(standard.HoopMethodOutOfScopeReason, "標準の断面を範囲外としています");

            s.PileBodyType = PileTypeNames.PrecastConcrete;
            s.PileSectionType = PileTypeNames.Phc;
            Assert.IsNull(s.HoopMethodOutOfScopeReason, "工法の式を使わない既製杭の断面を範囲外としています");
        }

        // ── 構造規定 (せん断補強筋比・間隔) ─────────────────────────────────
        // 以前は諸元表の注記に出すだけで、構造規定を満たさない断面でも検定は OK / NG を出していた。

        [TestMethod]
        public void DetailingRulesOfTheMethodAreChecked()
        {
            var mk = ShearReinforcementMethods.Get(ShearReinforcementMethods.MkPileRing785)!;
            Assert.IsNull(mk.DetailingViolation(0.003, 100, nearPileHead: true, ultimate: true), "規定を満たすのに違反としています");

            StringAssert.Contains(mk.DetailingViolation(0.0008, 100, true, false), "0.1% 以上", "最小せん断補強筋比");
            Assert.IsNull(mk.DetailingViolation(0.0015, 100, true, ultimate: false), "一次設計は 0.1% 以上でよい");
            StringAssert.Contains(mk.DetailingViolation(0.0015, 100, true, ultimate: true), "終局を検討する場合",
                "終局 (安全限界) は 0.2% 以上");

            // 間隔: 杭頭から杭径の 5 倍の範囲は 150mm、それより深いところは 300mm
            StringAssert.Contains(mk.DetailingViolation(0.003, 200, nearPileHead: true, ultimate: false), "150mm 以下");
            Assert.IsNull(mk.DetailingViolation(0.003, 200, nearPileHead: false, ultimate: false), "杭頭から離れた位置は 300mm 以下でよい");
            StringAssert.Contains(mk.DetailingViolation(0.003, 350, nearPileHead: false, ultimate: false), "300mm 以下");

            var ulbon = ShearReinforcementMethods.Get(ShearReinforcementMethods.UlbonSpiral)!;
            StringAssert.Contains(ulbon.DetailingViolation(0.006, 100, false, false), "0.5% 以下", "最大せん断補強筋比");
            StringAssert.Contains(ulbon.DetailingViolation(0.003, 200, nearPileHead: false, ultimate: false), "150mm 以下",
                "ウルボンは深さによらず 150mm 以下");
        }

        /// <summary>杭頭付近かどうかは、要素の上端の深さが杭径の 5 倍より浅いかで決めること (一部でもかかれば厳しい方)。</summary>
        [TestMethod]
        public void SpacingLimitDependsOnTheElementDepth()
        {
            var s = RcSection(ShearReinforcementMethods.MkPileRing785, 1000, 30);   // 杭頭から 5.0m まで 150mm
            s.HoopSize = "MD13";
            s.HoopSpacing = 200;
            Assert.IsNotNull(s.HoopMethodDetailingReason(ultimate: false, elementTopDepthM: 4.9), "上端が 5m より浅い要素は 150mm 以下");
            Assert.IsNull(s.HoopMethodDetailingReason(ultimate: false, elementTopDepthM: 5.0), "上端が 5m 以深の要素は 300mm 以下でよい");

            s.PileBodyType = PileTypeNames.PrecastConcrete;
            s.PileSectionType = PileTypeNames.Phc;
            Assert.IsNull(s.HoopMethodDetailingReason(false, 0.0), "工法の式を使わない断面を構造規定で止めています");
        }

        // ── 項目・集計・帯 ──────────────────────────────────────────

        private static EvaluationItem Item(double response, double limit, string? outOfScope = null) => new()
        {
            Kind = EvaluationKind.PileSectionShear,
            Level = 1,
            Response = response,
            Limit = limit,
            IsOk = !(response > limit),
            OutOfScopeReason = outOfScope,
        };

        [TestMethod]
        public void OutOfScopeItemIsNeitherOkNorNg()
        {
            var over = Item(150, 100, "杭径が範囲外");
            Assert.AreEqual("適用範囲外", over.StatusLabel, "範囲外の項目の判定が OK / NG になっています");
            Assert.IsFalse(over.IsJudged);

            var result = new EvaluationResult([Item(50, 100), over, Item(120, 100)]);
            Assert.AreEqual(1, result.OkCount);
            Assert.AreEqual(1, result.NgCount, "範囲外の項目が NG に数えられています");
            Assert.AreEqual(1, result.OutOfScopeCount);
            Assert.AreEqual(1.2, result.Governing!.Ratio, 1e-9, "範囲外の項目が支配ケースになっています");

            // どちらのフィルタでも残す (消すと見落とす / OK 側に残すと合格に読める)
            Assert.IsTrue(EvaluationResult.PassesFilter(over, 0));
            Assert.IsTrue(EvaluationResult.PassesFilter(Item(50, 100, "Fc が範囲外"), 1));
        }

        [TestMethod]
        public void PileBandShowsOutOfScopeWhenNothingElseIsJudgedBad()
        {
            Assert.AreEqual(PileRatioBand.OutOfScope, PileEvaluationSummary.BandOf(0.5, false, false, hasOutOfScope: true));
            Assert.AreEqual(PileRatioBand.Ng, PileEvaluationSummary.BandOf(1.2, true, false, hasOutOfScope: true),
                "判定できた NG があるのに、範囲外の帯になっています");
        }

        // ── 実物の経路 (計算例の解析結果から検定を組み立てる) ──────────────────

        [TestMethod]
        public void EvaluationMarksShearOfOutOfScopeSectionsAndKeepsMoment()
        {
            var options = new HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = 4,
                Level2Steps = 16,
                LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                UseLineSearch = true,
                Parallelism = 1,
            };
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example10", "PileExample10", options);
            }
            catch (System.InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題の解析ができない環境");
                return;
            }

            var before = EvaluationService.BuildEvaluationResult(vm, factored: false);
            int shearCount = before.Items.Count(i => i.Kind == EvaluationKind.PileSectionShear);
            Assert.IsTrue(shearCount > 0, "前提: せん断の検定項目があること");
            Assert.AreEqual(0, before.OutOfScopeCount, "前提: 計算例は工法を使っていないので範囲外は無い");

            // 検定が使う断面 (解析時の入力) の工法をエムケーパイルリング785 にし、Fc を範囲外 (60) にする
            var sections = vm.ResultInputModel!.ElementDivision!.SoilPiles!
                .SelectMany(sp => sp.PileBodySegments ?? [])
                .Select(seg => seg.PileSection)
                .Where(s => s != null && (s.PileBodyType == PileTypeNames.InsituRc))
                .Distinct()
                .ToList();
            Assert.IsTrue(sections.Count > 0, "前提: 場所打ちRC の断面があること");
            foreach (var s in sections)
            {
                s!.HoopMethod = ShearReinforcementMethods.MkPileRing785;
                // 構造規定 (補強筋比・間隔) は満たす配筋にしておく (ここで確かめたいのは Fc と軸力)
                s.HoopSize = "MD16";
                s.HoopSpacing = 100;
                s.ConcreteFc = 60;
            }

            var after = EvaluationService.BuildEvaluationResult(vm, factored: false);
            var shear = after.Items.Where(i => i.Kind == EvaluationKind.PileSectionShear).ToList();
            Assert.IsTrue(shear.Count > 0);
            Assert.IsTrue(shear.All(i => i.IsOutOfScope && i.StatusLabel is "適用範囲外" or "未収束"),
                "工法の適用範囲外 (Fc) の断面のせん断が、OK / NG で判定されています");
            StringAssert.Contains(shear.First(i => i.IsOutOfScope).OutOfScopeReason, "Fc");

            // 曲げは工法の式を使わないので、範囲外にしない
            Assert.IsTrue(after.Items.Where(i => i.Kind == EvaluationKind.PileSectionMoment).All(i => !i.IsOutOfScope),
                "工法と関係の無い曲げの検定まで範囲外になっています");
            Assert.AreEqual(after.Items.Count(i => i.IsJudged && !i.IsOk), after.NgCount);

            string text = EvaluationService.BuildEvaluationText(vm, factored: false, displayFilter: 2);
            StringAssert.Contains(text, "適用範囲外の検定:", "検定テキストに範囲外の件数が出ていません");
            Assert.IsFalse(text.Contains("検定: すべてOK", StringComparison.Ordinal),
                "判定できない (適用範囲外の) 項目があるのに、検定テキストが「すべてOK」と書いています");

            // ── 引張軸力の除外 ((3.2) 式の損傷限界だけ) ──
            // Fc を範囲内に戻す。杭径も範囲内であることを前提にする
            if (sections.Any(s => s!.ConcreteOutDia < 800 || s.ConcreteOutDia > 2600))
            {
                Assert.Inconclusive("前提: 計算例の杭径がエムケーパイルリング785 の適用範囲 (800〜2600mm) に入ること");
                return;
            }
            foreach (var s in sections) s!.ConcreteFc = 30;

            // 損傷限界の検定を作るため耐震グレードを S にする (レベル2 を損傷限界で照査する)。
            // この計算例の低減前の検定にはレベル1のせん断が無く、引張になる杭も無いので、
            // 1 本の杭のレベル2の入力軸力を引張 (−100kN) にする。
            // せん断耐力の曲線は引張側も持っている (−0.05·Fc·断面積まで) ので限界値は引ける
            vm.ResultInputModel!.FundamentalInput!.SeismicGrade = "S";
            var tensionPile = vm.ResultInputModel!.PileLayoutItems!.First(p => p.Beams != null && p.Beams.Count > 0);
            for (int k = 0; k < tensionPile.AxialForceLevel2s.Count; k++)
                tensionPile.AxialForceLevel2s[k] = -100.0;

            var tension = EvaluationService.BuildEvaluationResult(vm, factored: false).Items
                .Where(i => i.Kind == EvaluationKind.PileSectionShear)
                .ToList();
            foreach (var i in tension)
            {
                bool expectOut = i.LimitName == "損傷限界" && i.AxialForce < 0;
                Assert.AreEqual(expectOut, i.IsOutOfScope,
                    $"{i.LimitName} / 軸力 {i.AxialForce:F1}kN: 引張の損傷限界だけを適用範囲外にすること (理由: {i.OutOfScopeReason})");
            }
            Assert.IsTrue(tension.Any(i => i.LimitName == "損傷限界" && i.AxialForce < 0 && i.IsOutOfScope),
                "引張軸力の杭の損傷限界せん断が、適用範囲外になっていません");
            StringAssert.Contains(tension.First(i => i.IsOutOfScope).OutOfScopeReason, "引張軸力");

            // ── 構造規定 (間隔) ── 間隔を上限 (300mm) より大きくすると、すべてのせん断が適用範囲外になる
            foreach (var s in sections) s!.HoopSpacing = 400;
            var spacing = EvaluationService.BuildEvaluationResult(vm, factored: false).Items
                .Where(i => i.Kind == EvaluationKind.PileSectionShear)
                .ToList();
            Assert.IsTrue(spacing.All(i => i.IsOutOfScope),
                "構造規定 (間隔) を満たさない断面のせん断が、OK / NG で判定されています");
            // 間隔を広げると補強筋比も下がるので、理由は間隔か補強筋比のどちらかの構造規定になる
            // (間隔だけの判定は DetailingRulesOfTheMethodAreChecked で確かめている)
            Assert.IsTrue(spacing.Where(i => !i.OutOfScopeReason!.Contains("引張軸力")).All(i => i.OutOfScopeReason!.Contains("構造規定")),
                "理由に構造規定が示されていません");
        }
    }
}
