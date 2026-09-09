using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// ケースごとの結果が main モデルへ漏れなく移送されること。
    ///
    /// 水平解析はケースごとに <c>AnaModel.DeepCopy()</c> を作って走らせ、
    /// <see cref="AnaModel.AppendCaseResultsToMain"/> で main に差分を足す。
    /// 差分の起点 (snapshot) は<b>main 側の件数</b>で取るので、
    /// 複製が結果を写していないと起点が複製の件数と同じになり、
    /// <c>for (i = snap; i &lt; src.Count)</c> が 1 件も回らない。
    ///
    /// 実際に <see cref="RotationalSpring"/> だけが結果を写しておらず、
    /// <b>2 ケース目以降の杭頭回転ばねの結果が main に入っていなかった</b>。
    /// 検定 (CheckThetaLimit) は結果が無いと項目自体を作らないため、
    /// 杭頭回転角の検定が最初のケース以外で黙って消えていた。
    /// 単体テストはケースごとに閉じているので、この形は素通りする。
    /// </summary>
    [TestClass]
    public class CaseResultMergeTests
    {
        private static Node MakeNode(string name, double z)
        {
            var node = new Node();
            node.SetNodeInfo(name, 0.0, 0.0, z);
            return node;
        }

        /// <summary>杭頭回転ばねを 1 本だけ持つモデル。</summary>
        private static AnaModel MakeModelWithRotationalSpring()
        {
            var nodeI = MakeNode("CapNode-1", 0.0);
            var nodeJ = MakeNode("PileHead-1", 0.0);

            return new AnaModel(
                new PileDesign.Models.InputData.InputModel(),
                [nodeI, nodeJ],
                [],
                [],
                [],
                [],
                [new RotationalSpring("RotSpring-1", nodeI, nodeJ, 1.0e6)]);
        }

        private static void AddResults(RotationalSpring spring, int count, int firstStep)
        {
            for (int i = 0; i < count; i++)
                spring.RotationalSpringResults.Add(new RotationalSpringResult { Step = firstStep + i });
        }

        /// <summary>
        /// 複製は結果リストも写すこと。他のばね (HorizontalSoilSpring) と同じ約束。
        /// 写した先は<b>別のリスト</b>で、複製に足しても元は増えない。
        /// </summary>
        [TestMethod]
        public void DeepCopy_CarriesTheResultsLikeTheOtherSprings()
        {
            var model = MakeModelWithRotationalSpring();
            AddResults(model.RotationalSprings[0], 3, firstStep: 1);

            var copy = model.RotationalSprings[0].DeepCopy();

            Assert.AreEqual(3, copy.RotationalSpringResults.Count,
                "複製が結果を写していない。2 ケース目以降のマージが 1 件も回らなくなる");

            AddResults(copy, 1, firstStep: 99);
            Assert.AreEqual(3, model.RotationalSprings[0].RotationalSpringResults.Count,
                "複製の結果リストが元と同じインスタンス。複製に足すと元まで増える");
        }

        /// <summary>
        /// 2 ケースを続けて走らせたときの移送。
        ///
        /// 実際の流れ (HorizontalCalculationViewModel.Run) と同じ順で組む。
        ///   ケースごとに「main の件数を控える → main を複製 → 複製で解く → 差分を main へ」
        /// </summary>
        [TestMethod]
        public void TwoCases_BothLandInTheMainModel()
        {
            const int stepsPerCase = 16;
            var main = MakeModelWithRotationalSpring();

            for (int caseIndex = 0; caseIndex < 2; caseIndex++)
            {
                // 差分の起点は main 側の件数 (本番と同じ取り方)
                var snapshot = main.RotationalSprings.Select(rs => rs.RotationalSpringResults.Count).ToArray();

                var caseModel = main.DeepCopy();
                AddResults(caseModel.RotationalSprings[0], stepsPerCase, firstStep: caseIndex * 100);

                AnaModel.AppendCaseResultsToMain(
                    main, caseModel,
                    snapAnaStepResults: main.AnalysisStepResults?.Count ?? 0,
                    snapNodeResults: main.Nodes.Select(n => n.NodeResults.Count).ToArray(),
                    snapBeamResults: main.Beams.Select(b => b.BeamResults.Count).ToArray(),
                    snapHSpringResults: main.HorizontalSoilSprings.Select(s => s.HorizontalSpringResults.Count).ToArray(),
                    snapRotSpringResults: snapshot);
            }

            Assert.AreEqual(2 * stepsPerCase, main.RotationalSprings[0].RotationalSpringResults.Count,
                "2 ケース目の杭頭回転ばねの結果が main に入っていない。"
                + "杭頭回転角の検定が最初のケース以外で消える");

            // ケース 2 の結果 (Step 100 以降) が実際に届いていること
            Assert.IsTrue(main.RotationalSprings[0].RotationalSpringResults.Any(r => r.Step >= 100),
                "2 ケース目の結果が 1 件も入っていない");
        }
    }
}
