using MathNet.Numerics.LinearAlgebra;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System.Windows.Media.Media3D;

namespace TestProject1
{
    /// <summary>
    /// master 側の DOF が拘束されている従属節点に荷重が載っても落ちないこと。
    ///
    /// 拘束された DOF の方程式番号は負になる。荷重ベクトルの組立だけこの判定が
    /// 抜けており、負の添字でベクトルを引いて
    /// ArgumentOutOfRangeException (MathNet の ValidateRange) になっていた。
    ///
    /// 実際に踏むのは次の 3 つが揃ったとき。
    ///   ・「基礎のねじれを拘束」ON → 代表節点の Rz が固定される
    ///   ・剛床連結 → 接合節点が代表節点の Ux/Uy/Rz の従属になる
    ///   ・接合節点に荷重が載る → 杭軸力を外力として与えるモード
    ///     (「入力値＋応力解析結果」/ P-S 非線形ばね)
    /// 剛体連結は RigidBody 経由なのでこの道を通らない。
    ///
    /// 拘束された DOF に対応する式は行列に無く、荷重は支点反力になるので捨ててよい。
    /// 剛性行列の組立は元からこの判定を持っていた。
    /// </summary>
    [TestClass]
    public class RestrainedMasterLoadTests
    {
        /// <param name="restrainRz">代表節点の Rz を拘束するか。</param>
        private static (Node master, Node slave) Build(bool restrainRz)
        {
            var master = new Node
            {
                Name = "ActionPoint",
                Coord = new Point3D(0, 0, 0),
                // Ux, Uy, Rz が自由 (剛床連結の代表節点)。Rz だけ切り替える
                EquationNumber = [0, 1, -1, -1, -1, restrainRz ? -1 : 2],
            };

            var slave = new Node
            {
                Name = "FoundationNode-P1",
                Coord = new Point3D(3.0, 4.0, 0),
                EquationNumber = [-1, -1, 3, 4, 5, -1],
                IsLoaded = true,
                // Ux と Rz に成分を持つ荷重。Rz 成分が master の Rz へ行こうとする
                CumulativedLoad = new NodeLoad(100.0, 0, 0, 0, 0, 50.0),
                IncrementalLoad = new NodeLoad(10.0, 0, 0, 0, 0, 5.0),
            };

            var arm = slave.Coord - master.Coord;
            slave.SetMasterNode(0, master); // Ux
            slave.SetMasterNode(1, master); // Uy
            slave.SetMasterNode(5, master); // Rz
            slave.SetArmVector(0, arm);
            slave.SetArmVector(1, arm);
            slave.SetArmVector(2, arm);
            slave.SetTransferMatrix();

            return (master, slave);
        }

        /// <summary>
        /// master の Rz が自由なら、そこへモーメントが積まれること（対照）。
        ///
        /// 期待値は直接の Rz 荷重 50 だけではない。腕 (3, 4, 0) の先に載る Ux = 100 は
        /// master まわりに -100 x 4 = -400 のモーメントを生むので、合計 -350 になる。
        /// </summary>
        [TestMethod]
        public void FreeMasterRz_MapsTheMomentOntoItsEquation()
        {
            var (_, slave) = Build(restrainRz: false);
            var v = Vector<double>.Build.Sparse(6, 0.0);

            v = slave.MapCumulativeLoadOnGlobalLoad(v);

            Assert.AreEqual(100.0, v[0], 1e-9, "master の Ux へ水平力が行っていない");
            Assert.AreEqual(-350.0, v[2], 1e-9, "master の Rz へモーメントが行っていない");
        }

        /// <summary>拘束されていても落ちず、その成分だけ落ちること。</summary>
        [TestMethod]
        public void RestrainedMasterRz_DoesNotThrowAndDropsThatComponent()
        {
            var (_, slave) = Build(restrainRz: true);
            var v = Vector<double>.Build.Sparse(6, 0.0);

            v = slave.MapCumulativeLoadOnGlobalLoad(v);

            Assert.AreEqual(100.0, v[0], 1e-9, "拘束と無関係な Ux まで落ちている");

            // Rz には式が無いので、自由なときに積まれていた -350 がどこにも現れないこと。
            // 別の DOF へ紛れ込むと、拘束したはずのねじれが荷重として残る。
            for (int i = 0; i < v.Count; i++)
            {
                Assert.IsTrue(double.IsFinite(v[i]), $"eq {i} が有限でない");
                Assert.AreNotEqual(-350.0, v[i], 1e-9, $"Rz のモーメントが eq {i} へ紛れ込んでいる");
            }
        }

        /// <summary>増分側も同じであること（NR の各反復で通る）。</summary>
        [TestMethod]
        public void RestrainedMasterRz_IncrementalLoadIsAlsoSafe()
        {
            var (_, slave) = Build(restrainRz: true);
            var v = Vector<double>.Build.Sparse(6, 0.0);

            v = slave.MapIncrementalLoadOnGlobalLoad(v);

            Assert.AreEqual(10.0, v[0], 1e-9);
        }
    }
}
