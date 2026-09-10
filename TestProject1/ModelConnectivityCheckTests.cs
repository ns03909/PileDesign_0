using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 解析が組み上がる前に、モデルのつながりを見て止めること。
    ///
    /// 剛性行列を組んでから分かる不安定は、利用者に原因が読み取れない。
    /// 実機で「剛性マトリクスにゼロ/負の対角成分が 6 個」とだけ出て、
    /// 真因は「どこにもつながっていない一般節点が 1 つある」ことだった。
    ///
    /// ここでは入力の段階で分かるものを、名指しで知らせる。
    /// </summary>
    [TestClass]
    public class ModelConnectivityCheckTests
    {
        private static InputModel MakeModel()
        {
            var input = new InputModel
            {
                InputNodes = [],
                PileLayoutItems = [],
            };
            input.FoundationBeamInput ??= new FoundationBeamInput();
            input.FoundationBeamInput.Beams = [];
            input.FoundationBeamInput.Nodes = [];
            return input;
        }

        private static InputNode AddNode(InputModel m, int no, double x, double y = 0, double z = 0)
        {
            var n = new InputNode { No = no, X = x, Y = y, Z = z, Type = NodeType.General };
            m.InputNodes.Add(n);
            return n;
        }

        /// <summary>
        /// 杭を足す。<b>コレクションごと差し替える</b>こと。
        /// Add すると入力モデルの購読が働き、画面の参照を要求されて落ちる。
        /// </summary>
        private static PileLayoutDataItem AddPile(InputModel m, int no, double x, double y = 0)
        {
            var p = new PileLayoutDataItem { No = no, X = x, Y = y };
            var list = new ObservableCollection<PileLayoutDataItem>(m.PileLayoutItems) { p };
            m.PileLayoutItems = list;
            return p;
        }

        private static void AddBeam(InputModel m,
            NodeReferenceType ti, Guid idi, NodeReferenceType tj, Guid idj)
            => m.FoundationBeamInput.Beams.Add(new FoundationBeam
            {
                NodeI_Type = ti, NodeI_Id = idi,
                NodeJ_Type = tj, NodeJ_Id = idj,
            });

        /// <summary>基礎梁が無ければ何も言わないこと。</summary>
        [TestMethod]
        public void WithNoBeams_NothingIsReported()
        {
            Assert.AreEqual(0, ModelConnectivityCheck.CollectErrors(MakeModel()).Count);
        }

        /// <summary>
        /// 参照先を消したあとの基礎梁を見つけること。
        ///
        /// 解析側は端点を解決できず、その梁を静かに落とす。
        /// 「梁を描いたのに効いていない」形になるので止める。
        /// </summary>
        [TestMethod]
        public void ABeamPointingAtSomethingThatNoLongerExists_IsAnError()
        {
            var m = MakeModel();
            var pile = AddPile(m, 1, 0);
            AddBeam(m, NodeReferenceType.PileLayout, pile.UniqueId,
                       NodeReferenceType.GeneralNode, Guid.NewGuid());   // 存在しない節点

            var errors = ModelConnectivityCheck.CollectErrors(m);

            Assert.IsTrue(errors.Any(e => e.Contains("終点の参照先が見つかりません")),
                "消えた参照先を見つけていない: " + string.Join(" / ", errors));
        }

        /// <summary>両端が同じ点の基礎梁を見つけること。</summary>
        [TestMethod]
        public void AZeroLengthBeam_IsAnError()
        {
            var m = MakeModel();
            var pile = AddPile(m, 1, 0);
            var node = AddNode(m, 1, 3.0);
            AddBeam(m, NodeReferenceType.PileLayout, pile.UniqueId,
                       NodeReferenceType.PileLayout, pile.UniqueId);
            AddBeam(m, NodeReferenceType.PileLayout, pile.UniqueId,
                       NodeReferenceType.GeneralNode, node.UniqueId);

            var errors = ModelConnectivityCheck.CollectErrors(m);

            Assert.IsTrue(errors.Any(e => e.Contains("始点と終点が同じ点です")),
                "長さ 0 の梁を見つけていない: " + string.Join(" / ", errors));
        }

        /// <summary>
        /// 杭につながっていない基礎梁の島を見つけること。
        ///
        /// 杭頭は剛体を通して代表節点につながるので、杭を 1 本でも含む一群は支持される。
        /// 含まない一群はどこにも支えが無く、解析が不安定になる。
        /// </summary>
        [TestMethod]
        public void AnIslandOfBeamsWithNoPile_IsAnError()
        {
            var m = MakeModel();
            var pile = AddPile(m, 1, 0);
            var onPile = AddNode(m, 1, 3.0);
            var islandA = AddNode(m, 2, 20.0);
            var islandB = AddNode(m, 3, 25.0);

            // 杭につながる一群
            AddBeam(m, NodeReferenceType.PileLayout, pile.UniqueId,
                       NodeReferenceType.GeneralNode, onPile.UniqueId);
            // 杭を含まない一群
            AddBeam(m, NodeReferenceType.GeneralNode, islandA.UniqueId,
                       NodeReferenceType.GeneralNode, islandB.UniqueId);

            var errors = ModelConnectivityCheck.CollectErrors(m);

            Assert.AreEqual(1, errors.Count(e => e.Contains("どの杭にもつながっていません")),
                "杭を含まない一群を 1 つだけ挙げるはず: " + string.Join(" / ", errors));
        }

        /// <summary>杭につながっていれば、島として挙げないこと。</summary>
        [TestMethod]
        public void BeamsReachingAPile_AreNotReported()
        {
            var m = MakeModel();
            var pile = AddPile(m, 1, 0);
            var a = AddNode(m, 1, 3.0);
            var b = AddNode(m, 2, 6.0);

            AddBeam(m, NodeReferenceType.PileLayout, pile.UniqueId,
                       NodeReferenceType.GeneralNode, a.UniqueId);
            AddBeam(m, NodeReferenceType.GeneralNode, a.UniqueId,
                       NodeReferenceType.GeneralNode, b.UniqueId);

            Assert.AreEqual(0, ModelConnectivityCheck.CollectErrors(m).Count,
                "杭までつながっているのに問題として挙げている");
        }

        /// <summary>同じ位置に杭が重なっていたら警告すること。</summary>
        [TestMethod]
        public void PilesAtTheSamePlace_AreWarned()
        {
            var m = MakeModel();
            AddPile(m, 1, 2.5, 1.5);
            AddPile(m, 2, 2.5, 1.5);
            AddPile(m, 3, 9.0, 1.5);

            var warnings = ModelConnectivityCheck.CollectWarnings(m);

            Assert.AreEqual(1, warnings.Count, "重なりを 1 件だけ挙げるはず: " + string.Join(" / ", warnings));
            StringAssert.Contains(warnings[0], "杭 No.2");
        }

        /// <summary>
        /// 解析を始める前の検査が、この判定を通ること。
        /// 通さないと、行列を組むまで気づけない状態に戻る。
        /// </summary>
        [TestMethod]
        public void ThePreAnalysisGate_UsesTheseChecks()
        {
            var source = TestSource.Read("Graphics_r1", "Services", "CheckInputData.cs");

            var gate = TestSource.MethodBody(source,
                "public static bool ValidateForAnalysis(InputModel inputModel, string analysisName");
            StringAssert.Contains(gate, "ModelConnectivityCheck.CollectErrors(inputModel)",
                "解析前の検査がつながりを見ていない");

            var warn = TestSource.MethodBody(source, "public static List<string> CollectInputWarnings(InputModel inputModel, bool isElementSplit = true)");
            StringAssert.Contains(warn, "ModelConnectivityCheck.CollectWarnings(inputModel)",
                "警告の集約がつながりを見ていない");
        }
    }
}
