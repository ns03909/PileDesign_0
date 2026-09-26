using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Output;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 計算書・グラフの杭頭 M-θ が、表示するケースの曲線と結果点を使うこと (2026-09-27 のレビュー)。
    /// - 入力から曲線を作れないとき、ばね本体の CurveXY / Curve (最後に解いた別のケースのものかもしれない) を使っていた
    /// - 最終の結果点を荷重ケースの番号だけで探し、レベル 1 と 2 の同じ番号のケースを取り違え得た
    /// </summary>
    [TestClass]
    public class MThetaReportCaseCurveTests
    {
        private static string AddMThetaCurves()
            => TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Charts.cs"),
                "private void AddMThetaCurves(");

        [TestMethod]
        public void TheReportUsesTheCaseSnapshotNotTheSpringsCurrentCurve()
        {
            string body = AddMThetaCurves();
            StringAssert.Contains(body, "CaseMThetaSnapshots.TryGetValue(snapKey", "計算書がケース別の控えから曲線を取っていません");
            foreach (var stale in new[] { "rs.CurveXY", "rs.Curve", "rs.KthetaXY", "rs.Ktheta" })
                Assert.IsFalse(Regex.IsMatch(body, Regex.Escape(stale) + @"\b"),
                    $"計算書の M-θ 曲線がばね本体の {stale} (別のケースのものかもしれない) を使っています");
            StringAssert.Contains(body, "DescribeCasesWithoutMThetaCurve(", "曲線を描けなかったケースを知らせていません");
        }

        [TestMethod]
        public void ResultPointsAreMatchedByLevelAndNumber()
        {
            string body = AddMThetaCurves();
            StringAssert.Contains(body, "LoadCase.IsSameCase(r.LoadCase, loadCase)");

            // 回転ばねの結果を荷重ケースの番号だけで探す箇所が、計算書にもグラフにも残っていないこと
            var sources = new[]
            {
                ("Output", "WordDocument.Charts.cs"),
                ("ViewModels", "GraphViewModel.CurveGraphs.cs"),
            };
            foreach (var (dir, file) in sources)
                Assert.IsFalse(TestSource.Read("Graphics_r1", dir, file).Contains("r.LoadCase?.No == loadCase.No", StringComparison.Ordinal),
                    $"{file}: 結果を荷重ケースの番号だけで探しています (レベルを見ていない)");
        }

        [TestMethod]
        public void TheNoteNamesTheCasesAndSaysWhy()
        {
            var cases = Enumerable.Range(1, 10).Select(i => $"杭No.{i} U{i}").ToList();
            string note = WordDocument.DescribeCasesWithoutMThetaCurve("P1", cases);
            StringAssert.Contains(note, "杭体符号 P1");
            StringAssert.Contains(note, "再解析すると描けます");
            StringAssert.Contains(note, "杭No.8 U8");
            Assert.IsFalse(note.Contains("杭No.9 U9"));
            StringAssert.Contains(note, "ほか 2 件");
        }
    }
}
