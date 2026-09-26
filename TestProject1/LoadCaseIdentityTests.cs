using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 荷重ケースを名前ではなくレベルと番号で見分けること。
    ///
    /// 解析結果を荷重ケースの<b>名前</b>とステップで照合していたので、名前が空欄 (同梱の計算例はすべて空欄) や重複だと、
    /// レベル1 とレベル2 の結果を取り違えた。「レベル1 の最終ステップ」を求めるとレベル2 の最終ステップが返り、
    /// グラフや計算書のレベル1 の図にレベル2 の値が出得た (2026-09-26 に計算例9 で確認)。
    /// 結果の照合はレベルと番号で行い、画面の選択 (名前で行う) のために、空欄・重複の名前は読込で付け直す。
    /// </summary>
    [TestClass]
    public class LoadCaseIdentityTests
    {
        private static LoadCasesInput Cases(string[] level1, string[] level2)
            => new()
            {
                LoadCasesLevel1 = new ObservableCollection<LoadCase>(level1.Select((n, i) => new LoadCase { Level = 1, No = i + 1, LoadName = n })),
                LoadCasesLevel2 = new ObservableCollection<LoadCase>(level2.Select((n, i) => new LoadCase { Level = 2, No = i + 1, LoadName = n })),
            };

        [TestMethod]
        public void BlankAndDuplicateNamesAreRenamedToUniqueOnes()
        {
            var input = Cases(["", "E", "VL"], ["", "E"]);
            input.LoadCaseVL = new LoadCase { Level = 0, No = 3, LoadName = "VL" };

            Assert.AreEqual(4, input.DescribeInvalidLoadCaseNames().Count, "(前提) 空欄 2・重複 1・VL と同名 1");
            Assert.AreEqual("", input.LoadCasesLevel1[0].LoadName, "調べるだけの処理が名前を変えています");

            var changes = input.NormalizeLoadCaseNames();
            CollectionAssert.AreEqual(new[] { "L1-1", "E", "VL (L1-3)" }, input.LoadCasesLevel1.Select(c => c.LoadName).ToArray());
            CollectionAssert.AreEqual(new[] { "L2-1", "E (L2-2)" }, input.LoadCasesLevel2.Select(c => c.LoadName).ToArray());
            Assert.AreEqual(4, changes.Count);
            Assert.AreEqual(0, input.DescribeInvalidLoadCaseNames().Count, "付け直したあとも空欄・重複が残っています");
        }

        [TestMethod]
        public void UniqueNamesAreLeftAlone()
        {
            var input = Cases(["VL+E1", "VL+E2"], ["U1", "U2"]);
            Assert.AreEqual(0, input.NormalizeLoadCaseNames().Count);
            Assert.AreEqual("VL+E1", input.LoadCasesLevel1[0].LoadName);
        }

        /// <summary>
        /// 荷重ケース名がすべて空欄でも、レベル1 のケースの最終ステップ・結果がレベル1 のものであること。
        /// 実際に解析した計算例9 (レベル1 は 4 ステップ、レベル2 は 8 ステップ) で、解析のあとに名前を空欄にして確かめる。
        /// </summary>
        [TestMethod]
        public void ResultsAreFoundByLevelAndNumberEvenWithBlankNames()
        {
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4, Level2Steps = 8, UseLineSearch = true, Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive("例題ファイルなし");
                return;
            }

            var model = vm.CurrentModel!;
            foreach (var lc in model.AnalysisStepResults.Select(s => s.LoadCase).Distinct()) lc.LoadName = "";

            var level1 = model.AnalysisStepResults.Where(s => s.LoadCase.Level == 1).ToList();
            var level2 = model.AnalysisStepResults.Where(s => s.LoadCase.Level == 2).ToList();
            Assert.IsTrue(level1.Count > 0 && level2.Count > 0 && level1.Max(s => s.Step) != level2.Max(s => s.Step),
                "(前提) レベル1 とレベル2 で最終ステップが違うこと");

            var any1 = level1[0];
            var last = model.GetAnalysisLastStepResult(any1.LoadCase, any1.LoadCombination, any1.IsLiquefaction);
            Assert.AreEqual(1, last?.LoadCase.Level, "レベル1 の最終ステップを求めたのに、別のレベルのものが返りました");
            Assert.AreEqual(level1.Where(s => s.IsLiquefaction == any1.IsLiquefaction).Max(s => s.Step), last!.Step);

            var beam = model.Beams.First(b => b.BeamResults.Count > 0);
            var br = beam.GetBeamResult(model, any1.LoadCase, any1.LoadCombination, any1.IsLiquefaction);
            Assert.AreEqual(1, br?.LoadCase.Level, "レベル1 の要素の結果を求めたのに、別のレベルのものが返りました");

            var node = model.Nodes.First(n => n.NodeResults?.Count > 0);
            var nr = node.GetNodeResult(model, any1.LoadCase, any1.LoadCombination, any1.IsLiquefaction);
            Assert.AreEqual(1, nr?.LoadCase.Level, "レベル1 の節点の結果を求めたのに、別のレベルのものが返りました");
        }

        /// <summary>計算例の読込が、空欄の荷重ケース名を付け直すこと (計算例は名前がすべて空欄)。</summary>
        [TestMethod]
        public void ExampleLoadingNamesTheLoadCases()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.Examples.cs");
            Assert.AreEqual(2, Regex.Matches(src, @"NormalizeLoadCaseNames\(\);\s*\n\s*MarkProjectReplaced\(\);").Count,
                "計算例の読込 (2 か所) で荷重ケース名を付け直していません");
            string fileIo = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs");
            StringAssert.Contains(fileIo, "CurrentInputModel.LoadCasesInput?.NormalizeLoadCaseNames()",
                "ファイルの読込で荷重ケース名を付け直していません");
        }

        /// <summary>解析結果を荷重ケース名で照合する書き方が戻っていないこと。</summary>
        [TestMethod]
        public void ResultsAreNotMatchedByLoadCaseName()
        {
            var offenders = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                scanned++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (Regex.IsMatch(lines[i], @"\.LoadCase\?\.LoadName\s*==\s*\w+(\.LoadCase)?\??\.LoadName"))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
            TestSource.AssertScanned(scanned, 300, "本体のソース");
            Assert.AreEqual(0, offenders.Count,
                "解析結果を荷重ケース名で照合している箇所があります (LoadCase.IsSameCase を使うこと): " + string.Join(", ", offenders));
        }
    }
}
