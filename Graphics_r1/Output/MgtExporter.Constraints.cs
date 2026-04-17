using System.Collections.Generic;
using System.IO;
using System.Linq;
using PileDesign.FEM;

namespace PileDesign.Output
{
    // MGT の拘束・剛体連結セクション（CONSTRAINT, RIGIDLINK）を出力する partial。
    public partial class MgtExporter
    {
        private void WriteConstraints(StreamWriter writer, ExportContext ctx)
        {
            var nodeIdMap = ctx.NodeIdMap;
            var soilBoundaryNodes = ctx.SoilBoundaryNodes;

            writer.WriteLine("*CONSTRAINT    ; Supports");
            writer.WriteLine("; NODE_LIST, CONST(Dx,Dy,Dz,Rx,Ry,Rz), GROUP");

            // X方向地盤境界節点（NodeJ of HorizontalSoilSpring）を全DOF固定
            // MULTI LINEAR DIR=0 は軸方向のみ剛性を与えるため、他のDOFは特異になる
            // SPDISP で強制変位を与えるために完全固定（SUPPORT）状態が必要
            foreach (var node in soilBoundaryNodes)
            {
                if (!nodeIdMap.TryGetValue(node, out int id)) continue;
                writer.WriteLine($"   {id}, 111111, ");
            }

            // Y方向仮想地盤節点を全DOF固定
            foreach (var (spring, yId) in ctx.SpringYNodeIds)
            {
                writer.WriteLine($"   {yId}, 111111, ");
            }

            // それ以外の節点はGetBoundary/MasterNodesに基づく
            foreach (var (node, id) in nodeIdMap)
            {
                if (soilBoundaryNodes.Contains(node)) continue; // 既出

                char[] dofCode = new char[6];
                bool hasConstraint = false;
                for (int i = 0; i < 6; i++)
                {
                    bool isFixed = node.GetBoundary(i);
                    bool isSlave = node.MasterNodes[i] != null;
                    if (isFixed && !isSlave)
                    {
                        dofCode[i] = '1';
                        hasConstraint = true;
                    }
                    else
                    {
                        dofCode[i] = '0';
                    }
                }

                if (hasConstraint)
                {
                    writer.WriteLine($"   {id}, {new string(dofCode)}, ");
                }
            }
            writer.WriteLine();
        }

        /// <summary>
        /// ノードの指定DOFにおける最上位マスター（連鎖を辿った先のroot）を返す。
        /// master-slaveの連鎖（A→B→C）を解決してA→Cの直接関係に折り畳む。
        /// midasは master が slave としても使われる形式を許可しないため。
        /// </summary>
        private static Node ResolveRootMaster(Node slave, int dof)
        {
            if (slave?.MasterNodes?[dof] == null) return null;

            var visited = new HashSet<Node>(ReferenceEqualityComparer.Instance) { slave };
            Node master = slave.MasterNodes[dof];
            if (!visited.Add(master)) return master;

            while (master.MasterNodes?[dof] != null)
            {
                var next = master.MasterNodes[dof];
                if (!visited.Add(next)) break; // 循環検出
                master = next;
            }
            return master;
        }

        private void WriteRigidLinks(StreamWriter writer, ExportContext ctx)
        {
            var nodeIdMap = ctx.NodeIdMap;
            // 全節点の MasterNodes 関係を収集（RigidBody由来 + 直接設定の両方を捕捉）
            // 連鎖（A→B→C）を解決して A→C の直接関係に折り畳む
            // Key: (Root Master, DOFパターン) → slave節点リスト
            var groups = new Dictionary<(Node master, string dofStr), List<Node>>();

            foreach (var node in _anaModel.Nodes)
            {
                if (node.MasterNodes == null) continue;

                // この節点の root master 別DOFパターンを収集
                var byMaster = new Dictionary<Node, bool[]>(ReferenceEqualityComparer.Instance);
                for (int i = 0; i < 6; i++)
                {
                    var root = ResolveRootMaster(node, i);
                    if (root == null || ReferenceEquals(root, node)) continue;
                    if (!byMaster.ContainsKey(root))
                        byMaster[root] = new bool[6];
                    byMaster[root][i] = true;
                }

                foreach (var (master, dofs) in byMaster)
                {
                    string dofStr = string.Concat(dofs.Select(d => d ? "1" : "0"));
                    var key = (master, dofStr);
                    if (!groups.ContainsKey(key))
                        groups[key] = new List<Node>();
                    groups[key].Add(node);
                }
            }

            if (groups.Count == 0) return;

            writer.WriteLine("*RIGIDLINK    ; Rigid Link");
            writer.WriteLine("; M-NODE, DOF, S-NODE LIST, GROUP");
            foreach (var ((master, dofStr), slaves) in groups)
            {
                if (!nodeIdMap.TryGetValue(master, out int masterId)) continue;

                var slaveIds = new List<string>();
                foreach (var slave in slaves)
                {
                    if (nodeIdMap.TryGetValue(slave, out int slaveId))
                        slaveIds.Add(slaveId.ToString());
                }
                if (slaveIds.Count > 0)
                {
                    writer.WriteLine($" {masterId}, {dofStr}, {string.Join(" ", slaveIds)}, ");
                }
            }
            writer.WriteLine();
        }
    }
}
