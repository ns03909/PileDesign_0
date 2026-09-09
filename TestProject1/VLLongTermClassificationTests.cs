using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// VL (常時) 単独ケースが「長期」として扱われること。
    ///
    /// VL 擬似ケースは水平解析の中で組み立てられるが、その <c>LoadCase.Level</c> が 1
    /// (レベル1 地震動) になっていた。一方で検定側は<b>長期を Level==0 で拾う</b>と
    /// 書いてあり (EvaluationService.EvaluateLongTerm)、画面・変換器・計算書も
    /// 「VL 系は Level=0」と書いてある。結果として
    ///
    /// <list type="bullet">
    /// <item>長期の検定は 1 件も作られず (EvaluateLongTerm が呼ばれない)</item>
    /// <item>VL が<b>損傷限界</b>で検定され</item>
    /// <item>限界線に使う軸力が、VL の常時軸力ではなく<b>荷重ケース 1 のレベル1 地震時軸力</b>になる</item>
    /// </list>
    ///
    /// という食い違いが起きていた。
    /// </summary>
    [TestClass]
    public class VLLongTermClassificationTests
    {
        private static PileLayoutDataItem MakePile()
            => new()
            {
                No = 1,
                AxialForceVL0 = 800.0,
                AxialForceVLAdditional = 200.0,
                AxialForceLevel1s = [1500.0],
                AxialForceLevel2s = [2500.0],
            };

        // ───────── 長期の軸力 ─────────

        /// <summary>
        /// 長期 (level=0) は常時軸力を返すこと。
        ///
        /// 以前は「1 でなければレベル 2」という書き方だったので、level=0 を渡すと
        /// <b>黙ってレベル 2 の地震時軸力</b>が返っていた。限界線と検定が別物の軸力で引かれる。
        /// </summary>
        [TestMethod]
        public void LongTerm_UsesThePermanentAxialForce()
        {
            var pile = MakePile();

            Assert.AreEqual(pile.AxialForceVL, pile.GetSeismicAxialForce(loadCaseNo: 1, level: 0), 1e-9,
                "長期なのに地震時軸力が返っている");
            Assert.AreEqual(pile.AxialForceVL, pile.GetDesignAxialForce(loadCaseNo: 1, level: 0), 1e-9,
                "長期の設計軸力が常時軸力になっていない");

            Assert.AreNotEqual(pile.AxialForceLevel2s[0], pile.GetSeismicAxialForce(1, 0),
                "長期がレベル 2 の列を読んでいる");
        }

        /// <summary>レベル 1 / 2 は従来どおりそれぞれの列を読む。</summary>
        [TestMethod]
        public void SeismicLevels_StillReadTheirOwnColumns()
        {
            var pile = MakePile();

            Assert.AreEqual(1500.0, pile.GetSeismicAxialForce(1, 1), 1e-9, "レベル1 の軸力が変わっている");
            Assert.AreEqual(2500.0, pile.GetSeismicAxialForce(1, 2), 1e-9, "レベル2 の軸力が変わっている");
        }

        // ───────── 擬似ケースの分類 ─────────

        /// <summary>
        /// VL 擬似ケースは Level=0 (長期) で作られること。
        ///
        /// 検定側が長期を拾う条件と、生成側が付ける値が食い違うと、
        /// 長期の検定が 0 件のまま誰も気づかない。両方をここで突き合わせる。
        /// </summary>
        [TestMethod]
        public void ThePseudoCase_IsCreatedAsLongTerm()
        {
            var creation = ExtractBlock(
                ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs"),
                "LoadName = \"VL\",");

            StringAssert.Contains(creation, "Level = 0,",
                "VL 擬似ケースが長期 (Level=0) で作られていない。"
                + "長期の検定が 1 件も作られず、VL が損傷限界で検定される");
        }

        /// <summary>検定側が長期を拾う条件が Level==0 のままであること (上のテストの相方)。</summary>
        [TestMethod]
        public void TheEvaluation_PicksLongTermByLevelZero()
        {
            var source = ReadSource("Graphics_r1", "ViewModels", "EvaluationService.cs");

            StringAssert.Contains(source, "LoadCase?.Level == 0",
                "長期の抽出条件が変わっている。VL 擬似ケースの Level と揃っているか確認すること");
        }

        /// <summary>
        /// VL の荷重ステップ数は据え置くこと。
        ///
        /// 分割数は Level で決めており、Level を 0 にしただけだと 1 ステップになる。
        /// VL は鉛直軸力を段階的に載せるので 1 ステップでは収束しない。
        /// 画面の「計算回数」表示も VL 分をレベル1 のステップ数で数えている。
        /// </summary>
        [TestMethod]
        public void ThePseudoCase_KeepsTheLevel1StepCount()
        {
            var body = ExtractMethodBody(
                ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs"),
                "int configuredNStep =");

            StringAssert.Contains(body, "isVLCase ? Level1CalculationStepsCount",
                "VL の荷重分割数がレベル1 と揃っていない。Level=0 だと 1 ステップになり収束しない");
        }

        // ── ソース走査の道具 ──

        private static string FindSolutionRoot([CallerFilePath] string thisFile = "")
        {
            foreach (var start in new[] { Path.GetDirectoryName(typeof(VLLongTermClassificationTests).Assembly.Location), Path.GetDirectoryName(thisFile) })
            {
                if (string.IsNullOrEmpty(start)) continue;
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                        return dir.FullName;
                }
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private static string ReadSource(params string[] relativeParts)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = FindSolutionRoot();
            Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
            return File.ReadAllText(Path.Combine(parts));
        }

        /// <summary>目印の行から、その先 40 行ぶんを返す (初期化子の中を見るため)。</summary>
        private static string ExtractBlock(string source, string marker)
        {
            int at = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"目印が見つかりません: {marker}");
            int end = Math.Min(source.Length, at + 2000);
            return source[at..end];
        }

        private static string ExtractMethodBody(string source, string signatureFragment)
        {
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"シグネチャが見つかりません: {signatureFragment}");
            int end = Math.Min(source.Length, at + 800);
            return source[at..end];
        }
    }
}
