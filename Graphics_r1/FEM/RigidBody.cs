using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

namespace PileDesign.FEM
{
    public class RigidBody
    {
        public Node MasterNode { get; set; }
        public List<Node> SlaveNodes { get; set; }
        public bool[] Dofs { get; set; }

        public RigidBody(Node masterNode, bool[] dofs)
        {
            MasterNode = masterNode;
            SlaveNodes = [];
            Dofs = dofs;
        }

        // デフォルトコンストラクタ
        public RigidBody() { }

        // 複数の従属節点を追加するメソッド
        public void AddSlaveNodes(List<Node> slaveNodes)
        {
            SlaveNodes.AddRange(slaveNodes);
        }

        // 従属節点を追加し、従属節点の固定度を設定するメソッド
        public void AddSlaveNode(Node slaveNode)
        {
            SlaveNodes.Add(slaveNode);
            slaveNode.Boundary.Set(Dofs[0], Dofs[1], Dofs[2], Dofs[3], Dofs[4], Dofs[5]);　// 自由度 => 固定度
        }

        // Indexに対してスレイブ成分かどうかを返すメソッド
        public bool IsSlave(Node node, int index)
        {
            return Dofs[index] && SlaveNodes.Contains(node);
        }

        // スレイブ節点座標～マスター節点座標のベクトルを返すメソッド
        public Vector3D GetSlaveArmVector(Node slaveNode)
        {
            return slaveNode.Coord - MasterNode.Coord;
        }

        // マスター節点を返すメソッド
        public Node GetMasterNode()
        {
            return MasterNode;
        }

        public void SetSlaveNodeRelations()
        {
            foreach (var slaveNode in SlaveNodes)
            {
                for (int index = 0; index < 6; index++)
                {
                    if (Dofs[index])
                    {
                        slaveNode.SetMasterNode(index, MasterNode);
                        if (index < 3)
                        {
                            var armVector = GetSlaveArmVector(slaveNode);
                            slaveNode.SetArmVector(index, armVector);
                        }
                    }
                }
                slaveNode.SetTransferMatrix();
            }
        }

        public RigidBody DeepCopy()
        {
            // MasterNode, SlaveNodesをDeepCopy
            var masterNodeCopy = MasterNode?.DeepCopy();
            var slaveNodesCopy = SlaveNodes.Select(n => n?.DeepCopy()).ToList();
            var dofsCopy = (bool[])Dofs.Clone();

            var copy = new RigidBody(masterNodeCopy, dofsCopy)
            {
                SlaveNodes = slaveNodesCopy
            };
            return copy;
        }
    }
}
