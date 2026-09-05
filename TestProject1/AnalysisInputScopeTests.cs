using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 「入力が変更されています。再解析が必要です」は、<b>その入力が効く解析</b>についてだけ言うこと。
    ///
    /// 群杭沈下の入力 (矩形荷重・沈下用の土層・荷重面) は水平解析がまったく読まない。
    /// それでも一律に陳腐化の印を立てていたため、
    /// 「水平解析 → 矩形荷重を入れる → 群杭沈下解析」という当たり前の手順で
    /// <b>必ず</b>「再解析が必要です」と出ていた。水平解析の入力は何も触っていないのに。
    ///
    /// 印が立ったままだと実害もある。解析結果のスナップショットを取り直さなくなるので、
    /// 沈下解析が入力側に書く産物 (コンタの格子) が結果表示に渡らず、コンタが出ない。
    /// </summary>
    [TestClass]
    public class AnalysisInputScopeTests
    {
        private static (MainWindowViewModel vm, InputModel input)? BuildAnalyzed()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) return null;

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            var modelling = new AnalysisModelling(input);
            vm.CurrentModel = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            vm.IsHorizontalAnalysisDone = true;
            vm.CaptureAnalysisResultSet();
            return (vm, input);
        }

        /// <summary>
        /// 沈下の入力だけを触ったときは、水平解析の再解析を促さないこと。
        /// </summary>
        [TestMethod]
        public void SettlementOnlyEdit_DoesNotAskToRerunTheHorizontalAnalysis()
        {
            var built = BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            Assert.IsFalse(vm.InputChangedSinceAnalysis, "前提: 解析直後は変更なし");

            // 群杭沈下も実行済み (陳腐化する結果がある状態)
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Settlement);

            Assert.IsTrue(vm.InputChangedSinceAnalysis, "編集そのものは記録されること");
            StringAssert.Contains(vm.ResultSetStatusText, "沈下解析の入力が変更されています",
                "沈下の入力を触っただけなのに、文言が沈下の話になっていません");
            Assert.IsFalse(vm.ResultSetStatusText.Contains("表示中の解析結果は"),
                $"水平解析の再解析を促しています: {vm.ResultSetStatusText}");
        }

        /// <summary>
        /// 群杭沈下をまだ実行していないなら、沈下の入力を触っても<b>何も言わない</b>こと。
        ///
        /// 陳腐化するものが無い。水平解析だけ済ませて沈下の入力を用意している最中に
        /// 「沈下解析の再実行が必要です」と出るのは意味を成さない
        /// (登録済み土層のコピーで実際に出ていた)。
        /// </summary>
        [TestMethod]
        public void WithoutASettlementResult_ASettlementEditSaysNothing()
        {
            var built = BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            Assert.IsFalse(vm.IsGroupPileSettlementAnalysisDone, "前提: 群杭沈下は未実行");

            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Settlement);

            Assert.IsFalse(vm.InputChangedSinceAnalysis,
                "消える結果が無いのに陳腐化の印を立てています");
            Assert.IsFalse(vm.ResultSetStatusText.Contains("変更されています"),
                $"消える結果が無いのに警告が出ています: {vm.ResultSetStatusText}");
        }

        /// <summary>
        /// モデル側の入力を触ったときは、従来どおり水平解析の再解析を促すこと。
        /// </summary>
        [TestMethod]
        public void ModelEdit_StillAsksToRerunTheHorizontalAnalysis()
        {
            var built = BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            vm.MarkInputChangedSinceAnalysis();   // 既定 = モデル全体

            StringAssert.Contains(vm.ResultSetStatusText, "再解析が必要です");
            StringAssert.Contains(vm.ResultSetStatusText, "表示中の解析結果は");
        }

        /// <summary>
        /// 沈下の入力だけを触ったあとに解析を終えたら、<b>スナップショットを取り直す</b>こと。
        ///
        /// 取り直さないと、沈下解析が入力側に書く産物 (コンタの格子 SettlementGridX/Y) が
        /// 解析時の入力に移らず、コンタが描けない。
        /// </summary>
        [TestMethod]
        public void AfterASettlementOnlyEdit_TheSnapshotIsRetaken()
        {
            var built = BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, input) = built.Value;

            var firstSnapshot = vm.CurrentResultSet!.InputSnapshot;

            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.MarkInputChangedSinceAnalysis(MainWindowViewModel.AnalysisInputScope.Settlement);

            // 沈下解析が終わった体で取り込み直す
            input.PileGroupSettlement ??= new PileGroupSettlement();
            input.PileGroupSettlement.SettlementGridX = [0.0, 1.0, 2.0];
            input.PileGroupSettlement.SettlementGridY = [0.0, 1.0];
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.CaptureAnalysisResultSet();

            Assert.AreNotSame(firstSnapshot, vm.CurrentResultSet!.InputSnapshot,
                "スナップショットを取り直していません (コンタの格子が結果表示に渡らない)");
            Assert.AreEqual(3, vm.CurrentResultSet.InputSnapshot.PileGroupSettlement?.SettlementGridX?.Count ?? 0,
                "コンタの格子がスナップショットに入っていません");
            Assert.IsFalse(vm.InputChangedSinceAnalysis, "取り直したのに陳腐化の印が残っています");
        }

        /// <summary>
        /// モデル側を触ったあとは、<b>取り直さない</b>こと (従来どおり)。
        /// 取り直すと「編集後の入力」と「解析時の結果」が 1 組に組み直され、
        /// 変位は解析時・断面は編集後という混在に戻る。
        /// </summary>
        [TestMethod]
        public void AfterAModelEdit_TheSnapshotIsKept()
        {
            var built = BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, _) = built.Value;

            var firstSnapshot = vm.CurrentResultSet!.InputSnapshot;
            var capturedAnaModel = vm.CurrentResultSet.AnaModel;

            vm.MarkInputChangedSinceAnalysis();
            vm.IsGroupPileSettlementAnalysisDone = true;
            vm.CaptureAnalysisResultSet();

            Assert.AreSame(firstSnapshot, vm.CurrentResultSet!.InputSnapshot,
                "モデルを編集したのにスナップショットを取り直しています");
            Assert.AreSame(capturedAnaModel, vm.CurrentResultSet.AnaModel);
            Assert.IsTrue(vm.InputChangedSinceAnalysis, "陳腐化の記録が消えています");
        }

        /// <summary>
        /// 「沈下の入力は水平解析に効かない」という前提そのものを固定する。
        ///
        /// これが崩れたら上の判断は全部誤りになる。<b>FEM 側が群杭沈下の入力を読み始めたら</b>
        /// このテストが落ちるので、そのとき範囲の切り分けを考え直すこと。
        /// </summary>
        [TestMethod]
        public void TheHorizontalAnalysisDoesNotReadTheSettlementInput()
        {
            string root = FindSolutionRoot();
            var readers = Directory
                .EnumerateFiles(Path.Combine(root, "Graphics_r1", "FEM"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => File.ReadAllText(f).Contains("PileGroupSettlement", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToList();

            Assert.AreEqual(0, readers.Count,
                "FEM が群杭沈下の入力を読んでいます。"
                + "「沈下の入力は水平解析に効かない」という前提でツールチップと"
                + "スナップショットの取り直しを決めているので、切り分けを見直してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", readers));
        }

        /// <summary>
        /// 群杭沈下の入力を触るコマンドは、<b>効く範囲を明示して</b> Undo を積むこと。
        ///
        /// 既定 (<c>SaveUndoState()</c>) はモデル全体が変わった扱いなので、
        /// 沈下の入力しか触らないコマンドで既定のまま積むと
        /// 「水平解析の再解析が必要です」と言ってしまう。実際、矩形荷重・沈下用土層・
        /// 登録済み土層のコピーで 1 つずつ見つかった。
        ///
        /// 明示すれば <see cref="MainWindowViewModel.AnalysisInputScope.All"/> でも構わない
        /// (書いた人が決めた、という印になる)。
        /// </summary>
        [TestMethod]
        public void SettlementEditsDeclareTheirScope()
        {
            string root = FindSolutionRoot();
            var declaration = new Regex(
                @"^        (?:\[[^\]]*\]\s*)?(?:public|private|internal|protected)[^=;]*\(",
                RegexOptions.Compiled);

            var offenders = new List<string>();
            foreach (string cs in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                var lines = File.ReadAllLines(cs);
                var starts = Enumerable.Range(0, lines.Length)
                    .Where(i => declaration.IsMatch(lines[i]))
                    .ToList();

                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains("SaveUndoState", StringComparison.Ordinal)) continue;
                    if (lines[i].Contains("AnalysisInputScope", StringComparison.Ordinal)) continue;

                    int a = starts.LastOrDefault(s => s <= i);
                    int b = starts.FirstOrDefault(s => s > i, lines.Length);
                    string body = string.Join("\n", lines[a..b]);
                    if (!body.Contains("PileGroupSettlement", StringComparison.Ordinal)) continue;

                    offenders.Add($"{Path.GetFileName(cs)}:{i + 1}  {lines[a].Trim()}");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "群杭沈下の入力を触るのに、編集の効く範囲を指定せずに Undo を積んでいます。"
                + "SaveUndoState(AnalysisInputScope.Settlement) を使ってください "
                + "(モデル全体に効くなら AnalysisInputScope.All と明示):"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(AnalysisInputScopeTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }
    }
}
