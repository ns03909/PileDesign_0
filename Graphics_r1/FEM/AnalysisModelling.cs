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

        // 最適化用: Material/Section キャッシュ
        private readonly ConcurrentDictionary<double, Material> _materialCache = new();
        private readonly ConcurrentDictionary<(double, double, double, double, double), Section> _sectionCache = new();

        // 最適化用: 共通Boundaryオブジェクト（毎回newしない）
        private static readonly Boundary SoilNodeBoundary = new(false, false, true, true, true, true);
        private static readonly Boundary PileTipBoundary = new(false, false, true, false, false, false);

        // コンストラクタ
        public  AnalysisModelling(InputModel inputModel)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
            Initialize();
        }

        private void Initialize()
        {
            PreallocateCollections();
            AddActionPointNode();
            AddDoatsuGoryokuBane();
            AddPileOptimized();
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
            double youngsModulus = soilPile.PileBodySegments[segIndex].PileSection.ConcreteE * 1000.0; // kN/m2
            double shearModulus = Utils.GetShearModulus(youngsModulus, 0.2); // kN/m2
            double area = soilPile.PileBodySegments[segIndex].PileSection.EA / youngsModulus; // m2
            double inertia = soilPile.PileBodySegments[segIndex].PileSection.EI / youngsModulus; // m4
            double torsionalInertia = soilPile.PileBodySegments[segIndex].PileSection.GJ / shearModulus; // m4

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

            // 並列で各杭を処理（杭間に依存関係がないため安全）
            Parallel.For(0, pileCount, i =>
            {
                pileResults[i] = ProcessSinglePile(pileList[i]);
            });

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
            double initialRotK = 1e6;  // アーム変換後の条件数を改善するため1e6に低減

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
            // CapNodeはRigidBodyのスレーブとして使用されるため、境界条件を事前に設定
            // これによりAnaModel構築時に正しく固定DOFとして認識される
            capNode.SetBoundary(new Boundary(true, true, true, true, true, true));
            result.CapNode = capNode;

            Node? prevPileNode = null;
            int nodeCount = soilPile.ZDataItems.Count;

            for (int i = 0; i < nodeCount; i++)
            {
                double z = soilPile.ZDataItems[i].Z;

                // Pile Node
                var pileNode = new Node();
                pileNode.SetNodeInfo($"PileNode-{pile.No}-{i}", x, y, z);
                result.PileNodes.Add(pileNode);

                if (i == 0)
                {
                    // 杭頭回転ばね
                    var rxy = new RotationalSpring($"RθXY-{pile.No}", capNode, pileNode, initialRotK)
                    {
                        PileBodyNo = pile.PileBodyNo,
                        TieUx = true,
                        TieUy = true,
                        TieUz = true,
                        TieRz = true,
                        Kbig = 1e6  // アーム変換後の条件数を改善するため1e6に低減
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
                    if (i == 1) beam.SetPileTopFlag(true);
                    result.Beams.Add(beam);
                    prevPileNode = pileNode;
                }
                else
                {
                    // 先端
                    var beam = CreatePileElement(soilPile, i - 1, prevPileNode!, pileNode);
                    beam.PileBodyNo = soilPile.PileBodyNo;
                    beam.SegmentIndex = i - 1;
                    result.Beams.Add(beam);
                    pileNode.SetBoundary(PileTipBoundary);
                }

                // Soil Node
                var soilNode = new Node();
                soilNode.SetNodeInfo($"SoilNode-{pile.No}-{i}", x, y, z);
                soilNode.SetIsForcedDisped(true);
                soilNode.SetBoundary(SoilNodeBoundary);
                result.SoilNodes.Add(soilNode);

                // 水平土ばね
                var hspring = new HorizontalSoilSpring($"HorizontalSoilSpring-{pile.No}-{i}", pileNode, soilNode);
                result.HorizontalSoilSprings.Add(hspring);
            }

            return result;
        }

        // 処理結果をメインコレクションにマージ
        private void MergePileResults(PileProcessingResult[] results)
        {
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
                    RigidBodies[0].AddSlaveNode(result.CapNode);
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

    }
}
