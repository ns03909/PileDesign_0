using System;
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

        /// <summary>
        /// 書けない対象があれば、対象と理由を示して出力を止める (<see cref="InvalidOperationException"/>)。
        /// 梁 (<see cref="WriteElements"/>)・ばね・剛体連結で同じ文面にする。
        /// </summary>
        private static void ThrowIfCannotWrite(string what, string unit, List<string> problems, string consequence)
        {
            if (problems.Count == 0) return;
            throw new InvalidOperationException(
                $"解析モデルの{what} {problems.Count} {unit}を MGT に書けないため、出力を止めました (書くと{consequence})。\n"
                + string.Join("\n", problems.Take(10))
                + (problems.Count > 10 ? $"\n…ほか {problems.Count - 10} {unit}" : "")
                + "\n解析モデルを作り直す (杭要素分割・解析をやり直す) と直ることがあります。");
        }

        /// <summary>メッセージ用の節点の呼び名 (名前、無ければ座標)。</summary>
        private static string NodeLabel(Node n)
            => !string.IsNullOrWhiteSpace(n.Name) ? $"節点「{n.Name}」"
             : $"節点 ({n.Coord.X:0.###}, {n.Coord.Y:0.###}, {n.Coord.Z:0.###})";

        /// <summary>メッセージ用のばねの呼び名 (名前、無ければ一覧での番号)。</summary>
        private static string SpringLabel(TwoNodeSpringElement? s, int index)
            => s != null && !string.IsNullOrWhiteSpace(s.Name) ? $"「{s.Name}」" : $" #{index + 1}";

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

            // ★マスター・スレーブの節点が解析モデルの節点に無い剛体連結は、黙って飛ばさずに出力を止める
            //   (以前はマスターが無ければリンクごと、スレーブが無ければそのスレーブだけを外していた。
            //   出力は成功するので、剛体連結の欠けた MGT ファイルを正常な成果物として渡していた)。
            var problems = new List<string>();
            foreach (var ((master, dofStr), slaves) in groups)
            {
                if (!nodeIdMap.ContainsKey(master))
                    problems.Add($"・剛体連結 (マスター {NodeLabel(master)}・自由度 {dofStr}・スレーブ {slaves.Count} 個): "
                                 + "マスター節点が解析モデルの節点にありません");
                foreach (var slave in slaves.Where(s => !nodeIdMap.ContainsKey(s)))
                    problems.Add($"・剛体連結 (マスター {NodeLabel(master)}・自由度 {dofStr}): "
                                 + $"スレーブ節点 ({NodeLabel(slave)}) が解析モデルの節点にありません");
            }
            ThrowIfCannotWrite("剛体連結", "件", problems, "剛体連結の欠けたモデルになります");

            writer.WriteLine("*RIGIDLINK    ; Rigid Link");
            writer.WriteLine("; M-NODE, DOF, S-NODE LIST, GROUP");
            foreach (var ((master, dofStr), slaves) in groups)
            {
                int masterId = nodeIdMap[master];
                var slaveIds = slaves.Select(s => nodeIdMap[s].ToString()).ToList();
                writer.WriteLine($" {masterId}, {dofStr}, {string.Join(" ", slaveIds)}, ");
            }
            writer.WriteLine();
        }
    }
}
