using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 検定の根拠: 杭体の曲げ・せん断の各行について、限界値・応答値を<b>どの入力・曲線・式・係数から求めたか</b>を辿れること。
    /// 計算値は変えない (検定したときの値を書くだけ)。根拠が検定と別の計算で作られてずれないことを、
    /// 式で求め直した値と検定の限界値の一致で確かめる。
    /// </summary>
    [TestClass]
    public class EvaluationBasisTests
    {
        private static MainWindowViewModel? _vm;
        private static bool _loaded;

        /// <summary>場所打ち RC 杭 18 本の例題 (計算例9) を解析したもの。</summary>
        private static MainWindowViewModel Analyzed()
        {
            if (!_loaded)
            {
                _loaded = true;
                try
                {
                    _vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                    {
                        Level1Steps = 4,
                        Level2Steps = 8,
                        LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                        UseLineSearch = true,
                        Parallelism = 1,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗")) { _vm = null; }
            }
            if (_vm == null) Assert.Inconclusive("例題ファイルがありません");
            _vm!.ApplyConcreteModelOptions();
            return _vm;
        }

        /// <summary>判定した杭体の曲げ・せん断のすべての行に、限界曲線・軸力とその出所・補間した区間・応答の根拠がある。</summary>
        [TestMethod]
        public void EveryJudgedSectionCheck_HasItsBasis()
        {
            var vm = Analyzed();
            foreach (bool factored in new[] { false, true })
            {
                var items = EvaluationService.BuildEvaluationResult(vm, factored).Items
                    .Where(i => i.IsJudged && i.Kind is EvaluationKind.PileSectionMoment or EvaluationKind.PileSectionShear)
                    .ToList();
                TestSource.AssertScanned(items.Count, 100, "杭体の曲げ・せん断の検定");
                foreach (var item in items)
                {
                    var names = item.Basis.Select(b => b.Item).ToList();
                    foreach (var required in new[] { "限界曲線", "軸力 N", "補間", "応答" })
                        CollectionAssert.Contains(names, required, $"{item.Category} {item.TargetDescription}: 根拠に「{required}」がありません");
                    if (item.Kind == EvaluationKind.PileSectionShear)
                        CollectionAssert.Contains(names, "M/(Q·d)", $"{item.TargetDescription}: せん断の根拠に M/(Q·d) の内訳がありません");

                    // 補間の結果として書いた値は、行の限界値そのもの
                    string interpolation = item.Basis.First(b => b.Item == "補間").Value;
                    StringAssert.Contains(interpolation, $"{item.Limit:N1} {item.Unit}", "根拠の補間の値が限界値と違います");
                    StringAssert.Contains(item.Basis.First(b => b.Item == "軸力 N").Value, $"{item.AxialForce:N1} kN");
                    Assert.IsTrue(item.BasisText.Length > 0);
                }
            }
        }

        /// <summary>
        /// <b>本題。</b> 場所打ち RC 杭のせん断は、根拠の式と係数で求め直した値が検定の限界値 (曲線の補間値) と一致する。
        /// 根拠が検定と別の値を書いていれば、ここで分かる。使用・損傷・安全の 3 つの限界状態、低減の前後を見る。
        /// </summary>
        [TestMethod]
        public void RcShearFormula_ReproducesTheLimitUsedByTheCheck()
        {
            var vm = Analyzed();
            var seen = new HashSet<string>();
            int compared = 0;
            foreach (bool factored in new[] { false, true })
            {
                var shear = EvaluationService.BuildEvaluationResult(vm, factored).Items
                    .Where(i => i.IsJudged && i.Kind == EvaluationKind.PileSectionShear && double.IsFinite(i.Limit))
                    // 断面の計算をやり直すので、限界状態・軸力・M/(Q·d) の組ごとに 1 行だけ、限界状態ごとに 15 行まで見る
                    .GroupBy(i => i.LimitName)
                    .SelectMany(byLimit => byLimit
                        .GroupBy(i => (i.Level, i.AxialForce, i.MonQd, i.PileBodyNo, i.SegmentIndex))
                        .Select(g => g.First())
                        .Take(15))
                    .ToList();
                foreach (var item in shear)
                {
                    var formula = item.DescribeShearFormula();
                    Assert.IsNotNull(formula, $"{item.TargetDescription}: 場所打ち RC 杭のせん断に式と係数の内訳がありません");
                    Assert.IsTrue(formula!.Terms.Count >= 4, "係数の内訳が足りません");
                    double fromFormula = formula.ValueN / 1000.0;
                    Assert.AreEqual(item.Limit, fromFormula, 1e-6 * Math.Abs(item.Limit) + 1e-6,
                        $"{item.Category} {item.TargetDescription} {item.LoadCaseName}: 式で求め直した値 {fromFormula:N3} kN が "
                        + $"検定の限界値 {item.Limit:N3} kN と違います\n式: {formula.Formula}\n"
                        + string.Join("\n", formula.Terms.Select(t => $"  {t.Item}: {t.Value}")));
                    seen.Add($"{item.LimitName}/{(factored ? "低減後" : "低減前")}");
                    compared++;
                }
            }
            TestSource.AssertScanned(compared, 10, "式で照合したせん断の行");
            // 損傷・安全の限界状態を、低減の前後で照合していること (どれかが抜けると、その式の内訳が検定とずれても分からない)
            foreach (var state in new[] { "損傷限界", "安全限界" })
                foreach (var f in new[] { "低減前", "低減後" })
                    Assert.IsTrue(seen.Contains($"{state}/{f}"), $"(前提) この例題に {state}・{f} のせん断の行がありません: {string.Join(", ", seen)}");

            // 使用限界 (長期) は、この例題が長期のケースを解いていないので検定の行が無い。
            // 同じ断面・軸力・M/(Q·d) で、検定と同じ曲線 (GetQNCurvesForLevel) の補間値と照合する
            var sample = EvaluationService.BuildEvaluationResult(vm, true).Items
                .First(i => i.IsJudged && i.Kind == EvaluationKind.PileSectionShear && i.ShearFormula != null);
            var src = sample.ShearFormula!;
            var curves = src.Section.GetQNCurvesForLevel(1, src.MonQd);
            foreach (bool factored in new[] { false, true })
            {
                var curve = factored ? curves.FactoredService : curves.UnfactoredService;
                double limit = PileSection.InterpolateLimitAtAxialForce(curve.N, curve.Q, src.AxialKN);
                var formula = src.Section.DescribeShearLimit(SectionLimitState.Service, 1, factored, src.MonQd, src.AxialKN);
                Assert.IsNotNull(formula);
                Assert.AreEqual(limit, formula!.ValueN / 1000.0, 1e-6 * Math.Abs(limit) + 1e-6,
                    $"使用限界 ({(factored ? "低減後" : "低減前")}): 式で求め直した値が曲線の補間値と違います");
            }
        }

        /// <summary>軸力の出所は、値を決めたのと同じ判定から出す (値と説明が食い違わない)。</summary>
        [TestMethod]
        public void TheAxialForceSource_ComesFromTheSameDecisionAsTheValue()
        {
            var pile = new PileLayoutDataItem
            {
                AxialForceVL0 = 1000,
                AxialForceLevel1s = new System.Collections.ObjectModel.ObservableCollection<double> { 1200, 0 },
                AxialForceLevel2s = new System.Collections.ObjectModel.ObservableCollection<double> { 1500 },
            };
            void Check(int no, int level, double expected, string source)
            {
                var (n, s) = pile.ResolveDesignAxialForce(no, level);
                Assert.AreEqual(expected, n);
                Assert.AreEqual(pile.GetDesignAxialForce(no, level), n);
                StringAssert.Contains(s, source, $"荷重ケース {no}・レベル {level}");
            }
            Check(1, 1, 1200, "地震時軸力 (レベル1・荷重ケース No.1)");
            Check(2, 1, 1000, "常時軸力 (地震時軸力が未入力のため)");
            Check(1, 2, 1500, "地震時軸力 (レベル2・荷重ケース No.1)");
            Check(3, 2, 1000, "常時軸力 (その荷重ケースの地震時軸力の欄がないため)");
            Check(1, 0, 1000, "常時軸力 (長期)");
        }

        /// <summary>計算書は、NG の表のあと (NG が無ければ本文のあと) に検定の根拠を載せる。</summary>
        [TestMethod]
        public void TheReportPrintsTheBasis()
        {
            string src = TestSource.Read("Graphics_r1", "Output", "WordDocument.SummaryTables.cs");
            string report = TestSource.MethodBody(src, "private void AddHorizontalEvaluationReport(");
            Assert.AreEqual(2, report.Split("AddEvaluationBasisSection(body, result);").Length - 1,
                "NG がある場合と無い場合の両方で根拠を載せていません");
            string section = TestSource.MethodBody(src, "private void AddEvaluationBasisSection(");
            StringAssert.Contains(section, "item.DescribeShearFormula()");
            StringAssert.Contains(section, "式による値");
        }
    }
}
