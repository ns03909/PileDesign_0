using MathNet.Numerics.LinearAlgebra;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    public class AnaModel
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        public List<Node> Nodes { get; set; }
        public List<Beam> Beams { get; set; }
        public List<DummyBeam> DummyBeams { get; set; }
        public List<RigidBody> RigidBodies { get; set; }
        public List<HorizontalSoilSpring> HorizontalSoilSprings { get; set; }
        public List<RotationalSpring> RotationalSprings { get; set; }
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

        public List<bool> VectorDOFForcedDisp { get; private set; } = [];

        public double NormsROnNormsFint { get; private set; }

        // コンストラクタ
        public AnaModel(
            MainWindowViewModel mainWindowViewModel,
            List<Node> nodes,
            List<Beam> beams,
            List<DummyBeam> dummyBeams,
            List<RigidBody> rigidBodies,
            List<HorizontalSoilSpring> horizontalSoilSprings,
            List<RotationalSpring> rotationalSprings
            )
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            Nodes = nodes; // 節点リスト
            Beams = beams; // 要素リスト
            DummyBeams = dummyBeams; // 要素リスト
            RigidBodies = rigidBodies; // 剛体連結リスト
            HorizontalSoilSprings = horizontalSoilSprings; // 水平地盤ばねリスト
            RotationalSprings = rotationalSprings;

            int countFree = 0;
            int countFix = 0;
            var dofForcedDispList = new List<bool>();

            #if DEBUG
            // 診断: CapNodeの境界状態を確認
            var capNodes = Nodes.Where(n => n.Name != null && n.Name.StartsWith("CapNode")).ToList();
            if (capNodes.Count > 0)
            {
                var freeDofs = capNodes.SelectMany(n => Enumerable.Range(0, 6)
                    .Where(i => n.GetBoundary(i) == false)
                    .Select(i => $"{n.Name}:{i}")).ToList();
                if (freeDofs.Count > 0)
                {
                    //System.Diagnostics.Debug.WriteLine($"[AnaModel] CapNodes with FREE DOFs ({freeDofs.Count}):");
                    //foreach (var dof in freeDofs.Take(20))
                        //System.Diagnostics.Debug.WriteLine($"  {dof}");
                    //if (freeDofs.Count > 20)
                        //System.Diagnostics.Debug.WriteLine($"  ... and {freeDofs.Count - 20} more");
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine($"[AnaModel] All {capNodes.Count} CapNodes have FIXED boundaries (correct)");
                }
            }
            #endif

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
                Console.WriteLine($"[ERROR] CountFree is very large: {CountFree}. Aborting to avoid OOM.");
                throw new InvalidOperationException($"自由度が大きすぎます: {CountFree}. 入力データ／境界条件を確認してください。");
            }

            VectorF = Vector<double>.Build.Sparse(countFree, 0.0); // 荷重ベクトル
            VectorDF = Vector<double>.Build.Sparse(countFree, 0.0); // 荷重増分ベクトル
            VectorD = Vector<double>.Build.Sparse(countFree, 0.0); // 変位ベクトル
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
        private void MapOnKmat(bool isTan)
        {
            // 大規模解析では行列は疎であることが多いため Sparse を利用してメモリ消費を抑える
            Matrix<double> matrixKAA = Matrix<double>.Build.Sparse(CountFree, CountFree);
            Matrix<double> matrixKBA = Matrix<double>.Build.Sparse(CountFree, -CountFix);
            Matrix<double> matrixKAB = Matrix<double>.Build.Sparse(-CountFix, CountFree);
            Matrix<double> matrixKBB = Matrix<double>.Build.Sparse(-CountFix, -CountFix);

            foreach (var beam in Beams)
            {
                beam.SetKe(isTan);
                matrixKAA = beam.MapOnGlobalStiff(matrixKAA, isTan, true, true);
            }

            foreach (var horizontalSoilSpring in HorizontalSoilSprings)
            {
                matrixKAA = horizontalSoilSpring.MapOnGlobalStiff(matrixKAA, isTan, true, true);
            }

            // 追加: RotationalSprings も TwoNode 統一経路で加算
            if (RotationalSprings != null && RotationalSprings.Count > 0)
            {
                foreach (var rs in RotationalSprings)
                {
                    // KeTan/KeSec は PrepareKmat 内で SetKe 済みである前提
                    matrixKAA = rs.MapOnGlobalStiff(matrixKAA, isTan, true, true);
                }
            }

            // 正則化（ゼロ/負の対角値を診断）+ 小さい対角値の診断
            const double eps = 1e-9;
            const double smallThreshold = 1e-6; // この値以下の対角値を警告
            var zeroDiagDofs = new List<(int eq, string nodeName, double val)>();
            var smallDiagDofs = new List<(int eq, string nodeName, double val)>();

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
                    zeroDiagDofs.Add((i, nodeDofName ?? $"eq{i}", v));
                    matrixKAA[i, i] = eps;
                }
                else if (v < smallThreshold)
                {
                    smallDiagDofs.Add((i, nodeDofName ?? $"eq{i}", v));
                }
            }

            if (zeroDiagDofs.Count > 0)
            {
                // ノード名のプレフィックスでグループ化して出力
                var groupedByPrefix = zeroDiagDofs
                    .GroupBy(x => x.nodeName.Split(':')[0].Split('-')[0].Split('_')[0])
                    .Select(g => $"{g.Key}({g.Count()})")
                    .ToList();
                //System.Diagnostics.Debug.WriteLine($"[MapOnKmat] WARNING: {zeroDiagDofs.Count} DOFs have zero/negative diagonal (regularized to {eps}):");
                //System.Diagnostics.Debug.WriteLine($"  Node types: {string.Join(", ", groupedByPrefix)}");
                //foreach (var (eq, name, val) in zeroDiagDofs.Take(30))
                    //System.Diagnostics.Debug.WriteLine($"  eq={eq}: {name}, val={val:E3}");
                //if (zeroDiagDofs.Count > 30)
                    //System.Diagnostics.Debug.WriteLine($"  ... and {zeroDiagDofs.Count - 30} more");
            }
            if (smallDiagDofs.Count > 0)
            {
                //System.Diagnostics.Debug.WriteLine($"[MapOnKmat] INFO: {smallDiagDofs.Count} DOFs have SMALL diagonal (<{smallThreshold:E0}):");
                //foreach (var (eq, name, val) in smallDiagDofs.Take(20))
                    //System.Diagnostics.Debug.WriteLine($"  eq={eq}: {name}, val={val:E3}");
                //if (smallDiagDofs.Count > 20)
                    //System.Diagnostics.Debug.WriteLine($"  ... and {smallDiagDofs.Count - 20} more");
            }

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
        public (Matrix<double>, Vector<double>) GetForcedDispOnLoadVectorAndStiffnessMatrix(bool Istan)
        {
            var orig = Istan ? KAA_tan : KAA_sec;
            var vecOrig = VectorR ?? throw new InvalidOperationException("VectorR is null");

            // orig が null またはサイズ不正のガード
            if (orig == null) throw new InvalidOperationException("Stiffness matrix is not initialized.");
            if (orig.RowCount != CountFree) throw new InvalidOperationException("Matrix size mismatch.");

            // orig が Sparse であれば clone（Sparse clone はメモリ効率良い）
            Matrix<double> matrixK;
            if (orig.GetType().Name.Contains("Sparse"))
                matrixK = orig.Clone(); // Sparse clone -> OK
            else
            {
                // fallback: 明示的に sparse を作って非ゼロだけコピー（Dense 全複製を避ける）
                var sparse = Matrix<double>.Build.Sparse(CountFree, CountFree);
                // 非ゼロ要素だけをコピー（API に応じて EnumerateIndexed を使う）
                foreach (var (i, j, val) in orig.EnumerateIndexed(Zeros.AllowSkip))
                    sparse[i, j] = val;
                matrixK = sparse;
            }

            var vectorR = vecOrig.Clone();

            // 荷重ベクトルへの操作
            foreach (var node in Nodes)
            {
                if (node.IsForcedDisped == true)
                {
                    NodeDisp forcedDisp = node.CumulativeForcedDisp - node.CumulativeDisp; // 強制変位の取得

                    for (int k = 0; k < 2; k++)
                    {
                        var eq = node.EquationNumber[k];
                        {
                            double forcedDispComponent = forcedDisp.GetByIndex(k); // 強制変位の該当成分を取得
                            for (int i = 0; i < CountFree; i++)
                            {
                                vectorR[i] -= matrixK[i, eq] * forcedDispComponent; // 強制変位を考慮して調整
                            }
                        }
                    }
                }
            }

            foreach (var node in Nodes)
            {
                if (node.IsForcedDisped == true)
                {
                    NodeDisp forcedDisp = node.CumulativeForcedDisp - node.CumulativeDisp; // 強制変位の取得

                    for (int k = 0; k < 2; k++)
                    {
                        var eq = node.EquationNumber[k];
                        {
                            double forcedDispComponent = forcedDisp.GetByIndex(k); // 強制変位の該当成分を取得

                            vectorR[eq] = forcedDispComponent; // 荷重ベクトルの該当要素に強制変位を設定
                        }
                    }
                }
            }

            // 剛性マトリクスへの操作
            foreach (var node in Nodes)
            {
                if (node.IsForcedDisped == true)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        var eq = node.EquationNumber[k];
                        {
                            matrixK[eq, eq] = 1.0; // 剛性マトリクスの該当要素を1に設定
                            for (int i = 0; i < CountFree; i++)
                            {
                                if (i == eq) continue; // 自分自身の方程式番号はスキップ
                                matrixK[i, eq] = 0.0; // 剛性マトリクスの該当要素をゼロに設定
                                matrixK[eq, i] = 0.0; // 剛性マトリクスの該当要素をゼロに設定
                            }
                        }
                    }
                }
            }
            return (matrixK, vectorR);
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

            // 回転ばねの内力を組み立て（全体座標系で定義されている）
            if (RotationalSprings != null)
            {
                foreach (var rs in RotationalSprings)
                {
                    AssembleSpringForceToGlobal(rs.NodeI, rs.NodeJ, rs.CumulativeForce);
                }
            }

            // デバッグ: 組み立て方式と K_sec*d の差を確認（初回のみ出力）
            #if DEBUG
            if (KAA_sec != null && VectorD != null)
            {
                var T_old = KAA_sec * VectorD;
                double diff = (VectorT - T_old).L2Norm();
                double T_norm = VectorT.L2Norm();
                double T_old_norm = T_old.L2Norm();
                if (diff > 1e-6 * Math.Max(T_norm, T_old_norm))
                {
                    //System.Diagnostics.Debug.WriteLine($"[SetT] Assembly vs K*d: ||T_asm||={T_norm:E3}, ||T_old||={T_old_norm:E3}, ||diff||={diff:E3}");
                }
            }
            #endif
        }

        // 梁要素の内力を全体ベクトルに組み立て（要素座標系→全体座標系変換＋マスター節点変換を含む）
        private void AssembleBeamForceToGlobal(Beam beam)
        {
            // 要素内力（要素座標系）
            Vector<double> f_local = beam.CumulativeForce.GetVector();

            // 要素座標系→全体座標系への変換: f_global = T_coord^T * f_local
            Matrix<double> coordTransform = Utils.GetTransformMatrix(beam.NodeI, beam.NodeJ);
            Vector<double> f_global = coordTransform.Transpose() * f_local;

            // スレーブ節点のマスター変換（TransferMatrix）を適用
            // 剛性マトリクスと同様: K = T_slave^T * K_local * T_slave に対応して f = T_slave^T * f_local
            Matrix<double> slaveTransform = Matrix<double>.Build.DenseIdentity(12);
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    slaveTransform[r, c] = beam.NodeI.TransferMatrix[r, c];
                    slaveTransform[r + 6, c + 6] = beam.NodeJ.TransferMatrix[r, c];
                }
            }
            Vector<double> f_transformed = slaveTransform.Transpose() * f_global;

            // 方程式番号リストを取得（マスター節点を考慮）
            var eq = Utils.GetEquationNumbers(beam.NodeI, beam.NodeJ);

            // NodeI の内力を VectorT に加算（f_transformed[0:6]）
            for (int i = 0; i < 6; i++)
            {
                int eqNum = eq[i];
                if (eqNum >= 0) // 自由度のみ（固定DOFは負値）
                {
                    VectorT[eqNum] += f_transformed[i];
                }
            }

            // NodeJ の内力を VectorT に加算（f_transformed[6:12]）
            for (int i = 0; i < 6; i++)
            {
                int eqNum = eq[6 + i];
                if (eqNum >= 0)
                {
                    VectorT[eqNum] += f_transformed[6 + i];
                }
            }
        }

        // ばね要素の内力を全体ベクトルに組み立て（マスター節点変換を含む）
        private void AssembleSpringForceToGlobal(Node nodeI, Node nodeJ, BeamForce cumulativeForce)
        {
            // ばね内力（既に全体座標系）
            Vector<double> f = cumulativeForce.GetVector();

            // スレーブ節点のマスター変換（TransferMatrix）を適用
            // 剛性マトリクスと同様: K = T_slave^T * K_local * T_slave に対応して f = T_slave^T * f_local
            Matrix<double> slaveTransform = Matrix<double>.Build.DenseIdentity(12);
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    slaveTransform[r, c] = nodeI.TransferMatrix[r, c];
                    slaveTransform[r + 6, c + 6] = nodeJ.TransferMatrix[r, c];
                }
            }
            Vector<double> f_transformed = slaveTransform.Transpose() * f;

            // 方程式番号リストを取得（マスター節点を考慮）
            var eq = Utils.GetEquationNumbers(nodeI, nodeJ);

            // NodeI の内力を VectorT に加算（f_transformed[0:6]）
            for (int i = 0; i < 6; i++)
            {
                int eqNum = eq[i];
                if (eqNum >= 0)
                {
                    VectorT[eqNum] += f_transformed[i];
                }
            }

            // NodeJ の内力を VectorT に加算（f_transformed[6:12]）
            for (int i = 0; i < 6; i++)
            {
                int eqNum = eq[6 + i];
                if (eqNum >= 0)
                {
                    VectorT[eqNum] += f_transformed[6 + i];
                }
            }
        }

        // 残余力ベクトルの初期値のセット
        public void SetR()
        {
            VectorR += VectorDF;
        }

        // 残余力ベクトルの更新
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
            NormsROnNormsFint = normsqR / normsqF;
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
        private void InitializeAxialForces()  //PileDesign
        {
            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                pileLayoutItem.AxialForce =
                    pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional; // レベル1の杭軸力増分
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
            return AnalysisStepResults
                .Where(r => r.LoadCase?.LoadName == loadCase?.LoadName &&
                            r.LoadCombination?.Name == loadCombination?.Name &&
                            r.IsLiquefaction == isLiquefaction)
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

            #if DEBUG
            if (results.Count == 0)
            {
                //System.Diagnostics.Debug.WriteLine($"[GetAnalysisLastStep] No results found for LoadCase={loadCase?.LoadName}, Comb={loadCombination?.Name}, isLiq={isLiquefaction}");
                //System.Diagnostics.Debug.WriteLine($"[GetAnalysisLastStep] Available: {string.Join(", ", AnalysisStepResults.Take(5).Select(r => $"LC={r.LoadCase?.LoadName},Comb={r.LoadCombination?.Name},isLiq={r.IsLiquefaction}"))}");
            }
            #endif

            return results.DefaultIfEmpty(-999).Max();
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
            // 各リストの要素もDeepCopyする
            var nodes = this.Nodes.Select(n => n.DeepCopy()).ToList();
            var beams = this.Beams.Select(b => b.DeepCopy()).ToList();
            var dummyBeams = this.DummyBeams.Select(db => db.DeepCopy()).ToList();
            var rigidBodies = this.RigidBodies.Select(rb => rb.DeepCopy()).ToList();
            var horizontalSoilSprings = this.HorizontalSoilSprings.Select(s => s.DeepCopy()).ToList();
            var rotationalSprings = this.RotationalSprings.Select(rs => rs.DeepCopy()).ToList();

            var copy = new AnaModel(_mainWindowViewModel, nodes, beams, dummyBeams, rigidBodies, horizontalSoilSprings, rotationalSprings);

            // AnalysisStepResultsもDeepCopy
            foreach (var result in this.AnalysisStepResults)
                copy.AnalysisStepResults.Add(result.DeepCopy());

            // 必要に応じて他のプロパティもコピー
            // 例: copy.VectorF = this.VectorF.Clone();

            return copy;
        }
    }
}

