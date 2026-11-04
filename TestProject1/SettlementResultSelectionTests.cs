using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 沈下の結果は、選んでいる荷重ケース・組合せのものだけを表示し、無ければ「結果なし」とすること (2026-11-04 外部レビュー)。
    ///
    /// <para>以前は、指定した組合せの結果が無ければ同じケースの最初の結果を、ケースが無ければ先頭 (常時) の結果を
    /// 黙って使っていた。解析後に組合せを変えたときなどに、選んでいない条件の沈下量を表示しうる。
    /// 基礎梁考慮沈下の部材角は、選択に関係なくいつも先頭の結果を使っていた。</para>
    /// </summary>
    [TestClass]
    public class SettlementResultSelectionTests
    {
        private const string Combo1 = " / 組合せNo.1 (βU=1, βL=1)";
        private const string Combo2 = " / 組合せNo.2 (βU=1, βL=-1)";

        [TestMethod]
        public void Pick_ReturnsNoResult_WhenTheChosenCombinationWasNotAnalyzed()
        {
            var labels = new List<string> { "VL (常時+追加)", "1-1: U1" + Combo1 };
            bool U1(int i) => SeismicCaseLabels.BaseCaseName(labels[i]) == "U1";

            Assert.AreEqual(1, SeismicCaseLabels.Pick(labels, U1, Combo1), "(前提) 解析した組合せは選べる");
            Assert.AreEqual(-1, SeismicCaseLabels.Pick(labels, U1, Combo2),
                "解析していない組合せを選んだのに、別の組合せの結果を返しています");
            // 組合せを分けずに解いた結果 (見出しに組合せが無い) も、組合せを指定したら当たらない (分離入力へ切り替えたあと)
            var unseparated = new List<string> { "1-1: U1" };
            Assert.AreEqual(-1, SeismicCaseLabels.Pick(unseparated, _ => true, Combo1));
            // 組合せを指定しなければ従来どおり同じケースの最初
            Assert.AreEqual(1, SeismicCaseLabels.Pick(labels, U1, ""));
            Assert.AreEqual(-1, SeismicCaseLabels.Pick(labels, _ => false, ""));
        }

        [TestMethod]
        public void TheMainWindowShowsNoBeamAwareSettlement_ForACaseThatWasNotAnalyzed()
        {
            // 荷重ケースを切り替えると方向の選択肢を荷重ケースの入力から引き直すので、入れ物だけ用意する
            var input = new InputModel
            {
                LoadCasesInput = new LoadCasesInput { LoadCasesLevel1 = [], LoadCasesLevel2 = [], LoadCombinations = [] },
            };
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            var vl = new VerticalBeamCaseResult { LoadCaseName = "VL (常時+追加)" };
            vl.PileResults.Add(new VerticalBeamPileResult(1, 0, 0, 100, 100, 5.0));
            var u1 = new VerticalBeamCaseResult { LoadCaseName = "1-1: U1" };
            u1.PileResults.Add(new VerticalBeamPileResult(1, 0, 0, 120, 120, 7.0));
            vm.VerticalBeamCaseResults = new ObservableCollection<VerticalBeamCaseResult> { vl, u1 };

            vm.SelectedLoadCaseName = "U1";
            Assert.AreSame(u1, vm.GetActiveVerticalBeamCaseResult(), "(前提) 解析したケースは選べる");

            vm.SelectedLoadCaseName = "U2";
            Assert.IsNull(vm.GetActiveVerticalBeamCaseResult(),
                "解析していないケースを選んだのに、先頭 (常時) の結果を返しています");
            Assert.IsNull(vm.SelectedVerticalBeamCaseResult());
        }

        [TestMethod]
        public void TheGraphDoesNotDrawAMissingBeamAwareSettlement()
        {
            var input = new InputModel();
            input.AttachViewModel(new MainWindowViewModel { CurrentInputModel = input });
            input.PileLayoutItems = [
                new PileLayoutDataItem { No = 1, PileNo = 1, X = 0, Y = 0 },
                new PileLayoutDataItem { No = 2, PileNo = 2, X = 0, Y = 5 },
            ];
            // 杭 2 だけ結果が無い (結果なしは NaN)
            double Vb(PileLayoutDataItem p) => p.PileNo == 1 ? 6.0 : double.NaN;

            foreach (var option in new[] { "沈下 基礎梁考慮単杭", "沈下 杭矩形(基礎梁考慮)" })
            {
                var line = GraphViewModel.BuildSettlementLine(input, input.PileLayoutItems, "Y", option, "U1", null, Vb, Vb);
                Assert.AreEqual(6.0, line.Ys[0], 1e-12, option);
                Assert.IsTrue(double.IsNaN(line.Ys[1]), $"{option}: 結果の無い杭を {line.Ys[1]} として描いています");
                Assert.IsTrue(line.Notes.Any(n => n.Contains("杭No.2") && n.Contains("基礎梁考慮の沈下")),
                    $"{option}: 結果の無い杭を凡例で知らせていません: " + string.Join(" / ", line.Notes));
            }
        }

        /// <summary>
        /// 括弧を含むケース名でも結果を選べること (2026-11-04 外部レビュー)。重複を直したケース名は "U1 (L2-1)" の形になる。
        /// 以前は最初の " (" から後ろを捨てて "U1" と比べ、見つけられなかった。
        /// </summary>
        [TestMethod]
        public void CaseNamesWithParentheses_AreMatchedWhole()
        {
            Assert.AreEqual("U1 (L2-1)", SeismicCaseLabels.BaseCaseName("2-1: U1 (L2-1)"));
            Assert.AreEqual("U1 (L2-1)", SeismicCaseLabels.BaseCaseName("L2-1: U1 (L2-1)" + Combo2));
            Assert.AreEqual("U1", SeismicCaseLabels.BaseCaseName("1-1: U1"));
            Assert.AreEqual("VL", SeismicCaseLabels.BaseCaseName("VL" + SeismicCaseLabels.LongTermDecoration), "常時の補足は外す");

            var input = new InputModel
            {
                LoadCasesInput = new LoadCasesInput { LoadCasesLevel1 = [], LoadCasesLevel2 = [], LoadCombinations = [] },
            };
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            var u1 = new VerticalBeamCaseResult { LoadCaseName = "1-1: U1" };
            var renamed = new VerticalBeamCaseResult { LoadCaseName = "2-1: U1 (L2-1)" };
            vm.VerticalBeamCaseResults = new ObservableCollection<VerticalBeamCaseResult> { u1, renamed };

            vm.SelectedLoadCaseName = "U1 (L2-1)";
            Assert.AreSame(renamed, vm.SelectedVerticalBeamCaseResult(), "括弧を含むケース名の結果を選べません");
            vm.SelectedLoadCaseName = "U1";
            Assert.AreSame(u1, vm.SelectedVerticalBeamCaseResult(), "\"U1\" を選んだのに \"U1 (L2-1)\" の結果と取り違えています");
        }

        /// <summary>
        /// 荷重ケース名の中の文字で、記録の種類や組合せを読み違えないこと (2026-11-04 外部レビュー)。
        /// 末尾が「 VL」の地震時のケース名は常時と読まず、名前の途中の「 / 組合せ」では切らない。
        /// </summary>
        [TestMethod]
        public void CaseNamesThatLookLikeOtherPartsAreNotMisread()
        {
            Assert.IsFalse(new PileDesign.Models.Results.GroupSettlementCaseRecord { LoadCaseName = "L1-1: 地震 VL" }.IsLongTerm,
                "地震時の記録を、ケース名の末尾が「 VL」なので常時と読んでいます");
            Assert.IsFalse(new PileDesign.Models.Results.GroupSettlementCaseRecord { LoadCaseName = "L1-1: 地震 VL" + Combo2 }.IsLongTerm);
            foreach (var longTerm in new[] { "VL", "杭軸力 VL", "矩形荷重 (VL)" })
                Assert.IsTrue(new PileDesign.Models.Results.GroupSettlementCaseRecord { LoadCaseName = longTerm }.IsLongTerm, longTerm);

            const string name = "U1 / 組合せ用";   // 利用者のケース名に「 / 組合せ」が入っている
            Assert.AreEqual(name, SeismicCaseLabels.BaseCaseName("1-1: " + name), "ケース名の途中で切っています");
            Assert.AreEqual(name, SeismicCaseLabels.BaseCaseName("1-1: " + name + Combo2));
            Assert.AreEqual(Combo2, SeismicCaseLabels.CombinationPartOf("1-1: " + name + Combo2));
            Assert.AreEqual("", SeismicCaseLabels.CombinationPartOf("1-1: " + name));
            Assert.IsTrue(SeismicCaseLabels.RefersToCase("L1-1: " + name + Combo2, name));
        }

        /// <summary>
        /// 組合せの照合は係数を丸めずに行うこと (2026-11-04 外部レビュー)。以前は見出しの係数を小数 2 桁に丸めていたので、
        /// βU = 1.001 と 1.004 が同じ「βU=1」になり、解析後に係数を変えても古い結果と一致した。
        /// </summary>
        [TestMethod]
        public void CombinationsAreMatchedWithUnroundedCoefficients()
        {
            Assert.AreNotEqual(SeismicAxialForces.CombinationSuffix(1, 1.001, 1.0), SeismicAxialForces.CombinationSuffix(1, 1.004, 1.0),
                "係数の違う組合せの見出しが同じになっています (丸めている)");
            // よく使う係数は以前と同じ見た目
            Assert.AreEqual(Combo2, SeismicAxialForces.CombinationSuffix(2, 1.0, -1.0));
            Assert.AreEqual(" / 組合せNo.3 (βU=0.5, βL=1)", SeismicAxialForces.CombinationSuffix(3, 0.5, 1.0));

            // 古い係数 (1.001) で求めた結果は、新しい係数 (1.004) の組合せとして選ばない
            var labels = new List<string> { "1-1: U1" + SeismicAxialForces.CombinationSuffix(1, 1.001, 1.0) };
            Assert.AreEqual(-1, SeismicCaseLabels.Pick(labels, _ => true, SeismicAxialForces.CombinationSuffix(1, 1.004, 1.0)),
                "係数を変える前の結果を、変えたあとの組合せの結果として選んでいます");
        }

        /// <summary>沈下の結果を「無ければ先頭」で選ぶ書き方が戻っていないこと (グラフ・メイン画面・ツールチップ)。</summary>
        [TestMethod]
        public void NoFallbackToTheFirstSettlementResult()
        {
            var fallback = new Regex(@"(\?\?|:)\s*\w*(CaseResults|caseResults|vbResults|beamAware)\[0\]|vbResults\[0\]\.PileResults");
            var offenders = new List<string>();
            int scanned = 0;
            foreach (var dir in new[] { TestSource.Dir("Graphics_r1", "ViewModels"), TestSource.Dir("Graphics_r1", "Views") })
                foreach (var file in Directory.GetFiles(dir, "*.cs"))
                {
                    scanned++;
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                        if (fallback.IsMatch(lines[i].Split("//")[0])) offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            TestSource.AssertScanned(scanned, 50, "ViewModels・Views のソース");
            Assert.AreEqual(0, offenders.Count,
                "選んだケースの沈下の結果が無いとき、先頭の結果に戻しています (結果なしにする): " + string.Join(", ", offenders));
        }
    }
}
