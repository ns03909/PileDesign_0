using DocumentFormat.OpenXml.Wordprocessing;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 代わりの扱いで黙って続けていた 2 か所を、利用者に知らせること。
    ///
    /// <list type="bullet">
    /// <item>杭の鉛直地盤ばね: 作れないと杭先端を鉛直に固定して解析を続ける (支持条件が入力と変わる)。
    ///   以前はログだけだった。解析前の確認に出す。</item>
    /// <item>計算書の図・表: 作れないとその 1 つを省いて続ける。以前はログだけで、完成した計算書からは
    ///   欠けに気付けなかった。該当位置に赤字で注記し、出力後に一覧で知らせる。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class SilentFallbackNoticeTests
    {
        // ── 鉛直地盤ばね ─────────────────────────────

        /// <summary>杭の節点 <paramref name="pileNodes"/> 個、沈下解析の節点 <paramref name="analysisNodes"/> 個、履歴 <paramref name="steps"/> ステップ。</summary>
        private static SoilPile SoilPileWith(int pileNodes, int analysisNodes, int steps)
        {
            var soilPile = new SoilPile
            {
                ZDataItems = new ObservableCollection<PileZDataItem>(Enumerable.Range(0, pileNodes).Select(i => new PileZDataItem { Z = -i })),
                NodeDisplacements = [],
                NodeReactions = [],
            };
            for (int s = 0; s < steps; s++)
            {
                soilPile.NodeDisplacements.Add(Vector<double>.Build.Dense(2 * analysisNodes, i => 0.001 * (s + 1) * (i % 2 == 0 ? 1 : 0)));
                soilPile.NodeReactions.Add(Vector<double>.Build.Dense(2 * analysisNodes, i => 100.0 * (s + 1)));
            }
            return soilPile;
        }

        [TestMethod]
        public void ProblemsThatChangeTheSupportAreNamed()
        {
            StringAssert.Contains(AnalysisModelling.DescribeVerticalSpringProblem(new SoilPile()), "単杭沈下解析の結果が無い");
            StringAssert.Contains(AnalysisModelling.DescribeVerticalSpringProblem(new SoilPile()), "杭先端を鉛直に固定");

            StringAssert.Contains(AnalysisModelling.DescribeVerticalSpringProblem(SoilPileWith(5, 5, 1)), "2 ステップ未満");

            string fewer = AnalysisModelling.DescribeVerticalSpringProblem(SoilPileWith(6, 5, 3));
            StringAssert.Contains(fewer, "下の 1 節点 (杭先端を含む)");
            StringAssert.Contains(fewer, "杭先端を鉛直に固定");

            StringAssert.Contains(AnalysisModelling.DescribeVerticalSpringProblem(SoilPileWith(5, 6, 3)), "違う深さに付きます");

            Assert.AreEqual("", AnalysisModelling.DescribeVerticalSpringProblem(SoilPileWith(5, 5, 3)),
                "ばねを付けられるのに、問題があると言っています");
        }

        [TestMethod]
        public void ThePreAnalysisWarningsIncludeTheAffectedPiles()
        {
            var input = new InputModel
            {
                UsePsSpringAtPileTip = true,
                ElementDivision = new ElementDivision { SoilPiles = [SoilPileWith(6, 5, 3), SoilPileWith(5, 5, 3)] },
                PileLayoutItems =
                [
                    new PileLayoutDataItem { No = 1, SoilPileAltNo = 1 },
                    new PileLayoutDataItem { No = 2, SoilPileAltNo = 2 },
                    new PileLayoutDataItem { No = 3, SoilPileAltNo = 1 },
                ],
            };

            var problems = AnalysisModelling.DescribeVerticalSpringProblems(input);
            Assert.AreEqual(1, problems.Count, "問題の無い杭 (No.2) まで挙げているか、同じ杭体の杭をまとめていません");
            StringAssert.StartsWith(problems[0], "杭 No.1, 3:");

            var warnings = PileDesign.Services.CheckInputData.CollectInputWarnings(input);
            Assert.IsTrue(warnings.Any(w => w.StartsWith("鉛直地盤ばね: 杭 No.1, 3:", StringComparison.Ordinal)),
                "解析前の確認 (入力の警告) に、鉛直地盤ばねを付けられない杭が出ていません");

            input.UsePsSpringAtPileTip = false;
            Assert.AreEqual(0, AnalysisModelling.DescribeVerticalSpringProblems(input).Count,
                "鉛直地盤ばねを使わない設定なのに、問題として挙げています");
        }

        // ── 計算書の図・表 ─────────────────────────────

        [TestMethod]
        public void AnOmittedFigureLeavesARedNoteWhereItWouldHaveBeen()
        {
            var body = new Body();
            WordDocument.NoteOmitted(body, "試験の図", new InvalidOperationException("試験"));

            var paragraph = body.Elements<Paragraph>().Last();
            StringAssert.Contains(paragraph.InnerText, "試験の図を作成できませんでした");
            Assert.AreEqual("C00000", paragraph.Descendants<Color>().Single().Val?.Value, "注記が赤字になっていません");
        }

        [TestMethod]
        public void TheCompletionMessageListsTheOmittedItems()
        {
            var items = Enumerable.Range(1, 17).Select(i => $"図 {i}").ToList();
            string message = MainWindowViewModel.DescribeOmittedReportItems(items);
            StringAssert.Contains(message, "次の 17 件の図・表を作成できなかったため省きました");
            StringAssert.Contains(message, "・図 1");
            StringAssert.Contains(message, "…ほか 2 件");
        }

        /// <summary>
        /// 計算書の図・表を作る処理が、失敗をログに残すだけで黙って続けていないこと (注記と一覧に載せる)。
        /// 例外を握って続ける箇所は <see cref="WordDocument.NoteOmitted"/> を通すこと。
        /// </summary>
        [TestMethod]
        public void NoReportSectionSwallowsAFailureSilently()
        {
            // 図・表ではないので注記の対象外にしているもの (理由付き)
            var allowed = new Dictionary<string, string>
            {
                ["WordDocument.cs|Word 出力中にエラー"] = "計算書全体の失敗。投げ直して出力そのものを失敗にしている",
                ["WordDocument.cs|tempFile delete failed"] = "一時ファイルの後始末",
                ["WordDocument.TextHelpers.cs|SetColumnWidth"] = "列幅の調整だけで、内容は欠けない",
                ["WordDocument.Charts.cs|NodeResult取得失敗"] = "グラフの 1 点ぶんの値。グラフ自体は作る",
            };

            var silent = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1", "Output"), "WordDocument*.cs"))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!Regex.IsMatch(lines[i], @"catch \(Exception \w+\)")) continue;
                    string block = string.Join("\n", lines.Skip(i).Take(5));
                    if (!Regex.IsMatch(block, @"Log\.(Warning|Error)\(")) continue;
                    scanned++;
                    if (block.Contains("NoteOmitted(", StringComparison.Ordinal) || block.Contains("throw;", StringComparison.Ordinal)) continue;
                    string name = Path.GetFileName(file);
                    if (allowed.Keys.Any(k => k.StartsWith(name + "|", StringComparison.Ordinal) && block.Contains(k[(name.Length + 1)..], StringComparison.Ordinal)))
                        continue;
                    silent.Add($"{name}:{i + 1}");
                }
            }
            TestSource.AssertScanned(scanned, 3, "計算書の例外処理");
            Assert.AreEqual(0, silent.Count,
                "計算書の図・表の失敗をログだけで黙って続けている箇所があります (NoteOmitted を通すこと): " + string.Join(", ", silent));
        }
    }
}
