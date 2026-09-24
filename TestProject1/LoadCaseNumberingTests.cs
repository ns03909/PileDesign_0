using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 荷重ケースの番号が一覧の並び順と食い違ったファイルを読んだら、並び順に揃えて知らせること。
    ///
    /// 杭の地震時軸力は、並び順で対応させる所 (杭配置の表・計算書) と、番号で引く所
    /// (<c>GetSeismicAxialForce</c>: 解析・グラフ・ΣV) がある。番号は画面では変えられないが、
    /// 手で編集したファイルなどで重複・欠番があると、解析が別のケースの軸力を黙って使い、
    /// MGT 出力では同じ番号のケースが欠けた。
    /// </summary>
    [TestClass]
    public class LoadCaseNumberingTests
    {
        private static LoadCasesInput WithLevel1(params (int No, string Name)[] cases)
        {
            var input = new LoadCasesInput
            {
                LoadCasesLevel1 = new ObservableCollection<LoadCase>(
                    cases.Select(c => new LoadCase { Level = 1, No = c.No, LoadName = c.Name })),
                LoadCasesLevel2 = new ObservableCollection<LoadCase>([new LoadCase { Level = 2, No = 1, LoadName = "L2" }]),
            };
            return input;
        }

        [TestMethod]
        public void NumbersAreAlignedWithTheListOrder()
        {
            var input = WithLevel1((2, "X方向"), (2, "Y方向"), (5, "斜め"));

            var changes = input.NormalizeLoadCaseNumbers();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, input.LoadCasesLevel1.Select(c => c.No).ToArray(),
                "番号が並び順に揃っていません");
            Assert.AreEqual(2, changes.Count, "振り直したケースだけを知らせること (「Y方向」は元から 2)");
            StringAssert.Contains(changes[0], "レベル1「X方向」: 番号 2 → 1");
            StringAssert.Contains(changes[1], "レベル1「斜め」: 番号 5 → 3");
            Assert.AreEqual(1, input.LoadCasesLevel2[0].No, "揃っているレベル2 まで変えています");
        }

        [TestMethod]
        public void ConsistentNumbersAreLeftAlone()
        {
            var input = WithLevel1((1, "X方向"), (2, "Y方向"));
            Assert.AreEqual(0, input.NormalizeLoadCaseNumbers().Count);
        }

        [TestMethod]
        public void TheMessageAsksForReanalysisOnlyWhenResultsWereLoaded()
        {
            var changes = new[] { "レベル1「X方向」: 番号 2 → 1" };
            string withResults = MainWindowViewModel.DescribeRenumberedLoadCases(changes, hasResults: true);
            string withoutResults = MainWindowViewModel.DescribeRenumberedLoadCases(changes, hasResults: false);

            StringAssert.Contains(withResults, "レベル1「X方向」: 番号 2 → 1");
            StringAssert.Contains(withResults, "再解析してください");
            Assert.IsFalse(withoutResults.Contains("再解析", StringComparison.Ordinal));
        }

        /// <summary>
        /// 実際の読み込みの経路 (復元も同じ経路) で、重複した番号のファイルが並び順に揃うこと。
        /// </summary>
        [TestMethod]
        public void LoadingAFileWithDuplicateNumbersAlignsThem()
        {
            string path = Path.Combine(Path.GetTempPath(), $"LoadCaseNo_{Guid.NewGuid():N}.pdj");

            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var error = XamlSmokeTestSupport.RunOnStaThread(() =>
                {
                    // 新規作成した入力の荷重ケースの番号を、すべて 1 にして保存する (手で編集したファイルの代わり)
                    var source = new MainWindowViewModel();
                    var cases = source.CurrentInputModel!.LoadCasesInput!.LoadCasesLevel1;
                    if (cases.Count < 2)
                    {
                        source.EndAutoSaveSessionNormally();
                        Assert.Inconclusive("前提: 新規作成した入力にレベル1 の荷重ケースが 2 つ以上あること");
                        return;
                    }
                    var names = cases.Select(c => c.LoadName).ToArray();
                    foreach (var c in cases) c.No = 1;
                    new FileOperationService(new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve })
                        .SaveProjectData(path, source.CurrentInputModel, null);
                    source.EndAutoSaveSessionNormally();

                    var vm = new MainWindowViewModel();
                    try
                    {
                        Assert.IsTrue(vm.TryRestoreAutoSave(new AutoSaveService.RestoreCandidate(path, DateTime.Now, null)),
                            "(前提) 読み込めていません");
                        var level1 = vm.CurrentInputModel!.LoadCasesInput!.LoadCasesLevel1;
                        CollectionAssert.AreEqual(names, level1.Select(c => c.LoadName).ToArray(), "並び順が変わっています");
                        CollectionAssert.AreEqual(Enumerable.Range(1, names.Length).ToArray(), level1.Select(c => c.No).ToArray(),
                            "読み込んだ荷重ケースの番号が、並び順に揃っていません");
                    }
                    finally
                    {
                        vm.EndAutoSaveSessionNormally();
                    }
                }, out bool timedOut);
                Assert.IsFalse(timedOut);
                if (error != null) throw error;
            }
            finally
            {
                MessageService.IsUnattended = unattended;
                File.Delete(path);
            }
        }

        /// <summary>番号を振り直したときは、読み込んだ解析結果に再解析の印を立てること (別のケースの軸力で解いた恐れ)。</summary>
        [TestMethod]
        public void RenumberingMarksLoadedResultsAsNeedingReanalysis()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol(");
            int normalize = body.IndexOf("NormalizeLoadCaseNumbers()", StringComparison.Ordinal);
            int mark = body.IndexOf("if (renumbered.Count > 0 && CurrentModel != null)", StringComparison.Ordinal);
            int restore = body.IndexOf("RestoreInputChangedSinceAnalysis(changedSinceAnalysisOnLoad)", StringComparison.Ordinal);
            int undo = body.IndexOf("SaveUndoState();", StringComparison.Ordinal);
            Assert.IsTrue(normalize >= 0 && normalize < undo, "番号を揃えるのが、読込状態を Undo の起点にするより後です");
            Assert.IsTrue(mark > normalize && mark < restore, "番号を振り直したときに、解析結果へ再解析の印を立てていません");
        }
    }
}
