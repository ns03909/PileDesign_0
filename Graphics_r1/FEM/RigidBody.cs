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

        // スレーブ節点の元境界を保持して復元するためのマップ
        private readonly Dictionary<Node, Boundary> _savedBoundaries = [];

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
            foreach (var n in slaveNodes)
                AddSlaveNode(n);
        }

        // 従属節点を追加し、従属節点の固定度を設定するメソッド
        public void AddSlaveNode(Node slaveNode)
        {
            //SlaveNodes.Add(slaveNode);
            //slaveNode.Boundary.Set(Dofs[0], Dofs[1], Dofs[2], Dofs[3], Dofs[4], Dofs[5]);　// 自由度 => 固定度
            if (slaveNode == null) return;

            // 既に登録済みなら何もしない
            if (SlaveNodes.Contains(slaveNode)) return;

            // 元の境界を保存（初回のみ）
            if (!_savedBoundaries.ContainsKey(slaveNode) && slaveNode.Boundary != null)
            {
                _savedBoundaries[slaveNode] = new Boundary(
                    slaveNode.Boundary.Ux,
                    slaveNode.Boundary.Uy,
                    slaveNode.Boundary.Uz,
                    slaveNode.Boundary.Rx,
                    slaveNode.Boundary.Ry,
                    slaveNode.Boundary.Rz
                );
            }

            SlaveNodes.Add(slaveNode);

            // 自由度 => 固定度を設定 (line 38 で null チェック済、Boundary も line 44 で確認済)
            slaveNode.Boundary?.Set(Dofs[0], Dofs[1], Dofs[2], Dofs[3], Dofs[4], Dofs[5]);

            // master/slave 関係は SetSlaveNodeRelations()/AnaModel.SetSlaveNodes() で構築される想定だが、
            // ここでも一次的にセットしておく（念のため）
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

        // 従属節点を削除するメソッド
        public void RemoveSlaveNode(Node slaveNode)
        {
            //if (SlaveNodes.Remove(slaveNode))
            //{
            //    // スレーブ解除時は節点境界を「自由（デフォルト）」に戻す
            //    // （必要なら元の境界を保存して復元する仕組みに変更して下さい）
            //    slaveNode.SetBoundary(new Boundary(false, false, false, false, false, false));
            //}
            if (slaveNode == null) return;

            if (SlaveNodes.Remove(slaveNode))
            {
                // 1) 元の境界が保存されていれば復元、なければデフォルト自由にする
                if (_savedBoundaries.TryGetValue(slaveNode, out var origBoundary))
                {
                    slaveNode.SetBoundary(new Boundary(
                        origBoundary.Ux, origBoundary.Uy, origBoundary.Uz,
                        origBoundary.Rx, origBoundary.Ry, origBoundary.Rz));
                    _savedBoundaries.Remove(slaveNode);
                }
                else
                {
                    slaveNode.SetBoundary(new Boundary(false, false, false, false, false, false));
                }

                // 2) MasterNodes の参照をクリア（この RigidBody が与えた master のみ）
                for (int i = 0; i < 6; i++)
                {
                    if (slaveNode.MasterNodes != null && slaveNode.MasterNodes[i] == MasterNode)
                    {
                        slaveNode.MasterNodes[i] = null;
                    }
                }

                // 3) SlaveArm をリセット（副作用を避けるため）
                slaveNode.SlaveArm = new Vector3S(0, 0, 0);

                // 4) 転送行列を更新
                slaveNode.SetTransferMatrix();
            }
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
