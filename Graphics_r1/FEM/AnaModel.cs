using MathNet.Numerics.LinearAlgebra;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.FEM
{
    /// <summary>
    /// E3a (2026-04-23): ケース並列化で AnaModel を per-case DeepCopy する際、
    /// InputModel.PileLayoutItems / ElementDivision.DoatsuGoryokuBane は共有 (copy しない)
    /// なので、そこに格納されている Node 参照や AxialForce は「元モデルの値」のまま。
    /// これを caseModel 側で書換えるとワーカー間でレースする。
    ///
    /// 対策として、caseModel (=DeepCopy の戻り値) だけが持つ「ケース固有の Node マップ /
    /// AxialForce 値」を本スナップショットに保持する。主モデル (= 通常構築された AnaModel)
    /// は Snapshot を持たない (null) ので、従来通り InputModel を直接参照する。
    ///
    /// 使い方: 呼び出し側は AnaModel.GetPileNodes(item) / GetAxialForce(item) / SetAxialForce(...)
    /// 経由で Node や AxialForce にアクセスする。Snapshot が null なら InputModel へフォールバック、
    /// 非 null なら case-local dict を参照する。
    /// </summary>
    public sealed class CaseLocalSnapshot
    {
        /// <summary>PileLayoutItem → caseModel 側の PileNodes リスト</summary>
        public Dictionary<PileLayoutDataItem, List<Node>> PileNodes { get; } = new();

        /// <summary>PileLayoutItem → caseModel 側の SoilNodes リスト</summary>
        public Dictionary<PileLayoutDataItem, List<Node>> SoilNodes { get; } = new();

        /// <summary>PileLayoutItem → 現在の AxialForce (ケース毎に更新可能)</summary>
        public Dictionary<PileLayoutDataItem, double> AxialForces { get; } = new();

        /// <summary>PileLayoutItem → 現在の AxialForceIncrement (ケース毎に計算)</summary>
        public Dictionary<PileLayoutDataItem, double> AxialForceIncrements { get; } = new();

        /// <summary>DoatsuGoryokuBaneItem → caseModel 側の TopSoilNode</summary>
        public Dictionary<DoatsuGoryokuBaneItem, Node> DoatsuTopSoilNodes { get; } = new();

        /// <summary>DoatsuGoryokuBaneItem → caseModel 側の BtmSoilNode</summary>
        public Dictionary<DoatsuGoryokuBaneItem, Node> DoatsuBtmSoilNodes { get; } = new();

        /// <summary>DoatsuGoryokuBaneItem → caseModel 側の TopEmbedmentNode</summary>
        public Dictionary<DoatsuGoryokuBaneItem, Node> DoatsuTopEmbedmentNodes { get; } = new();

        /// <summary>DoatsuGoryokuBaneItem → caseModel 側の BtmEmbedmentNode</summary>
        public Dictionary<DoatsuGoryokuBaneItem, Node> DoatsuBtmEmbedmentNodes { get; } = new();

        /// <summary>PileLayoutItem → caseModel 側の PileTopRotationalSpring (null の場合あり)</summary>
        public Dictionary<PileLayoutDataItem, RotationalSpring?> PileTopRotationalSprings { get; } = new();

        /// <summary>PileLayoutItem → caseModel 側の HorizontalSoilSprings リスト</summary>
        public Dictionary<PileLayoutDataItem, List<HorizontalSoilSpring>> PileHorizontalSoilSprings { get; } = new();

        /// <summary>PileLayoutItem → caseModel 側の Beams リスト</summary>
        public Dictionary<PileLayoutDataItem, List<Beam>> PileBeams { get; } = new();

        /// <summary>DoatsuGoryokuBaneItem → caseModel 側の TopHorizontalSoilSpring</summary>
        public Dictionary<DoatsuGoryokuBaneItem, HorizontalSoilSpring> DoatsuTopHorizontalSoilSprings { get; } = new();

        /// <summary>DoatsuGoryokuBaneItem → caseModel 側の BtmHorizontalSoilSpring</summary>
        public Dictionary<DoatsuGoryokuBaneItem, HorizontalSoilSpring> DoatsuBtmHorizontalSoilSprings { get; } = new();
    }

    public class AnaModel
    {
        private readonly InputModel _inputModel;
        public InputModel InputModel => _inputModel;

        /// <summary>
        /// E3a: case-local 状態のスナップショット。主モデルでは null、DeepCopy の戻り値では非 null。
        /// これが null のとき、Get/Set 系ヘルパーは InputModel を直接参照する (従来挙動)。
        /// 非 null のときは caseModel 固有の値に切替わる。
        /// </summary>
        [JsonIgnore]
        public CaseLocalSnapshot? CaseLocalState { get; private set; }

        public List<Node> Nodes { get; set; }
        public List<Beam> Beams { get; set; }
        public List<DummyBeam> DummyBeams { get; set; }
        public List<RigidBody> RigidBodies { get; set; }
        public List<HorizontalSoilSpring> HorizontalSoilSprings { get; set; }
        public List<RotationalSpring> RotationalSprings { get; set; }
        public List<HorizontalSoilSpring> PenaltySprings { get; set; } = [];  // ConnectionNode↔CapNodeペナルティばね
        public List<AnalysisStepResult> AnalysisStepResults { get; set; } = []; // 解析ステップ結果のリスト

        public int CountFree { get; set; }
        public int CountFix { get; set; }

        public Vector<double> VectorF { get; private set; } // 荷重ベクトル
        public Vector<double> VectorDF { get; private set; } // 荷重増分ベクトル
        public Vector<double> VectorD { get; private set; } // 変位ベクトル
        public Vector<double> VectorDD { get; private set; } // 変位増分ベクトル
        public Vector<double> VectorT { get; private set; } // 内力ベクトル
        public Vector<double> VectorR { get; private set; } // 残差ベクトル

        public Matrix<double> KAA_tan { get; private set; }
        public Matrix<double> KBA_tan { get; private set; }
        public Matrix<double> KAB_tan { get; private set; }
        public Matrix<double> KBB_tan { get; private set; }

        public Matrix<double> KAA_sec { get; private set; }
        public Matrix<double> KBA_sec { get; private set; }
        public Matrix<double> KAB_sec { get; private set; }
        public Matrix<double> KBB_sec { get; private set; }

        [JsonIgnore]
        public List<bool> VectorDOFForcedDisp { get; private set; } = [];

        [JsonIgnore]
        public double NormsROnNormsFint { get; private set; }

        // 安定性チェック用: ゼロ/負の対角成分を持つ自由度のリスト
        [JsonIgnore]
        public List<(int eq, string nodeName, double val)> ZeroDiagDofs { get; private set; } = [];
        [JsonIgnore]
        public List<(int eq, string nodeName, double val)> SmallDiagDofs { get; private set; } = [];

        // コンストラクタ
        public AnaModel(
            InputModel inputModel,
            List<Node> nodes,
            List<Beam> beams,
            List<DummyBeam> dummyBeams,
            List<RigidBody> rigidBodies,
            List<HorizontalSoilSpring> horizontalSoilSprings,
            List<RotationalSpring> rotationalSprings
            )
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));

            Nodes = nodes; // 節点リスト
            Beams = beams; // 要素リスト
            DummyBeams = dummyBeams; // 要素リスト
            RigidBodies = rigidBodies; // 剛体連結リスト
            HorizontalSoilSprings = horizontalSoilSprings; // 水平地盤ばねリスト
            RotationalSprings = rotationalSprings;

            int countFree = 0;
            int countFix = 0;
            var dofForcedDispList = new List<bool>();

            foreach (var node in Nodes)
            {
                for (int index = 0; index < 6; index++)
                {
                    if (node.GetBoundary(index) == false)
                    {
                        // 自由 -> 正値
                        countFree++;
                        node.SetEquationNumber(index, countFree - 1);
                        dofForcedDispList.Add(node.IsForcedDisped);
                    }
                    else
                    {
                        // 固定 -> 負値
                        countFix--;
                        node.SetEquationNumber(index, countFix);
                    }
                }
            }
            VectorDOFForcedDisp = dofForcedDispList;
            CountFree = countFree; // 自由度数
            CountFix = countFix; // 固定度数

            // ガード: 異常に大きな自由度を検出して早期にエラーを出す（リリース版での OOM 回避）
            const int MaxReasonableDofs = 200000; // 必要に応じで調整
            if (CountFree < 0)
                throw new InvalidOperationException("CountFree must be non-negative.");
            if (CountFree > MaxReasonableDofs)
            {
                // 重大な入力/設定ミスの可能性。詳細を出力して例外にする。
                System.Diagnostics.Debug.WriteLine($"[ERROR] CountFree is very large: {CountFree}. Aborting to avoid OOM.");
                throw new InvalidOperationException($"自由度が大きすぎます: {CountFree}. 入力データ／境界条件を確認してください。");
            }

            VectorF = Vector<double>.Build.Sparse(countFree, 0.0); // 荷重ベクトル
            VectorDF = Vector<double>.Build.Sparse(countFree, 0.0); // 荷重増分ベクトル
            VectorD = Vector<double>.Build.Sparse(countFree, 0.0); // 変位ベクトル

            // Master-Slave チェーン解決: 各ノードの ResolvedDofMap を計算
            ResolveConstraintChains();
        }

        // コンストラクタ
        public AnaModel()
        { }

        // 剛体連結のスレイブ節点をセットするメソッド
        public void SetSlaveNodes()
        {
            foreach (var rigidBody in RigidBodies)
                rigidBody.SetSlaveNodeRelations();
        }

        /// <summary>
        /// Master-Slave チェーンを解決し、各ノードの ResolvedDofMap を計算する。
        /// ABAQUS/Code_Aster 方式: DOF 単位の依存グラフをトポロジカルソートし、
        /// 各 slave DOF を最終的な独立 DOF 群への線形写像として表現する。
        /// </summary>
        private void ResolveConstraintChains()
        {
            // 剛体運動学の cross-term 定義:
            // Ux(0) += Ry(4)×ΔZ - Rz(5)×ΔY
            // Uy(1) += Rz(5)×ΔX - Rx(3)×ΔZ
            // Uz(2) += Rx(3)×ΔY - Ry(4)×ΔX
            (int crossDof, int armIdx, double sign)[][] crossTermDefs =
            [
                [(4, 2, 1.0), (5, 1, -1.0)],   // DOF 0 (Ux): Ry×ΔZ, -Rz×ΔY
                [(5, 0, 1.0), (3, 2, -1.0)],   // DOF 1 (Uy): Rz×ΔX, -Rx×ΔZ
                [(3, 1, 1.0), (4, 0, -1.0)],   // DOF 2 (Uz): Rx×ΔY, -Ry×ΔX
                [],                              // DOF 3 (Rx): 回転にcross-termなし
                [],                              // DOF 4 (Ry)
                [],                              // DOF 5 (Rz)
            ];

            // Step 1: トポロジカルソート（Kahn's algorithm）
            // DOF 単位ではなくノード単位で十分（同一ノード内の循環はない）
            var inDegree = new Dictionary<Node, int>();
            var dependents = new Dictionary<Node, HashSet<Node>>();
            foreach (var node in Nodes)
            {
                if (!inDegree.ContainsKey(node)) inDegree[node] = 0;
                if (!dependents.ContainsKey(node)) dependents[node] = [];

                foreach (var master in node.MasterNodes)
                {
                    if (master != null && master != node)
                    {
                        if (!inDegree.ContainsKey(master)) inDegree[master] = 0;
                        if (!dependents.ContainsKey(master)) dependents[master] = [];

                        // slave → master の依存関係（HashSet で重複チェック O(1)）
                        if (dependents[master].Add(node))
                        {
                            inDegree[node]++;
                        }
                    }
                }
            }

            // BFS でトポロジカル順序を構築（マスターが先）
            var queue = new Queue<Node>();
            foreach (var (node, deg) in inDegree)
            {
                if (deg == 0) queue.Enqueue(node);
            }

            var topoOrder = new List<Node>();
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                topoOrder.Add(node);
                foreach (var dep in dependents.GetValueOrDefault(node, []))
                {
                    inDegree[dep]--;
                    if (inDegree[dep] == 0) queue.Enqueue(dep);
                }
            }

            // 循環検出
            if (topoOrder.Count < Nodes.Count)
            {
                var inOrder = new HashSet<Node>(topoOrder);
                var missing = Nodes.Where(n => !inOrder.Contains(n)).Select(n => n.Name);
                System.Diagnostics.Debug.WriteLine(
                    $"[WARNING] Master-Slave 循環検出: {string.Join(", ", missing)}");
                // 循環ノードも処理するためリストに追加
                foreach (var node in Nodes)
                {
                    if (!inOrder.Contains(node)) topoOrder.Add(node);
                }
            }

            // Step 2: トポロジカル順序で ResolvedDofMap を計算
            // マスターが先に処理されるので、slave 処理時に master の map は解決済み
            foreach (var node in topoOrder)
            {
                node.ResolvedDofMap = new DofTerm[6][];
                double[] armComponents = [node.SlaveArm.X, node.SlaveArm.Y, node.SlaveArm.Z];

                for (int d = 0; d < 6; d++)
                {
                    var master = node.MasterNodes[d];
                    if (master == null)
                    {
                        // Free DOF or boundary
                        int eq = node.EquationNumber[d];
                        node.ResolvedDofMap[d] = eq >= 0
                            ? [new DofTerm(eq, 1.0)]
                            : [];  // boundary (固定)
                        continue;
                    }

                    // Slave DOF: master の ResolvedDofMap[d] を取得
                    var terms = new List<DofTerm>();

                    // Primary: master の同一 DOF の写像
                    if (master.ResolvedDofMap?[d] != null)
                    {
                        foreach (var t in master.ResolvedDofMap[d])
                            terms.Add(new DofTerm(t.Eq, t.Coeff));
                    }

                    // Cross-terms: 剛体運動学（並進 DOF のみ）
                    foreach (var (crossDof, armIdx, sign) in crossTermDefs[d])
                    {
                        double armVal = armComponents[armIdx];
                        if (Math.Abs(armVal) < 1e-15) continue;

                        // master の crossDof の写像を取得
                        DofTerm[] crossMap = master.ResolvedDofMap?[crossDof];
                        if (crossMap == null || crossMap.Length == 0) continue;

                        foreach (var ct in crossMap)
                        {
                            terms.Add(new DofTerm(ct.Eq, ct.Coeff * armVal * sign));
                        }
                    }

                    // 同一 eq の terms をマージ（係数加算）
                    node.ResolvedDofMap[d] = MergeTerms(terms);
                }
            }
        }

        /// <summary>
        /// DofTerm リストから同一方程式番号の項をマージし、
        /// 係数がゼロに近い項を除去して配列化する。
        /// </summary>
        private static DofTerm[] MergeTerms(List<DofTerm> terms)
        {
            if (terms.Count == 0) return [];

            // 方程式番号でグループ化して係数を合計
            var merged = new Dictionary<int, double>();
            foreach (var t in terms)
            {
                merged[t.Eq] = merged.GetValueOrDefault(t.Eq, 0.0) + t.Coeff;
            }

            // 係数が実質ゼロの項を除去
            var result = new List<DofTerm>();
            foreach (var (eq, coeff) in merged)
            {
                if (Math.Abs(coeff) > 1e-15)
                    result.Add(new DofTerm(eq, coeff));
            }

            // primary term（coeff ≈ 1.0）を先頭に配置
            result.Sort((a, b) => Math.Abs(b.Coeff).CompareTo(Math.Abs(a.Coeff)));
            return result.ToArray();
        }

        // 全体剛性マトリクスの作成
        // KAAへの節点ばね剛性のマップオン
        // KBA, KAB, KBBの新規作成、要素剛性のマップオン
        public void MapOnKtanMat()
        {
            MapOnKmat(true);
        }

        public void MapOnKsecMat()
        {
            MapOnKmat(false);
        }

        //  全体剛性マトリクスの作成
        // v28 F-old (2026-04-23): Beams の Ke 計算 + 全体座標変換 + COO 分配を Parallel.ForEach 化。
        //   要素ごとの SetKe / TransElemStiffToGlobal / AppendStiffnessToCoo は相互独立
        //   (各 beam は自身の KeTan/KeSec のみ mutate、ResolvedDofMap は read-only)。
        //   ThreadLocal で COO 三つ組を蓄積し、最後に SparseOfIndexed で一括構築する。
        //   MathNet Sparse の indexed += は O(log nnz) で遅いため、COO 一括構築が 3〜10 倍速い。
        //   Springs (HorizontalSoilSpring / RotationalSpring / PenaltySpring) は数が少なく、
        //   Ke は PrepareKmat で既にセット済みのため serial loop で COO 追加。
        private void MapOnKmat(bool isTan)
        {
            // Phase 1: Beams を並列組立 (thread-local COO → 集約)
            var cooBags = new System.Collections.Concurrent.ConcurrentBag<List<(int r, int c, double v)>>();

            if (Beams != null && Beams.Count > 0)
            {
                int threadInitial = Math.Max(256, Beams.Count * 144 / Math.Max(1, Environment.ProcessorCount));

                System.Threading.Tasks.Parallel.ForEach(
                    Beams,
                    () => new List<(int r, int c, double v)>(threadInitial),
                    (beam, _, local) =>
                    {
                        beam.SetKe(isTan);
                        var tkt = beam.TransElemStiffToGlobal(isTan);
                        Utils.AppendStiffnessToCoo(local, tkt, true, true, beam.NodeI, beam.NodeJ);
                        return local;
                    },
                    local => cooBags.Add(local)
                );
            }

            // Phase 2: Springs を serial で COO 追加 (Ke は PrepareKmat で設定済み)
            int springCount = (HorizontalSoilSprings?.Count ?? 0)
                            + (RotationalSprings?.Count ?? 0)
                            + (PenaltySprings?.Count ?? 0);
            var serialCoo = new List<(int r, int c, double v)>(springCount * 48);

            if (HorizontalSoilSprings != null)
            {
                foreach (var hs in HorizontalSoilSprings)
                {
                    var ke = isTan ? hs.KeTan : hs.KeSec;
                    if (ke == null) continue;
                    Utils.AppendStiffnessToCoo(serialCoo, ke, true, true, hs.NodeI, hs.NodeJ);
                }
            }

            if (RotationalSprings != null && RotationalSprings.Count > 0)
            {
                foreach (var rs in RotationalSprings)
                {
                    var ke = isTan ? rs.KeTan : rs.KeSec;
                    if (ke == null) continue;
                    Utils.AppendStiffnessToCoo(serialCoo, ke, true, true, rs.NodeI, rs.NodeJ);
                }
            }

            if (PenaltySprings != null && PenaltySprings.Count > 0)
            {
                foreach (var ps in PenaltySprings)
                {
                    var ke = isTan ? ps.KeTan : ps.KeSec;
                    if (ke == null) continue;
                    Utils.AppendStiffnessToCoo(serialCoo, ke, true, true, ps.NodeI, ps.NodeJ);
                }
            }

            // Phase 3: 重複インデックスを事前加算して SparseOfIndexed で一括構築
            // MathNet 5.0.0 の SparseOfIndexed は重複 index を SUM せず最初の値を採用する仕様のため、
            // Dictionary で (row,col) ごとに加算してから 1 エントリ/キーで構築する。
            int totalCount = serialCoo.Count;
            foreach (var list in cooBags) totalCount += list.Count;

            var aggregated = new Dictionary<(int r, int c), double>(totalCount);

            void Accumulate(int r, int c, double v)
            {
                var key = (r, c);
                aggregated[key] = aggregated.TryGetValue(key, out double existing) ? existing + v : v;
            }

            foreach (var (r, c, v) in serialCoo) Accumulate(r, c, v);
            foreach (var list in cooBags)
                foreach (var (r, c, v) in list)
                    Accumulate(r, c, v);

            Matrix<double> matrixKAA;
            if (aggregated.Count == 0)
            {
                matrixKAA = Matrix<double>.Build.Sparse(CountFree, CountFree);
            }
            else
            {
                var tuples = new List<Tuple<int, int, double>>(aggregated.Count);
                foreach (var kv in aggregated)
                    tuples.Add(Tuple.Create(kv.Key.r, kv.Key.c, kv.Value));
                matrixKAA = Matrix<double>.Build.SparseOfIndexed(CountFree, CountFree, tuples);
            }
            // KBA/KAB/KBB は現状使われていないが API 互換のため空 sparse を維持
            Matrix<double> matrixKBA = Matrix<double>.Build.Sparse(CountFree, -CountFix);
            Matrix<double> matrixKAB = Matrix<double>.Build.Sparse(-CountFix, CountFree);
            Matrix<double> matrixKBB = Matrix<double>.Build.Sparse(-CountFix, -CountFix);

            // 正則化（ゼロ/負の対角値を診断）+ 小さい対角値の診断
            const double eps = 1e-9;
            const double smallThreshold = 1e-6; // この値以下の対角値を警告
            ZeroDiagDofs = [];
            SmallDiagDofs = [];

            for (int i = 0; i < CountFree; i++)
            {
                double v = matrixKAA[i, i];
                string nodeDofName = null;

                // この方程式番号に対応するノードと自由度を特定
                foreach (var node in Nodes)
                {
                    for (int d = 0; d < 6; d++)
                    {
                        if (node.EquationNumber[d] == i)
                        {
                            string dofName = d switch { 0 => "Ux", 1 => "Uy", 2 => "Uz", 3 => "Rx", 4 => "Ry", _ => "Rz" };
                            nodeDofName = $"{node.Name}:{dofName}";
                            break;
                        }
                    }
                    if (nodeDofName != null) break;
                }

                if (!double.IsFinite(v) || v <= 0.0)
                {
                    ZeroDiagDofs.Add((i, nodeDofName ?? $"eq{i}", v));
                    matrixKAA[i, i] = eps;
                }
                else if (v < smallThreshold)
                {
                    SmallDiagDofs.Add((i, nodeDofName ?? $"eq{i}", v));
                }
            }

            // NaN診断: 組立後の剛性マトリクス検査
            // NaNDiagnostics.CheckMatrixDiag(matrixKAA, $"KAA_{(isTan ? "tan" : "sec")} (post-assembly)", this);

            if (isTan)
            {
                KAA_tan = matrixKAA;
                KBA_tan = matrixKBA;
                KAB_tan = matrixKAB;
                KBB_tan = matrixKBB;
            }
            else
            {
                KAA_sec = matrixKAA;
                KBA_sec = matrixKBA;
                KAB_sec = matrixKAB;
                KBB_sec = matrixKBB;
            }
        }

        /// <summary>
        /// 剛性マトリクスの安定性を検証する。
        /// 対角チェック（常時）と固有値チェック（オプション）の2段階。
        /// 不安定な場合は InvalidOperationException をスローする。
        /// </summary>
        public void ValidateStability(bool useEigenvalueCheck = false)
        {
            // 1. 対角チェック（MapOnKmat で既に計算済み）
            // ActionPointのUz/Rx/Ryは水平解析では常にゼロ剛性（正常）なので除外
            var expectedZeroDofs = new HashSet<string> { "ActionPoint:Uz", "ActionPoint:Rx", "ActionPoint:Ry" };
            var problematicDofs = ZeroDiagDofs
                .Where(d => !expectedZeroDofs.Contains(d.nodeName))
                .ToList();

            if (problematicDofs.Count > 0)
            {
                var details = problematicDofs
                    .Select(d => $"  {d.nodeName} (対角値={d.val:E2})")
                    .Take(20);
                var msg = $"剛性マトリクスにゼロ/負の対角成分が{problematicDofs.Count}個検出されました。\n" +
                          $"モデルが不安定です（剛体移動が拘束されていない自由度があります）。\n\n" +
                          $"問題のある自由度:\n{string.Join("\n", details)}";
                if (problematicDofs.Count > 20)
                    msg += $"\n  ...他{problematicDofs.Count - 20}件";
                throw new InvalidOperationException(msg);
            }

            // 2. 固有値チェック（オプション: CountFree ≤ 2000 の場合のみ実行）
            if (useEigenvalueCheck && CountFree > 0 && CountFree <= 2000 && KAA_tan != null)
            {
                var dense = Matrix<double>.Build.DenseOfMatrix(KAA_tan);
                var evd = dense.Evd();
                var eigenvalues = evd.EigenValues.Real();
                double minEig = eigenvalues.Minimum();

                if (minEig <= 0.0)
                {
                    int negCount = eigenvalues.Count(e => e <= 0.0);
                    throw new InvalidOperationException(
                        $"固有値解析により不安定と判定されました。\n" +
                        $"最小固有値: {minEig:E3}\n" +
                        $"非正固有値の数: {negCount}\n" +
                        $"（不足拘束 = {negCount} 自由度分の剛体モードが存在します）");
                }
            }
        }

        // 荷重ベクトルFのマップオン
        public void MapOnVectorF()
        {
            MapOnLoadsOnVector(false);
        }

        // 荷重増分ベクトルDFのマップオン
        public void MapOnVectorDF()
        {
            MapOnLoadsOnVector(true);
        }

        // Node.dLoad、Node.Loadの全体荷重ベクトルAnamodel.dF、Anamodel.Fのへのマップオン
        private void MapOnLoadsOnVector(bool isVectorDF)
        {
            int countFree = CountFree;
            if (isVectorDF == false) //"F"
            {
                VectorF = Vector<double>.Build.Sparse(countFree, 0.0); // 初期化
                foreach (var node in Nodes)
                {
                    if (node.IsLoaded == true)
                    {
                        VectorF = node.MapCumulativeLoadOnGlobalLoad(VectorF);
                    }
                }
            }
            else  //"dF"
            {
                VectorDF = Vector<double>.Build.Sparse(countFree, 0.0); // 初期化
                foreach (var node in Nodes)
                {
                    if (node.IsLoaded == true)
                    {
                        VectorDF = node.MapIncrementalLoadOnGlobalLoad(VectorDF);
                    }
                }
            }
        }

        // 強制変位のマップオン
        // v28 F-new 最適化 (2026-04-23): K の全非ゼロを 1 パスで走査し、その場で R 更新 +
        // 強制 DOF 行/列を除外した新 K を COO 形式で構築する。
        //   従来: for i=0..CountFree × for 強制 DOF × sparse indexed access で O(N_forced × N)
        //          かつ matrixK[i,j]=0 の sparse set もスロット更新で低速。1.9 秒/iter。
        //   今回: EnumerateIndexed 1 パス (O(nnz)) で完結。数十 ms/iter に期待。
        public (Matrix<double>, Vector<double>) GetForcedDispOnLoadVectorAndStiffnessMatrix(bool Istan)
        {
            var orig = Istan ? KAA_tan : KAA_sec;
            var vecOrig = VectorR ?? throw new InvalidOperationException("VectorR is null");

            if (orig == null) throw new InvalidOperationException("Stiffness matrix is not initialized.");
            if (orig.RowCount != CountFree) throw new InvalidOperationException("Matrix size mismatch.");

            var vectorR = vecOrig.Clone();

            // 強制変位 DOF を収集 (eq → u_forced)
            var forcedUByEq = new Dictionary<int, double>();
            foreach (var node in Nodes)
            {
                if (!node.IsForcedDisped) continue;
                NodeDisp forcedDisp = node.CumulativeForcedDisp - node.CumulativeDisp;
                for (int k = 0; k < 2; k++)
                {
                    int eq = node.EquationNumber[k];
                    if (eq >= 0 && eq < CountFree)
                        forcedUByEq[eq] = forcedDisp.GetByIndex(k);
                }
            }

            // 強制変位なし → K は clone だけで良い (fast path)
            if (forcedUByEq.Count == 0)
            {
                Matrix<double> kClone;
                if (orig.GetType().Name.Contains("Sparse"))
                    kClone = orig.Clone();
                else
                {
                    kClone = Matrix<double>.Build.Sparse(CountFree, CountFree);
                    foreach (var entry in orig.EnumerateIndexed(Zeros.AllowSkip))
                        kClone[entry.Item1, entry.Item2] = entry.Item3;
                }
                return (kClone, vectorR);
            }

            // 強制 DOF あり: 1 パスで R 更新 + フィルタ済み新 K の COO を構築
            int nnzEstimate = 1024;
            var rowList = new List<int>(nnzEstimate);
            var colList = new List<int>(nnzEstimate);
            var valList = new List<double>(nnzEstimate);

            foreach (var entry in orig.EnumerateIndexed(Zeros.AllowSkip))
            {
                int i = entry.Item1;
                int j = entry.Item2;
                double v = entry.Item3;
                if (v == 0.0) continue;

                bool iForced = forcedUByEq.ContainsKey(i);
                bool jForced = forcedUByEq.ContainsKey(j);

                // R[i] -= K[i, j] × u_forced (j が強制、i は強制でない場合のみ意味あり。
                // i が強制なら後段で R[eq]=u に上書きされる)
                if (jForced && !iForced)
                {
                    vectorR[i] -= v * forcedUByEq[j];
                }

                // 強制 DOF の行・列でない成分だけ新 K に転記
                if (!iForced && !jForced)
                {
                    rowList.Add(i);
                    colList.Add(j);
                    valList.Add(v);
                }
            }

            // vectorR[eq] = u_forced (強制 DOF の最終値)
            foreach (var kv in forcedUByEq)
            {
                vectorR[kv.Key] = kv.Value;
            }

            // 強制 DOF 対角 1.0 を追加
            foreach (var eq in forcedUByEq.Keys)
            {
                rowList.Add(eq);
                colList.Add(eq);
                valList.Add(1.0);
            }

            // 新 K を一括構築
            var newK = Matrix<double>.Build.Sparse(CountFree, CountFree);
            for (int idx = 0; idx < rowList.Count; idx++)
            {
                newK[rowList[idx], colList[idx]] = valList[idx];
            }

            return (newK, vectorR);
        }

        // 残余力の初期値を得るメソッド
        public void InitializeVectorR()
        {
            VectorR = Vector<double>.Build.Sparse(CountFree, 0.0);
        }

        // ||R||**2/||Fint||**2の初期値を得るメソッド
        public void InitializeNormsqR_onNormsqFint()
        {
            NormsROnNormsFint = 999;
        }

        // 増分変位ベクトルのセットメソッド
        public void SetDispVector(Vector<double> incrementalDispVector)
        {
            VectorDD = incrementalDispVector; // 増分変位
            VectorD += incrementalDispVector; // 累積変位
        }

        // ラインサーチ用: 累積変位と増分変位を直接設定
        public void SetDispVectorDirect(Vector<double> cumulativeDispVector, Vector<double> incrementalDispVector)
        {
            VectorD = cumulativeDispVector.Clone();
            VectorDD = incrementalDispVector.Clone();
        }

        // v15: 変位予測器用: 変位増分を累積変位に加算
        public void ApplyDispIncrement(Vector<double> dispIncrement)
        {
            if (VectorD == null || dispIncrement == null) return;
            VectorD += dispIncrement;
        }

        // 内力ベクトルの計算メソッド
        // Newton-Raphson法: 各要素の構成則から計算された内力を全体ベクトルに組み立て
        public void SetT()
        {
            // VectorTをゼロで初期化
            VectorT = Vector<double>.Build.Sparse(CountFree, 0.0);

            // 梁要素の内力を組み立て
            foreach (var beam in Beams)
            {
                // beam.CumulativeForce は SetBeamDispAndForce() で計算済み（要素座標系）
                AssembleBeamForceToGlobal(beam);
            }

            // 水平地盤ばねの内力を組み立て（全体座標系で定義されている）
            foreach (var spring in HorizontalSoilSprings)
            {
                AssembleSpringForceToGlobal(spring.NodeI, spring.NodeJ, spring.CumulativeForce);
            }

            // 回転ばねの内力を組み立て（co-located ノード前提で T=I として直接アセンブリ）
            if (RotationalSprings != null)
            {
                foreach (var rs in RotationalSprings)
                {
                    rs.AssembleInternalForceToGlobal(VectorT);
                }
            }

            // ペナルティばねの内力を組み立て（全体座標系で定義されている）
            if (PenaltySprings != null)
            {
                foreach (var ps in PenaltySprings)
                {
                    AssembleSpringForceToGlobal(ps.NodeI, ps.NodeJ, ps.CumulativeForce);
                }
            }

            // NaN診断: 内力ベクトル検査
            // NaNDiagnostics.CheckVector(VectorT, "VectorT (post-SetT)", this);
        }

        // 梁要素の内力を全体ベクトルに組み立て（ResolvedDofMap scatter 方式）
        private void AssembleBeamForceToGlobal(Beam beam)
        {
            // 要素内力（要素座標系）
            Vector<double> f_local = beam.CumulativeForce.GetVector();

            // 要素座標系→全体座標系への変換: f_global = T_coord^T * f_local
            Matrix<double> coordTransform = beam.GetCachedCoordTransform();
            Vector<double> f_global = coordTransform.Transpose() * f_local;

            // ResolvedDofMap scatter 方式で力を分配
            // TransferMatrix^T × f の代わりに、各DOFの ResolvedDofMap terms で scatter
            ScatterForceToGlobal(f_global, beam.NodeI, beam.NodeJ);
        }

        // ばね要素の内力を全体ベクトルに組み立て（ResolvedDofMap scatter 方式）
        private void AssembleSpringForceToGlobal(Node nodeI, Node nodeJ, BeamForce cumulativeForce)
        {
            Vector<double> f = cumulativeForce.GetVector();
            ScatterForceToGlobal(f, nodeI, nodeJ);
        }

        /// <summary>
        /// 12成分の力ベクトルを ResolvedDofMap で全体内力ベクトルに分配する。
        /// TransferMatrix^T × f の一般化。各DOFの全termsに coeff を乗じて分配。
        /// </summary>
        private void ScatterForceToGlobal(Vector<double> f, Node nodeI, Node nodeJ)
        {
            bool useResolvedMap = nodeI?.ResolvedDofMap != null && nodeJ?.ResolvedDofMap != null;
            if (useResolvedMap)
            {
                for (int i = 0; i < 12; i++)
                {
                    double fval = f[i];
                    if (fval == 0.0) continue;
                    int dof = i % 6;
                    var node = i < 6 ? nodeI : nodeJ;
                    var terms = node.ResolvedDofMap[dof];
                    if (terms == null) continue;
                    foreach (var term in terms)
                    {
                        if (term.Eq >= 0)
                            VectorT[term.Eq] += term.Coeff * fval;
                    }
                }
            }
            else
            {
                // フォールバック: 従来の TransferMatrix^T × f + GetEquationNumbers
                Vector<double> f_transformed;
                if (nodeI.HasMasterSlave || nodeJ.HasMasterSlave)
                {
                    var slaveTransform = Matrix<double>.Build.DenseIdentity(12);
                    for (int r = 0; r < 6; r++)
                        for (int c = 0; c < 6; c++)
                        {
                            slaveTransform[r, c] = nodeI.TransferMatrix[r, c];
                            slaveTransform[r + 6, c + 6] = nodeJ.TransferMatrix[r, c];
                        }
                    f_transformed = slaveTransform.Transpose() * f;
                }
                else
                {
                    f_transformed = f;
                }
                var eq = Utils.GetEquationNumbers(nodeI, nodeJ);
                for (int i = 0; i < 12; i++)
                {
                    if (eq[i] >= 0)
                        VectorT[eq[i]] += f_transformed[i];
                }
            }
        }

        // 残余力ベクトルの初期値のセット
        public void SetR()
        {
            VectorR += VectorDF;
        }

        // 残余力ベクトルの更新
        private int _findRCallCount = 0;
        public void FindR()
        {
            VectorR.Clear();
            for (int i = 0; i < CountFree; i++)
            {
                if (VectorDOFForcedDisp[i] == false)
                {
                    VectorR[i] = VectorF[i] - VectorT[i];
                }
                else // 強制変位の自由度は残余力をゼロにする
                {
                    VectorR[i] = 0.0;
                }
            }

            double normsqR = VectorR.L2Norm() * VectorR.L2Norm();
            double normsqF = VectorF.L2Norm() * VectorF.L2Norm();
            NormsROnNormsFint = normsqF > 1e-30 ? normsqR / normsqF : (normsqR > 1e-30 ? 1e30 : 0.0);

            // NaN診断
            // NaNDiagnostics.DiagnoseResidual(VectorF, VectorT, VectorR, NormsROnNormsFint);

            // 残差の大きいDOFを診断出力（最初3回 + 10回おき）
            _findRCallCount++;
            if (NormsROnNormsFint > 1e-4 && (_findRCallCount <= 3 || _findRCallCount % 10 == 0))
            {
                string[] dofNames = { "Ux", "Uy", "Uz", "Rx", "Ry", "Rz" };
                var log = new System.Text.StringBuilder();
                log.AppendLine($"=== FindR 診断 (呼び出し#{_findRCallCount}) ===");
                log.AppendLine($"||R||²/||F||² = {NormsROnNormsFint:E3}");
                log.AppendLine($"||F|| = {VectorF.L2Norm():E3}, ||T|| = {VectorT.L2Norm():E3}, ||R|| = {VectorR.L2Norm():E3}");

                // 残差の大きいDOFトップ10
                var residuals = new List<(int eq, double r, double f, double t)>();
                for (int i = 0; i < CountFree; i++)
                {
                    if (!VectorDOFForcedDisp[i])
                        residuals.Add((i, VectorR[i], VectorF[i], VectorT[i]));
                }
                residuals.Sort((a, b) => Math.Abs(b.r).CompareTo(Math.Abs(a.r)));

                log.AppendLine("残差の大きいDOF (top 15):");
                log.AppendLine("  eq    | R(=F-T)     | F           | T           | K_diag       | Node:DOF (master)");
                foreach (var (eq, r, f, t) in residuals.Take(15))
                {
                    string nodeDof = "?";
                    string masterInfo = "";
                    foreach (var node in Nodes)
                    {
                        for (int d = 0; d < 6; d++)
                        {
                            if (node.EquationNumber[d] == eq)
                            {
                                nodeDof = $"{node.Name}:{dofNames[d]}";
                                break;
                            }
                            // slave DOF の master eq 番号もチェック
                            if (node.MasterNodes[d] != null && node.MasterNodes[d].EquationNumber[d] == eq)
                            {
                                masterInfo += $" ←slave:{node.Name}:{dofNames[d]}";
                            }
                        }
                        if (nodeDof != "?") break;
                    }
                    double kDiag = (KAA_tan != null && eq < KAA_tan.RowCount) ? KAA_tan[eq, eq] : 0;
                    log.AppendLine($"  {eq,5} | {r,11:E3} | {f,11:E3} | {t,11:E3} | {kDiag,12:E3} | {nodeDof}{masterInfo}");
                }

                // F≠0のDOF数とT≠0のDOF数
                int fNonZero = 0, tNonZero = 0;
                for (int i = 0; i < CountFree; i++)
                {
                    if (Math.Abs(VectorF[i]) > NumericalConstants.NEAR_ZERO_EPSILON) fNonZero++;
                    if (Math.Abs(VectorT[i]) > NumericalConstants.NEAR_ZERO_EPSILON) tNonZero++;
                }
                log.AppendLine($"F≠0のDOF数: {fNonZero}, T≠0のDOF数: {tNonZero}, 全自由度: {CountFree}");

                // --- 残差トップDOFの内力分解（要素別寄与） ---
                log.AppendLine("\n--- 内力分解 (残差トップ5 DOF) ---");
                foreach (var (eq, r, f, t) in residuals.Take(5))
                {
                    if (Math.Abs(r) < 1e-6) continue;

                    // DOF特定
                    string nodeDof2 = "?";
                    Node targetNode = null;
                    int targetDof = -1;
                    foreach (var node in Nodes)
                    {
                        for (int d2 = 0; d2 < 6; d2++)
                        {
                            if (node.EquationNumber[d2] == eq)
                            {
                                nodeDof2 = $"{node.Name}:{dofNames[d2]}";
                                targetNode = node;
                                targetDof = d2;
                                break;
                            }
                        }
                        if (targetNode != null) break;
                    }

                    log.AppendLine($"\n  [{nodeDof2}] eq={eq}, R={r:E4}");

                    // Beam寄与: 各beamのT^T×f_localの当該eq成分を再計算
                    double beamTotal = 0;
                    foreach (var beam in Beams)
                    {
                        var eqList = Utils.GetEquationNumbers(beam.NodeI, beam.NodeJ);
                        bool involves = false;
                        for (int k = 0; k < 12; k++) { if (eqList[k] == eq) { involves = true; break; } }
                        if (!involves) continue;

                        Vector<double> f_local = beam.CumulativeForce.GetVector();
                        Matrix<double> coordT = beam.GetCachedCoordTransform();
                        Vector<double> f_global = coordT.Transpose() * f_local;

                        Vector<double> f_trans;
                        if (beam.NodeI.HasMasterSlave || beam.NodeJ.HasMasterSlave)
                        {
                            var slT = Matrix<double>.Build.DenseIdentity(12);
                            for (int rr = 0; rr < 6; rr++)
                                for (int cc = 0; cc < 6; cc++)
                                {
                                    slT[rr, cc] = beam.NodeI.TransferMatrix[rr, cc];
                                    slT[rr + 6, cc + 6] = beam.NodeJ.TransferMatrix[rr, cc];
                                }
                            f_trans = slT.Transpose() * f_global;
                        }
                        else
                        {
                            f_trans = f_global;
                        }

                        double contrib = 0;
                        for (int k = 0; k < 12; k++)
                        {
                            if (eqList[k] == eq) contrib += f_trans[k];
                        }
                        if (Math.Abs(contrib) > NumericalConstants.NEAR_ZERO_EPSILON)
                        {
                            log.AppendLine($"    Beam[{beam.Name}]: T→{contrib:E4}");
                            beamTotal += contrib;
                        }
                    }

                    // RotationalSpring寄与
                    double rsTotal = 0;
                    if (RotationalSprings != null)
                    {
                        foreach (var rs in RotationalSprings)
                        {
                            var rsF = rs.CumulativeForce.GetVector();
                            var rsEq = new List<int>(12);
                            for (int d2 = 0; d2 < 6; d2++)
                            {
                                var m = rs.NodeI.MasterNodes[d2];
                                rsEq.Add(m != null ? m.EquationNumber[d2] : rs.NodeI.EquationNumber[d2]);
                            }
                            for (int d2 = 0; d2 < 6; d2++)
                            {
                                var m = rs.NodeJ.MasterNodes[d2];
                                rsEq.Add(m != null ? m.EquationNumber[d2] : rs.NodeJ.EquationNumber[d2]);
                            }

                            double contrib = 0;
                            for (int k = 0; k < 12; k++)
                            {
                                if (rsEq[k] == eq) contrib += rsF[k];
                            }
                            if (Math.Abs(contrib) > NumericalConstants.NEAR_ZERO_EPSILON)
                            {
                                // M-θ状態も出力
                                double dRx = (rs.NodeJ.CumulativeDisp?.Rx ?? 0) - (rs.NodeI.CumulativeDisp?.Rx ?? 0);
                                double dRy = (rs.NodeJ.CumulativeDisp?.Ry ?? 0) - (rs.NodeI.CumulativeDisp?.Ry ?? 0);
                                double theta = Math.Sqrt(dRx * dRx + dRy * dRy);
                                double kSec = rs.KeSec?[10, 10] ?? 0;  // NodeJ Ry diagonal
                                double kTan = rs.KeTan?[10, 10] ?? 0;
                                log.AppendLine($"    RotSpring[{rs.Name}]: T→{contrib:E4}  θ={theta:E4} dRx={dRx:E4} dRy={dRy:E4} kSec={kSec:E3} kTan={kTan:E3}");
                                rsTotal += contrib;
                            }
                        }
                    }

                    // HorizontalSoilSpring寄与
                    double hsTotal = 0;
                    foreach (var hs in HorizontalSoilSprings)
                    {
                        var hsF = hs.CumulativeForce.GetVector();
                        var hsEq = Utils.GetEquationNumbers(hs.NodeI, hs.NodeJ);
                        double contrib = 0;
                        for (int k = 0; k < 12; k++)
                        {
                            if (hsEq[k] == eq) contrib += hsF[k];
                        }
                        if (Math.Abs(contrib) > NumericalConstants.NEAR_ZERO_EPSILON) hsTotal += contrib;
                    }

                    log.AppendLine($"    合計: Beam={beamTotal:E4}, RotSpring={rsTotal:E4}, SoilSpring={hsTotal:E4}, Sum={beamTotal + rsTotal + hsTotal:E4} vs T={t:E4}");
                }

                log.AppendLine("=== FindR 診断終了 ===");
                System.Diagnostics.Debug.WriteLine(log.ToString());
            }
        }

        public void InitializeStates()
        {
            foreach (var node in Nodes)
            {
                node.InitializeDisp();  // set zero
                node.InitializeReact();  // set zero
                node.InitializeSpringForce(); // set zero
                node.InitializeSoilDisp(); // set zero
            }

            foreach (var beam in Beams)
            {
                beam.InitializeForce();  // 慣性力、杭軸力のセット set zero
                beam.InitializeDisp();  // set zero
            }

            InitializeVectorF();  // 荷重ベクトル set zero
            InitializeVectorD();  // 変位ベクトル set zero
            InitializeVectorR();  // 残余力の初期値 R = 0

            InitializeAxialForces(); // 杭軸力を長期荷重に設定
        }

        public void InitializeVectorF()
        {
            VectorF = Vector<double>.Build.Sparse(CountFree, 0.0); // 荷重ベクトル
            VectorDF = Vector<double>.Build.Sparse(CountFree, 0.0); // 内力ベクトル
        }

        public void InitializeVectorD()
        {
            VectorD = Vector<double>.Build.Sparse(CountFree, 0.0); // 変位ベクトル
            VectorDD = Vector<double>.Build.Sparse(CountFree, 0.0); // 変位ベクトル
        }

        // 常時荷重のセット
        // E3b: CaseLocalSnapshot 経由で書込。主モデルでは InputModel.pileLayoutItem.AxialForce を
        // 直接更新 (従来挙動)、case-local コピーでは snapshot.AxialForces[pli] を更新する。
        private void InitializeAxialForces()  //PileDesign
        {
            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                double baselineAxial = pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional; // レベル1の杭軸力増分
                SetAxialForce(pileLayoutItem, baselineAxial);
            }
        }

        // 節点名、座標(x, y, z)で節点を検索
        public Node? FindNode(
            string name,
            double? x = null,
            double? y = null,
            double? z = null,
            double tolerance = 1e-8)
        {
            foreach (var node in Nodes)
            {
                if (node.Name != name)
                    continue;
                if (x.HasValue && Math.Abs(node.Coord.X - x.Value) >= tolerance)
                    continue;
                if (y.HasValue && Math.Abs(node.Coord.Y - y.Value) >= tolerance)
                    continue;
                if (z.HasValue && Math.Abs(node.Coord.Z - z.Value) >= tolerance)
                    continue;
                return node;
            }
            return null;
        }

        public AnalysisStepResult? GetAnalysisLastStepResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction)
        {
            // 名前ベースで比較（参照比較の代わりに）
            var result = AnalysisStepResults
                .Where(r => r.LoadCase?.LoadName == loadCase?.LoadName &&
                            r.LoadCombination?.Name == loadCombination?.Name &&
                            r.IsLiquefaction == isLiquefaction)
                .OrderByDescending(r => r.Step)
                .FirstOrDefault();

            if (result != null) return result;

            // フォールバック: 逆の液状化状態で検索
            return AnalysisStepResults
                .Where(r => r.LoadCase?.LoadName == loadCase?.LoadName &&
                            r.LoadCombination?.Name == loadCombination?.Name &&
                            r.IsLiquefaction == !isLiquefaction)
                .OrderByDescending(r => r.Step)
                .FirstOrDefault();
        }

        public int GetAnalysisLastStep(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction)
        {
            // 名前ベースで比較（参照比較の代わりに）
            var results = AnalysisStepResults
                .Where(r => r.LoadCase?.LoadName == loadCase?.LoadName &&
                            r.LoadCombination?.Name == loadCombination?.Name &&
                            r.IsLiquefaction == isLiquefaction)
                .Select(r => r.Step)
                .ToList();

            if (results.Count > 0)
                return results.Max();

            // フォールバック: 逆の液状化状態で検索
            var fallback = AnalysisStepResults
                .Where(r => r.LoadCase?.LoadName == loadCase?.LoadName &&
                            r.LoadCombination?.Name == loadCombination?.Name &&
                            r.IsLiquefaction == !isLiquefaction)
                .Select(r => r.Step)
                .ToList();

            return fallback.DefaultIfEmpty(-999).Max();
        }

        // 荷重ベクトルの更新メソッド
        public void UpdateVectorF()
        {
            VectorF += VectorDF;
        }

        // 強制変位の荷重ベクトルと剛性マトリクスをセットするメソッド
        public void SetForcedDispOnLoadVectorAndStiffnessMatrix(bool isTan)
        {
            (KAA_tan, VectorR) = GetForcedDispOnLoadVectorAndStiffnessMatrix(isTan); // KAA_tanとVectorRを取得
        }

        // 強制変位の更新メソッド
        public void UpdateVectorDOFForcedDisp()
        {
            VectorDOFForcedDisp = [.. Nodes
                .SelectMany(node => Enumerable.Range(0, 6)
                    .Where(index => !node.GetBoundary(index))
                    .Select(_ => node.IsForcedDisped))];
        }

        public AnaModel DeepCopy()
        {
            // E1 (2026-04-23): ケース並列化の前提条件として、以下の不変量を保証する。
            //   - copy.Nodes 上の Node インスタンスと、Beam.NodeI/J / RigidBody.MasterNode & SlaveNodes /
            //     各 TwoNodeSpringElement.NodeI/J / Node.MasterNodes[] が「同一参照」であること
            //   従来の DeepCopy は各要素が独自に Node を DeepCopy していたため、copy.Nodes と
            //   copy.Beams[i].NodeI が別インスタンスになり、並列ワーカーで状態が分岐する。
            //   解決策: 先に Node 一覧を DeepCopy してマップを構築し、他要素の Node 参照を
            //   マップ経由で fixup する。

            // ---- Step 1: Node コピーと参照マップ構築 ----
            var nodeMap = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
            var nodes = new List<Node>(this.Nodes.Count);
            foreach (var n in this.Nodes)
            {
                var nc = n.DeepCopy();
                nodeMap[n] = nc;
                nodes.Add(nc);
            }

            // ---- Step 2: コピー済 Node の MasterNodes[] を新 Node 群へ fixup ----
            // (Node.DeepCopy は MasterNodes 配列をシャローコピーし元インスタンスを指したままになる)
            foreach (var n in this.Nodes)
            {
                var nc = nodeMap[n];
                for (int d = 0; d < 6; d++)
                {
                    var oldMaster = n.MasterNodes[d];
                    if (oldMaster != null && nodeMap.TryGetValue(oldMaster, out var newMaster))
                        nc.MasterNodes[d] = newMaster;
                }
            }

            // ---- Step 3: 要素コピー + NodeI/J 参照 fixup ----
            Node MapNode(Node n) => (n != null && nodeMap.TryGetValue(n, out var nn)) ? nn : n;

            var beams = new List<Beam>(this.Beams.Count);
            foreach (var b in this.Beams)
            {
                var bc = b.DeepCopy();
                bc.NodeI = MapNode(b.NodeI);
                bc.NodeJ = MapNode(b.NodeJ);
                beams.Add(bc);
            }

            // DummyBeam: NodeI/J は get only なので、DeepCopy を回避してコンストラクタに new Node を渡す
            var dummyBeams = new List<DummyBeam>(this.DummyBeams.Count);
            foreach (var db in this.DummyBeams)
            {
                var dbc = new DummyBeam(db.Name, MapNode(db.NodeI), MapNode(db.NodeJ))
                {
                    Length = db.Length,
                    DummyBeamResults = new System.Collections.ObjectModel.ObservableCollection<DummyBeamResult>(
                        db.DummyBeamResults.Select(r => r.DeepCopy()))
                };
                dummyBeams.Add(dbc);
            }

            var rigidBodies = new List<RigidBody>(this.RigidBodies.Count);
            foreach (var rb in this.RigidBodies)
            {
                var rbc = rb.DeepCopy();
                rbc.MasterNode = MapNode(rb.MasterNode);
                rbc.SlaveNodes = rb.SlaveNodes.Select(MapNode).ToList();
                rigidBodies.Add(rbc);
            }

            var horizontalSoilSprings = new List<HorizontalSoilSpring>(this.HorizontalSoilSprings.Count);
            foreach (var s in this.HorizontalSoilSprings)
            {
                var sc = s.DeepCopy();
                sc.NodeI = MapNode(s.NodeI);
                sc.NodeJ = MapNode(s.NodeJ);
                horizontalSoilSprings.Add(sc);
            }

            var rotationalSprings = new List<RotationalSpring>(this.RotationalSprings.Count);
            foreach (var rs in this.RotationalSprings)
            {
                var rsc = rs.DeepCopy();
                rsc.NodeI = MapNode(rs.NodeI);
                rsc.NodeJ = MapNode(rs.NodeJ);
                rotationalSprings.Add(rsc);
            }

            // ここでコンストラクタを呼ぶ時点で MasterNodes は新 Node 群を指しているため、
            // ResolveConstraintChains が正しい ResolvedDofMap を構築する
            var copy = new AnaModel(_inputModel, nodes, beams, dummyBeams, rigidBodies, horizontalSoilSprings, rotationalSprings);

            // PenaltySprings
            if (this.PenaltySprings != null)
            {
                var psList = new List<HorizontalSoilSpring>(this.PenaltySprings.Count);
                foreach (var p in this.PenaltySprings)
                {
                    var pc = p.DeepCopy();
                    pc.NodeI = MapNode(p.NodeI);
                    pc.NodeJ = MapNode(p.NodeJ);
                    psList.Add(pc);
                }
                copy.PenaltySprings = psList;
            }

            // AnalysisStepResults
            foreach (var result in this.AnalysisStepResults)
                copy.AnalysisStepResults.Add(result.DeepCopy());

            // E1: NR state ベクトル類を明示 clone (ケース間で前ケースの state が残らないよう
            // InitializeVectorF/D でゼロ化されるが、デバッグ時の再現性のため現状をコピー)
            if (this.VectorF != null) copy.VectorF = this.VectorF.Clone();
            if (this.VectorDF != null) copy.VectorDF = this.VectorDF.Clone();
            if (this.VectorD != null) copy.VectorD = this.VectorD.Clone();
            if (this.VectorDD != null) copy.VectorDD = this.VectorDD.Clone();
            if (this.VectorT != null) copy.VectorT = this.VectorT.Clone();
            if (this.VectorR != null) copy.VectorR = this.VectorR.Clone();

            // ---- Step 6: Case-local snapshot を構築 (E3a/E3b) ----
            // InputModel.PileLayoutItems / DoatsuGoryokuBane は main と共有するため、
            // そこに格納されている Node / Spring / Beam 参照 (元インスタンス) を new インスタンス群へ
            // 付け替えるスナップショットを caseModel 専用に持たせる。
            var snap = new CaseLocalSnapshot();

            // Spring / Beam の reference → reference マップを構築 (Node と同様の fixup)
            var hsSpringMap = new Dictionary<HorizontalSoilSpring, HorizontalSoilSpring>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < this.HorizontalSoilSprings.Count; i++)
                hsSpringMap[this.HorizontalSoilSprings[i]] = horizontalSoilSprings[i];
            if (this.PenaltySprings != null && copy.PenaltySprings != null)
            {
                int n = Math.Min(this.PenaltySprings.Count, copy.PenaltySprings.Count);
                for (int i = 0; i < n; i++)
                    hsSpringMap[this.PenaltySprings[i]] = copy.PenaltySprings[i];
            }
            var rsSpringMap = new Dictionary<RotationalSpring, RotationalSpring>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < this.RotationalSprings.Count; i++)
                rsSpringMap[this.RotationalSprings[i]] = rotationalSprings[i];
            var beamMap = new Dictionary<Beam, Beam>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < this.Beams.Count; i++)
                beamMap[this.Beams[i]] = beams[i];

            if (_inputModel?.PileLayoutItems != null)
            {
                foreach (var pli in _inputModel.PileLayoutItems)
                {
                    if (pli == null) continue;
                    var pileNodeList = new List<Node>(pli.PileNodes?.Count ?? 0);
                    if (pli.PileNodes != null)
                    {
                        foreach (var n in pli.PileNodes)
                            pileNodeList.Add(n != null && nodeMap.TryGetValue(n, out var nn) ? nn : n);
                    }
                    snap.PileNodes[pli] = pileNodeList;

                    var soilNodeList = new List<Node>(pli.SoilNodes?.Count ?? 0);
                    if (pli.SoilNodes != null)
                    {
                        foreach (var n in pli.SoilNodes)
                            soilNodeList.Add(n != null && nodeMap.TryGetValue(n, out var nn) ? nn : n);
                    }
                    snap.SoilNodes[pli] = soilNodeList;

                    snap.AxialForces[pli] = pli.AxialForce;
                    snap.AxialForceIncrements[pli] = pli.AxialForceIncrement;

                    // PileTopRotationalSpring
                    if (pli.PileTopRotationalSpring != null
                        && rsSpringMap.TryGetValue(pli.PileTopRotationalSpring, out var newRs))
                        snap.PileTopRotationalSprings[pli] = newRs;
                    else
                        snap.PileTopRotationalSprings[pli] = pli.PileTopRotationalSpring;

                    // HorizontalSoilSprings
                    var hsList = new List<HorizontalSoilSpring>(pli.HorizontalSoilSprings?.Count ?? 0);
                    if (pli.HorizontalSoilSprings != null)
                    {
                        foreach (var s in pli.HorizontalSoilSprings)
                            hsList.Add(s != null && hsSpringMap.TryGetValue(s, out var nn) ? nn : s);
                    }
                    snap.PileHorizontalSoilSprings[pli] = hsList;

                    // Beams
                    var bList = new List<Beam>(pli.Beams?.Count ?? 0);
                    if (pli.Beams != null)
                    {
                        foreach (var b in pli.Beams)
                            bList.Add(b != null && beamMap.TryGetValue(b, out var nn) ? nn : b);
                    }
                    snap.PileBeams[pli] = bList;
                }
            }
            var dgb = _inputModel?.ElementDivision?.DoatsuGoryokuBane;
            if (dgb?.Items != null)
            {
                foreach (var item in dgb.Items)
                {
                    if (item == null) continue;
                    if (item.TopSoilNode != null && nodeMap.TryGetValue(item.TopSoilNode, out var tsn))
                        snap.DoatsuTopSoilNodes[item] = tsn;
                    if (item.BtmSoilNode != null && nodeMap.TryGetValue(item.BtmSoilNode, out var bsn))
                        snap.DoatsuBtmSoilNodes[item] = bsn;
                    if (item.TopEmbedmentNode != null && nodeMap.TryGetValue(item.TopEmbedmentNode, out var ten))
                        snap.DoatsuTopEmbedmentNodes[item] = ten;
                    if (item.BtmEmbedmentNode != null && nodeMap.TryGetValue(item.BtmEmbedmentNode, out var ben))
                        snap.DoatsuBtmEmbedmentNodes[item] = ben;
                    if (item.TopHorizontalSoilSpring != null && hsSpringMap.TryGetValue(item.TopHorizontalSoilSpring, out var ths))
                        snap.DoatsuTopHorizontalSoilSprings[item] = ths;
                    if (item.BtmHorizontalSoilSpring != null && hsSpringMap.TryGetValue(item.BtmHorizontalSoilSpring, out var bhs))
                        snap.DoatsuBtmHorizontalSoilSprings[item] = bhs;
                }
            }
            copy.CaseLocalState = snap;

            return copy;
        }

        // ---- Case-local アクセサ (E3a) ----
        // 主モデルでは InputModel を直接返す (従来挙動)、case-local コピーでは snapshot を返す。
        // E3b で UpdateSoilDisp / PrepareKmat / UpdateAxialForceFromAnalysis 等をこれらに切替える。

        /// <summary>ケース固有の PileNodes を取得。主モデルでは item.PileNodes、コピーでは caseModel 側 Node 群。</summary>
        public IList<Node> GetPileNodes(PileLayoutDataItem item)
        {
            if (CaseLocalState != null && CaseLocalState.PileNodes.TryGetValue(item, out var list))
                return list;
            return item.PileNodes;
        }

        /// <summary>ケース固有の SoilNodes を取得。</summary>
        public IList<Node> GetSoilNodes(PileLayoutDataItem item)
        {
            if (CaseLocalState != null && CaseLocalState.SoilNodes.TryGetValue(item, out var list))
                return list;
            return item.SoilNodes;
        }

        /// <summary>ケース固有の AxialForce を取得。</summary>
        public double GetAxialForce(PileLayoutDataItem item)
        {
            if (CaseLocalState != null && CaseLocalState.AxialForces.TryGetValue(item, out var v))
                return v;
            return item.AxialForce;
        }

        /// <summary>ケース固有の AxialForce を設定。主モデルでは InputModel 本体を書換える (従来挙動)。</summary>
        public void SetAxialForce(PileLayoutDataItem item, double value)
        {
            if (CaseLocalState != null)
                CaseLocalState.AxialForces[item] = value;
            else
                item.AxialForce = value;
        }

        /// <summary>ケース固有の AxialForceIncrement を取得。</summary>
        public double GetAxialForceIncrement(PileLayoutDataItem item)
        {
            if (CaseLocalState != null && CaseLocalState.AxialForceIncrements.TryGetValue(item, out var v))
                return v;
            return item.AxialForceIncrement;
        }

        /// <summary>ケース固有の AxialForceIncrement を設定。</summary>
        public void SetAxialForceIncrement(PileLayoutDataItem item, double value)
        {
            if (CaseLocalState != null)
                CaseLocalState.AxialForceIncrements[item] = value;
            else
                item.AxialForceIncrement = value;
        }

        /// <summary>DoatsuGoryokuBane Item の TopSoilNode を取得 (case-local 対応)。</summary>
        public Node GetDoatsuTopSoilNode(DoatsuGoryokuBaneItem item)
        {
            if (CaseLocalState != null && CaseLocalState.DoatsuTopSoilNodes.TryGetValue(item, out var n))
                return n;
            return item.TopSoilNode;
        }

        /// <summary>DoatsuGoryokuBane Item の BtmSoilNode を取得 (case-local 対応)。</summary>
        public Node GetDoatsuBtmSoilNode(DoatsuGoryokuBaneItem item)
        {
            if (CaseLocalState != null && CaseLocalState.DoatsuBtmSoilNodes.TryGetValue(item, out var n))
                return n;
            return item.BtmSoilNode;
        }

        /// <summary>DoatsuGoryokuBane Item の TopEmbedmentNode を取得 (case-local 対応)。</summary>
        public Node GetDoatsuTopEmbedmentNode(DoatsuGoryokuBaneItem item)
        {
            if (CaseLocalState != null && CaseLocalState.DoatsuTopEmbedmentNodes.TryGetValue(item, out var n))
                return n;
            return item.TopEmbedmentNode;
        }

        /// <summary>DoatsuGoryokuBane Item の BtmEmbedmentNode を取得 (case-local 対応)。</summary>
        public Node GetDoatsuBtmEmbedmentNode(DoatsuGoryokuBaneItem item)
        {
            if (CaseLocalState != null && CaseLocalState.DoatsuBtmEmbedmentNodes.TryGetValue(item, out var n))
                return n;
            return item.BtmEmbedmentNode;
        }

        /// <summary>ケース固有の PileTopRotationalSpring を取得。</summary>
        public RotationalSpring? GetPileTopRotationalSpring(PileLayoutDataItem item)
        {
            if (CaseLocalState != null && CaseLocalState.PileTopRotationalSprings.TryGetValue(item, out var rs))
                return rs;
            return item.PileTopRotationalSpring;
        }

        /// <summary>ケース固有の HorizontalSoilSprings を取得。</summary>
        public IList<HorizontalSoilSpring> GetPileHorizontalSoilSprings(PileLayoutDataItem item)
        {
            if (CaseLocalState != null && CaseLocalState.PileHorizontalSoilSprings.TryGetValue(item, out var list))
                return list;
            return item.HorizontalSoilSprings;
        }

        /// <summary>ケース固有の Beams を取得。</summary>
        public IList<Beam> GetPileBeams(PileLayoutDataItem item)
        {
            if (CaseLocalState != null && CaseLocalState.PileBeams.TryGetValue(item, out var list))
                return list;
            return item.Beams;
        }

        /// <summary>DoatsuGoryokuBane Item の TopHorizontalSoilSpring を取得。</summary>
        public HorizontalSoilSpring? GetDoatsuTopHorizontalSoilSpring(DoatsuGoryokuBaneItem item)
        {
            if (CaseLocalState != null && CaseLocalState.DoatsuTopHorizontalSoilSprings.TryGetValue(item, out var s))
                return s;
            return item.TopHorizontalSoilSpring;
        }

        /// <summary>DoatsuGoryokuBane Item の BtmHorizontalSoilSpring を取得。</summary>
        public HorizontalSoilSpring? GetDoatsuBtmHorizontalSoilSpring(DoatsuGoryokuBaneItem item)
        {
            if (CaseLocalState != null && CaseLocalState.DoatsuBtmHorizontalSoilSprings.TryGetValue(item, out var s))
                return s;
            return item.BtmHorizontalSoilSpring;
        }

        /// <summary>
        /// E3c 診断 (2026-04-23): DeepCopy 後の caseModel が main (原本) と状態的に等価か検証する。
        /// 等価とは: 対応する Node/Beam/Spring の state (Boundary, EquationNumber, CumulativeDisp,
        /// Section 値, Spring.KeTan 等) が bit-exact に一致すること。
        /// 差分が見つかったら文字列に列挙して返す (空文字列なら OK)。
        /// </summary>
        public static string ValidateDeepCopyEquivalence(AnaModel main, AnaModel copy)
        {
            var diffs = new System.Text.StringBuilder();

            if (main.CountFree != copy.CountFree)
                diffs.AppendLine($"CountFree: {main.CountFree} vs {copy.CountFree}");
            if (main.CountFix != copy.CountFix)
                diffs.AppendLine($"CountFix: {main.CountFix} vs {copy.CountFix}");
            if (main.Nodes.Count != copy.Nodes.Count)
                diffs.AppendLine($"Nodes.Count: {main.Nodes.Count} vs {copy.Nodes.Count}");
            if (main.Beams.Count != copy.Beams.Count)
                diffs.AppendLine($"Beams.Count: {main.Beams.Count} vs {copy.Beams.Count}");
            if (main.HorizontalSoilSprings.Count != copy.HorizontalSoilSprings.Count)
                diffs.AppendLine($"HorizontalSoilSprings.Count: {main.HorizontalSoilSprings.Count} vs {copy.HorizontalSoilSprings.Count}");

            int nodesToCheck = Math.Min(main.Nodes.Count, copy.Nodes.Count);
            for (int i = 0; i < nodesToCheck; i++)
            {
                var mn = main.Nodes[i];
                var cn = copy.Nodes[i];
                if (mn.Name != cn.Name) { diffs.AppendLine($"Nodes[{i}].Name: {mn.Name} vs {cn.Name}"); continue; }
                for (int d = 0; d < 6; d++)
                {
                    if (mn.EquationNumber[d] != cn.EquationNumber[d])
                        diffs.AppendLine($"Nodes[{i}={mn.Name}].EquationNumber[{d}]: {mn.EquationNumber[d]} vs {cn.EquationNumber[d]}");
                    if (mn.GetBoundary(d) != cn.GetBoundary(d))
                        diffs.AppendLine($"Nodes[{i}={mn.Name}].Boundary[{d}]: {mn.GetBoundary(d)} vs {cn.GetBoundary(d)}");
                }
                if (mn.IsForcedDisped != cn.IsForcedDisped)
                    diffs.AppendLine($"Nodes[{i}={mn.Name}].IsForcedDisped: {mn.IsForcedDisped} vs {cn.IsForcedDisped}");
                if (mn.IsLoaded != cn.IsLoaded)
                    diffs.AppendLine($"Nodes[{i}={mn.Name}].IsLoaded: {mn.IsLoaded} vs {cn.IsLoaded}");
                // CumulativeDisp (6 成分)
                for (int d = 0; d < 6; d++)
                {
                    double m = mn.CumulativeDisp?.GetByIndex(d) ?? 0;
                    double c = cn.CumulativeDisp?.GetByIndex(d) ?? 0;
                    if (m != c)
                        diffs.AppendLine($"Nodes[{i}={mn.Name}].CumulativeDisp[{d}]: {m:G17} vs {c:G17}");
                }
                // Master の整合 (名前ベースで比較)
                for (int d = 0; d < 6; d++)
                {
                    var mMaster = mn.MasterNodes[d]?.Name;
                    var cMaster = cn.MasterNodes[d]?.Name;
                    if (mMaster != cMaster)
                        diffs.AppendLine($"Nodes[{i}={mn.Name}].MasterNodes[{d}].Name: {mMaster} vs {cMaster}");
                }
            }

            int beamsToCheck = Math.Min(main.Beams.Count, copy.Beams.Count);
            for (int i = 0; i < beamsToCheck; i++)
            {
                var mb = main.Beams[i];
                var cb = copy.Beams[i];
                if (mb.NodeI?.Name != cb.NodeI?.Name)
                    diffs.AppendLine($"Beams[{i}={mb.Name}].NodeI.Name: {mb.NodeI?.Name} vs {cb.NodeI?.Name}");
                if (mb.NodeJ?.Name != cb.NodeJ?.Name)
                    diffs.AppendLine($"Beams[{i}={mb.Name}].NodeJ.Name: {mb.NodeJ?.Name} vs {cb.NodeJ?.Name}");
                if (mb.KTan_y != cb.KTan_y)
                    diffs.AppendLine($"Beams[{i}={mb.Name}].KTan_y: {mb.KTan_y:G17} vs {cb.KTan_y:G17}");
                if (mb.Ryi_tan != cb.Ryi_tan)
                    diffs.AppendLine($"Beams[{i}={mb.Name}].Ryi_tan: {mb.Ryi_tan:G17} vs {cb.Ryi_tan:G17}");
                if (mb.Section?.IY != cb.Section?.IY)
                    diffs.AppendLine($"Beams[{i}={mb.Name}].Section.IY: {mb.Section?.IY} vs {cb.Section?.IY}");
                // CumulativeForce
                if (mb.CumulativeForce?.Fxi != cb.CumulativeForce?.Fxi)
                    diffs.AppendLine($"Beams[{i}={mb.Name}].CumulativeForce.Fxi: {mb.CumulativeForce?.Fxi:G17} vs {cb.CumulativeForce?.Fxi:G17}");
            }

            int springsToCheck = Math.Min(main.HorizontalSoilSprings.Count, copy.HorizontalSoilSprings.Count);
            for (int i = 0; i < springsToCheck; i++)
            {
                var ms = main.HorizontalSoilSprings[i];
                var cs = copy.HorizontalSoilSprings[i];
                if (ms.NodeI?.Name != cs.NodeI?.Name)
                    diffs.AppendLine($"HS[{i}].NodeI.Name: {ms.NodeI?.Name} vs {cs.NodeI?.Name}");
                // KeTan diagonal
                if (ms.KeTan != null && cs.KeTan != null)
                {
                    for (int d = 0; d < Math.Min(6, ms.KeTan.RowCount); d++)
                    {
                        if (ms.KeTan[d, d] != cs.KeTan[d, d])
                            diffs.AppendLine($"HS[{i}={ms.Name}].KeTan[{d},{d}]: {ms.KeTan[d, d]:G17} vs {cs.KeTan[d, d]:G17}");
                    }
                }
                else if ((ms.KeTan == null) != (cs.KeTan == null))
                    diffs.AppendLine($"HS[{i}={ms.Name}].KeTan null mismatch: main={ms.KeTan == null} copy={cs.KeTan == null}");
            }

            return diffs.ToString();
        }

        /// <summary>
        /// E2 (2026-04-23): ケース並列化 E3 で使用する結果マージ。
        /// case-local (DeepCopy 済) AnaModel で蓄積した結果を main AnaModel に append する。
        /// snapshot は DeepCopy 前の main の各結果コレクション長を渡す。caseModel はコピーなので
        /// snapshot 以前のエントリは main と同一内容であり、snapshot 以降が「そのケースで追加した結果」。
        /// これを main へ移送する。
        /// ※ E2 ではまだ呼び出し側を繋いでいない（逐次実行のまま）。E3 で per-case DeepCopy と
        ///    Parallel.ForEach を導入した時点で使用開始する。
        /// </summary>
        public static void AppendCaseResultsToMain(
            AnaModel main,
            AnaModel caseModel,
            int snapAnaStepResults,
            int[] snapNodeResults,
            int[] snapBeamResults,
            int[] snapHSpringResults,
            int[]? snapRotSpringResults)
        {
            if (main == null) throw new ArgumentNullException(nameof(main));
            if (caseModel == null) throw new ArgumentNullException(nameof(caseModel));

            // AnalysisStepResults: caseModel[snap..] を main に append
            if (caseModel.AnalysisStepResults != null)
            {
                for (int i = snapAnaStepResults; i < caseModel.AnalysisStepResults.Count; i++)
                    main.AnalysisStepResults.Add(caseModel.AnalysisStepResults[i]);
            }

            // Node.NodeResults
            if (snapNodeResults != null)
            {
                int nodeCount = Math.Min(main.Nodes.Count, caseModel.Nodes.Count);
                for (int n = 0; n < nodeCount; n++)
                {
                    var src = caseModel.Nodes[n].NodeResults;
                    var dst = main.Nodes[n].NodeResults;
                    int snap = n < snapNodeResults.Length ? snapNodeResults[n] : 0;
                    for (int i = snap; i < src.Count; i++) dst.Add(src[i]);
                }
            }

            // Beam.BeamResults
            if (snapBeamResults != null)
            {
                int beamCount = Math.Min(main.Beams.Count, caseModel.Beams.Count);
                for (int b = 0; b < beamCount; b++)
                {
                    var src = caseModel.Beams[b].BeamResults;
                    var dst = main.Beams[b].BeamResults;
                    int snap = b < snapBeamResults.Length ? snapBeamResults[b] : 0;
                    for (int i = snap; i < src.Count; i++) dst.Add(src[i]);
                }
            }

            // HorizontalSoilSpring.HorizontalSpringResults
            if (snapHSpringResults != null && main.HorizontalSoilSprings != null && caseModel.HorizontalSoilSprings != null)
            {
                int hsCount = Math.Min(main.HorizontalSoilSprings.Count, caseModel.HorizontalSoilSprings.Count);
                for (int h = 0; h < hsCount; h++)
                {
                    var src = caseModel.HorizontalSoilSprings[h].HorizontalSpringResults;
                    var dst = main.HorizontalSoilSprings[h].HorizontalSpringResults;
                    int snap = h < snapHSpringResults.Length ? snapHSpringResults[h] : 0;
                    for (int i = snap; i < src.Count; i++) dst.Add(src[i]);
                }
            }

            // RotationalSpring.RotationalSpringResults
            if (snapRotSpringResults != null && main.RotationalSprings != null && caseModel.RotationalSprings != null)
            {
                int rsCount = Math.Min(main.RotationalSprings.Count, caseModel.RotationalSprings.Count);
                for (int r = 0; r < rsCount; r++)
                {
                    var src = caseModel.RotationalSprings[r].RotationalSpringResults;
                    var dst = main.RotationalSprings[r].RotationalSpringResults;
                    int snap = r < snapRotSpringResults.Length ? snapRotSpringResults[r] : 0;
                    for (int i = snap; i < src.Count; i++) dst.Add(src[i]);
                }
            }
        }
    }
}

