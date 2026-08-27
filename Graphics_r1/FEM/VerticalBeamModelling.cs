using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    /// <summary>
    /// 基礎梁鉛直解析用のFEMモデル構築クラス。
    /// AnalysisModelling.csのパターンに準拠した簡易版。
    ///
    /// モデル構成:
    ///   - 接合節点: 各PileLayoutItemの位置に FoundationNode-P{No}（Uz,Rx,Ry自由）
    ///   - 地盤節点: 各杭直下に GroundNode-P{No}（全DOF固定）
    ///   - 基礎梁中間節点: FoundationBeamInput.NodesからFoundationNode-{No}（Uz,Rx,Ry自由）
    ///   - 一般節点: InputNodes(Type==General)からInputNode-{No}（Uz,Rx,Ry自由）
    ///   - 基礎梁: FoundationBeamInput.Beamsから梁要素
    ///   - 杭ばね: HorizontalSoilSpring(ConnectionNode↔GroundNode)、Kzのみ設定
    ///   - 境界条件: 全節点Ux,Uy,Rz固定（鉛直解析のみ）、Uz,Rx,Ry自由
    /// </summary>
    public class VerticalBeamModelling
    {
        private readonly InputModel _inputModel;

        public List<Node> Nodes { get; } = [];
        public List<Beam> Beams { get; } = [];
        public List<HorizontalSoilSpring> VerticalPileSprings { get; } = [];

        /// <summary>
        /// 杭No → 非線形ばね曲線
        /// </summary>
        public Dictionary<int, VerticalPileSpringCurve> SpringCurves { get; } = [];

        /// <summary>
        /// 杭No → ConnectionNode（ばねのNodeI）
        /// </summary>
        public Dictionary<int, Node> ConnectionNodes { get; } = [];

        /// <summary>
        /// 杭No → VerticalPileSpring
        /// </summary>
        public Dictionary<int, HorizontalSoilSpring> PileSpringMap { get; } = [];

        // Guid→FEM節点名のルックアップ
        private Dictionary<Guid, string> _pileGuidToFemName = [];

        // Material/Section キャッシュ
        // 鍵の取り方は AnalysisModelling と同じ理由 (E だけ / せん断断面積抜きだと取り違える)
        private readonly ConcurrentDictionary<(double E, double Nu), Material> _materialCache = new();
        private readonly ConcurrentDictionary<(double, double, double, double, double, double, double), Section> _sectionCache = new();

        // 鉛直解析の共通Boundary
        private static readonly Boundary VerticalFreeBoundary = new(true, true, false, false, false, true);  // Ux,Uy,Rz固定, Uz,Rx,Ry自由
        private static readonly Boundary AllFixedBoundary = new(true, true, true, true, true, true);         // 全固定（地盤節点）

        public VerticalBeamModelling(InputModel inputModel)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
            Initialize();
        }

        private void Initialize()
        {
            AddFoundationBeamNodes();     // 基礎梁中間節点
            AddInputNodes();              // 一般節点（General型）
            AddConnectionAndGroundNodes(); // 接合節点＋地盤節点＋杭ばね曲線
            AddFoundationBeams();          // 基礎梁
            AddVerticalPileSprings();      // 鉛直杭ばね
        }

        /// <summary>
        /// 基礎梁中間節点（FoundationNode-{No}）の追加
        /// </summary>
        private void AddFoundationBeamNodes()
        {
            if (_inputModel.FoundationBeamInput?.Nodes == null)
                return;

            foreach (var fbNode in _inputModel.FoundationBeamInput.Nodes)
            {
                string name = $"FoundationNode-{fbNode.No}";
                if (Nodes.Any(n => n.Name == name))
                    continue;

                var node = new Node();
                node.SetNodeInfo(name, fbNode.X, fbNode.Y, fbNode.Z);
                node.SetBoundary(VerticalFreeBoundary);
                Nodes.Add(node);
            }
        }

        /// <summary>
        /// 一般節点（InputNode Type==General）の追加
        /// </summary>
        private void AddInputNodes()
        {
            if (_inputModel.InputNodes == null)
                return;

            foreach (var inputNode in _inputModel.InputNodes)
            {
                if (inputNode.Type != NodeType.General) continue;

                var node = new Node();
                node.SetNodeInfo($"InputNode-{inputNode.No}", inputNode.X, inputNode.Y, inputNode.Z);
                node.SetBoundary(VerticalFreeBoundary);
                Nodes.Add(node);
            }
        }

        /// <summary>
        /// 接合節点（FoundationNode-P{No}）＋地盤節点（GroundNode-P{No}）の追加
        /// 同時に杭ばね曲線(SpringCurves)を構築する
        /// </summary>
        private void AddConnectionAndGroundNodes()
        {
            if (_inputModel.PileLayoutItems == null)
                return;

            _pileGuidToFemName = [];

            foreach (var pile in _inputModel.PileLayoutItems)
            {
                string connName = $"FoundationNode-P{pile.No}";
                string groundName = $"GroundNode-P{pile.No}";

                // Guid→FEM名辞書に登録
                if (pile.UniqueId != Guid.Empty)
                    _pileGuidToFemName[pile.UniqueId] = connName;

                // 接合節点 (v2 セマンティクス: pile.Z はすでに接合節点 Z)
                if (!Nodes.Any(n => n.Name == connName))
                {
                    var connNode = new Node();
                    connNode.SetNodeInfo(connName, pile.X, pile.Y, pile.Z);
                    connNode.SetBoundary(VerticalFreeBoundary);
                    Nodes.Add(connNode);
                    ConnectionNodes[pile.No] = connNode;
                }
                else
                {
                    ConnectionNodes[pile.No] = Nodes.First(n => n.Name == connName);
                }

                // 地盤節点（接合節点と同位置、全固定）
                var groundNode = new Node();
                groundNode.SetNodeInfo(groundName, pile.X, pile.Y, pile.Z);
                groundNode.SetBoundary(AllFixedBoundary);
                Nodes.Add(groundNode);

                // 杭ばね曲線の構築（LoadDisplacementsが存在する場合のみ）
                var soilPile = pile.SoilPile;
                if (soilPile?.LoadDisplacements != null && soilPile.LoadDisplacements.Count > 0)
                {
                    SpringCurves[pile.No] = new VerticalPileSpringCurve(soilPile.LoadDisplacements);
                }
            }
        }

        /// <summary>
        /// 基礎梁の追加（AnalysisModelling.AddFoundationBeams()のパターンに準拠）
        /// </summary>
        private void AddFoundationBeams()
        {
            if (_inputModel.FoundationBeamInput?.Beams == null || _inputModel.FoundationBeamInput.Beams.Count == 0)
                return;

            foreach (var fbBeam in _inputModel.FoundationBeamInput.Beams)
            {
                var nodeI = ResolveFemNode(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                var nodeJ = ResolveFemNode(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);

                if (nodeI == null || nodeJ == null)
                {
                    int beamNo = _inputModel.FoundationBeamInput.GetBeamNo(fbBeam);
                    string detail = $"基礎梁{beamNo}の節点が見つかりません。\n" +
                        $"I端: Type={fbBeam.NodeI_Type}, Id={fbBeam.NodeI_Id} → {(nodeI != null ? nodeI.Name : "未解決")}\n" +
                        $"J端: Type={fbBeam.NodeJ_Type}, Id={fbBeam.NodeJ_Id} → {(nodeJ != null ? nodeJ.Name : "未解決")}";
                    throw new InvalidOperationException(detail);
                }

                var section = CreateFoundationBeamSection(fbBeam);
                var beam = new Beam($"FoundationBeam-{_inputModel.FoundationBeamInput.GetBeamNo(fbBeam)}", section, nodeI, nodeJ, 1.0, 1.0);
                Beams.Add(beam);
            }
        }

        /// <summary>
        /// 鉛直杭ばねの追加（ConnectionNode↔GroundNode）
        /// 初期剛性として各杭の InitialTangentStiffness を Kz に設定
        /// </summary>
        private void AddVerticalPileSprings()
        {
            if (_inputModel.PileLayoutItems == null)
                return;

            foreach (var pile in _inputModel.PileLayoutItems)
            {
                if (!ConnectionNodes.TryGetValue(pile.No, out var connNode))
                    continue;

                var groundNode = Nodes.FirstOrDefault(n => n.Name == $"GroundNode-P{pile.No}");
                if (groundNode == null) continue;

                // 杭ばねの作成
                var spring = new HorizontalSoilSpring($"VerticalPileSpring-P{pile.No}", connNode, groundNode);

                // 初期剛性の設定
                double kz0 = SpringCurves.TryGetValue(pile.No, out var curve)
                    ? curve.InitialTangentStiffness
                    : FemConstants.DefaultVerticalPileStiffness; // フォールバック

                spring.SetKe(0, 0, kz0, 0, 0, 0, true);  // KeTan
                spring.SetKe(0, 0, kz0, 0, 0, 0, false); // KeSec

                VerticalPileSprings.Add(spring);
                PileSpringMap[pile.No] = spring;
            }
        }

        /// <summary>
        /// AnaModelを構築する
        /// </summary>
        public AnaModel BuildAnaModel()
        {
            return new AnaModel(
                _inputModel,
                Nodes,
                Beams,
                [],                     // DummyBeams: なし
                [],                     // RigidBodies: なし
                VerticalPileSprings,    // HorizontalSoilSprings → 鉛直杭ばねとして使用
                []                      // RotationalSprings: なし
            );
        }

        // ── 節点解決ロジック（AnalysisModelling.csから移植） ──

        private Node? ResolveFemNode(NodeReferenceType type, Guid id)
        {
            if (id == Guid.Empty) return null;

            // 方法1a: 杭参照→辞書から直接引く
            if (type == NodeReferenceType.PileLayout &&
                _pileGuidToFemName != null && _pileGuidToFemName.TryGetValue(id, out var dictName))
            {
                var node = Nodes.FirstOrDefault(n => n.Name == dictName);
                if (node != null) return node;
            }

            // 方法1b: Type+GuidからFEM節点名を生成して検索
            string femName = GetFemNodeName(type, id);
            if (femName != null)
            {
                var node = Nodes.FirstOrDefault(n => n.Name == femName);
                if (node != null) return node;
            }

            // 方法2: 座標ベースフォールバック
            var coords = _inputModel.GetNodeCoordinates(type, id);
            if (coords.HasValue)
                return FindClosestNode(coords.Value.X, coords.Value.Y, coords.Value.Z);

            return null;
        }

        private string? GetFemNodeName(NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.FoundationNode:
                    var fnode = _inputModel.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.Id == id);
                    return fnode != null ? $"FoundationNode-{fnode.No}" : null;
                case NodeReferenceType.PileLayout:
                    var pile = _inputModel.PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    return pile != null ? $"FoundationNode-P{pile.No}" : null;
                case NodeReferenceType.GeneralNode:
                    var gnode = _inputModel.InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return gnode != null ? $"InputNode-{gnode.No}" : null;
                default:
                    return null;
            }
        }

        private Node? FindClosestNode(double x, double y, double z)
        {
            Node closest = null;
            double minDist = double.MaxValue;
            const double tolerance = 0.01;

            foreach (var node in Nodes)
            {
                if (!node.Name.StartsWith("FoundationNode-") && !node.Name.StartsWith("InputNode-"))
                    continue;

                double dx = node.Coord.X - x;
                double dy = node.Coord.Y - y;
                double dz = node.Coord.Z - z;
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist < minDist)
                {
                    minDist = dist;
                    closest = node;
                }
            }

            return minDist < tolerance ? closest : null;
        }

        // ── 基礎梁断面作成（AnalysisModelling.csから移植） ──

        private Section CreateFoundationBeamSection(FoundationBeam fbBeam)
        {
            double youngsModulus;
            double poissonRatio;

            var beamMaterial = fbBeam.MaterialNo >= 1
                ? _inputModel.FoundationBeamInput?.Materials?.ElementAtOrDefault(fbBeam.MaterialNo - 1)
                : null;
            if (beamMaterial != null)
            {
                youngsModulus = beamMaterial.YoungModulus;
                poissonRatio = beamMaterial.PoissonRatio;
            }
            else
            {
                youngsModulus = fbBeam.YoungModulus;
                poissonRatio = 0.2;
            }

            double area, shearAreaY, shearAreaZ, torsionalMoment, iy, iz;

            var beamSection = fbBeam.SectionNo >= 1
                ? _inputModel.FoundationBeamInput?.Sections?.ElementAtOrDefault(fbBeam.SectionNo - 1)
                : null;
            if (beamSection != null)
            {
                area = beamSection.Area;
                shearAreaY = beamSection.ShearAreaY;
                shearAreaZ = beamSection.ShearAreaZ;
                torsionalMoment = beamSection.TorsionalMoment;
                iy = beamSection.MomentOfInertiaYY;
                iz = beamSection.MomentOfInertiaZZ;
            }
            else
            {
                double b = fbBeam.Width;
                double h = fbBeam.Height;
                area = b * h;
                shearAreaY = (5.0 / 6.0) * b * h;
                shearAreaZ = (5.0 / 6.0) * b * h;
                iy = b * h * h * h / 12.0;
                iz = h * b * b * b / 12.0;
                double a = Math.Max(b, h);
                double c = Math.Min(b, h);
                torsionalMoment = a * c * c * c * (1.0 / 3.0 - 0.21 * c / a * (1 - c * c * c * c / (12 * a * a * a * a)));
            }

            var material = _materialCache.GetOrAdd((youngsModulus, poissonRatio),
                k => new Material(k.E, k.Nu));
            var sectionKey = (
                Math.Round(area, 5),
                Math.Round(torsionalMoment, 5),
                Math.Round(iy, 5),
                Math.Round(iz, 5),
                Math.Round(youngsModulus, 0),
                Math.Round(shearAreaY, 5),
                Math.Round(shearAreaZ, 5)
            );
            var section = _sectionCache.GetOrAdd(sectionKey, _ => new Section(material, area, shearAreaY, shearAreaZ, torsionalMoment, iy, iz));

            return section;
        }
    }
}
