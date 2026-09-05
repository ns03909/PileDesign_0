using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 解析結果を捨てる処理が 1 か所にまとまっていること。
    ///
    /// 捨てるものが 3 か所に分かれている。
    ///   ・VM のフラグ (IsHorizontalAnalysisDone ほか) と CurrentModel
    ///   ・解析結果セット (入力スナップショット + AnaModel)
    ///   ・<b>入力モデルの中に格納された沈下の結果</b>
    ///     (PileGroupSettlement の CaseRecords / SettlementGridData、各杭の GroupPileSettlement)
    ///
    /// 経路ごとに部分集合しか消していなかったため、
    /// 新規作成や計算例の読み込みでは前のモデルの結果セットが残り、
    /// 破棄したはずの沈下結果は保存 → 再読込で復活していた
    /// (解析済みかどうかを入力モデル内のデータから推定するため)。
    /// </summary>
    [TestClass]
    public class AnalysisStateClearingTests
    {
        /// <summary>
        /// 解析済みフラグを直接 false にしてよい場所。
        ///
        /// ・ResultSet.cs   … 一本化した本体 (ClearAllAnalysisState)
        /// ・FileIO.cs      … RestoreAnalysisState。読込時に一旦全部落としてから保存値で建て直す
        /// これ以外で直接落とすと、必ず何かを消し忘れる。
        /// </summary>
        private static readonly string[] AllowedFiles =
        [
            "MainWindowViewModel.ResultSet.cs",
            "MainWindowViewModel.FileIO.cs",
        ];

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(AnalysisStateClearingTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        [TestMethod]
        public void AnalysisFlags_AreOnlyClearedInOnePlace()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");
            var violations = new List<string>();

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (AllowedFiles.Contains(Path.GetFileName(file))) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComment(lines[i]);
                    if (code.Contains("IsHorizontalAnalysisDone = false"))
                        violations.Add($"{Path.GetFileName(file)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, violations.Count,
                "解析済みフラグを直接落としている (ClearAllAnalysisState を使うこと):\n  "
                + string.Join("\n  ", violations));
        }

        // ── 実際に消えること ───────────────────────────────

        private static MainWindowViewModel? BuildAnalyzed()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) return null;

            var vm = new MainWindowViewModel { CurrentInputModel = inputModel };
            inputModel.AttachViewModel(vm);

            var modelling = new AnalysisModelling(inputModel);
            vm.CurrentModel = new AnaModel(
                inputModel, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            vm.IsHorizontalAnalysisDone = true;
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.CaptureAnalysisResultSet();

            // 群杭沈下の結果 (ケース記録)。杭ごとの沈下量も記録が持つ
            inputModel.PileGroupSettlement ??= new PileGroupSettlement();
            inputModel.PileGroupSettlement.CaseRecords =
            [
                new PileDesign.Models.Results.GroupSettlementCaseRecord
                {
                    LoadCaseName = "VL",
                    SettlementGridData = [new() { X = 0, Y = 0, Settlement = 12.3 }],
                    PileSettlements_mm = new() { [inputModel.PileLayoutItems[0].PileNo] = 12.3 },
                }
            ];
            inputModel.PileGroupSettlement.ActiveCaseIndex = 0;

            return vm;
        }

        /// <summary>
        /// 解析結果を破棄したら、<b>入力モデルの中の沈下結果まで</b>消えること。
        ///
        /// 残すと、保存 → 再読込で「群杭沈下解析済み」が復活する。
        /// 読込時の解析済み判定はケース記録の有無から推定しているため。
        /// </summary>
        [TestMethod]
        public void Discard_AlsoClearsTheSettlementResultsInsideTheInputModel()
        {
            var vm = BuildAnalyzed();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var input = vm.CurrentInputModel!;

            Assert.AreEqual(1, input.PileGroupSettlement.ActiveSettlementGridData.Count, "前提が崩れている");

            vm.DiscardAnalysisResults();

            Assert.IsFalse(vm.HasAnalysisResultSet, "結果セットが残っている");
            Assert.IsNull(vm.CurrentModel, "解析モデルが残っている");
            Assert.IsFalse(vm.IsHorizontalAnalysisDone);
            Assert.IsFalse(vm.IsGroupPileSettlementAnalysisDone);

            Assert.AreEqual(0, input.PileGroupSettlement.ActiveSettlementGridData.Count,
                "入力モデル内の沈下結果が残っている (保存→再読込で解析済みが復活する)");
            Assert.AreEqual(0, input.PileGroupSettlement.CaseRecords.Count,
                "ケースの記録が残っている");
            Assert.AreEqual(0.0, input.PileLayoutItems[0].GroupPileSettlement, 1e-9,
                "杭の沈下量が残っている");
        }

        /// <summary>
        /// 破棄したあとは、読込時の解析済み推定が「未解析」になること。
        /// 判定の入口 (<c>RestoreAnalysisState</c>) を通して確かめる。
        /// </summary>
        [TestMethod]
        public void AfterDiscard_ReloadDoesNotResurrectTheAnalyzedState()
        {
            var vm = BuildAnalyzed();
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            vm.DiscardAnalysisResults();

            // 解析結果を含まないファイルを読み込んだのと同じ経路
            vm.RestoreAnalysisState(new PileDesign.Models.ProjectData());

            Assert.IsFalse(vm.IsGroupPileSettlementAnalysisDone,
                "破棄したはずの群杭沈下が「解析済み」として復活している");
            Assert.IsFalse(vm.IsHorizontalAnalysisDone);
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }
    }
}
