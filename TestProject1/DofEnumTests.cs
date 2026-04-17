using PileDesign.FEM;
using System.Windows.Media.Media3D;

namespace TestProject1
{
    /// <summary>
    /// Dof 列挙子と Node の型安全マスター節点アクセサのテスト。
    /// </summary>
    [TestClass]
    public class DofEnumTests
    {
        [TestMethod]
        public void Dof_IntegerValuesMatchLegacyIndices()
        {
            // MasterNodes[] / EquationNumber[] の従来の 0-5 添字と一致することを固定する。
            Assert.AreEqual(0, (int)Dof.Ux);
            Assert.AreEqual(1, (int)Dof.Uy);
            Assert.AreEqual(2, (int)Dof.Uz);
            Assert.AreEqual(3, (int)Dof.Rx);
            Assert.AreEqual(4, (int)Dof.Ry);
            Assert.AreEqual(5, (int)Dof.Rz);
        }

        [TestMethod]
        public void GetMaster_ReturnsNullWhenUnset()
        {
            var node = new Node();
            Assert.IsNull(node.GetMaster(Dof.Ux));
            Assert.IsNull(node.GetMaster(Dof.Rz));
        }

        [TestMethod]
        public void SetMaster_ThenGetMaster_RoundTrips()
        {
            var node = new Node();
            var master = new Node { Name = "M", Coord = new Point3D(0, 0, 0) };
            node.SetMaster(Dof.Uy, master);

            Assert.AreSame(master, node.GetMaster(Dof.Uy));
            Assert.IsNull(node.GetMaster(Dof.Ux));
            Assert.IsNull(node.GetMaster(Dof.Uz));
        }

        [TestMethod]
        public void HasMasterAt_ReflectsSetState()
        {
            var node = new Node();
            Assert.IsFalse(node.HasMasterAt(Dof.Rx));

            node.SetMaster(Dof.Rx, new Node());
            Assert.IsTrue(node.HasMasterAt(Dof.Rx));
            Assert.IsFalse(node.HasMasterAt(Dof.Ry));
        }

        [TestMethod]
        public void HasMasterAt_SetNullClearsState()
        {
            var node = new Node();
            node.SetMaster(Dof.Rz, new Node());
            Assert.IsTrue(node.HasMasterAt(Dof.Rz));

            node.SetMaster(Dof.Rz, null!);
            Assert.IsFalse(node.HasMasterAt(Dof.Rz));
        }

        [TestMethod]
        public void HasMasterSlave_FalseWhenNoMasters()
        {
            var node = new Node();
            Assert.IsFalse(node.HasMasterSlave);
        }

        [TestMethod]
        public void HasMasterSlave_TrueWhenAnyMasterSet()
        {
            var node = new Node();
            node.SetMaster(Dof.Uz, new Node());
            Assert.IsTrue(node.HasMasterSlave);
        }

        [TestMethod]
        public void LegacyArrayAccess_StillWorks()
        {
            // 既存のループベースコード（MasterNodes[i]）が引き続き機能することを確認。
            var node = new Node();
            var master = new Node();
            node.SetMaster(Dof.Ry, master);

            Assert.AreSame(master, node.MasterNodes[(int)Dof.Ry]);
            Assert.AreSame(master, node.MasterNodes[4]); // 従来の数値インデックス
        }
    }
}
