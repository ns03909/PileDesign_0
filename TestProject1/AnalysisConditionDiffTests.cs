using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 「解析したあとに設定を変えて計算書を出した」ことに気づけること。
    ///
    /// 設計条件の表は<b>現在の入力</b>を読む。一方、解析条件のいくつかは切り替えても
    /// 解析結果が破棄されないので (<c>MarkInputChangedSinceAnalysis</c> を通らない)、
    /// 「解析 → 設定変更 → 計算書出力」の順で操作すると、解析に使った条件と計算書の
    /// 記載がずれる。それを照合して注意を出すのが
    /// <c>WordDocument.CollectAnalysisConditionDiffs</c>。
    ///
    /// <b>照合の一覧は手で並べるものなので、記録した設定が増えたときに取り残される。</b>
    /// 実際に <c>UseAnalysisAxialForce</c> (杭軸力モード) が抜けていた
    /// — 説明が 3 つ挙げているのに 2 つしか照合しておらず、切り替えても注意が出なかった。
    /// 計算書のグラフは <c>inputModel.UseAnalysisAxialForce</c> をその場で読むので、
    /// 解析と違う軸力の出所で耐力曲線が描かれる。
    ///
    /// そこで<b>記録されている設定を残さず判断しているか</b>を見張る。
    /// 照合しないものは、ここに理由付きで挙げること。
    /// </summary>
    [TestClass]
    public class AnalysisConditionDiffTests
    {
        /// <summary>
        /// 照合しない設定と、その理由。<b>増やすときは理由を書くこと。</b>
        /// </summary>
        private static readonly Dictionary<string, string> NotCompared = new(StringComparer.Ordinal)
        {
            // 解き方の設定。設計条件として計算書に載る値ではない。
            // 毎回出る注意は読まれなくなるので、載らない設定では出さない
            ["UseModifiedNewtonRaphson"] = "解き方の設定 (計算書の設計条件に載らない)",
            ["FullNRIterations"] = "解き方の設定",
            ["SkipIteration"] = "解き方の設定",
            ["UseLineSearch"] = "解き方の設定",
            ["RelaxationFactor"] = "解き方の設定",
            ["Level1StepsCount"] = "解き方の設定 (刻み数)",
            ["Level2StepsCount"] = "解き方の設定 (刻み数)",

            // 実行するケースの範囲が変わるので、結果表そのものに現れる
            ["LiquefactionOption"] = "ケースの範囲が変わり、結果表に現れる",

            // 設定ではなく記録
            ["ExecutedCaseKeys"] = "設定ではなく記録",
            ["InputModelHash"] = "設定ではなく記録",
        };

        private static List<string> Diffs(AnalysisRunSnapshot run, InputModel model)
        {
            var t = typeof(PileDesign.Output.WordDocument);
            var m = t.GetMethod("CollectAnalysisConditionDiffs",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? t.GetMethod("CollectAnalysisConditionDiffs",
                    BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, "CollectAnalysisConditionDiffs が見つかりません (名前が変わった?)");

            return (List<string>)m!.Invoke(null, [run, model])!;
        }

        /// <summary>
        /// 記録されている設定が、照合されているか「照合しない理由」が書かれているかの
        /// どちらかであること。
        ///
        /// これが無いと、<see cref="AnalysisRunSnapshot"/> に設定を足したときに
        /// 照合を足し忘れても静かに通る。実際に杭軸力モードで起きた。
        /// </summary>
        [TestMethod]
        public void EveryRecordedSetting_IsEitherComparedOrExcusedByName()
        {
            string src = TestSource.Read("Graphics_r1", "Output", "WordDocument.Assumptions.cs");
            int at = src.IndexOf("CollectAnalysisConditionDiffs", StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "照合の処理が見つかりません");
            string body = src[at..];

            var props = typeof(AnalysisRunSnapshot)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToList();

            TestSource.AssertScanned(props.Count, 8, "解析時に記録している設定");

            var unhandled = props
                .Where(n => !NotCompared.ContainsKey(n) && !body.Contains(n, StringComparison.Ordinal))
                .ToList();

            Assert.AreEqual(0, unhandled.Count,
                "解析時に記録している設定のうち、照合もされず理由も書かれていないものがあります。"
                + "照合するか、NotCompared に理由を書いて除いてください。"
                + "放っておくと『解析 → 設定変更 → 計算書出力』で食い違いに気づけません: "
                + string.Join(", ", unhandled));
        }

        /// <summary>
        /// 照合する 3 つが、それぞれ単独で注意になること。
        /// 1 つでも取り残されていると、その設定だけ静かに食い違う。
        /// </summary>
        [TestMethod]
        public void EachComparedSetting_RaisesItsOwnNote()
        {
            // 何も違わなければ注意なし
            Assert.AreEqual(0, Diffs(Run(), Model()).Count,
                "条件が同じなのに注意が出ています。毎回出る注意は読まれなくなります");

            // 基礎のねじれ拘束
            var d1 = Diffs(Run(torsion: true), Model(torsion: false));
            Assert.AreEqual(1, d1.Count, "基礎のねじれ拘束の食い違いが注意になりません");
            StringAssert.Contains(d1[0], "ねじれ");

            // 杭頭の接続仮定
            var d2 = Diffs(Run(connection: "Hinge"), Model());
            Assert.AreEqual(1, d2.Count, "杭頭の接続仮定の食い違いが注意になりません");
            StringAssert.Contains(d2[0], "接続");

            // 杭軸力モード — ここが抜けていた
            var d3 = Diffs(Run(useAnalysisAxial: true), Model(useAnalysisAxial: false));
            Assert.AreEqual(1, d3.Count,
                "杭軸力モードの食い違いが注意になりません。"
                + "計算書のグラフはこのモードをその場で読むので、"
                + "解析と違う軸力の出所で耐力曲線が描かれます");
            StringAssert.Contains(d3[0], "軸力");
        }

        /// <summary>注意の文に「解析時」の値が入っていること。どちらが解析時か分からないと直せない。</summary>
        [TestMethod]
        public void TheNote_SaysWhichValueWasUsedForTheAnalysis()
        {
            foreach (var d in Diffs(Run(torsion: true, connection: "Hinge", useAnalysisAxial: true),
                                    Model(torsion: false, useAnalysisAxial: false)))
                StringAssert.Contains(d, "解析時",
                    "注意の文に解析時の値が入っていません。どちらの条件で解いたのか読めません: " + d);
        }

        private static AnalysisRunSnapshot Run(
            bool torsion = false, string connection = "RigidBody", bool useAnalysisAxial = false)
            => new()
            {
                RestrainFoundationTorsion = torsion,
                ConnectionMode = connection,
                UseAnalysisAxialForce = useAnalysisAxial,
            };

        private static InputModel Model(bool torsion = false, bool useAnalysisAxial = false)
        {
            var m = new InputModel
            {
                RestrainFoundationTorsion = torsion,
                UseAnalysisAxialForce = useAnalysisAxial,
            };
            return m;
        }
    }
}
