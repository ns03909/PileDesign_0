using MathNet.Numerics.LinearAlgebra;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using Serilog;
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
        /// <summary>
        /// 材料のキャッシュ。<b>ヤング係数とポアソン比の両方</b>を鍵にする。
        ///
        /// 以前はヤング係数だけを鍵にしていた。杭は &#957;=0.2 固定、基礎梁は利用者が入力した値を使うため、
        /// 同じヤング係数で &#957; が違う組合せ (鋼の基礎梁など) では先に作られた方が使い回され、
        /// せん断・ねじり剛性 G = E / (2(1+&#957;)) が黙って別の値になっていた。
        /// </summary>
        private readonly ConcurrentDictionary<(double E, double Nu), Material> _materialCache = new();

        /// <summary>
        /// 断面のキャッシュ。<b>せん断断面積も鍵に含める</b>。
        ///
        /// 杭は「せん断断面積 = 断面積」、基礎梁は (5/6)bh と作り方が違うのに
        /// 同じキャッシュを共有している。鍵に無いと、断面積・断面二次モーメントが一致した
        /// 組合せで一方のせん断断面積がもう一方に使われる。
        /// </summary>
        private readonly ConcurrentDictionary<(double, double, double, double, double, double, double), Section> _sectionCache = new();

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
        /// PileLayoutItems の No / PileNo を 1-based 連番で振り直す。
        /// **必ず UI スレッドから呼出すこと**。AnalysisModelling コンストラクタは Task.Run で
        /// バックグラウンド実行されるため、ここで item プロパティを書き換えると
        /// 同コレクションをバインドする DataGrid (CanUserSortColumns + ソート中) の CollectionView が
        /// bg スレッドから CollectionChanged を発火し NotSupportedException が発生する。
        /// </summary>
        public static void EnsurePileNumbersSequential(InputModel inputModel)
        {
            if (inputModel?.PileLayoutItems == null) return;
            for (int i = 0; i < inputModel.PileLayoutItems.Count; i++)
            {
                var item = inputModel.PileLayoutItems[i];
                if (item.No != i + 1) item.No = i + 1;
                if (item.PileNo != i + 1) item.PileNo = i + 1;
            }
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
            // Serilog.Log.Debug($"[AnalysisModelling] PreallocateCollections: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddActionPointNode();
            // Serilog.Log.Debug($"[AnalysisModelling] AddActionPointNode: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddInputNodes();               // InputNode（General型）を追加
            // Serilog.Log.Debug($"[AnalysisModelling] AddInputNodes: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddDoatsuGoryokuBane();
            // Serilog.Log.Debug($"[AnalysisModelling] AddDoatsuGoryokuBane: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddPileOptimized();            // ← pile.No を 1-based に振り直す
            // Serilog.Log.Debug($"[AnalysisModelling] AddPileOptimized: {sw.ElapsedMilliseconds}ms (Piles={InputModel.PileLayoutItems?.Count ?? 0})");

            sw.Restart();
            AddFoundationBeamNodes();      // 基礎梁節点を追加（pile.No 確定後）
            // Serilog.Log.Debug($"[AnalysisModelling] AddFoundationBeamNodes: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            AddFoundationBeams();          // 基礎梁を追加
            // Serilog.Log.Debug($"[AnalysisModelling] AddFoundationBeams: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            ConnectCapsToFoundation();     // CapNode と基礎梁節点を接続
            // Serilog.Log.Debug($"[AnalysisModelling] ConnectCapsToFoundation: {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            ValidateFemModel();            // FEMモデルの安定性チェック
            // Serilog.Log.Debug($"[AnalysisModelling] ValidateFemModel: {sw.ElapsedMilliseconds}ms");
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
            // (この場合 Rz は元から拘束されるので、ねじれ拘束の有無で結果は変わらない)
            if (xMax - xMin < 1e-6 || yMax - yMin < 1e-6)
                return new Boundary(false, false, false, true, true, true);

            // 杭配置がX、Y方向ともにスタンスがある場合、拘束しない。
            // ただし「基礎のねじれを拘束」が選ばれていれば Rz だけ拘束する。
            // 境界条件は方程式番号による縮約なので、Rz の式が剛性行列から消え、
            // ねじれが厳密にゼロになる。剛体の cross-term (Ux += -Rz·ΔY 等) も
            // master の Rz が消えることで自動的に落ち、杭頭の水平変位が揃う。
            bool fixRz = InputModel.RestrainFoundationTorsion;
            return new Boundary(false, false, false, false, false, fixRz);
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

            // Material キャッシュ (杭は ν=0.2 固定)
            const double pilePoissonRatio = 0.2;
            var material = _materialCache.GetOrAdd((youngsModulus, pilePoissonRatio),
                k => new Material(k.E, k.Nu));

            // Section キャッシュ（丸め誤差を考慮して5桁で丸める）
            // 杭はせん断断面積に断面積をそのまま使う (基礎梁は (5/6)bh)。両者が同じキャッシュを
            // 共有するので、せん断断面積も鍵に入れる。
            var sectionKey = (
                Math.Round(area, 5),
                Math.Round(torsionalInertia, 5),
                Math.Round(inertia, 5),
                Math.Round(inertia, 5),
                Math.Round(youngsModulus, 0),
                Math.Round(area, 5),
                Math.Round(area, 5)
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

            // 重要: ここに以前あった No/PileNo の振り直しは EnsurePileNumbersSequential() に分離し
            // UI スレッドで AnalysisModelling コンストラクタ呼出前に実行するように変更した。
            // 理由: AnalysisModelling は Task.Run (background thread) から構築されるため、
            // ここで item.No/PileNo を書き換えると PileLayoutItems がバインドされた DataGrid
            // (CanUserSortColumns=True で No 列にソートが掛かっていると特に) の CollectionView が
            // bg スレッドから CollectionChanged を発火し NotSupportedException を投げる。
            //
            // この時点で No/PileNo は既に sequential である前提。安全のため未同期なら例外で気づく。
            for (int i = 0; i < InputModel.PileLayoutItems.Count; i++)
            {
                var item = InputModel.PileLayoutItems[i];
                if (item.No != i + 1 || item.PileNo != i + 1)
                {
                    throw new InvalidOperationException(
                        $"AnalysisModelling: PileLayoutItems[{i}] の No={item.No}/PileNo={item.PileNo} が " +
                        $"期待値 {i + 1} と一致しません。EnsurePileNumbersSequential() を UI スレッドで先に呼出してください。");
                }
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
            // 節点別 Z ばね (UsePsSpringAtPileTip ON 時に各杭節点ごとに 1 個)
            // PileNodes と同じ長さ・同じインデックス。先端ばね = VerticalNodeSprings[^1]。
            public List<HorizontalSoilSpring> VerticalNodeSprings { get; } = [];
            public List<FEM.VerticalPileSpringCurve> VerticalNodeSpringCurves { get; } = [];
            // 沈下解析の物理関数を直接呼ぶ剛性モデル (PrepareKmat で優先使用)
            public List<FEM.PileVerticalSoilSpringModel> VerticalNodeSpringModels { get; } = [];
        }

        // 各杭節点に Z 非線形ばねを設置するか判定
        // 条件: UsePsSpringAtPileTip が ON、かつ沈下解析の節点別履歴 (NodeDisplacements/NodeReactions) が存在
        private bool ShouldApplyVerticalSpringsToPile(Models.InputData.SoilPile soilPile)
        {
            if (!_inputModel.UsePsSpringAtPileTip) return false;
            if (soilPile == null) return false;
            if (soilPile.NodeDisplacements == null || soilPile.NodeReactions == null) return false;
            if (soilPile.NodeDisplacements.Count == 0 || soilPile.NodeReactions.Count == 0) return false;
            return true;
        }

        // 沈下解析の節点別 (相対変位, 反力) 履歴から、杭の各節点に非線形 Z ばねを構築する。
        // NodeDisplacements[step] / NodeReactions[step] は paired DOF ベクトル (長さ 2*pileNodesCount):
        //   index 2k: 杭側、index 2k+1: 地盤側
        //   相対変位 = NodeDisplacements[step][2k] - NodeDisplacements[step][2k+1]
        //   節点反力 = NodeReactions[step][2k] (kN, 圧縮正)
        //
        // 案 Z モード: 杭軸力 N0 は SetVectorDF で各杭の接合節点に外力として与えられるため、
        // ここでは PreLoad / 操作点シフトは設定しない。FEM ソルバが自然に δ_op を見つける。
        private void BuildVerticalNodeSprings(
            PileLayoutDataItem pile,
            Models.InputData.SoilPile soilPile,
            PileProcessingResult result)
        {
            var disps = soilPile.NodeDisplacements;
            var reacts = soilPile.NodeReactions;
            int steps = Math.Min(disps.Count, reacts.Count);
            if (steps < 2) throw new InvalidOperationException("沈下解析の履歴ステップが不足 (< 2)。");

            int pileNodeCount = result.PileNodes.Count;
            int vecLen = disps[0].Count;
            int analysisNodeCount = vecLen / 2; // 沈下解析側の節点数

            // 沈下解析側の節点数と AnalysisModelling 側の杭節点数が一致しない場合がある
            // (要素分割の違い等)。最小本数で対応付けする (先頭から順番に)。
            int mapCount = Math.Min(pileNodeCount, analysisNodeCount);

            // 杭節点別自重 [kN] を沈下解析 SetWeights() と同一ロジックで事前計算。
            // 0番目: pcv[0].W × pcv[0].L × 0.5
            // 中間 : pcv[k-1].W × pcv[k-1].L × 0.5 + pcv[k].W × pcv[k].L × 0.5
            // 先端 : pcv[count-1].W × pcv[count-1].L × 0.5
            var pcvList = soilPile.PileCircumVerticals;
            int pcvCount = pcvList?.Count ?? 0;
            var weights = new List<double>(mapCount);
            for (int k = 0; k < mapCount; k++)
            {
                double w = 0.0;
                if (pcvCount > 0)
                {
                    if (k == 0)
                        w = pcvList[0].PileBodySegment.PileSection.W * pcvList[0].L * 0.5;
                    else if (k < pcvCount)
                        w = pcvList[k - 1].PileBodySegment.PileSection.W * pcvList[k - 1].L * 0.5
                          + pcvList[k].PileBodySegment.PileSection.W * pcvList[k].L * 0.5;
                    else // k == pcvCount (= 先端)
                        w = pcvList[pcvCount - 1].PileBodySegment.PileSection.W * pcvList[pcvCount - 1].L * 0.5;
                }
                weights.Add(w);
            }

            // 杭先端パラメータ (PileVerticalSoilSpringModel 先端ノード用)
            double dp = soilPile.Dp / 1000.0; // m
            double rpu = soilPile.SettleRpu;  // kN
            // 先端沈下曲線パラメータは、この SoilPile が属する杭体のものを使う。
            // 直上の dp (= soilPile.Dp) / rpu と同じ杭体でなければ整合しない。
            // (旧実装は _inputModel.PileBodies[^1] と最終要素固定で、杭体が複数あると
            //  別の杭体の α・n が使われていた)
            double alpha = soilPile.PileBodyInput?.SettleAlpha ?? 0.3;
            double n = soilPile.PileBodyInput?.SettleN ?? 2.0;

            for (int k = 0; k < mapCount; k++)
            {
                // 節点 k の (相対変位 m, 反力 kN) ペアをステップ全件抽出 (フォールバック curve 用)
                var points = new List<(double Settlement_m, double Force_kN)>(steps);
                for (int s = 0; s < steps; s++)
                {
                    if (disps[s] == null || reacts[s] == null) continue;
                    if (disps[s].Count <= 2 * k + 1) continue;
                    if (reacts[s].Count <= 2 * k) continue;
                    double sett = disps[s][2 * k] - disps[s][2 * k + 1];
                    double frc = reacts[s][2 * k];
                    points.Add((sett, frc));
                }
                if (points.Count < 2)
                {
                    if (k == pileNodeCount - 1)
                        result.PileNodes[k].SetBoundary(PileTipBoundary);
                    continue;
                }

                FEM.VerticalPileSpringCurve curve;
                try
                {
                    curve = new FEM.VerticalPileSpringCurve(points);
                }
                catch
                {
                    if (k == pileNodeCount - 1)
                        result.PileNodes[k].SetBoundary(PileTipBoundary);
                    continue;
                }

                // 物理モデル (沈下解析と同じ τ-s + R-S 曲線をリアルタイム評価)
                bool isToe = (k == mapCount - 1);
                var model = new FEM.PileVerticalSoilSpringModel(
                    pcvList, k, isToe, dp, rpu, alpha, n, weights[k]);

                var sp = new HorizontalSoilSpring($"杭Zばね-{pile.No}-{k}", result.PileNodes[k], result.SoilNodes[k]);

                // 初期 K は物理モデルの s=+0 接線 (= 線形弾性域の K)
                // N0 は SetVectorDF で外力として与えられるため、ソルバが δ_op を自然に求める
                double kz0 = model.InitialTangentStiffness;
                if (!(double.IsFinite(kz0) && kz0 > 0)) kz0 = curve.InitialTangentStiffness;
                sp.SetKe(0, 0, kz0, 0, 0, 0, true);
                sp.SetKe(0, 0, kz0, 0, 0, 0, false);

                result.VerticalNodeSprings.Add(sp);
                result.VerticalNodeSpringCurves.Add(curve);
                result.VerticalNodeSpringModels.Add(model);
            }
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

            // 杭頭部 M-φ 適用範囲
            //   - 場所打ち鋼管コンクリート杭 (鋼管コンクリート部): 杭頭から 0.5D
            //   - 鋼管杭 (コンクリート充填鋼管部): 杭頭から D (鋼管外径相当)
            double pileTopZoneBottom = double.NegativeInfinity;
            if (soilPile.PileBodySegments.Count > 0)
            {
                var firstSection = soilPile.PileBodySegments[0].PileSection;
                if (firstSection?.PileBodyType == PileTypeNames.InsituSteelPipeConcrete
                    && firstSection.PileSectionType == PileTypeNames.SteelPipeConcreteSection)
                {
                    double pileDia_m = firstSection.PileDiameter / 1000.0;
                    pileTopZoneBottom = z0 - 0.5 * pileDia_m;
                }
                else if (firstSection?.PileBodyType == PileTypeNames.SteelPipe
                    && firstSection.PileSectionType == PileTypeNames.CftSection)
                {
                    double pileDia_m = firstSection.PileDiameter / 1000.0;
                    pileTopZoneBottom = z0 - pileDia_m;
                }
            }

            for (int i = 0; i < nodeCount; i++)
            {
                double z = soilPile.ZDataItems[i].Z;

                // 前の節点とZ座標が同一の場合はスキップ（ゼロ長要素防止）
                if (i > 0 && Math.Abs(z - prevPileNode!.Coord.Z) < 1e-10)
                {
                    Log.Warning(
                        "[AnalysisModelling] 重複 Z 座標スキップ: Pile-{PileNo} i={Index} Z={Z:F6}",
                        pile.No, i, z);
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
                        Kbig = FemConstants.KbigRotation  // CapNode-PileNode間のペナルティ剛性（Ux/Uy用、Uzはmaster-slaveで不要）
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

                    // 杭先端 Z 境界: P-S 非線形ばねが本ループ末尾で各節点に追加される場合は
                    // 杭先端ノードも Uz 自由 (ばねが支持) とする。それ以外は従来の Uz 固定。
                    if (!ShouldApplyVerticalSpringsToPile(soilPile))
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

            // 全節点 Z ばね (沈下解析の節点別 (相対変位, 反力) 履歴から各節点ごとに非線形ばねを構築)
            if (ShouldApplyVerticalSpringsToPile(soilPile))
            {
                try
                {
                    BuildVerticalNodeSprings(pile, soilPile, result);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex,
                        "[AnalysisModelling] Pile-{PileNo}: 節点別Zばね構築に失敗。先端 Uz 固定にフォールバック。",
                        pile.No);
                    // フォールバック: 先端ノードに Uz 固定を適用 (上のループでスキップしたため再適用)
                    if (result.PileNodes.Count > 0)
                        result.PileNodes[^1].SetBoundary(PileTipBoundary);
                    result.VerticalNodeSprings.Clear();
                    result.VerticalNodeSpringCurves.Clear();
                }
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

                // 節点別 Z ばね: master の HorizontalSoilSprings には追加して K 組立対象にする一方、
                // pile.HorizontalSoilSprings (= 各杭の水平ばね loop) には入れない。
                // (水平ばね K 更新 loop が Z を 0 で上書きしないようにするため)
                pile.VerticalNodeSprings.Clear();
                pile.VerticalNodeSpringCurves.Clear();
                pile.VerticalNodeSpringModels.Clear();
                if (result.VerticalNodeSprings.Count > 0)
                {
                    foreach (var vs in result.VerticalNodeSprings)
                    {
                        HorizontalSoilSprings.Add(vs);
                        pile.VerticalNodeSprings.Add(vs);
                    }
                    foreach (var cv in result.VerticalNodeSpringCurves)
                        pile.VerticalNodeSpringCurves.Add(cv);
                    foreach (var md in result.VerticalNodeSpringModels)
                        pile.VerticalNodeSpringModels.Add(md);
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
                // ※ IsVisible で絞らないこと。表示専用のフラグであり、これを見ると
                //    「非表示にした節点が解析モデルから消える」= 画面の見た目で結果が変わる。
                if (inputNode.Type == NodeType.General)
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

            // 専用FoundationNodeからFEM節点を作成（RigidFloorモードのみ：基礎梁の端点として使用）
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
                    // v2 セマンティクス: pile.Z は接合節点 Z なので、ConnectionNode 位置はそのまま pile.Z
                    node.SetNodeInfo(name, pile.X, pile.Y, pile.Z);
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

        // 基礎梁の追加
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
                var nodeI = ResolveFemNode(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                var nodeJ = ResolveFemNode(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);

                if (nodeI == null || nodeJ == null)
                {
                    int beamNo = InputModel.FoundationBeamInput.GetBeamNo(fbBeam);
                    string detail = $"基礎梁{beamNo}の節点が見つかりません。\n" +
                        $"I端: Type={fbBeam.NodeI_Type}, Id={fbBeam.NodeI_Id} → {(nodeI != null ? nodeI.Name : "未解決")}\n" +
                        $"J端: Type={fbBeam.NodeJ_Type}, Id={fbBeam.NodeJ_Id} → {(nodeJ != null ? nodeJ.Name : "未解決")}";
                    throw new InvalidOperationException(detail);
                }

                // Section を作成
                var section = CreateFoundationBeamSection(fbBeam);

                // Beam 要素を作成 (識別名は 1-based の位置インデックスを使用)
                var beam = new Beam($"FoundationBeam-{InputModel.FoundationBeamInput.GetBeamNo(fbBeam)}", section, nodeI, nodeJ, 1.0, 1.0);
                Beams.Add(beam);
            }
        }

        // Type+Guid方式でFEM節点を検索（辞書＋座標ベースフォールバック付き）
        private Node? ResolveFemNode(NodeReferenceType type, Guid id)
        {
            if (id == Guid.Empty) return null;

            // 方法1a: 杭参照 → 構築済み辞書から直接引く（Guid不一致に強い）
            if (type == NodeReferenceType.PileLayout &&
                _pileGuidToFemName != null && _pileGuidToFemName.TryGetValue(id, out var dictName))
            {
                var node = Nodes.FirstOrDefault(n => n.Name == dictName);
                if (node != null) return node;
            }

            // 方法1b: Type+Guid から FEM節点名を生成して検索
            string femName = GetFemNodeName(type, id);
            if (femName != null)
            {
                var node = Nodes.FirstOrDefault(n => n.Name == femName);
                if (node != null) return node;
            }

            // 方法2: 座標ベースのフォールバック（上記すべて失敗した場合）
            var coords = InputModel.GetNodeCoordinates(type, id);
            if (coords.HasValue)
            {
                return FindClosestFoundationNode(coords.Value.X, coords.Value.Y, coords.Value.Z);
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

        // 基礎梁断面の作成（BeamSection/BeamMaterialから断面諸元を取得）
        private Section CreateFoundationBeamSection(FoundationBeam fbBeam)
        {
            double youngsModulus;
            double poissonRatio;

            // BeamMaterialからヤング係数・ポアソン比を取得 (MaterialNo は 1-based の位置インデックス)
            var beamMaterial = fbBeam.MaterialNo >= 1
                ? InputModel.FoundationBeamInput?.Materials?.ElementAtOrDefault(fbBeam.MaterialNo - 1)
                : null;
            if (beamMaterial != null)
            {
                youngsModulus = beamMaterial.YoungModulus;
                poissonRatio = beamMaterial.PoissonRatio;
            }
            else
            {
                // フォールバック: FoundationBeamの値を使用
                youngsModulus = fbBeam.YoungModulus;
                poissonRatio = 0.2;
            }

            double area, shearAreaY, shearAreaZ, torsionalMoment, iy, iz;

            // BeamSectionから断面諸元を取得（ウィザードの増減係数が反映済み）
            var beamSection = fbBeam.SectionNo >= 1
                ? InputModel.FoundationBeamInput?.Sections?.ElementAtOrDefault(fbBeam.SectionNo - 1)
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

            // Material キャッシュ (基礎梁は利用者が入力したポアソン比)
            var material = _materialCache.GetOrAdd((youngsModulus, poissonRatio),
                k => new Material(k.E, k.Nu));

            // Section キャッシュ
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

            // RigidFloorモードでは基礎梁が必要
            if (mode == FoundationBeamConnectionMode.RigidFloor)
            {
                var beamElements = InputModel.FoundationBeamInput.Beams;
                if (beamElements == null || beamElements.Count == 0)
                    throw new InvalidOperationException("剛床連結モードでは基礎梁が必要です。");
            }

            // 剛リンク用セクション (CapNode-ConnectionNode 間を実質剛結合)
            // 2026-05-06: E を 1e10 → 1e9 kN/m² (= 1,000 GPa、鋼の約 5 倍) に低減。
            //   K 行列条件数を 1 桁改善し非線形収束を安定化。1m×1m 断面・L=1m で
            //   軸変位は 10000 kN 載荷時 0.06mm 程度に抑えられ、剛体扱いとして実用上問題なし。
            var rigidLinkMat = _materialCache.GetOrAdd((FemConstants.RigidLinkYoungModulus, 0.2),
                k => new Material(k.E, k.Nu));
            var rigidLinkSecKey = (1.0, 0.14, Math.Round(1.0 / 12.0, 5), Math.Round(1.0 / 12.0, 5),
                FemConstants.RigidLinkYoungModulus, 1.0, 1.0);
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
                    // 2026-05-06 (改): DOF 別 Kbig を採用。並進 KbigT=1e7 kN/m と回転 KbigR=1e8 kN·m/rad
                    // を分離。並進は条件数改善のため 1 桁低減を維持、回転は基礎梁/RC 杭の典型 EI/L
                    // (1e7 オーダー) に対して 10× の余裕で剛接合扱いを担保。
                    const double KbigT = FemConstants.KbigTranslation;
                    const double KbigR = FemConstants.KbigRotation;
                    var penaltySpring = new HorizontalSoilSpring(
                        $"PenaltySpring-{pile.No}", connectionNode, capNode);
                    penaltySpring.SetKe(KbigT, KbigT, KbigT, KbigR, KbigR, KbigR, true);
                    penaltySpring.SetKe(KbigT, KbigT, KbigT, KbigR, KbigR, KbigR, false);
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

            // 2026-05-06 (改): DOF 別 Kbig — 並進 1e7 kN/m / 回転 1e8 kN·m/rad
            // (上記 ConnectCapsRigidBody と同方針)。
            const double KbigT = FemConstants.KbigTranslation; // 並進ペナルティ剛性 [kN/m]
            const double KbigR = FemConstants.KbigRotation;    // 回転ペナルティ剛性 [kN·m/rad]

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
                    penaltySpring.SetKe(KbigT, KbigT, KbigT, KbigR, KbigR, KbigR, true);
                    penaltySpring.SetKe(KbigT, KbigT, KbigT, KbigR, KbigR, KbigR, false);
                    PenaltySprings.Add(penaltySpring);
                    penaltyCount++;
                    log.AppendLine($"  {connectionNode.Name} ↔ {capNode.Name}: ペナルティばね KbigT={KbigT:E1}, KbigR={KbigR:E1}");
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
            log.AppendLine($"基礎梁本数: {fbCount}");
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
            // Serilog.Log.Debug(log.ToString());

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

            // 3. 重複要素の検出（同じNodeI-NodeJ組合せ）
            var elementPairs = new HashSet<(string, string)>();
            foreach (var beam in Beams)
            {
                if (beam.NodeI == null || beam.NodeJ == null) continue;
                var pair = (beam.NodeI.Name, beam.NodeJ.Name);
                var pairRev = (beam.NodeJ.Name, beam.NodeI.Name);
                if (elementPairs.Contains(pair) || elementPairs.Contains(pairRev))
                {
                    warnings.Add($"要素 '{beam.Name}': 同じ節点組合せの要素が重複しています " +
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
                Log.Warning(
                    "[ValidateFemModel] {Count} 件の警告:\n{Warnings}",
                    warnings.Count, string.Join("\n", warnings));
            }
        }

    }
}
