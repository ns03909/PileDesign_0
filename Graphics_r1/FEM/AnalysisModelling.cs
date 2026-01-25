using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    public class AnalysisModelling : Models.BaseModel
    {
        private InputModel _inputModel;
        public InputModel InputModel
        {
            get => _inputModel;
            set => SetProperty(ref _inputModel, value);
        }

        public List<Node> Nodes { get; set; } = [];
        public List<DummyBeam> DummyBeams { get; set; } = [];
        public List<Beam> Beams { get; set; } = [];
        public List<RigidBody> RigidBodies { get; set; } = [];
        public List<HorizontalSoilSpring> HorizontalSoilSprings { get; set; } = []; // 水平地盤ばね
        public List<RotationalSpring> RotationalSprings { get; set; } = [];        // 杭頭回転ばね（RunAsyncでカーブ/剛性をセット）

        // コンストラクタ
        public AnalysisModelling(InputModel inputModel)
        {
            InputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
            Initialize();
        }

        private void Initialize()
        {
            AddActionPointNode();
            AddDoatsuGoryokuBane();
            AddPile();
        }

        // 代表点の追加
        private void AddActionPointNode()
        {
            if (InputModel.LoadCasesInput?.LoadCasesLevel1 == null || InputModel.LoadCasesInput.LoadCasesLevel1.Count == 0)
                throw new InvalidOperationException("LoadCasesLevel1 が存在しません。");

            var loadCase = InputModel.LoadCasesInput.LoadCasesLevel1[0];
            Node actionNode = new();
            actionNode.SetNodeInfo("ActionPoint", loadCase.ForceActionPointX, loadCase.ForceActionPointY, loadCase.ForceActionPointAltitude);
            Nodes.Add(actionNode);

            Nodes[^1].IsLoaded = true;
            Nodes[0].SetBoundary(GetActionPointBoundary());

            // RigidBodies[0] : 完全剛体（DGB ノード + CapNode 用）
            RigidBodies.Add(new(actionNode, [true, true, true, true, true, true])); // 
        }

        // 杭配置により作用点の拘束を返すメソッド
        private Boundary GetActionPointBoundary()
        {
            if (InputModel.PileLayoutItems == null || !InputModel.PileLayoutItems.Any())
                throw new InvalidOperationException("杭配置が存在しません。");

            double xmax = InputModel.PileLayoutItems.Max(p => p.X);
            double xmin = InputModel.PileLayoutItems.Min(p => p.X);
            double ymax = InputModel.PileLayoutItems.Max(p => p.Y);
            double ymin = InputModel.PileLayoutItems.Min(p => p.Y);

            // 杭配置がX、Y方向いずれかにスタンスがない場合、回転拘束をする
            if (xmax - xmin < 1e-6 || ymax - ymin < 1e-6)
                return new Boundary(false, false, false, true, true, true);

            // 杭配置がX、Y方向ともにスタンスがある場合、拘束しない
            return new Boundary(false, false, false, false, false, false);
        }

        // 土圧合力ばねの追加
        private void AddDoatsuGoryokuBane()
        {
            if (InputModel.ElementDivision.SoilEmbedment == null || InputModel.ElementDivision.DoatsuGoryokuBane == null)
                return;

            var doatsuGoryokuBane = InputModel.ElementDivision.DoatsuGoryokuBane;

            // ノード生成・初期化
            (Node embedmentNode, Node soilNode, HorizontalSoilSpring spring) CreateDGBNodesAndSpring(double x, double y, double z)
            {
                var embedmentNode = new Node();
                embedmentNode.SetNodeInfo("EmbedmentNode", x, y, z);
                var soilNode = new Node();
                soilNode.SetNodeInfo("SoilNode", x, y, z);
                soilNode.SetIsForcedDisped(true);
                soilNode.SetBoundary(new(false, false, true, true, true, true));
                var spring = new HorizontalSoilSpring("地盤ばね", embedmentNode, soilNode);
                return (embedmentNode, soilNode, spring);
            }

            // 追加処理
            void AddDGBNodesAndSpring(Node embedmentNode, Node soilNode, HorizontalSoilSpring spring)
            {
                Nodes.Add(embedmentNode);
                RigidBodies[0].AddSlaveNode(embedmentNode);
                Nodes.Add(soilNode);
                HorizontalSoilSprings.Add(spring);
            }

            Node? prevEmbedmentNode = null;
            Node? prevSoilNode = null;
            HorizontalSoilSpring? prevSpring = null;

            for (int i = 0; i < doatsuGoryokuBane.Items.Count; i++)
            {
                var item = doatsuGoryokuBane.Items[i];
                double x = (item.X1 + item.X2) * 0.5;
                double y = (item.Y1 + item.Y2) * 0.5;

                // 上端
                if (i == 0)
                {
                    (prevEmbedmentNode, prevSoilNode, prevSpring) = CreateDGBNodesAndSpring(x, y, item.ZTop);
                    AddDGBNodesAndSpring(prevEmbedmentNode, prevSoilNode, prevSpring);
                }
                item.SetTopNodesAndSpring(prevEmbedmentNode, prevSoilNode, prevSpring);

                // 下端
                (Node embedmentNode, Node soilNode, HorizontalSoilSpring spring) = CreateDGBNodesAndSpring(x, y, item.ZBtm);
                AddDGBNodesAndSpring(embedmentNode, soilNode, spring);
                item.SetBtmNodesAndSpring(embedmentNode, soilNode, spring);

                DummyBeams.Add(new("dummyBeam", Nodes[^4], Nodes[^2]));
                prevEmbedmentNode = embedmentNode;
                prevSoilNode = soilNode;
                prevSpring = spring;
            }
        }

        // 杭要素の追加（接続ノードを明示引数に）
        private void SetPileElement(SoilPile soilPile, int segIndex, Node upperNode, Node lowerNode)
        {
            double youngsModulus = soilPile.PileBodySegments[segIndex].PileSection.ConcreteE * 1000.0; // kN/m2
            double shearModulus = Utils.GetShearModulus(youngsModulus, 0.2); // kN/m2
            double area = soilPile.PileBodySegments[segIndex].PileSection.EA / youngsModulus; // m2
            double inertia = soilPile.PileBodySegments[segIndex].PileSection.EI / youngsModulus; // m4
            double torsionalInertia = soilPile.PileBodySegments[segIndex].PileSection.GJ / shearModulus; // m4
            Material material = new(youngsModulus, 0.2);
            Section section = new(material, area, area, area, torsionalInertia, inertia, inertia);

            Beams.Add(new("beam", section, upperNode, lowerNode, 1.0, 1.0));
            Beams[^1].HorizontalSoilReactionItem = soilPile.HorizontalSoilReactions[segIndex];

            // 重要: M-φ は RunAsync で N に応じてセットするため、ここでは設定しない
            // Beams[^1].PileBodyNo/SegmentIndex のみ付与（RunAsyncがSectionへ辿るため）
        }

        // 杭の追加
        private void AddPile()
        {
            if (InputModel.PileLayoutItems == null) return;

            foreach (var pile in InputModel.PileLayoutItems)
            {
                pile.PileNodes.Clear();
                pile.SoilNodes.Clear();
                pile.Beams.Clear();
                pile.HorizontalSoilSprings.Clear();

                double x = pile.X;
                double y = pile.Y;

                int soilPileAltNo = pile.SoilPileAltNo;

                double initialRotK = 10e12;

                if (InputModel.ElementDivision.SoilPiles == null ||
                    soilPileAltNo - 1 < 0 ||
                    soilPileAltNo - 1 >= InputModel.ElementDivision.SoilPiles.Count)
                {
                    throw new InvalidOperationException("対応するSoilPileが存在しません。");
                }

                SoilPile soilPile = InputModel.ElementDivision.SoilPiles[soilPileAltNo - 1];

                // 剛床側（キャップ側）節点を同一点に生成し、剛体へスレーブ
                Node capNode = new();
                double z0 = soilPile.ZDataItems[0].Z;
                capNode.SetNodeInfo($"CapNode-{pile.No}", x, y, z0);
                Nodes.Add(capNode);
                RigidBodies[0].AddSlaveNode(capNode); // Cap Nodeをスレーブにする。

                Node? prevPileNode = null; // 直前の杭節点
                for (int i = 0; i < soilPile.ZDataItems.Count; i++)
                {
                    double z = soilPile.ZDataItems[i].Z;

                    Node pileNode = new();
                    pileNode.SetNodeInfo($"PileNode-{pile.No}-{i}", x, y, z);
                    Nodes.Add(pileNode);
                    pile.PileNodes.Add(pileNode);


                    if (i == 0)
                    {
                        // 杭頭回転ばね（初期剛性を与える）
                        var rxy = new RotationalSpring($"RθXY-{pile.No}", capNode, pileNode, initialRotK)
                        {
                            PileBodyNo = pile.PileBodyNo,
                            //TieUx = false,
                            //TieUy = false,
                            //TieUz = false,
                            //TieRz = false,
                            TieUx = true,
                            TieUy = true,
                            TieUz = true,
                            TieRz = true, // Rz も一致させたいなら true
                            Kbig = 10e12
                        };
                        RotationalSprings.Add(rxy);
                        pile.PileTopRotationalSpring = rxy;

                        // pileNode を RigidBodies[1] にスレーブ登録（並進＋Rz を剛結、Rx,Ry は自由）
                        //RigidBodies[0].AddSlaveNode(pileNode); // pile_0をスレーブにする。

                        prevPileNode = pileNode; // 上端ノードはここで初期化
                    }
                    else if (i != soilPile.ZDataItems.Count - 1)
                    {
                        // 杭中間：上端(prevPileNode) と 下端(pileNode) で要素を作る
                        if (prevPileNode == null) throw new InvalidOperationException("prevPileNode is null");
                        SetPileElement(soilPile, i - 1, prevPileNode, pileNode);
                        Beams[^1].PileBodyNo = soilPile.PileBodyNo;
                        Beams[^1].SegmentIndex = i - 1;
                        pile.Beams.Add(Beams[^1]);
                        if (i == 1) Beams[^1].SetPileTopFlag(true);

                        prevPileNode = pileNode; // 次要素の上端に更新
                    }
                    else // 先端
                    {
                        // 先端処理（同様に prevPileNode を利用）
                        if (prevPileNode == null) throw new InvalidOperationException("prevPileNode is null for tip");
                        SetPileElement(soilPile, i - 1, prevPileNode, pileNode);
                        Beams[^1].PileBodyNo = soilPile.PileBodyNo;
                        Beams[^1].SegmentIndex = i - 1;
                        pile.Beams.Add(Beams[^1]);
                        pileNode.SetBoundary(new(false, false, true, false, false, false));
                    }

                    // 土節点
                    Node soilNode = new();
                    soilNode.SetNodeInfo($"SoilNode-{pile.No}-{i}", x, y, z);
                    soilNode.SetIsForcedDisped(true);
                    soilNode.SetBoundary(new(false, false, true, true, true, true));
                    Nodes.Add(soilNode);
                    pile.SoilNodes.Add(soilNode);

                    // 水平土ばね（杭節点−土節点）
                    var hspring = new HorizontalSoilSpring($"HorizontalSoilSpring-{pile.No}-{i}", pileNode, soilNode);
                    HorizontalSoilSprings.Add(hspring);
                    pile.HorizontalSoilSprings.Add(hspring);
                }
            }

            foreach (var rb in RigidBodies)
            {
                rb.SetSlaveNodeRelations();
            }

#if DEBUG
            // デバッグ出力を追加
            DumpRigidBodyAndNodeRelationsForDebug();
#endif
        }

#if DEBUG
        // デバッグ用: 剛体／節点／回転ばねの関係を出力
        private void DumpRigidBodyAndNodeRelationsForDebug()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== DumpRigidBodyAndNodeRelations START ===");

                // RigidBodies の一覧（存在すれば MasterNode/SlaveNodes を列挙）
                for (int i = 0; i < RigidBodies.Count; i++)
                {
                    var rb = RigidBodies[i];
                    string masterName = rb?.MasterNode?.Name ?? "null";
                    var slaveNames = (rb?.SlaveNodes != null) ? string.Join(", ", rb.SlaveNodes.Select(n => n?.Name ?? "null")) : "<no slaves>";
                    System.Diagnostics.Debug.WriteLine($"RigidBody[{i}] Master={masterName} Slaves=[{slaveNames}]");
                }

                // 各 Node の EquationNumber / MasterNodes / TransferMatrix を出力
                foreach (var node in Nodes)
                {
                    var eqNums = node.EquationNumber != null ? string.Join(",", node.EquationNumber) : "<null>";
                    var masterNames = node.MasterNodes != null ? string.Join(",", node.MasterNodes.Select(m => m != null ? m.Name : "null")) : "<null>";
                    string tmat = node.TransferMatrix != null ? $"[{string.Join(";", node.TransferMatrix.EnumerateRows().Select(r => string.Join(",", r)))}]" : "<null>";
                    System.Diagnostics.Debug.WriteLine($"Node: {node.Name}, Eq=[{eqNums}], Masters=[{masterNames}], Boundary=[Ux{node.Boundary.Ux},Uy{node.Boundary.Uy},Uz{node.Boundary.Uz},Rx{node.Boundary.Rx},Ry{node.Boundary.Ry},Rz{node.Boundary.Rz}], TransferMatrix={tmat}");
                }

                // 回転ばねの端点確認
                for (int i = 0; i < RotationalSprings.Count; i++)
                {
                    var rs = RotationalSprings[i];
                    System.Diagnostics.Debug.WriteLine($"RotationalSpring[{i}] Name={rs.Name}, NodeI={(rs.NodeI?.Name ?? "null")}, NodeJ={(rs.NodeJ?.Name ?? "null")}, Mode={rs.Mode}, Ktheta={rs.Ktheta}, KthetaXY={rs.KthetaXY}");
                }

                // 杭頭 Beam の簡易チェック（IsPileTop）
                foreach (var b in Beams.Where(b => b.IsPileTop))
                {
                    System.Diagnostics.Debug.WriteLine($"Beam (pile top) Name={b.Name}, NodeI={b.NodeI?.Name}, NodeJ={b.NodeJ?.Name}, Length={b.Length}, PileBodyNo={b.PileBodyNo}, SegmentIndex={b.SegmentIndex}");
                }

                System.Diagnostics.Debug.WriteLine("=== DumpRigidBodyAndNodeRelations END ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DumpRigidBodyAndNodeRelations ERROR: " + ex);
            }
        }
#endif
    }
}
