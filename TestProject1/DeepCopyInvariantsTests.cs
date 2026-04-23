using PileDesign.FEM;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// E1 (2026-04-23): ケース並列化の前提として AnaModel.DeepCopy の参照不変量を検証する。
    /// コピー後に以下が満たされていなければならない:
    ///   - copy.Beams[i].NodeI/J は copy.Nodes 上のインスタンスと同一参照
    ///   - copy.HorizontalSoilSprings[i].NodeI/J も同上
    ///   - copy.RotationalSprings[i].NodeI/J も同上
    ///   - copy.Nodes[i].MasterNodes[d] が存在する場合、copy.Nodes のどこかのインスタンスを指す
    ///   - 元 AnaModel と新 AnaModel は完全に独立 (片方を書き換えても他方に影響なし)
    /// </summary>
    [TestClass]
    public class DeepCopyInvariantsTests
    {
        private static (AnaModel model, Node nI, Node nJ, Node nK, Beam beam1, Beam beam2) BuildSimpleModel()
        {
            var nI = new Node
            {
                Name = "I",
                Coord = new Point3D(0, 0, 0),
                Boundary = new Boundary(true, true, true, true, true, true)
            };
            var nJ = new Node
            {
                Name = "J",
                Coord = new Point3D(1, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };
            var nK = new Node
            {
                Name = "K",
                Coord = new Point3D(2, 0, 0),
                Boundary = new Boundary(false, false, false, false, false, false)
            };
            var mat = new Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam1 = new Beam("B1", sec, nI, nJ, 1.0, 1.0);
            var beam2 = new Beam("B2", sec, nJ, nK, 1.0, 1.0);

            var inputModel = new PileDesign.Models.InputData.InputModel();
            var model = new AnaModel(inputModel, [nI, nJ, nK], [beam1, beam2], [], [], [], []);
            return (model, nI, nJ, nK, beam1, beam2);
        }

        [TestMethod]
        public void DeepCopy_BeamsShareNodesWithAnaModelNodesList()
        {
            var (model, _, _, _, _, _) = BuildSimpleModel();
            var copy = model.DeepCopy();

            var nodeSet = new HashSet<Node>(copy.Nodes, ReferenceEqualityComparer.Instance);
            foreach (var b in copy.Beams)
            {
                Assert.IsTrue(nodeSet.Contains(b.NodeI),
                    $"Beam {b.Name}.NodeI ({b.NodeI.Name}) は copy.Nodes に含まれる参照でなければならない");
                Assert.IsTrue(nodeSet.Contains(b.NodeJ),
                    $"Beam {b.Name}.NodeJ ({b.NodeJ.Name}) は copy.Nodes に含まれる参照でなければならない");
            }
        }

        [TestMethod]
        public void DeepCopy_SharedNodeReferenceBetweenBeams()
        {
            // B1 (nI, nJ), B2 (nJ, nK) で nJ は共有されている。
            // コピー後も B1.NodeJ と B2.NodeI は同一インスタンスでなければならない。
            var (model, _, _, _, _, _) = BuildSimpleModel();
            var copy = model.DeepCopy();

            Assert.AreSame(copy.Beams[0].NodeJ, copy.Beams[1].NodeI,
                "共有ノード nJ はコピー後も単一インスタンスでなければならない");
        }

        [TestMethod]
        public void DeepCopy_MasterNodeFixupPointsIntoCopyNodes()
        {
            // nJ の Ux を nK.Ux の slave にする (master-slave チェーン)
            var (model, nI, nJ, nK, _, _) = BuildSimpleModel();
            nJ.MasterNodes[0] = nK;

            var copy = model.DeepCopy();

            var nodeSet = new HashSet<Node>(copy.Nodes, ReferenceEqualityComparer.Instance);
            var copiedJ = copy.Nodes.First(n => n.Name == "J");
            Assert.IsNotNull(copiedJ.MasterNodes[0], "MasterNodes[0] は null であってはならない");
            Assert.IsTrue(nodeSet.Contains(copiedJ.MasterNodes[0]),
                "MasterNodes[0] は copy.Nodes 上のインスタンスを指さなければならない (元の nK を指していてはダメ)");

            var copiedK = copy.Nodes.First(n => n.Name == "K");
            Assert.AreSame(copiedK, copiedJ.MasterNodes[0],
                "copiedJ.MasterNodes[0] は copiedK と同一参照でなければならない");
        }

        [TestMethod]
        public void DeepCopy_IndependentState_MutatingOriginalDoesNotAffectCopy()
        {
            var (model, _, _, _, _, _) = BuildSimpleModel();
            var copy = model.DeepCopy();

            // 元の nJ の変位を書き換える
            var originalJ = model.Nodes.First(n => n.Name == "J");
            originalJ.CumulativeDisp = new NodeDisp(1.234, 0, 0, 0, 0, 0);

            // コピー側は影響を受けてはならない
            var copiedJ = copy.Nodes.First(n => n.Name == "J");
            Assert.AreNotEqual(1.234, copiedJ.CumulativeDisp.Ux, 1e-15,
                "元の状態変更がコピーに伝播してはいけない (DeepCopy が浅い可能性)");
        }

        [TestMethod]
        public void DeepCopy_IndependentBeamState_MutatingOriginalDoesNotAffectCopy()
        {
            var (model, _, _, _, beam1, _) = BuildSimpleModel();
            var copy = model.DeepCopy();

            // 元 beam の内力を書き換え
            beam1.CumulativeForce = new BeamForce(9.9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            var copiedBeam1 = copy.Beams[0];
            Assert.AreNotEqual(9.9, copiedBeam1.CumulativeForce.Fxi, 1e-15,
                "元 beam の CumulativeForce 変更がコピーに伝播してはいけない");
        }

        [TestMethod]
        public void DeepCopy_RunAnalysisOnCopy_DoesNotMutateOriginal()
        {
            // コピーで剛性組立 → 元モデルの KAA_tan が null のまま
            var (model, _, _, _, _, _) = BuildSimpleModel();
            Assert.IsNull(model.KAA_tan, "初期状態では KAA_tan は null");

            var copy = model.DeepCopy();
            copy.MapOnKtanMat();

            Assert.IsNotNull(copy.KAA_tan, "コピー側は MapOnKtanMat で KAA_tan が設定される");
            Assert.IsNull(model.KAA_tan, "元モデルの KAA_tan は影響を受けてはならない");
        }
    }
}
