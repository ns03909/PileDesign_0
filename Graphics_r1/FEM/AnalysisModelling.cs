using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        public List<HorizontalSoilSpring> PenaltySprings { get; set; } = [];       // ConnectionNode↔CapNodeペナルティばね（剛床モード用）

        // 最適化用: Material/Section キャッシュ
        private readonly ConcurrentDictionary<double, Material> _materialCache = new();
        private readonly ConcurrentDictionary<(double, double, double, double, double), Section> _sectionCache = new();

        // 最適化用: 共通Boundaryオブジェクト（毎回newしない）
        private static readonly Boundary SoilNodeBoundary = new(false, false, true, true, true, true);
        private static readonly Boundary PileTipBoundary = new(false, false, true, false, false, false);

        // コンストラクタ
        public AnalysisModelling(InputModel inputModel)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
            Initialize();
        }

        /// <summary>
        /// FoundationBeamInput が有効（非null かつ梁要素あり）かどうか
        /// </summary>
        private bool HasFoundationBeams =>
            InputModel.FoundationBeamInput != null &&
            InputModel.FoundationBeamInput.Beams != null &&
            InputModel.FoundationBeamInput.Beams.Count > 0;

        // Guid→FEM節点名のルックアップ（AddFoundationBeamNodesで構築）
        private Dictionary<Guid, string> _pileGuidToFemName;

        private void Initialize()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PreallocateCollections();
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] PreallocateCollections: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddActionPointNode();
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] AddActionPointNode: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddInputNodes();               // InputNode（General型）を追加
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] AddInputNodes: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddDoatsuGoryokuBane();
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] AddDoatsuGoryokuBane: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddPileOptimized();            // ← pile.No を 1-based に振り直す
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] AddPileOptimized: {sw.ElapsedMilliseconds}ms (Piles={InputModel.PileLayoutItems?.Count ?? 0})");

            sw.Restart();
            AddFoundationBeamNodes();      // 基礎梁節点を追加（pile.No 確定後）
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] AddFoundationBeamNodes: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddFoundationBeams();          // 基礎梁要素を追加
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] AddFoundationBeams: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            ConnectCapsToFoundation();     // CapNode と基礎梁節点を接続
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] ConnectCapsToFoundation: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            ValidateFemModel();            // FEMモデルの安定性チェック
            // System.Diagnostics.Debug.WriteLine($"[AnalysisModelling] ValidateFemModel: {sw.ElapsedMilliseconds}ms");
        }

        // 最適化: リストの事前割り当て
        private void PreallocateCollections()
        {
            if (InputModel.PileLayoutItems == null) return;

            int pileCount = InputModel.PileLayoutItems.Count;
            int avgNodesPerPile = 0;

            // 各杭の節点数を計算
            foreach (var pile in InputModel.PileLayoutItems)
            {
                int soilPileAltNo = pile.SoilPileAltNo;
                if (InputModel.ElementDivision.SoilPiles != null &&
                    soilPileAltNo - 1 >= 0 &&
                    soilPileAltNo - 1 < InputModel.ElementDivision.SoilPiles.Count)
                {
                    avgNodesPerPile = Math.Max(avgNodesPerPile, InputModel.ElementDivision.SoilPiles[soilPileAltNo - 1].ZDataItems.Count);
                }
            }

            // 杭ごと: capNode(1) + pileNodes(N) + soilNodes(N) = 1 + 2*N
            // 合計: pileCount * (1 + 2*avgNodesPerPile) + ActionPoint(1) + DGB nodes
            int estimatedNodes = pileCount * (1 + 2 * avgNodesPerPile) + 10;
            int estimatedBeams = pileCount * (avgNodesPerPile - 1) + 10;
            int estimatedSprings = pileCount * avgNodesPerPile + 10;

            Nodes = new List<Node>(estimatedNodes);
            Beams = new List<Beam>(estimatedBeams);
            HorizontalSoilSprings = new List<HorizontalSoilSpring>(estimatedSprings);
            RotationalSprings = new List<RotationalSpring>(pileCount + 5);
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
            RigidBodies.Add(new(actionNode, [true, true, true, true, true, true])); 
        }

        // 杭配置により作用点の拘束を返すメソッド
        private Boundary GetActionPointBoundary()
        {
            if (InputModel.PileLayoutItems == null || !InputModel.PileLayoutItems.Any())
                throw new InvalidOperationException("杭配置が存在しません。");

            double xMax = InputModel.PileLayoutItems.Max(p => p.X);
            double xMin = InputModel.PileLayoutItems.Min(p => p.X);
            double yMax = InputModel.PileLayoutItems.Max(p => p.Y);
            double yMin = InputModel.PileLayoutItems.Min(p => p.Y);

            // 杭配置がX、Y方向いずれかにスタンスがない場合、回転拘束をする
            if (xMax - xMin < 1e-6 || yMax - yMin < 1e-6)
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
                embedmentNode.SetNodeInfo("根入部節点", x, y, z);
                var soilNode = new Node();
                soilNode.SetNodeInfo("根入部地盤節点", x, y, z);
                soilNode.SetIsForcedDisped(true);
                soilNode.SetBoundary(new(false, false, true, true, true, true));
                var spring = new HorizontalSoilSpring("土圧合力ばね", embedmentNode, soilNode);
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
                // i > 0 の場合は前回の反復で prevSpring が設定されているため null ではない
                item.SetTopNodesAndSpring(prevEmbedmentNode!, prevSoilNode!, prevSpring!);

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

        // 杭要素の追加（接続ノードを明示引数に）- キャッシュ対応版
        private Beam CreatePileElement(SoilPile soilPile, int segIndex, Node upperNode, Node lowerNode)
        {
            // PileBodySegments の範囲チェック
            if (segIndex < 0 || segIndex >= soilPile.PileBodySegments.Count)
                throw new InvalidOperationException(
                    $"杭要素作成エラー: segIndex={segIndex} が PileBodySegments.Count={soilPile.PileBodySegments.Count} の範囲外です。" +
                    $"\n上端: {upperNode.Name} ({upperNode.Coord.X:F3},{upperNode.Coord.Y:F3},{upperNode.Coord.Z:F3})" +
                    $"\n下端: {lowerNode.Name} ({lowerNode.Coord.X:F3},{lowerNode.Coord.Y:F3},{lowerNode.Coord.Z:F3})");

            var pileSection = soilPile.PileBodySegments[segIndex].PileSection;
            double concreteE = pileSection.ConcreteE;

            // ConcreteE の妥当性チェック
            if (!double.IsFinite(concreteE) || concreteE <= 0)
                throw new InvalidOperationException(
                    $"杭要素作成エラー: ConcreteE={concreteE} が無効です (segIndex={segIndex})。" +
                    $"\n上端: {upperNode.Name}, 下端: {lowerNode.Name}");

            double youngsModulus = concreteE * 1000.0; // kN/m2
            double shearModulus = Utils.GetShearModulus(youngsModulus, 0.2); // kN/m2
            double ea = pileSection.EA;
            double ei = pileSection.EI;
            double gj = pileSection.GJ;

            // 断面値の妥当性チェック
            if (!double.IsFinite(ea) || ea <= 0 || !double.IsFinite(ei) || ei <= 0 || !double.IsFinite(gj) || gj <= 0)
                throw new InvalidOperationException(
                    $"杭要素作成エラー: 断面値が無効です (segIndex={segIndex})。" +
                    $"\nEA={ea}, EI={ei}, GJ={gj}, ConcreteE={concreteE}" +
                    $"\n上端: {upperNode.Name}, 下端: {lowerNode.Name}");

            double area = ea / youngsModulus; // m2
            double inertia = ei / youngsModulus; // m4
            double torsionalInertia = gj / shearModulus; // m4

            // ゼロ長さビームのチェック
            double beamLength = Utils.GetLengthBetweenTwoNodes(upperNode, lowerNode);
            if (beamLength < 1e-10)
                throw new InvalidOperationException(
                    $"杭要素作成エラー: ビーム長さがゼロです (L={beamLength:E3}, segIndex={segIndex})。" +
                    $"\n上端: {upperNode.Name} ({upperNode.Coord.X:F3},{upperNode.Coord.Y:F3},{upperNode.Coord.Z:F3})" +
                    $"\n下端: {lowerNode.Name} ({lowerNode.Coord.X:F3},{lowerNode.Coord.Y:F3},{lowerNode.Coord.Z:F3})");

            // Material キャッシュ
            var material = _materialCache.GetOrAdd(youngsModulus, y => new Material(y, 0.2));

            // Section キャッシュ（丸め誤差を考慮して5桁で丸める）
            var sectionKey = (
                Math.Round(area, 5),
                Math.Round(torsionalInertia, 5),
                Math.Round(inertia, 5),
                Math.Round(youngsModulus, 0),
                Math.Round(shearModulus, 0)
            );
            var section = _sectionCache.GetOrAdd(sectionKey, _ => new Section(material, area, area, area, torsionalInertia, inertia, inertia));

            var beam = new Beam("beam", section, upperNode, lowerNode, 1.0, 1.0)
            {
                HorizontalSoilReactionItem = soilPile.HorizontalSoilReactions[segIndex]
            };

            return beam;
        }

        // 杭の追加（並列処理対応）
        private void AddPileOptimized()
        {
            if (InputModel.PileLayoutItems == null) return;

            // 重要: 並列処理を始める前に PileLayoutItems の No（および PileNo）をリスト番号で上書きする
            // これにより ProcessSinglePile 内で使用される pile.No が 0 で固定される問題を解消します。
            for (int i = 0; i < InputModel.PileLayoutItems.Count; i++)
            {
                var item = InputModel.PileLayoutItems[i];
                item.No = i + 1;       // Node.No（表示／識別に使われる）
                item.PileNo = i + 1;   // PileLayoutDataItem 側の PileNo（必要なら同期）
            }
            var pileList = InputModel.PileLayoutItems.ToList();
            int pileCount = pileList.Count;

            // 並列処理用: 各杭の処理結果を格納
            var pileResults = new PileProcessingResult[pileCount];

            // 逐次処理（SoilPile共有オブジェクトへの並行アクセスによるNaN問題を回避）
            for (int i = 0; i < pileCount; i++)
            {
                pileResults[i] = ProcessSinglePile(pileList[i]);
            }

            // メインスレッドで結果をマージ
            MergePileResults(pileResults);

            // RigidBody の関係設定
            foreach (var rb in RigidBodies)
            {
                rb.SetSlaveNodeRelations();
            }
        }

        // 単一杭の処理結果を格納する構造体
        private class PileProcessingResult
        {
            public PileLayoutDataItem? Pile { get; set; }
            public Node? CapNode { get; set; }
            public List<Node> PileNodes { get; } = [];
            public List<Node> SoilNodes { get; } = [];
            public List<Beam> Beams { get; } = [];
            public List<HorizontalSoilSpring> HorizontalSoilSprings { get; } = [];
            public RotationalSpring? RotationalSpring { get; set; }
        }

        // 単一杭を処理（スレッドセーフ）
        private PileProcessingResult ProcessSinglePile(PileLayoutDataItem pile)
        {
            var result = new PileProcessingResult { Pile = pile };

            double x = pile.X;
            double y = pile.Y;
            int soilPileAltNo = pile.SoilPileAltNo;
            double initialRotK = 1e10;  // 杭断面 4EI/L ≈ 1e8 に対して十分大きい剛体相当値

            if (InputModel.ElementDivision.SoilPiles == null ||
                soilPileAltNo - 1 < 0 ||
                soilPileAltNo - 1 >= InputModel.ElementDivision.SoilPiles.Count)
            {
                throw new InvalidOperationException("対応するSoilPileが存在しません。");
            }

            SoilPile soilPile = InputModel.ElementDivision.SoilPiles[soilPileAltNo - 1];

            // Cap Node
            double z0 = soilPile.ZDataItems[0].Z;
            var capNode = new Node();
            capNode.SetNodeInfo($"CapNode-{pile.No}", x, y, z0);
            // CapNodeはマスター節点として機能するため、自由度を解放しておく
            capNode.SetBoundary(new Boundary(false, false, false, false, false, false));
            result.CapNode = capNode;

            Node? prevPileNode = null;
            int nodeCount = soilPile.ZDataItems.Count;

            // 場所打ち鋼管コンクリート杭の杭頭部M-φ適用範囲（杭頭から0.5D）
            double pileTopZoneBottom = double.NegativeInfinity;
            if (soilPile.PileBodySegments.Count > 0)
            {
                var firstSection = soilPile.PileBodySegments[0].PileSection;
                if (firstSection?.PileBodyType == "場所打ち鋼管コンクリート杭"
                    && firstSection.PileSectionType == "鋼管コンクリート部")
                {
                    double pileDia_m = firstSection.PileDiameter / 1000.0;
                    pileTopZoneBottom = z0 - 0.5 * pileDia_m;
                }
            }

            for (int i = 0; i < nodeCount; i++)
            {
                double z = soilPile.ZDataItems[i].Z;

                // 前の節点とZ座標が同一の場合はスキップ（ゼロ長要素防止）
                if (i > 0 && Math.Abs(z - prevPileNode!.Coord.Z) < 1e-10)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AnalysisModelling] WARNING: 重複Z座標スキップ: Pile-{pile.No} i={i} Z={z:F6}");
                    continue;
                }

                // Pile Node
                var pileNode = new Node();
                pileNode.SetNodeInfo($"杭節点-{pile.No}-{i}", x, y, z);
                result.PileNodes.Add(pileNode);

                if (prevPileNode == null)
                {
                    // 杭頭回転ばね
                    var rxy = new RotationalSpring($"RθXY-{pile.No}", capNode, pileNode, initialRotK)
                    {
                        PileBodyNo = pile.PileBodyNo,
                        TieUx = true,
                        TieUy = true,
                        TieUz = false,  // Uz は master-slave チェーン（PileNode→CapNode→AP）で厳密拘束
                        TieRz = true,
                        Kbig = 1e8  // CapNode-PileNode間のペナルティ剛性（Ux/Uy用、Uzはmaster-slaveで不要）
                    };
                    result.RotationalSpring = rxy;
                    prevPileNode = pileNode;
                }
                else if (i != nodeCount - 1)
                {
                    // 杭中間
                    var beam = CreatePileElement(soilPile, i - 1, prevPileNode!, pileNode);
                    beam.PileBodyNo = soilPile.PileBodyNo;
                    beam.SegmentIndex = i - 1;
                    // 要素下端が0.5D境界以上であれば杭頭部とする
                    if (z >= pileTopZoneBottom - NumericalConstants.COORDINATE_TOLERANCE)
                        beam.SetPileTopFlag(true);
                    // 各杭の最上段要素（SegmentIndex == 0）
                    if (beam.SegmentIndex == 0)
                        beam.IsPileHeadElement = true;
                    result.Beams.Add(beam);
                    prevPileNode = pileNode;
                }
                else
                {
                    // 先端
                    var beam = CreatePileElement(soilPile, i - 1, prevPileNode!, pileNode);
                    beam.PileBodyNo = soilPile.PileBodyNo;
                    beam.SegmentIndex = i - 1;
                    if (z >= pileTopZoneBottom - NumericalConstants.COORDINATE_TOLERANCE)
                        beam.SetPileTopFlag(true);
                    // 2 要素構成の杭では先端要素が同時に最上段
                    if (beam.SegmentIndex == 0)
                        beam.IsPileHeadElement = true;
                    result.Beams.Add(beam);
                    pileNode.SetBoundary(PileTipBoundary);
                }

                // Soil Node
                var soilNode = new Node();
                soilNode.SetNodeInfo($"杭地盤節点-{pile.No}-{i}", x, y, z);
                soilNode.SetIsForcedDisped(true);
                soilNode.SetBoundary(SoilNodeBoundary);
                result.SoilNodes.Add(soilNode);

                // 水平土ばね
                var hspring = new HorizontalSoilSpring($"杭地盤ばね-{pile.No}-{i}", pileNode, soilNode);
                result.HorizontalSoilSprings.Add(hspring);
            }

            return result;
        }

        // 処理結果をメインコレクションにマージ
        private void MergePileResults(PileProcessingResult[] results)
        {
            //var mode = InputModel.FoundationBeamInput?.ConnectionMode ?? FoundationBeamConnectionMode.RigidBody;

            foreach (var result in results)
            {
                var pile = result.Pile;
                if (pile == null) return;

                // クリア
                pile.PileNodes.Clear();
                pile.SoilNodes.Clear();
                pile.Beams.Clear();
                pile.HorizontalSoilSprings.Clear();

                // Cap Node
                if (result.CapNode != null)
                {
                    Nodes.Add(result.CapNode);

                    if (!HasFoundationBeams)
                    {
                        // 基礎梁未設定（または梁要素なし）: 従来通りCapNode → RigidBodies[0] 直接スレーブ
                        RigidBodies[0].AddSlaveNode(result.CapNode);
                    }
                    // FoundationBeamInput設定時（両モード共通）:
                    // ConnectCapsToFoundation() で ConnectionNode をスレーブにし、
                    // CapNode は RigidLink ビーム経由で接続する
                }

                // Pile Nodes
                foreach (var pileNode in result.PileNodes)
                {
                    Nodes.Add(pileNode);
                    pile.PileNodes.Add(pileNode);
                }

                // Soil Nodes
                foreach (var soilNode in result.SoilNodes)
                {
                    Nodes.Add(soilNode);
                    pile.SoilNodes.Add(soilNode);
                }

                // Beams
                foreach (var beam in result.Beams)
                {
                    Beams.Add(beam);
                    pile.Beams.Add(beam);
                }

                // Rotational Spring
                if (result.RotationalSpring != null)
                {
                    RotationalSprings.Add(result.RotationalSpring);
                    pile.PileTopRotationalSpring = result.RotationalSpring;
                }

                // Horizontal Soil Springs
                foreach (var hspring in result.HorizontalSoilSprings)
                {
                    HorizontalSoilSprings.Add(hspring);
                    pile.HorizontalSoilSprings.Add(hspring);
                }
            }
        }

        // InputNode（General型）の追加
        private void AddInputNodes()
        {
            if (InputModel.InputNodes == null)
                return;

            foreach (var inputNode in InputModel.InputNodes)
            {
                // General型のみFEM.Nodeとして追加（Pile型は杭生成で処理済み）
                if (inputNode.Type == NodeType.General && inputNode.IsVisible)
                {
                    var node = new Node();
                    node.SetNodeInfo($"InputNode-{inputNode.No}", inputNode.X, inputNode.Y, inputNode.Z);
                    // 一般節点は自由（境界条件なし）
                    node.SetBoundary(new Boundary(false, false, false, false, false, false));
                    Nodes.Add(node);
                }
            }
        }

        // 基礎梁節点の追加（AddPileOptimized後に呼ぶこと：pile.Noが確定済み）
        // 両モード（RigidBody/RigidFloor）でConnectionNode（FoundationNode-P{No}）を作成する
        // ConnectionNodeがRigidBodyのslaveとなるため、両モードで必要
        private void AddFoundationBeamNodes()
        {
            _pileGuidToFemName = [];

            if (!HasFoundationBeams)
                return;

            var mode = InputModel.FoundationBeamInput.ConnectionMode;

            // 専用FoundationNodeからFEM節点を作成（RigidFloorモードのみ：基礎梁要素の端点として使用）
            if (mode == FoundationBeamConnectionMode.RigidFloor && InputModel.FoundationBeamInput.Nodes != null)
            {
                foreach (var fbNode in InputModel.FoundationBeamInput.Nodes)
                {
                    if (Nodes.Any(n => n.Name == $"FoundationNode-{fbNode.No}"))
                        continue;

                    var node = new Node();
                    node.SetNodeInfo($"FoundationNode-{fbNode.No}", fbNode.X, fbNode.Y, fbNode.Z);
                    node.SetBoundary(new Boundary(false, false, false, false, false, false));
                    Nodes.Add(node);
                }
            }

            // 全杭のConnectionNode位置にFEM節点を作成 + Guid→FEM名辞書を構築
            // 両モードで作成（ConnectionNodeがRigidBodyのslaveとなる）
            if (InputModel.PileLayoutItems != null)
            {
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    string name = $"FoundationNode-P{pile.No}";

                    // Guid→FEM名の辞書に登録（ResolveFemNodeで使用）
                    if (pile.UniqueId != Guid.Empty)
                        _pileGuidToFemName[pile.UniqueId] = name;

                    if (Nodes.Any(n => n.Name == name))
                        continue;

                    var node = new Node();
                    node.SetNodeInfo(name, pile.X, pile.Y, pile.Z + pile.FoundationBeamDeltaZc);
                    node.SetBoundary(new Boundary(false, false, false, false, false, false));
                    Nodes.Add(node);
                }
            }
        }

        // Type+GuidからFEM節点名を生成
        private string? GetFemNodeName(NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.FoundationNode:
                    var fnode = InputModel.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.Id == id);
                    return fnode != null ? $"FoundationNode-{fnode.No}" : null;
                case NodeReferenceType.PileLayout:
                    var pile = InputModel.PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    return pile != null ? $"FoundationNode-P{pile.No}" : null;
                case NodeReferenceType.GeneralNode:
                    var gnode = InputModel.InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return gnode != null ? $"InputNode-{gnode.No}" : null;
                default:
                    return null;
            }
        }

        // 基礎梁要素の追加
        private void AddFoundationBeams()
        {
            if (!HasFoundationBeams)
                return;

            //var mode = InputModel.FoundationBeamInput.ConnectionMode;

            // 梁要素が存在しない場合はスキップ
            if (InputModel.FoundationBeamInput.Beams == null || InputModel.FoundationBeamInput.Beams.Count == 0)
                return;

            foreach (var fbBeam in InputModel.FoundationBeamInput.Beams)
            {
                // Type+Guid方式で節点を検索（旧方式はフォールバック）
                var nodeI = ResolveFemNode(fbBeam.NodeI_Type, fbBeam.NodeI_Id, fbBeam.NodeI_No);
                var nodeJ = ResolveFemNode(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id, fbBeam.NodeJ_No);

                if (nodeI == null || nodeJ == null)
                {
                    string detail = $"基礎梁要素{fbBeam.No}の節点が見つかりません。\n" +
                        $"I端: Type={fbBeam.NodeI_Type}, Id={fbBeam.NodeI_Id}, No={fbBeam.NodeI_No} → {(nodeI != null ? nodeI.Name : "未解決")}\n" +
                        $"J端: Type={fbBeam.NodeJ_Type}, Id={fbBeam.NodeJ_Id}, No={fbBeam.NodeJ_No} → {(nodeJ != null ? nodeJ.Name : "未解決")}";
                    throw new InvalidOperationException(detail);
                }

                // Section を作成
                var section = CreateFoundationBeamSection(fbBeam);

                // Beam 要素を作成
                var beam = new Beam($"FoundationBeam-{fbBeam.No}", section, nodeI, nodeJ, 1.0, 1.0);
                Beams.Add(beam);
            }
        }

        // Type+Guid方式でFEM節点を検索（辞書＋座標ベースフォールバック付き）
        private Node? ResolveFemNode(NodeReferenceType type, Guid id, int fallbackNo)
        {
            // 方法1a: 杭参照 → 構築済み辞書から直接引く（Guid不一致に強い）
            if (type == NodeReferenceType.PileLayout && id != Guid.Empty &&
                _pileGuidToFemName != null && _pileGuidToFemName.TryGetValue(id, out var dictName))
            {
                var node = Nodes.FirstOrDefault(n => n.Name == dictName);
                if (node != null) return node;
            }

            // 方法1b: Type+Guid から FEM節点名を生成して検索
            if (id != Guid.Empty)
            {
                string femName = GetFemNodeName(type, id);
                if (femName != null)
                {
                    var node = Nodes.FirstOrDefault(n => n.Name == femName);
                    if (node != null) return node;
                }
            }

            // 方法2: fallbackNo を使って検索
            if (fallbackNo > 0)
            {
                if (type == NodeReferenceType.PileLayout)
                {
                    var node = Nodes.FirstOrDefault(n => n.Name == $"FoundationNode-P{fallbackNo}");
                    if (node != null) return node;
                }
                else if (type == NodeReferenceType.GeneralNode)
                {
                    var node = Nodes.FirstOrDefault(n => n.Name == $"InputNode-{fallbackNo}");
                    if (node != null) return node;
                }

                var fbNode = Nodes.FirstOrDefault(n => n.Name == $"FoundationNode-{fallbackNo}");
                if (fbNode != null) return fbNode;
            }

            // 方法3: 座標ベースのフォールバック（上記すべて失敗した場合）
            if (id != Guid.Empty)
            {
                var coords = InputModel.GetNodeCoordinates(type, id);
                if (coords.HasValue)
                {
                    return FindClosestFoundationNode(coords.Value.X, coords.Value.Y, coords.Value.Z);
                }
            }

            return null;
        }

        // 座標に最も近い FoundationNode-* / InputNode-* ノードを検索
        private Node? FindClosestFoundationNode(double x, double y, double z)
        {
            Node closest = null;
            double minDist = double.MaxValue;
            const double tolerance = 0.01; // 10mm以内

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

        // 梁要素の節点参照からFEM節点名と杭Noを収集するヘルパー
        private void CollectBeamRefInfo(NodeReferenceType type, Guid id, int fallbackNo,
            HashSet<string> femNodeNames, HashSet<int> pileNos)
        {
            // ResolveFemNodeと同じロジックで実際のFEM節点を探す
            var femNode = ResolveFemNode(type, id, fallbackNo);
            if (femNode != null)
                femNodeNames.Add(femNode.Name);

            // 杭参照の杭Noを収集（FEM節点名から抽出）
            if (type == NodeReferenceType.PileLayout && femNode != null &&
                femNode.Name.StartsWith("FoundationNode-P"))
            {
                if (int.TryParse(femNode.Name["FoundationNode-P".Length..], out int pileNo))
                    pileNos.Add(pileNo);
            }
        }

        // 基礎梁断面の作成（BeamSection/BeamMaterialから断面諸元を取得）
        private Section CreateFoundationBeamSection(FoundationBeamElement fbBeam)
        {
            double youngsModulus;
            double poissonRatio;

            // BeamMaterialからヤング係数・ポアソン比を取得
            var beamMaterial = InputModel.FoundationBeamInput?.Materials?.FirstOrDefault(m => m.No == fbBeam.MaterialNo);
            if (beamMaterial != null)
            {
                youngsModulus = beamMaterial.YoungModulus;
                poissonRatio = beamMaterial.PoissonRatio;
            }
            else
            {
                // フォールバック: FoundationBeamElementの値を使用
                youngsModulus = fbBeam.YoungModulus;
                poissonRatio = 0.2;
            }

            double area, shearAreaY, shearAreaZ, torsionalMoment, iy, iz;

            // BeamSectionから断面諸元を取得（ウィザードの増減係数が反映済み）
            var beamSection = InputModel.FoundationBeamInput?.Sections?.FirstOrDefault(s => s.No == fbBeam.SectionNo);
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
                // フォールバック: Width/Heightから矩形断面の標準公式で計算
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

            // Material キャッシュ
            var material = _materialCache.GetOrAdd(youngsModulus, y => new Material(y, poissonRatio));

            // Section キャッシュ
            var sectionKey = (
                Math.Round(area, 5),
                Math.Round(torsionalMoment, 5),
                Math.Round(iy, 5),
                Math.Round(iz, 5),
                Math.Round(youngsModulus, 0)
            );
            var section = _sectionCache.GetOrAdd(sectionKey, _ => new Section(material, area, shearAreaY, shearAreaZ, torsionalMoment, iy, iz));

            return section;
        }

        // ConnectionNode（FoundationNode-P{No}）をRigidBodyのslaveとし、
        // CapNode を RigidLink ビームで ConnectionNode に接続する
        // 両モード（RigidBody/RigidFloor）で動作する
        private void ConnectCapsToFoundation()
        {
            if (!HasFoundationBeams)
            {
                // 基礎梁未設定: PileNode-0 → CapNode のmaster-slave設定のみ行う
                // （CapNodeはMergePileResultsでRigidBodies[0]のslaveに追加済み）
                if (InputModel.PileLayoutItems != null)
                {
                    foreach (var pile in InputModel.PileLayoutItems)
                    {
                        var capNode = Nodes.FirstOrDefault(n => n.Name == $"CapNode-{pile.No}");
                        if (capNode != null)
                            SetPileHeadMasterSlave(pile, capNode);
                    }
                    foreach (var rb in RigidBodies)
                        rb.SetSlaveNodeRelations();
                }
                return;
            }

            if (InputModel.PileLayoutItems == null)
                return;

            var mode = InputModel.FoundationBeamInput.ConnectionMode;

            // RigidFloorモードでは基礎梁要素が必要
            if (mode == FoundationBeamConnectionMode.RigidFloor)
            {
                var beamElements = InputModel.FoundationBeamInput.Beams;
                if (beamElements == null || beamElements.Count == 0)
                    throw new InvalidOperationException("剛床連結モードでは基礎梁要素が必要です。");
            }

            // 剛リンク用セクション（非常に高い剛性でCapNode-ConnectionNode間を実質剛結合）
            var rigidLinkMat = _materialCache.GetOrAdd(1e10, y => new Material(y, 0.2));
            var rigidLinkSecKey = (1.0, 0.14, Math.Round(1.0 / 12.0, 5), 1e10, Math.Round(1e10 / 2.4, 0));
            var rigidLinkSec = _sectionCache.GetOrAdd(rigidLinkSecKey,
                _ => new Section(rigidLinkMat, 1.0, 1.0, 1.0, 0.14, 1.0 / 12.0, 1.0 / 12.0));

            if (mode == FoundationBeamConnectionMode.RigidBody)
            {
                // ── 剛体連結: RigidBodies[0]（全6DOF）を使用 ──
                // 全ConnectionNodeを同一剛体のslaveにする → 基礎梁は剛性に寄与しない
                ConnectCapsRigidBody(rigidLinkSec);
            }
            else
            {
                // ── 剛床連結（柔梁モード）: 剛体拘束を使わず基礎梁が全自由度の剛性を負担 ──
                // ConnectionNodeをRigidBodyのslaveにしないため、基礎梁が水平曲げ(Mz)を
                // 含む全方向の力を伝達し、荷重を各杭に分配する。
                ConnectCapsFlexibleBeam(rigidLinkSec);
            }
        }

        /// <summary>
        /// 剛体連結モード: ConnectionNodeをRigidBodies[0]のslaveに追加
        /// CapNodeはConnectionNodeにRigidLinkビームで接続（AP直接接続なし）
        /// PileNode-0はCapNodeにのみslave接続
        /// </summary>
        private void ConnectCapsRigidBody(Section rigidLinkSec)
        {
            var targetRigidBody = RigidBodies[0];

            foreach (var pile in InputModel.PileLayoutItems)
            {
                var capNode = Nodes.FirstOrDefault(n => n.Name == $"CapNode-{pile.No}");
                if (capNode == null) continue;

                var connectionNode = Nodes.FirstOrDefault(n => n.Name == $"FoundationNode-P{pile.No}");
                if (connectionNode == null)
                {
                    // ConnectionNodeがない場合はCapNodeを直接RigidBodyのslaveに
                    targetRigidBody.AddSlaveNode(capNode);
                    SetPileHeadMasterSlave(pile, capNode);
                    continue;
                }

                // ConnectionNode → RigidBody slave（全6DOF）
                targetRigidBody.AddSlaveNode(connectionNode);

                // CapNode は自由節点（RigidBodyのslaveにしない）
                capNode.SetBoundary(new Boundary(false, false, false, false, false, false));

                // ConnectionNode ↔ CapNode: 距離に応じてRigidLinkビームまたはペナルティばね
                double dist = (capNode.Coord - connectionNode.Coord).Length;
                if (dist > 0.001)
                {
                    var rigidBeam = new Beam($"RigidLink-{pile.No}", rigidLinkSec, connectionNode, capNode, 1.0, 1.0);
                    Beams.Add(rigidBeam);
                    pile.Beams.Add(rigidBeam);
                }
                else
                {
                    // 同位置: ペナルティばねで接続
                    const double Kbig = 1e8;
                    var penaltySpring = new HorizontalSoilSpring(
                        $"PenaltySpring-{pile.No}", connectionNode, capNode);
                    penaltySpring.SetKe(Kbig, Kbig, Kbig, Kbig, Kbig, Kbig, true);
                    penaltySpring.SetKe(Kbig, Kbig, Kbig, Kbig, Kbig, Kbig, false);
                    PenaltySprings.Add(penaltySpring);
                }

                // PileNode-0 → CapNode のみslave接続（AP直接接続なし）
                SetPileHeadMasterSlaveFlexible(pile, capNode);
            }

            foreach (var rb in RigidBodies)
                rb.SetSlaveNodeRelations();
        }

        /// <summary>
        /// 剛床連結モード:
        /// ・ActionPoint → ConnectionNode: 剛床仮定（Ux, Uy, Rz をmaster-slave拘束）
        /// ・ConnectionNode → CapNode: 完全剛（ペナルティばね Kbig=1e8 で全6DOF結合）
        /// ・ConnectionNode の自由DOF: Uz, Rx, Ry（基礎梁が剛性を負担）
        /// ・PileNode-0 → CapNode: master-slave（Ux, Uy, Uz, Rz）
        ///
        /// ペナルティばね方式を採用する理由:
        /// ・旧RigidLinkビーム（L=0.01m）は 12EI/L³≈1e16 の曲げ剛性でK行列がill-conditioning
        /// ・master-slaveチェーンは GetEquationNumbers が1レベルしか辿れない
        /// ・ペナルティばね（Kbig=1e8）はK行列に適度な剛性を加算し、チェーン問題を回避
        /// </summary>
        private void ConnectCapsFlexibleBeam(Section rigidLinkSec)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== ConnectCapsFlexibleBeam 診断 v4 (剛床slave + ペナルティばね) ===");

            var actionPoint = Nodes[0]; // ActionPoint
            log.AppendLine($"ActionPoint: ({actionPoint.Coord.X:F3}, {actionPoint.Coord.Y:F3}, {actionPoint.Coord.Z:F3})");

            // 剛床モードではActionPointは面内（Ux, Uy, Rz）のみ荷重を受け持つ。
            // Uz, Rx, Ry は固定。
            var apBnd = actionPoint.Boundary;
            actionPoint.SetBoundary(new Boundary(
                apBnd.Ux,  // Ux: 維持（面内：荷重方向）
                apBnd.Uy,  // Uy: 維持（面内：荷重方向）
                true,      // Uz: 固定（鉛直支持）
                true,      // Rx: 固定
                true,      // Ry: 固定
                apBnd.Rz   // Rz: 維持（面内：剛床回転）
            ));
            log.AppendLine($"ActionPoint boundary updated: Uz=fixed, Rx=fixed, Ry=fixed (rigid floor mode)");

            const double Kbig = 1e8; // ペナルティ剛性 [kN/m or kNm/rad]

            // ── 1. 各ConnectionNodeを ActionPoint の部分slave（Ux, Uy, Rz）に設定 ──
            // ── 2. 各ConnectionNode↔CapNode 間にペナルティばね（全6DOF）を作成 ──
            int slaveCount = 0;
            int penaltyCount = 0;
            foreach (var pile in InputModel.PileLayoutItems)
            {
                var capNode = Nodes.FirstOrDefault(n => n.Name == $"CapNode-{pile.No}");
                if (capNode == null) continue;

                var connectionNode = Nodes.FirstOrDefault(n => n.Name == $"FoundationNode-P{pile.No}");
                if (connectionNode == null) continue;

                // CapNodeを自由節点に設定（PileNode-0のmasterとなるため、slave不可）
                capNode.SetBoundary(new Boundary(false, false, false, false, false, false));

                // ConnectionNode → ActionPoint: 剛床拘束（Ux, Uy, Rz のみslave）
                // Uz, Rx, Ry は自由 → 基礎梁が剛性を負担
                var armToAP = connectionNode.Coord - actionPoint.Coord;
                connectionNode.SetBoundary(new Boundary(
                    true,  // Ux: slave of ActionPoint
                    true,  // Uy: slave of ActionPoint
                    false, // Uz: free（基礎梁 + ペナルティばねが負担）
                    false, // Rx: free（基礎梁 + ペナルティばねが負担）
                    false, // Ry: free（基礎梁 + ペナルティばねが負担）
                    true   // Rz: slave of ActionPoint
                ));
                connectionNode.SetMasterNode(0, actionPoint); // Ux → ActionPoint
                connectionNode.SetMasterNode(1, actionPoint); // Uy → ActionPoint
                connectionNode.SetMasterNode(5, actionPoint); // Rz → ActionPoint
                // arm vector（剛床回転によるUx,Uy変位の寄与を計算するため）
                connectionNode.SetArmVector(0, armToAP);
                connectionNode.SetArmVector(1, armToAP);
                connectionNode.SetArmVector(2, armToAP);
                connectionNode.SetTransferMatrix();
                slaveCount++;

                log.AppendLine($"  {connectionNode.Name} → ActionPoint: 剛床slave(Ux,Uy,Rz) arm=({armToAP.X:F3},{armToAP.Y:F3},{armToAP.Z:F3})");

                // ConnectionNode ↔ CapNode: 距離に応じてRigidLinkビームまたはペナルティばね
                double dist = (capNode.Coord - connectionNode.Coord).Length;
                if (dist > 0.001)
                {
                    var rigidBeam = new Beam($"RigidLink-{pile.No}", rigidLinkSec, connectionNode, capNode, 1.0, 1.0);
                    Beams.Add(rigidBeam);
                    pile.Beams.Add(rigidBeam);
                    log.AppendLine($"  {connectionNode.Name} ↔ {capNode.Name}: RigidLinkビーム L={dist:F3}m");
                }
                else
                {
                    // 同位置: ペナルティばねで接続
                    var penaltySpring = new HorizontalSoilSpring(
                        $"PenaltySpring-{pile.No}", connectionNode, capNode);
                    penaltySpring.SetKe(Kbig, Kbig, Kbig, Kbig, Kbig, Kbig, true);
                    penaltySpring.SetKe(Kbig, Kbig, Kbig, Kbig, Kbig, Kbig, false);
                    PenaltySprings.Add(penaltySpring);
                    penaltyCount++;
                    log.AppendLine($"  {connectionNode.Name} ↔ {capNode.Name}: ペナルティばね Kbig={Kbig:E1}");
                }

                // PileNode-0 → CapNode slave（全6DOF: Ux,Uy,Uz,Rx,Ry,Rz）
                // 剛床モードではAP直接接続せず、CapNode → PenaltySpring → ConnectionNode → AP の経路
                SetPileHeadMasterSlaveFlexible(pile, capNode);
            }
            log.AppendLine($"剛床slave拘束: {slaveCount}組, ペナルティばね: {penaltyCount}組");

            // ── 2b. InputNode（General型）とFoundationNode（中間節点）もActionPointの剛床slaveに設定 ──
            // 剛床仮定ではスラブ上の全節点が面内(Ux,Uy,Rz)で剛体運動する。
            // ConnectionNode(FoundationNode-P{No})は上で処理済み。
            // ここでは残りのスラブ上節点（InputNode-*、FoundationNode-{N}）をslave化する。
            int additionalSlaveCount = 0;
            var alreadySlavedNames = new HashSet<string>();
            foreach (var pile in InputModel.PileLayoutItems)
                alreadySlavedNames.Add($"FoundationNode-P{pile.No}");

            foreach (var node in Nodes)
            {
                // ActionPoint自身、杭節点、CapNode、EmbedmentNode等はスキップ
                if (node == actionPoint) continue;
                if (alreadySlavedNames.Contains(node.Name)) continue; // 既にslave化済み

                // InputNode-* または FoundationNode-{数字} が剛床slave対象
                bool isInputNode = node.Name.StartsWith("InputNode-");
                bool isFoundationNode = node.Name.StartsWith("FoundationNode-") && !node.Name.StartsWith("FoundationNode-P");
                if (!isInputNode && !isFoundationNode) continue;

                // 既にmaster-slave関係がある場合はスキップ（安全ガード）
                if (node.HasMasterAt(Dof.Ux) || node.HasMasterAt(Dof.Uy) || node.HasMasterAt(Dof.Rz))
                    continue;

                // 剛床slave設定: Ux, Uy, Rz → ActionPoint
                var arm = node.Coord - actionPoint.Coord;
                node.SetBoundary(new Boundary(
                    true,  // Ux: slave of ActionPoint
                    true,  // Uy: slave of ActionPoint
                    false, // Uz: free（基礎梁が負担）
                    false, // Rx: free（基礎梁が負担）
                    false, // Ry: free（基礎梁が負担）
                    true   // Rz: slave of ActionPoint
                ));
                node.SetMasterNode(0, actionPoint); // Ux → ActionPoint
                node.SetMasterNode(1, actionPoint); // Uy → ActionPoint
                node.SetMasterNode(5, actionPoint); // Rz → ActionPoint
                node.SetArmVector(0, arm);
                node.SetArmVector(1, arm);
                node.SetArmVector(2, arm);
                node.SetTransferMatrix();
                additionalSlaveCount++;

                log.AppendLine($"  {node.Name} → ActionPoint: 剛床slave(Ux,Uy,Rz) arm=({arm.X:F3},{arm.Y:F3},{arm.Z:F3})");
            }
            log.AppendLine($"追加剛床slave拘束（InputNode/FoundationNode）: {additionalSlaveCount}組");

            // ── 3. ConnectionNodeの基礎梁接続状況を確認 ──
            var connNodeNames = new HashSet<string>();
            foreach (var pile in InputModel.PileLayoutItems)
                connNodeNames.Add($"FoundationNode-P{pile.No}");

            var connectedByBeam = new HashSet<string>();
            foreach (var beam in Beams)
            {
                if (beam.Name.StartsWith("FoundationBeam"))
                {
                    if (connNodeNames.Contains(beam.NodeI.Name)) connectedByBeam.Add(beam.NodeI.Name);
                    if (connNodeNames.Contains(beam.NodeJ.Name)) connectedByBeam.Add(beam.NodeJ.Name);
                }
            }
            var orphanNodes = connNodeNames.Except(connectedByBeam).ToList();
            log.AppendLine($"ConnectionNode総数: {connNodeNames.Count}");
            log.AppendLine($"  基礎梁に接続: {connectedByBeam.Count}");
            log.AppendLine($"  孤立（基礎梁未接続）: {orphanNodes.Count}");
            if (orphanNodes.Count > 0)
                log.AppendLine($"  孤立ノード: {string.Join(", ", orphanNodes.Take(10))}");

            int fbCount = Beams.Count(b => b.Name.StartsWith("FoundationBeam"));
            log.AppendLine($"基礎梁要素数: {fbCount}");
            log.AppendLine($"全Beam要素数: {Beams.Count}");
            log.AppendLine($"全Node数: {Nodes.Count}");
            log.AppendLine($"PenaltySprings数: {PenaltySprings.Count}");

            // ── 4. Master-Slave関係の診断 ──
            log.AppendLine("=== Master-Slave 診断 ===");
            string[] dofNames = { "Ux", "Uy", "Uz", "Rx", "Ry", "Rz" };
            foreach (var node in Nodes)
            {
                if (node.MasterNodes == null) continue;
                var slavedDofs = new List<string>();
                for (int d = 0; d < 6; d++)
                {
                    if (node.MasterNodes[d] != null)
                        slavedDofs.Add($"{dofNames[d]}→{node.MasterNodes[d].Name}");
                }
                if (slavedDofs.Count > 0)
                {
                    log.AppendLine($"  {node.Name}: slave[{string.Join(", ", slavedDofs)}]" +
                        $"  Boundary=({node.Boundary.Ux},{node.Boundary.Uy},{node.Boundary.Uz},{node.Boundary.Rx},{node.Boundary.Ry},{node.Boundary.Rz})");
                }
            }
            log.AppendLine($"RigidBodies[0]: master={RigidBodies[0].MasterNode?.Name}, slaves=[{string.Join(", ", RigidBodies[0].SlaveNodes.Select(n => n.Name))}]");

            log.AppendLine("=== 診断終了 ===");
            // System.Diagnostics.Debug.WriteLine(log.ToString());

            // RigidBodies[0]の関係を再設定（EmbedmentNode等の既存slave用）
            foreach (var rb in RigidBodies)
                rb.SetSlaveNodeRelations();
        }

        /// <summary>
        /// PileNode-0 を ActionPoint の master-slave として設定（Ux, Uy, Uz, Rz）。
        /// CapNode は RigidBody のスレーブで方程式番号が負のため、
        /// 2段階チェーン（PileNode-0→CapNode→ActionPoint）は変位更新で辿れない。
        /// PileNode-0 → ActionPoint を直接設定することで回避する。
        /// PileNode-0 と CapNode は同位置なので arm vector は CapNode と同一。
        /// Rx, Ry は RotationalSpring（M-θ曲線 or 高回転剛性）で制御するため自由のまま。
        /// </summary>
        private void SetPileHeadMasterSlave(PileLayoutDataItem pile, Node capNode)
        {
            var pileHeadNode = Nodes.FirstOrDefault(n => n.Name == $"杭節点-{pile.No}-0");
            if (pileHeadNode == null) return;

            var actionPoint = RigidBodies[0].MasterNode;

            // Uz を CapNode の slave にする。ResolvedDofMap のチェーン解決により
            // PileNode.Uz → CapNode.Uz → AP.Uz + AP.Rx×ΔY - AP.Ry×ΔX と解決される。
            // Rx, Ry は free（M-θ 用）。
            pileHeadNode.SetBoundary(new Boundary(true, true, true, false, false, true));
            pileHeadNode.SetMasterNode(0, actionPoint); // Ux → AP
            pileHeadNode.SetMasterNode(1, actionPoint); // Uy → AP
            pileHeadNode.SetMasterNode(2, capNode);     // Uz → CapNode（チェーン→AP）
            pileHeadNode.SetMasterNode(5, actionPoint); // Rz → AP
            // Rx, Ry は free（M-θ 用）

            var armToAP = pileHeadNode.Coord - actionPoint.Coord;
            pileHeadNode.SetArmVector(0, armToAP);
            pileHeadNode.SetArmVector(1, armToAP);
            // Uz の arm は CapNode→AP 間の距離（PileNode と CapNode は co-located なので同一）
            var armCapToAP = capNode.Coord - actionPoint.Coord;
            pileHeadNode.SetArmVector(2, armCapToAP);
            pileHeadNode.SetTransferMatrix();
        }

        /// <summary>
        /// 剛床モード用: PileNode-0 → CapNode のみ接続（AP直接接続なし）
        /// 力の伝達経路: PileNode-0 → CapNode → PenaltySpring → ConnectionNode → AP
        /// CapNode は自由節点（RigidBodyのslaveではない）なのでmaster-slave設定が有効。
        /// Rx, Ry は free（RotationalSpringのM-θ曲線で制御）。
        /// </summary>
        private void SetPileHeadMasterSlaveFlexible(PileLayoutDataItem pile, Node capNode)
        {
            var pileHeadNode = Nodes.FirstOrDefault(n => n.Name == $"杭節点-{pile.No}-0");
            if (pileHeadNode == null) return;

            // PileNode-0 → CapNode: Ux, Uy, Uz, Rz をslave
            // Rx, Ry は free（M-θ 用）
            pileHeadNode.SetBoundary(new Boundary(true, true, true, false, false, true));
            pileHeadNode.SetMasterNode(0, capNode); // Ux → CapNode
            pileHeadNode.SetMasterNode(1, capNode); // Uy → CapNode
            pileHeadNode.SetMasterNode(2, capNode); // Uz → CapNode
            pileHeadNode.SetMasterNode(5, capNode); // Rz → CapNode

            // PileNode と CapNode は同位置なので arm ≈ (0,0,0)
            var arm = pileHeadNode.Coord - capNode.Coord;
            pileHeadNode.SetArmVector(0, arm);
            pileHeadNode.SetArmVector(1, arm);
            pileHeadNode.SetArmVector(2, arm);
            pileHeadNode.SetTransferMatrix();
        }

        // ======== FEMモデル安定性バリデーション ========

        /// <summary>
        /// FEMモデル構築後のバリデーション。
        /// 一般的なFEM解析プログラムと同様に、解析前にモデルの安定性をチェックする。
        /// </summary>
        private void ValidateFemModel()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            // 1. ゼロ長要素の検出
            foreach (var beam in Beams)
            {
                if (beam.NodeI == null || beam.NodeJ == null)
                {
                    errors.Add($"要素 '{beam.Name}': 節点がnullです。");
                    continue;
                }
                double len = beam.Length;
                if (len < 1e-10)
                {
                    errors.Add($"要素 '{beam.Name}': 要素長がゼロです " +
                        $"(NodeI={beam.NodeI.Name} [{beam.NodeI.Coord.X:F3},{beam.NodeI.Coord.Y:F3},{beam.NodeI.Coord.Z:F3}], " +
                        $"NodeJ={beam.NodeJ.Name} [{beam.NodeJ.Coord.X:F3},{beam.NodeJ.Coord.Y:F3},{beam.NodeJ.Coord.Z:F3}])。");
                }
            }

            // 2. 孤立節点の検出（どの要素にも接続されていない自由節点）
            var connectedNodes = new HashSet<Node>();
            foreach (var beam in Beams)
            {
                if (beam.NodeI != null) connectedNodes.Add(beam.NodeI);
                if (beam.NodeJ != null) connectedNodes.Add(beam.NodeJ);
            }
            foreach (var spring in HorizontalSoilSprings)
            {
                if (spring.NodeI != null) connectedNodes.Add(spring.NodeI);
                if (spring.NodeJ != null) connectedNodes.Add(spring.NodeJ);
            }
            foreach (var ps in PenaltySprings)
            {
                if (ps.NodeI != null) connectedNodes.Add(ps.NodeI);
                if (ps.NodeJ != null) connectedNodes.Add(ps.NodeJ);
            }
            foreach (var rs in RotationalSprings)
            {
                if (rs.NodeI != null) connectedNodes.Add(rs.NodeI);
                if (rs.NodeJ != null) connectedNodes.Add(rs.NodeJ);
            }

            foreach (var node in Nodes)
            {
                if (connectedNodes.Contains(node)) continue;
                // RigidBodyのmaster/slave、または全DOF固定の節点は除外
                bool isRigidBodyMember = RigidBodies.Any(rb =>
                    rb.MasterNode == node || (rb.SlaveNodes?.Contains(node) ?? false));
                bool allFixed = node.Boundary.Ux && node.Boundary.Uy && node.Boundary.Uz &&
                                node.Boundary.Rx && node.Boundary.Ry && node.Boundary.Rz;
                bool isSlave = node.MasterNodes.Any(m => m != null);
                if (!isRigidBodyMember && !allFixed && !isSlave)
                {
                    warnings.Add($"節点 '{node.Name}' [{node.Coord.X:F3},{node.Coord.Y:F3},{node.Coord.Z:F3}]: " +
                        $"どの要素/ばね/剛体にも接続されていません（孤立節点）。");
                }
            }

            // 3. 重複要素の検出（同じNodeI-NodeJ組み合わせ）
            var elementPairs = new HashSet<(string, string)>();
            foreach (var beam in Beams)
            {
                if (beam.NodeI == null || beam.NodeJ == null) continue;
                var pair = (beam.NodeI.Name, beam.NodeJ.Name);
                var pairRev = (beam.NodeJ.Name, beam.NodeI.Name);
                if (elementPairs.Contains(pair) || elementPairs.Contains(pairRev))
                {
                    warnings.Add($"要素 '{beam.Name}': 同じ節点組み合わせの要素が重複しています " +
                        $"({beam.NodeI.Name} - {beam.NodeJ.Name})。");
                }
                else
                {
                    elementPairs.Add(pair);
                }
            }

            // 4. 剛体モード（拘束不足）の簡易チェック
            // 自由DOF数のチェック
            int freeDofCount = 0;
            int fixedDofCount = 0;
            foreach (var node in Nodes)
            {
                bool[] bndArr = [node.Boundary.Ux, node.Boundary.Uy, node.Boundary.Uz,
                                 node.Boundary.Rx, node.Boundary.Ry, node.Boundary.Rz];
                bool[] slaveArr = [node.HasMasterAt(Dof.Ux), node.HasMasterAt(Dof.Uy), node.HasMasterAt(Dof.Uz),
                                   node.HasMasterAt(Dof.Rx), node.HasMasterAt(Dof.Ry), node.HasMasterAt(Dof.Rz)];
                for (int d = 0; d < 6; d++)
                {
                    if (bndArr[d] || slaveArr[d]) fixedDofCount++;
                    else freeDofCount++;
                }
            }

            if (freeDofCount == 0)
                errors.Add("全ての自由度が拘束されています。解析する自由度がありません。");

            // 5. 要素断面特性の検証（ゼロ/負の断面値）
            foreach (var beam in Beams)
            {
                if (beam.Section == null)
                {
                    errors.Add($"要素 '{beam.Name}': 断面が定義されていません。");
                    continue;
                }
                if (beam.Section.AX <= 0)
                    warnings.Add($"要素 '{beam.Name}': 軸方向断面積がゼロまたは負です (AX={beam.Section.AX:E3})。");
                if (beam.Section.IY <= 0 && beam.Section.IZ <= 0)
                    warnings.Add($"要素 '{beam.Name}': 断面二次モーメントがゼロまたは負です (IY={beam.Section.IY:E3}, IZ={beam.Section.IZ:E3})。");
                if (beam.Section.Material == null)
                    errors.Add($"要素 '{beam.Name}': 材料が定義されていません。");
                else if (beam.Section.Material.E <= 0)
                    errors.Add($"要素 '{beam.Name}': ヤング率がゼロまたは負です (E={beam.Section.Material.E:E3})。");
            }

            // 結果の処理
            if (errors.Count > 0)
            {
                string msg = $"FEMモデルに{errors.Count}件のエラーが検出されました:\n\n" +
                    string.Join("\n", errors.Take(20));
                if (errors.Count > 20) msg += $"\n...他{errors.Count - 20}件";
                if (warnings.Count > 0)
                    msg += $"\n\n加えて{warnings.Count}件の警告があります。";
                throw new InvalidOperationException(msg);
            }

            if (warnings.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ValidateFemModel] {warnings.Count}件の警告:\n" +
                    string.Join("\n", warnings));
            }
        }

    }
}
