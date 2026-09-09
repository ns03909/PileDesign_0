using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// どこにもつながっていない一般節点で解析が止まらないこと。
    ///
    /// 一般節点は境界条件なしの自由な節点として解析モデルに入る。基礎梁の端点に
    /// 使われていなければ剛性が 1 つも入らず、剛性行列の対角が 6 個とも 0 になって
    /// 「モデルが不安定です」で例外になる。実機で、一般節点 1 個・基礎梁 0 本の
    /// モデルがこれで止まった。
    ///
    /// 利用者から見ると、剛性行列の内部的なメッセージから
    /// 「使っていない節点が 1 つある」ことを読み取ることになる。
    /// 解析モデルからは取り除いて計算を続け、入力の検査が警告として知らせる。
    /// </summary>
    [TestClass]
    public class UnconnectedGeneralNodeTests
    {
        private static InputModel MakeModel(params InputNode[] nodes)
        {
            var input = new InputModel
            {
                InputNodes = new ObservableCollection<InputNode>(nodes),
            };
            // 新規の InputModel は基礎梁の入れ物を持たないことがある
            input.FoundationBeamInput ??= new FoundationBeamInput();
            return input;
        }

        private static InputNode MakeNode(int no, double x = 0, double y = 0, double z = 0)
            => new() { No = no, X = x, Y = y, Z = z, Type = NodeType.General };

        /// <summary>基礎梁が 1 本も無ければ、一般節点はすべて「つながっていない」。</summary>
        [TestMethod]
        public void WithNoBeams_EveryGeneralNodeIsUnconnected()
        {
            var input = MakeModel(MakeNode(1), MakeNode(2, x: 5.0));

            var unconnected = input.GetUnconnectedGeneralNodes();

            Assert.AreEqual(2, unconnected.Count, "基礎梁が無いのに、つながっている扱いの節点がある");
        }

        /// <summary>基礎梁の端点に使われている節点は「つながっている」。</summary>
        [TestMethod]
        public void ANodeUsedByABeam_IsConnected()
        {
            var used = MakeNode(1);
            var unused = MakeNode(2, x: 5.0);
            var input = MakeModel(used, unused);

            input.FoundationBeamInput.Beams =
            [
                new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.GeneralNode,
                    NodeI_Id = used.UniqueId,
                    NodeJ_Type = NodeReferenceType.GeneralNode,
                    NodeJ_Id = unused.UniqueId,
                },
            ];

            Assert.AreEqual(0, input.GetUnconnectedGeneralNodes().Count,
                "両端とも使われているのに、つながっていない扱いになっている");
        }

        /// <summary>片方だけ使われている場合、もう片方だけが挙がること。</summary>
        [TestMethod]
        public void OnlyTheUnusedNodeIsReported()
        {
            var used = MakeNode(1);
            var unused = MakeNode(2, x: 5.0);
            var input = MakeModel(used, unused);

            input.FoundationBeamInput.Beams =
            [
                new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.GeneralNode,
                    NodeI_Id = used.UniqueId,
                    NodeJ_Type = NodeReferenceType.PileLayout,
                    NodeJ_Id = Guid.NewGuid(),
                },
            ];

            var unconnected = input.GetUnconnectedGeneralNodes();

            Assert.AreEqual(1, unconnected.Count, "挙がる件数が違う");
            Assert.AreEqual(unused.No, unconnected[0].No, "使われている側が挙がっている");
        }

        /// <summary>
        /// 解析モデルを組む側と入力の検査が、<b>同じ判定</b>を使うこと。
        ///
        /// 別々に書くと、片方だけ直したときに「取り除いたのに知らせない」
        /// あるいは「知らせたのに止まる」という食い違いが起きる。
        /// </summary>
        [TestMethod]
        public void TheModelBuilderAndTheCheckShareOneRule()
        {
            var modelling = TestSource.Read("Graphics_r1", "FEM", "AnalysisModelling.cs");
            var check = TestSource.Read("Graphics_r1", "Services", "CheckInputData.cs");

            StringAssert.Contains(modelling, "GetUnconnectedGeneralNodes()",
                "解析モデルを組む側が、共通の判定を使っていない");
            StringAssert.Contains(check, "GetUnconnectedGeneralNodes()",
                "入力の検査が、共通の判定を使っていない");
        }

        /// <summary>入力の検査が、その節点を名指しで知らせること。</summary>
        [TestMethod]
        public void TheCheckNamesTheNode()
        {
            var input = MakeModel(MakeNode(7, x: 1.5, y: 2.5, z: 3.5));

            var warnings = PileDesign.Services.CheckInputData.CollectInputWarnings(input);

            Assert.IsTrue(warnings.Any(w => w.Contains("一般節点 No.7")),
                "つながっていない節点が警告に出ない: " + string.Join(" / ", warnings));
        }

        /// <summary>つながっていれば、余計な警告を出さないこと。</summary>
        [TestMethod]
        public void NoWarningWhenEverythingIsConnected()
        {
            var input = MakeModel();   // 一般節点なし

            var warnings = PileDesign.Services.CheckInputData.CollectInputWarnings(input);

            Assert.IsFalse(warnings.Any(w => w.Contains("一般節点")),
                "一般節点が無いのに警告が出ている: " + string.Join(" / ", warnings));
        }
    }
}
