using PileDesign.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MathNet.Numerics;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    // HorizontalCalculationViewModel partial: 数値ソルバ（Newton 方向・line search・荷重増分・K 行列組立）
    public partial class HorizontalCalculationViewModel
    {
        #region Line Search (線探索)

        /// <summary>
        /// Newton方向を解く（変位を更新しない）
        /// </summary>
        /// <returns>増分変位ベクトル（Newton方向）</returns>
        private static MathNet.Numerics.LinearAlgebra.Vector<double> SolveNewtonDirection(AnaModel targetModel)
        {
            targetModel.SetForcedDispOnLoadVectorAndStiffnessMatrix(true); // KAA_tanとVectorRを取得

            MathNet.Numerics.LinearAlgebra.Vector<double> newtonDirection;
            try
            {
                // cache を渡すと K 不変な反復で Cholesky 因子を再利用 (CSC + 分解をスキップ)
                var x = CsparseLinearSolver.Solve(targetModel.KAA_tan, targetModel.VectorR, isSpd: false, cache: targetModel.SolverCache);
                newtonDirection = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(x);
            }
            catch
            {
                newtonDirection = targetModel.KAA_tan.Solve(targetModel.VectorR);
            }

            // 変位増分制限（発散防止）
            const double maxDispIncrement = 0.05; // 最大増分 50mm
            double maxAbsIncrement = newtonDirection.AbsoluteMaximum();
            if (maxAbsIncrement > maxDispIncrement)
            {
                double scaleFactor = maxDispIncrement / maxAbsIncrement;
                newtonDirection *= scaleFactor;
            }

            return newtonDirection;
        }

        /// <summary>
        /// 指定したステップ長αで変位を更新し、残差を評価する
        /// </summary>
        /// <param name="targetModel">解析モデル</param>
        /// <param name="savedVectorD">保存された累積変位</param>
        /// <param name="newtonDirection">Newton方向</param>
        /// <param name="alpha">ステップ長</param>
        /// <param name="iLC">荷重ケース番号</param>
        /// <param name="isPileNonLinear">杭非線形フラグ</param>
        /// <param name="isLightweight">軽量評価モード（割線剛性更新をスキップ）</param>
        /// <returns>残差ノルム ||R||²/||Fint||²</returns>
        private double EvaluateResidualAtAlpha(
            AnaModel targetModel,
            MathNet.Numerics.LinearAlgebra.Vector<double> savedVectorD,
            MathNet.Numerics.LinearAlgebra.Vector<double> newtonDirection,
            double alpha,
            int iLC,
            bool isPileNonLinear,
            bool isLightweight = false)
        {
            // αを適用した増分変位
            var incrementalDisp = newtonDirection * alpha;

            // 累積変位を更新（ラインサーチ用メソッドで直接設定）
            targetModel.SetDispVectorDirect(savedVectorD + incrementalDisp, incrementalDisp);

            // 節点変位を更新（Solver.SolveDisp内のロジックを再現）
            UpdateNodeDisplacementsForLineSearch(targetModel, incrementalDisp);

            // 割線剛性更新（非線形の場合）
            // 軽量モードでは現在の割線剛性を使用（近似的だが高速）
            if (isPileNonLinear && !isLightweight)
                UpdateBeamMPhiSecant(targetModel);

            // 内力計算
            FindT(iLC, targetModel);

            // 残差計算
            targetModel.FindR();

            return targetModel.NormsROnNormsFint;
        }

        /// <summary>
        /// ラインサーチ用: 増分変位から節点変位を更新する
        /// Solver.SolveDispのロジックを再現
        /// </summary>
        private static void UpdateNodeDisplacementsForLineSearch(AnaModel targetModel, MathNet.Numerics.LinearAlgebra.Vector<double> incrementalDispVector)
        {
            foreach (var node in targetModel.Nodes)
            {
                double[] ddisp = new double[6];

                if (node.ResolvedDofMap != null)
                {
                    // ResolvedDofMap 方式
                    for (int i = 0; i < 6; i++)
                    {
                        var terms = node.ResolvedDofMap[i];
                        if (terms == null || terms.Length == 0) { ddisp[i] = 0; continue; }
                        double disp = 0;
                        foreach (var term in terms)
                        {
                            if (term.Eq >= 0)
                                disp += term.Coeff * incrementalDispVector[term.Eq];
                        }
                        ddisp[i] = disp;
                    }
                }
                else
                {
                    // フォールバック: 従来ロジック
                    (int crossIdx, Func<Vector3S, double> arm, double sign)[][] crossTerms =
                    [
                        [(4, v => v.Z, 1.0), (5, v => v.Y, -1.0)],
                        [(5, v => v.X, 1.0), (3, v => v.Z, -1.0)],
                        [(3, v => v.Y, 1.0), (4, v => v.X, -1.0)],
                        [], [], [],
                    ];
                    for (int i = 0; i < 6; i++)
                    {
                        int e_num = node.EquationNumber[i];
                        if (node.MasterNodes[i] != null)
                        {
                            int eq = node.MasterNodes[i].EquationNumber[i];
                            ddisp[i] = eq < 0 ? 0 : incrementalDispVector[eq];
                            foreach (var (crossIdx, arm, sign) in crossTerms[i])
                            {
                                if (node.MasterNodes[crossIdx] != null)
                                {
                                    int crossEq = node.MasterNodes[crossIdx].EquationNumber[crossIdx];
                                    double armVal = arm(node.SlaveArm);
                                    ddisp[i] += (crossEq >= 0 ? incrementalDispVector[crossEq] : 0.0) * armVal * sign;
                                }
                            }
                        }
                        else
                        {
                            ddisp[i] = e_num < 0 ? 0 : incrementalDispVector[e_num];
                        }
                    }
                }

                var incDisp = new NodeDisp(ddisp[0], ddisp[1], ddisp[2], ddisp[3], ddisp[4], ddisp[5]);
                node.IncrementalDisp = incDisp;

                // 累積変位の更新
                if (node.CumulativeDisp == null)
                    node.CumulativeDisp = incDisp;
                else
                    node.CumulativeDisp = new NodeDisp(
                        node.CumulativeDisp.Ux + incDisp.Ux,
                        node.CumulativeDisp.Uy + incDisp.Uy,
                        node.CumulativeDisp.Uz + incDisp.Uz,
                        node.CumulativeDisp.Rx + incDisp.Rx,
                        node.CumulativeDisp.Ry + incDisp.Ry,
                        node.CumulativeDisp.Rz + incDisp.Rz
                    );
            }
        }

        /// <summary>
        /// 2次補間を用いた高速ラインサーチ（v14 → v25 G+ 改良版）
        /// v25 G+: f'(0) ≈ -2f(0) の線形化勾配を使った 2 点 quadratic fit を先頭で試す。
        /// 成功すれば中間点 trial を省略できる（α=1 の 1 回のみで閉形式解）。
        /// 失敗時は従来の 3 点 quadratic fit（中間点を full eval に精度向上）にフォールバック。
        /// </summary>
        /// <param name="targetModel">解析モデル</param>
        /// <param name="newtonDirection">Newton方向</param>
        /// <param name="currentResidual">現在の残差</param>
        /// <param name="iLC">荷重ケース番号</param>
        /// <param name="isPileNonLinear">杭非線形フラグ</param>
        /// <returns>最適なステップ長α</returns>
        private double BacktrackingLineSearch(
            AnaModel targetModel,
            MathNet.Numerics.LinearAlgebra.Vector<double> newtonDirection,
            double currentResidual,
            int iLC,
            bool isPileNonLinear,
            out int trialCount)
        {
            // 現在の累積変位と節点変位を保存
            var savedVectorD = targetModel.VectorD.Clone();
            var savedNodeDisps = SaveNodeDisplacements(targetModel);

            // 試行回数カウンタ (EvaluateResidualAtAlpha 呼出し回数を集計)
            trialCount = 0;

            // Step 1: α=1.0で完全評価
            double alpha1 = 1.0;
            trialCount++;
            double f1 = EvaluateResidualAtAlpha(
                targetModel, savedVectorD, newtonDirection, alpha1, iLC, isPileNonLinear, isLightweight: false);

            // α=1.0で残差が減少すれば即採用
            if (f1 <= currentResidual)
            {
                _lastAcceptedAlpha = 1.0;
                return alpha1;
            }

            double f0 = currentResidual;

            // v25 G+: 勾配情報による 2 点 quadratic fit（閉形式）
            // Newton 方向 Δu は K_tan Δu = -R を満たす。f(α) = ||R(u+αΔu)||² とおくと
            // linearize: R(u+αΔu) ≈ R(u) - α R(u) = (1-α) R(u)
            // ⇒ f(α) ≈ (1-α)² f(0), f'(0) ≈ -2 f(0)
            // Quadratic fit: a α² + b α + c with c=f0, b=-2 f0, a=f1+f0
            // α* = -b/(2a) = f0 / (f0 + f1), clamped to [0.05, 0.95]
            // 3 点 fit と比べて中間点 trial が不要なので、成功時は評価 1 回節約できる。
            if (f0 > 0 && f1 > 0)
            {
                double alphaGrad = f0 / (f0 + f1);
                alphaGrad = Math.Clamp(alphaGrad, 0.05, 0.95);

                RestoreNodeDisplacements(targetModel, savedNodeDisps);
                trialCount++;
                double fGrad = EvaluateResidualAtAlpha(
                    targetModel, savedVectorD, newtonDirection, alphaGrad, iLC, isPileNonLinear, isLightweight: true);

                if (fGrad < f0)
                {
                    RestoreNodeDisplacements(targetModel, savedNodeDisps);
                    trialCount++;
                    EvaluateResidualAtAlpha(
                        targetModel, savedVectorD, newtonDirection, alphaGrad, iLC, isPileNonLinear, isLightweight: false);
                    _lastAcceptedAlpha = alphaGrad;
                    return alphaGrad;
                }
            }

            // Step 2: α=0.5 で評価（v25 G+: lightweight → full に変更し fit 精度向上）
            RestoreNodeDisplacements(targetModel, savedNodeDisps);
            double alpha2 = 0.5;
            trialCount++;
            double f2 = EvaluateResidualAtAlpha(
                targetModel, savedVectorD, newtonDirection, alpha2, iLC, isPileNonLinear, isLightweight: false);

            // α=0.5で残差が減少すれば採用（full eval 済みなので再評価不要）
            if (f2 < currentResidual)
            {
                _lastAcceptedAlpha = alpha2;
                return alpha2;
            }

            // Step 3: 3 点 quadratic fit（f0, f2, f1 から α* を推定）
            // f(α) ≈ a*α² + b*α + c の係数を推定
            // 3点: (0, f0=currentResidual), (0.5, f2), (1.0, f1)
            // f(0) = c = f0
            // f(0.5) = 0.25a + 0.5b + c = f2
            // f(1) = a + b + c = f1
            // 解くと:
            // a = 2*f1 - 4*f2 + 2*f0
            // b = -3*f0 + 4*f2 - f1
            double a = 2 * f1 - 4 * f2 + 2 * f0;
            double b = -3 * f0 + 4 * f2 - f1;

            // 2次関数の頂点 α* = -b / (2a)（a > 0 の場合のみ有効な最小値）
            double alphaOpt;
            if (a > 1e-12)
            {
                alphaOpt = -b / (2 * a);
                alphaOpt = Math.Clamp(alphaOpt, 0.05, 0.95);  // 範囲制限
            }
            else
            {
                // 2次係数が小さい/負の場合は線形補間でα=0.25を試す
                alphaOpt = 0.25;
            }

            // Step 4: 最適αで評価
            RestoreNodeDisplacements(targetModel, savedNodeDisps);
            trialCount++;
            double fOpt = EvaluateResidualAtAlpha(
                targetModel, savedVectorD, newtonDirection, alphaOpt, iLC, isPileNonLinear, isLightweight: true);

            if (fOpt < currentResidual)
            {
                RestoreNodeDisplacements(targetModel, savedNodeDisps);
                trialCount++;
                double finalResidual = EvaluateResidualAtAlpha(
                    targetModel, savedVectorD, newtonDirection, alphaOpt, iLC, isPileNonLinear, isLightweight: false);
                _lastAcceptedAlpha = alphaOpt;
                return alphaOpt;
            }

            // Step 5: 補間が失敗した場合、フォールバックとして幾何縮小
            double bestAlpha = (f1 < f2) ? alpha1 : alpha2;
            double bestResidual = Math.Min(f1, f2);
            if (fOpt < bestResidual)
            {
                bestResidual = fOpt;
                bestAlpha = alphaOpt;
            }

            // 追加試行: α=0.25, 0.125
            double[] fallbackAlphas = [0.25, 0.125, 0.0625];
            foreach (double alpha in fallbackAlphas)
            {
                if (Math.Abs(alpha - alphaOpt) < 0.05) continue; // 既に試したαはスキップ

                RestoreNodeDisplacements(targetModel, savedNodeDisps);
                trialCount++;
                double trialResidual = EvaluateResidualAtAlpha(
                    targetModel, savedVectorD, newtonDirection, alpha, iLC, isPileNonLinear, isLightweight: true);

                if (trialResidual < bestResidual)
                {
                    bestResidual = trialResidual;
                    bestAlpha = alpha;
                }

                if (trialResidual < currentResidual)
                {
                    RestoreNodeDisplacements(targetModel, savedNodeDisps);
                    trialCount++;
                    double finalResidual = EvaluateResidualAtAlpha(
                        targetModel, savedVectorD, newtonDirection, alpha, iLC, isPileNonLinear, isLightweight: false);
                    _lastAcceptedAlpha = alpha;
                    return alpha;
                }
            }

            // すべて失敗した場合、最良のαを使用
            RestoreNodeDisplacements(targetModel, savedNodeDisps);
            trialCount++;
            EvaluateResidualAtAlpha(targetModel, savedVectorD, newtonDirection, bestAlpha, iLC, isPileNonLinear, isLightweight: false);
            _lastAcceptedAlpha = bestAlpha;

            return bestAlpha;
        }

        // 前回のライン探索で採用されたα（次回の参考用）
        private double _lastAcceptedAlpha = 1.0;

        /// <summary>
        /// 節点変位を保存
        /// </summary>
        private static Dictionary<FEM.Node, (NodeDisp incremental, NodeDisp cumulative)> SaveNodeDisplacements(AnaModel targetModel)
        {
            var saved = new Dictionary<FEM.Node, (NodeDisp, NodeDisp)>();
            foreach (var node in targetModel.Nodes)
            {
                saved[node] = (
                    node.IncrementalDisp?.Clone(),
                    node.CumulativeDisp?.Clone()
                );
            }
            return saved;
        }

        /// <summary>
        /// 節点変位を復元
        /// </summary>
        private static void RestoreNodeDisplacements(AnaModel targetModel, Dictionary<FEM.Node, (NodeDisp incremental, NodeDisp cumulative)> saved)
        {
            foreach (var node in targetModel.Nodes)
            {
                if (saved.TryGetValue(node, out var displacement))
                {
                    node.IncrementalDisp = displacement.incremental?.Clone();
                    node.CumulativeDisp = displacement.cumulative?.Clone();
                }
            }
        }

        #endregion

        // Phase 2 (step-level cut-back): resetCumulative パラメータを追加。
        //   resetCumulative=true (default): 従来挙動 — IncrementalForcedDisp と CumulativeForcedDisp を両方上書き (= ステップ 1 から開始)
        //   resetCumulative=false: substep モード — IncrementalForcedDisp のみ上書きし、CumulativeForcedDisp は保持 (=チェックポイント復元後の継続実行)
        private void InitializeSoilDisplacementIncrement(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, bool isLiquefaction, double nStep, bool resetCumulative = true)
        {
            // VL (常時) ケースは地震時の地盤強制変位を一切受けない (鉛直軸力のみ)。
            // ここで早期 return しないと、液状化「あり」設定で groundDisp1L × cos(LoadAngle) の
            // X 強制変位が SoilNode に乗り、Chang ばね K_x → 杭周地盤反力に擬似 Fx が発生する。
            // (例: 計算例9 で 杭周地盤反力合計 Fx=+82.9 kN / 土圧合力ばね反力 Fx=-82.9 kN の循環応力)
            bool isVLCase = loadCase != null && loadCase.LoadName == "VL";
            if (isVLCase)
            {
                NodeDisp zero = new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
                foreach (var pileLayoutItem in InputModel.PileLayoutItems)
                {
                    var soilNodes = targetModel.GetSoilNodes(pileLayoutItem);
                    foreach (var sn in soilNodes)
                    {
                        sn.SetIncrementalForcedDisp(zero);
                        if (resetCumulative) sn.SetCumulativeForcedDisp(zero);
                    }
                }
                if (InputModel.ElementDivision.DoatsuGoryokuBane != null &&
                    InputModel.ElementDivision.DoatsuGoryokuBane.Items.Count > 1 &&
                    InputModel.ElementDivision.SoilEmbedment != null)
                {
                    for (int i = 0; i < InputModel.ElementDivision.SoilEmbedment.ZDataItems.Count; i++)
                    {
                        var zDataItem = InputModel.ElementDivision.SoilEmbedment.ZDataItems[i];
                        var soilNode = targetModel.FindNode("根入部地盤節点", null, null, zDataItem.Z);
                        if (soilNode == null) continue;
                        soilNode.SetIncrementalForcedDisp(zero);
                        if (resetCumulative) soilNode.SetCumulativeForcedDisp(zero);
                    }
                }
                return;
            }

            double loadAngle = loadCase.LoadAngle;
            double alpha1 = loadCombination.Alpha1;
            NodeDisp initialCumulativeSoilDisplacement = new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

            // 共通の地盤変位計算ローカル関数
            static NodeDisp CalcDisplacement(double displacement1, double displacement2, int level, double alpha1, double nStep, double loadAngle)
            {
                double groundDisp = (level == 1 ? displacement1 : displacement2) * alpha1 / nStep / 1000.0;

                double rad = loadAngle * Math.PI / 180.0;
                double groundDisplacementX = groundDisp * Math.Cos(rad);
                double groundDisplacementY = groundDisp * Math.Sin(rad);
                return new NodeDisp(groundDisplacementX, groundDisplacementY, 0.0, 0.0, 0.0, 0.0);
            }

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                var soilPile = InputModel.ElementDivision.SoilPiles[pileLayoutItem.SoilPileAltNo - 1];
                // E3b: case-local SoilNodes 経由 (主モデルでは InputModel.PileLayoutItems.SoilNodes と同一参照)
                var soilNodes = targetModel.GetSoilNodes(pileLayoutItem);
                for (int i = 0; i < soilPile.ZDataItems.Count; i++)
                {
                    var zData = soilPile.ZDataItems[i];
                    double groundDisp1 = isLiquefaction ? zData.GroundDisp1L : zData.GroundDisp1;
                    double groundDisp2 = isLiquefaction ? zData.GroundDisp2L : zData.GroundDisp2;
                    NodeDisp dd = CalcDisplacement(groundDisp1, groundDisp2, level, alpha1, nStep, loadAngle);

                    soilNodes[i].SetIncrementalForcedDisp(dd);
                    if (resetCumulative)
                        soilNodes[i].SetCumulativeForcedDisp(initialCumulativeSoilDisplacement);
                }
            }

            if (InputModel.ElementDivision.DoatsuGoryokuBane != null &&
                InputModel.ElementDivision.DoatsuGoryokuBane.Items.Count > 1)
            {
                for (int i = 0; i < InputModel.ElementDivision.SoilEmbedment.ZDataItems.Count; i++)
                {
                    var zDataItem = InputModel.ElementDivision.SoilEmbedment.ZDataItems[i];
                    var z = zDataItem.Z;
                    double groundDisp1 = isLiquefaction ? zDataItem.GroundDisp1L : zDataItem.GroundDisp1;
                    double groundDisp2 = isLiquefaction ? zDataItem.GroundDisp2L : zDataItem.GroundDisp2;
                    NodeDisp dd = CalcDisplacement(groundDisp1, groundDisp2, level, alpha1, nStep, loadAngle);
                    FEM.Node soilNode = targetModel.FindNode("根入部地盤節点", null, null, z);
                    soilNode.SetIncrementalForcedDisp(dd);
                    if (resetCumulative)
                        soilNode.SetCumulativeForcedDisp(initialCumulativeSoilDisplacement);
                }
            }
        }

        // 増分荷重の取得 慣性力の節点荷重へのセット
        // Phase 2 (step-level cut-back): resetCumulative パラメータを追加。
        //   resetCumulative=true (default): 従来挙動 — IncrementalLoad/VectorDF と CumulativeLoad/VectorF を初期化 (=ステップ 1 から)
        //   resetCumulative=false: substep モード — IncrementalLoad/VectorDF のみ更新、累積側は保持 (=チェックポイント復元後の継続実行)
        // AxialForceIncrement は常に書換 (per-step 値、累積はモデル内で別管理)。
        private void SetVectorDF(AnaModel targetModel, LoadCase loadCase, LoadCombination loadCombination, int level, int iLC, double nStep, bool resetCumulative = true) // PileDesign
        {
            double loadAngle = loadCase.LoadAngle;
            double beta1 = loadCombination.Beta1; // 荷重組合せ上部構造慣性力の荷重係数β1
            double beta2 = loadCombination.Beta2; // 荷重組合せ基礎構造慣性力の荷重係数β2

            double upperMassForce = loadCase.UpperMassForce; // 上部構造質量荷重 [kN]
            double foundationMassForce = loadCase.FoundationMassForce; // 基礎構造質量荷重 [kN]

            double force = beta1 * upperMassForce + beta2 * foundationMassForce; // 上部構造質量荷重 + 基礎構造質量荷重[kN]
            double deltaForce = force / nStep; // 増分荷重 [kN]
            double x = deltaForce * Math.Cos(loadAngle * Math.PI / 180.0); // x方向の増分荷重 [kN]
            double y = deltaForce * Math.Sin(loadAngle * Math.PI / 180.0); // y方向の増分荷重 [kN]

            targetModel.Nodes[0].SetIncrementalLoad(new(x, y, 0.0, 0.0, 0.0, 0.0)); // 増分荷重ベクトル [kN]
            // 案 Z (P-S 非線形ばね 有効時): 杭軸力 (ケース別) を各杭の接合節点 Z 方向に下向き外力として段階適用。
            // 接合節点優先順位:
            //   1. ConnectionNode "FoundationNode-P{No}" (基礎梁設定時に存在)
            //   2. CapNode "CapNode-{pile.No}" (フォールバック、基礎梁未設定時)
            // どちらも RigidBody[0] の直接スレーブとなるため、MapOnGlobalLoad の slave 分岐で
            // 正しく AP に荷重 + モーメント (arm × Fz の Mx/My 両成分) を伝達できる。
            // 各杭ごとに独立した N0_i を与えるため、AP が並進+回転して各杭が arm に応じた異なる Z 変位を持ち、
            // 杭ごとに異なる軸力が FEM Fxi に現れる (前後方杭の差を再現)。
            if (InputModel.UsePsSpringAtPileTip)
            {
                bool isVLCase = loadCase != null && loadCase.LoadName == "VL";
                foreach (var pli in InputModel.PileLayoutItems)
                {
                    double targetN;
                    if (isVLCase)
                    {
                        // VL 単独ケース: 常時軸力 AxialForceVL を使用
                        targetN = pli.AxialForceVL;
                    }
                    else if (level == 1)
                        targetN = pli.AxialForceLevel1s != null && iLC < pli.AxialForceLevel1s.Count
                            ? pli.AxialForceLevel1s[iLC] : pli.AxialForceVL;
                    else
                        targetN = pli.AxialForceLevel2s != null && iLC < pli.AxialForceLevel2s.Count
                            ? pli.AxialForceLevel2s[iLC] : pli.AxialForceVL;
                    if (!double.IsFinite(targetN) || Math.Abs(targetN) < 1e-12) continue;

                    double deltaN_per_step = targetN / nStep;
                    var jointNode = ResolvePileJointNodeInModel(targetModel, pli.No);
                    if (jointNode == null) continue;

                    // 既存の IncrementalLoad を保持しつつ Z だけ加算 (圧縮 → -Z 力)
                    var prev = jointNode.IncrementalLoad ?? new FEM.NodeLoad(0, 0, 0, 0, 0, 0);
                    jointNode.SetIncrementalLoad(new FEM.NodeLoad(
                        prev.Fx, prev.Fy, prev.Fz - deltaN_per_step,
                        prev.Mx, prev.My, prev.Mz));
                }

                // 杭体自重の注入: 沈下解析 (VerticalLoadTransferMethod.SetWeights / line 996)
                // と物理的に同一にするため、各杭節点に Weights[k] (kN, 圧縮=正) を Fz=-W として外力に追加。
                // nStep で均等分割し、ステップごとに加算 (cap N0 と同じ増分手順に揃える)。
                // PileVerticalSoilSpringModel.Weight に各節点別自重が格納されている。
                //
                // 重要 (2026-05-17 修正): 杭頭節点 (k=0) は Uz が CapNode の slave (AnalysisModelling.cs:1327)
                // で、さらに CapNode は AP の slave。MapOnGlobalLoad の slave 経路は 1 段しか chain 解決
                // しないため、杭頭節点に直接 Fz を与えると MasterNodes[2].EquationNumber[2] = -1 で
                // ArgumentOutOfRangeException 発生。k=0 の自重は jointNode (cap/ConnectionNode) に集約する。
                foreach (var pli in InputModel.PileLayoutItems)
                {
                    var pileNodesForWeight = targetModel.GetPileNodes(pli);
                    var modelsForWeight = pli.VerticalNodeSpringModels;
                    if (pileNodesForWeight == null || modelsForWeight == null) continue;
                    int nw = Math.Min(pileNodesForWeight.Count, modelsForWeight.Count);
                    var jointNodeForWeight = ResolvePileJointNodeInModel(targetModel, pli.No);
                    for (int k = 0; k < nw; k++)
                    {
                        var md = modelsForWeight[k];
                        if (md == null || md.Weight <= 0.0) continue;
                        double dWz = md.Weight / nStep; // 1 ステップ当たりの自重増分

                        // k=0 の自重は CapNode (= jointNode) に加算。slave 1 段だけなので安全に AP へ伝達。
                        FEM.Node target;
                        if (k == 0)
                        {
                            target = jointNodeForWeight;
                            if (target == null) continue;
                        }
                        else
                        {
                            target = pileNodesForWeight[k];
                            if (target == null) continue;
                        }

                        var prevW = target.IncrementalLoad ?? new FEM.NodeLoad(0, 0, 0, 0, 0, 0);
                        target.SetIncrementalLoad(new FEM.NodeLoad(
                            prevW.Fx, prevW.Fy, prevW.Fz - dWz,
                            prevW.Mx, prevW.My, prevW.Mz));
                    }
                }
            }
            targetModel.MapOnVectorDF();

            if (resetCumulative)
            {
                targetModel.Nodes[0].SetCumulativeLoad(new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0)); // 荷重ベクトル [kN]
                if (InputModel.UsePsSpringAtPileTip)
                {
                    foreach (var pli in InputModel.PileLayoutItems)
                    {
                        var jointNode = ResolvePileJointNodeInModel(targetModel, pli.No);
                        jointNode?.SetCumulativeLoad(new FEM.NodeLoad(0, 0, 0, 0, 0, 0));

                        // 杭節点自重もリセット (k=0 は jointNode 側で既にリセット済み)
                        var pileNodesForWeight = targetModel.GetPileNodes(pli);
                        var modelsForWeight = pli.VerticalNodeSpringModels;
                        if (pileNodesForWeight == null || modelsForWeight == null) continue;
                        int nw = Math.Min(pileNodesForWeight.Count, modelsForWeight.Count);
                        for (int k = 1; k < nw; k++)
                        {
                            var pn = pileNodesForWeight[k];
                            var md = modelsForWeight[k];
                            if (pn == null || md == null || md.Weight <= 0.0) continue;
                            pn.SetCumulativeLoad(new FEM.NodeLoad(0, 0, 0, 0, 0, 0));
                        }
                    }
                }
                targetModel.MapOnVectorF();
            }

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                // E3b: case-local AxialForceIncrement 経由で書込
                double increment;
                bool isVLCase_local = loadCase != null && loadCase.LoadName == "VL";
                if (isVLCase_local)
                {
                    // VL ケース: 地震時軸力増分なし (常時のみ)。SetAxialForceIncrement は 0
                    increment = 0.0;
                }
                else if (level == 1)
                {
                    // iLC が有効範囲か確認 (VL ケースで iLC=-1 になる対策)
                    increment = (pileLayoutItem.AxialForceLevel1s != null && iLC >= 0 && iLC < pileLayoutItem.AxialForceLevel1s.Count
                        ? pileLayoutItem.AxialForceLevel1s[iLC]
                        : (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional))
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional);
                    increment /= nStep;
                }
                else //(level == 2)
                {
                    increment = (pileLayoutItem.AxialForceLevel2s != null && iLC >= 0 && iLC < pileLayoutItem.AxialForceLevel2s.Count
                        ? pileLayoutItem.AxialForceLevel2s[iLC]
                        : (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional))
                        - (pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional);
                    increment /= nStep;
                }
                targetModel.SetAxialForceIncrement(pileLayoutItem, increment);
            }

        }

        // 地盤変位の更新
        // E3b: targetModel 引数を受取り、AnaModel ヘルパー経由で case-local な
        // Node を書換えるよう変更。主モデルでは従来通り InputModel 側 Node を更新、
        // case-local コピーでは snapshot 上の Node を更新する。
        private void UpdateSoilDisp(AnaModel targetModel)
        {
            // DoatsuGoryokuBaneの節点の地盤変位を更新
            var doatsuGoryokuBane = InputModel.ElementDivision.DoatsuGoryokuBane;
            if (doatsuGoryokuBane != null)
            {
                for (int i = 0; i < doatsuGoryokuBane.Items.Count; i++)
                {
                    var dgbItem = doatsuGoryokuBane.Items[i];
                    if (i == 0)
                    {
                        var topSoilNode = targetModel.GetDoatsuTopSoilNode(dgbItem);
                        if (topSoilNode != null)
                            topSoilNode.CumulativeForcedDisp += topSoilNode.IncrementalForcedDisp;
                    }
                    var btmSoilNode = targetModel.GetDoatsuBtmSoilNode(dgbItem);
                    if (btmSoilNode != null)
                        btmSoilNode.CumulativeForcedDisp += btmSoilNode.IncrementalForcedDisp;
                }
            }

            // PileLayoutItemsの節点の地盤変位を更新
            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                if (pileLayoutItem == null) continue;
                var soilNodes = targetModel.GetSoilNodes(pileLayoutItem);
                if (soilNodes == null) continue;
                foreach (var node in soilNodes)
                {
                    if (node?.IncrementalForcedDisp != null)
                    {
                        node.CumulativeForcedDisp += node.IncrementalForcedDisp;
                    }
                }
            }
        }

        // ばね剛性の安全化ヘルパ
        private static double SafeK(double v)
            => (double.IsFinite(v) && v > 0.0) ? v : 0.0;

        /// <summary>
        /// ペナルティばね（RotationalSpring・PenaltySprings）の両端相対変位を検証し、
        /// 全体変位に対して十分小さいことを確認する。
        /// 閾値を超えた場合はログに警告を出力する。
        /// </summary>
        private async Task VerifyPenaltySpringAccuracy(AnaModel model)
        {
            const double threshold = 0.001; // 0.1%
            if (model == null) return;

            // 全節点の最大変位を取得（基準値）
            double maxGlobalDisp = 0;
            double maxGlobalRot = 0;
            foreach (var node in model.Nodes)
            {
                var d = node.CumulativeDisp;
                if (d == null) continue;
                maxGlobalDisp = Math.Max(maxGlobalDisp, Math.Max(Math.Abs(d.Ux), Math.Max(Math.Abs(d.Uy), Math.Abs(d.Uz))));
                maxGlobalRot = Math.Max(maxGlobalRot, Math.Max(Math.Abs(d.Rx), Math.Max(Math.Abs(d.Ry), Math.Abs(d.Rz))));
            }

            if (maxGlobalDisp < 1e-15 && maxGlobalRot < 1e-15) return; // 変位がない

            var warnings = new List<string>();

            // RotationalSpring の検証
            if (model.RotationalSprings != null)
            {
                foreach (var rs in model.RotationalSprings)
                {
                    if (rs.NodeI?.CumulativeDisp == null || rs.NodeJ?.CumulativeDisp == null) continue;
                    var di = rs.NodeI.CumulativeDisp;
                    var dj = rs.NodeJ.CumulativeDisp;

                    // 並進方向の相対変位（ペナルティで拘束されている場合）
                    if (rs.TieUx && maxGlobalDisp > 1e-15)
                    {
                        double relUx = Math.Abs(di.Ux - dj.Ux);
                        if (relUx / maxGlobalDisp > threshold)
                            warnings.Add($"{rs.Name}: Ux相対変位={relUx:E3} ({relUx / maxGlobalDisp * 100:F2}%)");
                    }
                    if (rs.TieUy && maxGlobalDisp > 1e-15)
                    {
                        double relUy = Math.Abs(di.Uy - dj.Uy);
                        if (relUy / maxGlobalDisp > threshold)
                            warnings.Add($"{rs.Name}: Uy相対変位={relUy:E3} ({relUy / maxGlobalDisp * 100:F2}%)");
                    }
                    if (rs.TieUz && maxGlobalDisp > 1e-15)
                    {
                        double relUz = Math.Abs(di.Uz - dj.Uz);
                        if (relUz / maxGlobalDisp > threshold)
                            warnings.Add($"{rs.Name}: Uz相対変位={relUz:E3} ({relUz / maxGlobalDisp * 100:F2}%)");
                    }
                    if (rs.TieRz && maxGlobalRot > 1e-15)
                    {
                        double relRz = Math.Abs(di.Rz - dj.Rz);
                        if (relRz / maxGlobalRot > threshold)
                            warnings.Add($"{rs.Name}: Rz相対変位={relRz:E3} ({relRz / maxGlobalRot * 100:F2}%)");
                    }
                }
            }

            // PenaltySprings の検証
            if (model.PenaltySprings != null)
            {
                foreach (var ps in model.PenaltySprings)
                {
                    if (ps.NodeI?.CumulativeDisp == null || ps.NodeJ?.CumulativeDisp == null) continue;
                    var di = ps.NodeI.CumulativeDisp;
                    var dj = ps.NodeJ.CumulativeDisp;

                    double relDisp = Math.Sqrt(
                        Math.Pow(di.Ux - dj.Ux, 2) + Math.Pow(di.Uy - dj.Uy, 2) + Math.Pow(di.Uz - dj.Uz, 2));
                    if (maxGlobalDisp > 1e-15 && relDisp / maxGlobalDisp > threshold)
                        warnings.Add($"{ps.Name}: 相対変位={relDisp:E3} ({relDisp / maxGlobalDisp * 100:F2}%)");
                }
            }

            if (warnings.Count > 0)
            {
                await AddLogAsync($"⚠ ペナルティばね精度警告: {warnings.Count}件のばねで相対変位が閾値({threshold * 100:F1}%)を超えています");
                foreach (var w in warnings.Take(20))
                    await AddLogAsync($"  {w}");
                if (warnings.Count > 20)
                    await AddLogAsync($"  ...他{warnings.Count - 20}件");
                await AddLogAsync("  → ペナルティ定数(KBig)の増加を検討してください。");
            }
            else
            {
                await AddLogAsync("ペナルティばね精度検証: OK（全ばねで相対変位 < 0.1%）");
            }
        }

        private void PrepareKmat(int iLC, bool isTan, AnaModel model, out double springKMin, out double springKMax)
        {
            // 診断: ばね剛性の min/max を集計（out で呼び出し元に返す）
            double springMin = double.PositiveInfinity;
            double springMax = double.NegativeInfinity;

            // 土圧合力ばね
            //
            // v22 修正（バグ#1）: 隣接する DoatsuGoryoku Item は「内部節点のスプリング」を共有する
            // （AnalysisModelling.cs: Items[i+1].TopSpring === Items[i].BtmSpring）。
            // 旧実装は Items を素直に反復し SetKe を呼んでいたため、共有スプリングは後続 Item の
            // 半分面積 (DY × DZ × 0.5) で上書きされ、**もう一方の層の寄与が失われていた**。
            //
            // 結果として:
            //   - 内部節点の K/F が本来の ~半分になる（両層の半面積合算であるべきところ）
            //   - 構造側が土圧合力の一部しか受け取らず、解析結果が物理的に不正確
            //
            // 修正: 一旦各ユニークスプリングへの寄与を (kx, ky) ペアで集計し、最後に 1 回だけ SetKe する。
            // これで内部節点は「上層の下半分 + 下層の上半分」の寄与を正しく合算できる。
            if (InputModel.ElementDivision.DoatsuGoryokuBane != null)
            {
                var items = InputModel.ElementDivision.DoatsuGoryokuBane.Items;
                // ユニークスプリング → 累積 (kx, ky)
                var accum = new Dictionary<FEM.HorizontalSoilSpring, (double kx, double ky)>(ReferenceEqualityComparer.Instance);

                void AddContribution(FEM.HorizontalSoilSpring spring, double kx, double ky)
                {
                    if (spring == null) return;
                    if (accum.TryGetValue(spring, out var prev))
                        accum[spring] = (prev.kx + kx, prev.ky + ky);
                    else
                        accum[spring] = (kx, ky);
                }

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];

                    // E3b: case-local な Node/Spring を取得 (主モデルでは item.TopEmbedmentNode などと同一参照)
                    var topEmb = model.GetDoatsuTopEmbedmentNode(item);
                    var topSoil = model.GetDoatsuTopSoilNode(item);
                    var btmEmb = model.GetDoatsuBtmEmbedmentNode(item);
                    var btmSoil = model.GetDoatsuBtmSoilNode(item);
                    var topHs = model.GetDoatsuTopHorizontalSoilSpring(item);
                    var btmHs = model.GetDoatsuBtmHorizontalSoilSpring(item);

                    var relDispTop = topEmb.CumulativeDisp - topSoil.CumulativeDisp;
                    var kVecTop = isTan ? item.GetTangentStiffnessVector(relDispTop) : item.GetSecantStiffnessVector(relDispTop);
                    AddContribution(topHs, SafeK(kVecTop.Kx), SafeK(kVecTop.Ky));

                    var relDisplacementBtm = btmEmb.CumulativeDisp - btmSoil.CumulativeDisp;
                    var kVecBtm = isTan ? item.GetTangentStiffnessVector(relDisplacementBtm) : item.GetSecantStiffnessVector(relDisplacementBtm);
                    AddContribution(btmHs, SafeK(kVecBtm.Kx), SafeK(kVecBtm.Ky));
                }

                foreach (var kvp in accum)
                {
                    double kxTotal = kvp.Value.kx;
                    double kyTotal = kvp.Value.ky;
                    kvp.Key.SetKe(kxTotal, kyTotal, 0, 0, 0, 0, isTan);
                    springMin = Math.Min(springMin, Math.Min(kxTotal, kyTotal));
                    springMax = Math.Max(springMax, Math.Max(kxTotal, kyTotal));
                }
            }

            // 杭ばね
            //
            // v23 (B-1): 接線剛性では 2D Jacobian（交差項あり）を使用する。
            // 旧実装は K_tan を対角 (k, k) として set していたが、p–y 曲線の力は
            // f = p(|u|) × u/|u| の形で「変位方向に沿う」ため、非線形領域では
            // df/du は 対称 2x2 ブロック [[kxx, kxy],[kxy, kyy]] になる。
            // この真の Jacobian を使うことで Newton 方向が改善し α=1.0 に近い収束を期待できる。
            // 割線剛性（isTan=false）側は F_int = K_sec(|u|) × u（等方）で正しく力を表現するため
            // 従来通り対角で OK（secant × disp がそのまま force を返す性質を維持）。
            // 地盤 (p-y) 非線形モードはケース単位。caseModel に設定済みの値を使う。
            var soilMode = model.SoilNonlinearityMode;

            foreach (var pileLayoutItem in InputModel.PileLayoutItems)
            {
                var horizontalReactions = InputModel.ElementDivision.SoilPiles[pileLayoutItem.SoilPileAltNo - 1].HorizontalSoilReactions;
                // VL ケースは iLC=-1 となるため安全アクセス
                var isFrontPile = pileLayoutItem.IsFrontPiles != null && iLC >= 0 && iLC < pileLayoutItem.IsFrontPiles.Count
                    ? pileLayoutItem.IsFrontPiles[iLC] : false;

                // E3b: case-local な PileNodes / SoilNodes / HorizontalSoilSprings を取得
                var pileNodes = model.GetPileNodes(pileLayoutItem);
                var soilNodes = model.GetSoilNodes(pileLayoutItem);
                var pileSprings = model.GetPileHorizontalSoilSprings(pileLayoutItem);

                int reactionCount = horizontalReactions.Count;
                for (int i = 0; i < pileNodes.Count; i++)
                {
                    var pileNode = pileNodes[i];
                    var soilNode = soilNodes[i];
                    var relDisplacement = pileNode.CumulativeDisp - soilNode.CumulativeDisp;
                    // NaN防止
                    double abs = (double.IsFinite(relDisplacement.Ux) && double.IsFinite(relDisplacement.Uy))
                        ? Math.Sqrt(relDisplacement.Ux * relDisplacement.Ux + relDisplacement.Uy * relDisplacement.Uy)
                        : 0.0;

                    // 接線・割線両方を蓄積（2D Jacobian 用にどちらも必要）
                    double kTan = 0.0;
                    double kSec = 0.0;
                    if (i > 0 && i - 1 < reactionCount)
                    {
                        bool isTop = false;
                        kTan += horizontalReactions[i - 1].GetSoilTangentReactionCoefficient(abs, isTop, isFrontPile, soilMode);
                        kSec += horizontalReactions[i - 1].GetSoilSecantReactionCoefficient(abs, isTop, isFrontPile, soilMode);
                    }
                    if (i < pileNodes.Count - 1 && i < reactionCount)
                    {
                        bool isTop = true;
                        kTan += horizontalReactions[i].GetSoilTangentReactionCoefficient(abs, isTop, isFrontPile, soilMode);
                        kSec += horizontalReactions[i].GetSoilSecantReactionCoefficient(abs, isTop, isFrontPile, soilMode);
                    }

                    kTan = SafeK(kTan);
                    kSec = SafeK(kSec);

                    // 2026-05-06 簡素化: 単一節点で k=0 となるケース (砂質土の有効上載圧 σv'=0 等) は、
                    // 杭が他深さの地盤ばねで支持されているため解析の安定性に問題はない。
                    // ただし「杭全体で k=0」(杭全体が地表より上にある等) の極端なケースでは
                    // 剛性マトリクスが特異化して解析が解けなくなるため、最上端節点 (i=0) のみ
                    // 物理的に無視できる極小値 1e-3 kN/m で代用する保険的処置を残す。
                    if ((isTan ? kTan : kSec) <= 0.0 && i == 0)
                    {
                        const double KFloor = 1.0e-3; // kN/m, 物理的に無視できる値
                        if (kTan <= 0.0) kTan = KFloor;
                        if (kSec <= 0.0) kSec = KFloor;
                    }

                    var spring = pileSprings[i];

                    if (isTan)
                    {
                        // v23 (B-1) 接線剛性: 2D Jacobian
                        // |u| が極小なら方向が不定なので等方に縮退（従来通り）
                        double kxxDiag, kxyOff, kyyDiag;
                        if (abs < 1e-12)
                        {
                            kxxDiag = kTan; kyyDiag = kTan; kxyOff = 0.0;
                        }
                        else
                        {
                            double cosT = relDisplacement.Ux / abs;
                            double sinT = relDisplacement.Uy / abs;
                            kxxDiag = kTan * cosT * cosT + kSec * sinT * sinT;
                            kyyDiag = kTan * sinT * sinT + kSec * cosT * cosT;
                            kxyOff = (kTan - kSec) * cosT * sinT;
                        }
                        spring.SetKeWithXYCoupling(kxxDiag, kxyOff, kyyDiag, 0, 0, 0, 0, isTan: true);
                        springMin = Math.Min(springMin, Math.Min(kxxDiag, kyyDiag));
                        springMax = Math.Max(springMax, Math.Max(kxxDiag, kyyDiag));
                    }
                    else
                    {
                        // 割線剛性: 等方 (K_sec × disp がそのまま force を向ける)
                        spring.SetKe(kSec, kSec, 0, 0, 0, 0, isTan: false);
                        springMin = Math.Min(springMin, kSec);
                        springMax = Math.Max(springMax, kSec);
                    }
                }

                // 節点別 Z ばね (UsePsSpringAtPileTip 有効時): 沈下解析の物理関数を直接呼んで K を更新
                //   優先1: PileVerticalSoilSpringModel — 沈下解析と同じ τ-s + R-S 曲線をリアルタイム評価
                //   優先2: VerticalPileSpringCurve  — (δ, P) 履歴の線形補間 (フォールバック)
                // 沈下解析の節点別履歴 (NodeDisplacements/NodeReactions) が無い場合は
                // AnalysisModelling 側でばねが構築されず、ここでは空コレクションとなる。
                var vSprings = model.GetVerticalNodeSprings(pileLayoutItem);
                var vModels = pileLayoutItem.VerticalNodeSpringModels;
                var vCurves = pileLayoutItem.VerticalNodeSpringCurves;
                if (vSprings != null && vSprings.Count > 0)
                {
                    int nv = vSprings.Count;
                    if (vModels != null && vModels.Count > 0) nv = Math.Min(nv, vModels.Count);
                    if (vCurves != null && vCurves.Count > 0) nv = Math.Min(nv, vCurves.Count);
                    nv = Math.Min(nv, pileNodes.Count);
                    for (int k = 0; k < nv; k++)
                    {
                        var sp = vSprings[k];
                        if (sp == null) continue;
                        var pn = pileNodes[k];
                        var sn = soilNodes[k];
                        // FEM の相対変位 = pileNode.Uz - soilNode.Uz
                        // s > 0 = 沈下方向 (FEM の (pile-soil) Uz < 0 のとき s > 0)
                        double relUz = (double.IsFinite(pn.CumulativeDisp.Uz) ? pn.CumulativeDisp.Uz : 0.0)
                                     - (double.IsFinite(sn.CumulativeDisp.Uz) ? sn.CumulativeDisp.Uz : 0.0);
                        double s_rel = -relUz;

                        double kz;
                        var md = (vModels != null && k < vModels.Count) ? vModels[k] : null;
                        if (md != null)
                        {
                            kz = isTan ? md.GetTangentStiffness(s_rel) : md.GetSecantStiffness(s_rel);
                            if (!(double.IsFinite(kz) && kz > 0)) kz = md.InitialTangentStiffness;
                        }
                        else
                        {
                            var cv = (vCurves != null && k < vCurves.Count) ? vCurves[k] : null;
                            if (cv == null) continue;
                            kz = isTan ? cv.GetTangentStiffness(s_rel) : cv.GetSecantStiffness(s_rel);
                            if (!(double.IsFinite(kz) && kz > 0)) kz = Math.Max(cv.InitialTangentStiffness * 0.01, 1.0);
                        }
                        kz = SafeK(kz);
                        if (kz <= 0.0) kz = 1.0;
                        sp.SetKe(0, 0, kz, 0, 0, 0, isTan);
                        springMin = Math.Min(springMin, kz);
                        springMax = Math.Max(springMax, kz);
                    }
                }
            }

            // 追加: 杭頭 M-θ を RotationalSpring の Ke に反映
            if (model?.RotationalSprings != null && model.RotationalSprings.Count > 0)
            {
                // M-θ曲線から接線/割線剛性を評価（クランプなし: K/F整合性を保つ）
                foreach (var pile in InputModel.PileLayoutItems)
                {
                    // E3b: case-local な RotationalSpring を取得
                    var rxy = model.GetPileTopRotationalSpring(pile);
                    if (rxy == null) continue;

                    var pileHeadNode = rxy.NodeJ;
                    var capNode = rxy.NodeI;
                    double dRx = (pileHeadNode.CumulativeDisp?.Rx ?? 0.0) - (capNode.CumulativeDisp?.Rx ?? 0.0);
                    double dRy = (pileHeadNode.CumulativeDisp?.Ry ?? 0.0) - (capNode.CumulativeDisp?.Ry ?? 0.0);

                    double kRx = 0.0, kRy = 0.0;
                    double kRxy = 0.0;       // v28 問題 B: Rx-Ry off-diagonal (2D Jacobian)
                    bool useRxRyCoupling = false;
                    if (rxy.Mode == RotationalSpringMode.CombinedXY)
                    {
                        const double KBigRigid = 1e10;  // SetupNonlinearMThetaForLoadCase の KBig と同値

                        // v28 アプローチ I: 場所打ち RC 杭 post-crack で方向ロック + ヒステリシス
                        if (rxy.McrXY.HasValue && rxy.HasCrackedXY
                            && rxy.CrackNx.HasValue && rxy.CrackNy.HasValue && rxy.CurveXY != null)
                        {
                            double nx = rxy.CrackNx.Value;
                            double ny = rxy.CrackNy.Value;
                            // n 方向への投影 (符号付き): forward なら +、reverse なら -
                            double thetaProj = dRx * nx + dRy * ny;

                            // ヒステリシス: θ_proj_max の更新 (前進時のみ大きくなる)
                            if (thetaProj > rxy.ThetaProjMax) rxy.ThetaProjMax = thetaProj;

                            // 2026-05-06 (A): forward / unloading branch の K_tan が境界 (thetaProj = thetaMax) で
                            // 100× ジャンプして Newton 方向を毎反復激変させる問題の対策。
                            // K_tan を境界周辺で smooth blend し連続化する (K_sec は元々連続なので変更不要)。
                            //   forward (thetaProj >= thetaMax)               → K_post_crack
                            //   transition (thetaMax - δ <= thetaProj < thetaMax) → smoothstep blend
                            //   pure unloading (thetaProj < thetaMax - δ)      → K_unload_jac
                            // δ = max(5% × thetaMax, 1e-6) の幅で smoothstep (3t² − 2t³)。
                            //
                            double thetaMax = rxy.ThetaProjMax;
                            double mMaxLock = rxy.CurveXY.EvaluateMoment(thetaMax);
                            double kUnload = (thetaMax > 1e-15)
                                ? SafeK(Math.Abs(mMaxLock) / thetaMax)
                                : SafeK(rxy.CurveXY.EvaluateTangent(0));

                            double kParTan, kParSec;  // n 方向の接線/割線剛性
                            if (thetaProj >= thetaMax - 1e-15)
                            {
                                // 前進: post-crack curve (1e-8 の急勾配はバイパス)
                                double absProj = Math.Abs(thetaProj);
                                kParTan = SafeK(rxy.CurveXY.EvaluatePostCrackTangent(absProj));
                                kParSec = SafeK(rxy.CurveXY.EvaluateSecant(absProj));
                            }
                            else
                            {
                                // 除荷: K_sec は線形戻り (M_max/thetaMax)、K_tan は境界周辺で smooth blend
                                kParSec = kUnload;
                                double delta = thetaMax - thetaProj;  // > 0 in unloading
                                double transitionWidth = Math.Max(0.05 * Math.Abs(thetaMax), 1e-6);
                                if (delta < transitionWidth && transitionWidth > 0.0)
                                {
                                    // smooth blend: K_post_crack (delta=0) → K_unload (delta=transitionWidth)
                                    double t = delta / transitionWidth;
                                    double s = t * t * (3.0 - 2.0 * t);  // smoothstep
                                    double kFwdAtBoundary = SafeK(rxy.CurveXY.EvaluatePostCrackTangent(thetaMax));
                                    kParTan = (1.0 - s) * kFwdAtBoundary + s * kUnload;
                                }
                                else
                                {
                                    kParTan = kUnload;
                                }
                            }

                            // ランク 1 + 小さな直交剛性 (数値的安定化)
                            // n 方向: kParallel (剛性フル)
                            // 直交方向: kPerp = kParallel × 0.05 (5%, 特異行列防止)
                            const double PERP_RATIO = 0.05;
                            double kParallel = isTan ? kParTan : kParSec;
                            double kPerp = kParallel * PERP_RATIO;
                            double nx2 = nx * nx;
                            double ny2 = ny * ny;
                            double nxy = nx * ny;
                            kRx = kParallel * nx2 + kPerp * ny2;    // kRxx
                            kRy = kParallel * ny2 + kPerp * nx2;    // kRyy
                            kRxy = (kParallel - kPerp) * nxy;        // off-diagonal
                            useRxRyCoupling = true;
                        }
                        else
                        {
                            // 未クラック / 他杭種: 従来の等方モデル (2D Jacobian)
                            double theta = Math.Sqrt(dRx * dRx + dRy * dRy);
                            double kTanIso, kSecIso;
                            if (rxy.CurveXY != null)
                            {
                                if (rxy.McrXY.HasValue && !rxy.HasCrackedXY)
                                {
                                    kTanIso = KBigRigid;
                                    kSecIso = KBigRigid;
                                }
                                else
                                {
                                    kTanIso = SafeK(rxy.CurveXY.EvaluateTangent(theta));
                                    kSecIso = SafeK(rxy.CurveXY.EvaluateSecant(theta));
                                }
                            }
                            else
                            {
                                kTanIso = kSecIso = SafeK(rxy.KthetaXY ?? 0.0);
                            }

                            // isotropic 2D Jacobian: K_tan ≠ K_sec で off-diagonal 発生
                            if (isTan && theta >= 1e-12 && Math.Abs(kTanIso - kSecIso) > 1e-6 * Math.Max(kTanIso, kSecIso))
                            {
                                double cosA = dRx / theta;
                                double sinA = dRy / theta;
                                double cos2 = cosA * cosA;
                                double sin2 = sinA * sinA;
                                kRx = kTanIso * cos2 + kSecIso * sin2;
                                kRy = kTanIso * sin2 + kSecIso * cos2;
                                kRxy = (kTanIso - kSecIso) * cosA * sinA;
                                useRxRyCoupling = true;
                            }
                            else
                            {
                                double k = isTan ? kTanIso : kSecIso;
                                kRx = k; kRy = k; kRxy = 0.0;
                            }
                        }
                    }
                    else
                    {
                        // SingleDof モード
                        if (rxy.Dof == RotationalDof.Rx)
                        {
                            double k;
                            if (rxy.Curve != null)
                            {
                                k = isTan
                                    ? SafeK(rxy.Curve.EvaluateTangent(dRx))
                                    : SafeK(rxy.Curve.EvaluateSecant(dRx));
                            }
                            else
                            {
                                k = SafeK(rxy.Ktheta ?? 0.0);
                            }
                            kRx = k;
                        }
                        else if (rxy.Dof == RotationalDof.Ry)
                        {
                            double k;
                            if (rxy.Curve != null)
                            {
                                k = isTan
                                    ? SafeK(rxy.Curve.EvaluateTangent(dRy))
                                    : SafeK(rxy.Curve.EvaluateSecant(dRy));
                            }
                            else
                            {
                                k = SafeK(rxy.Ktheta ?? 0.0);
                            }
                            kRy = k;
                        }
                    }

                    // 並進(Ux,Uy,Uz)・Rz の拘束:
                    // PileNode-0 は CapNode の master-slave として拘束されるべきだが、
                    // Boundaryの設定タイミングにより拘束が不完全な場合がある。
                    // そのため常にペナルティ剛性を適用して安全にする。
                    // Uz はペナルティのみで CapNode.Uz に追従させるため、
                    // Beam 軸剛性 EA/L (~2.7E+8) に対して十分大きい値が必要。
                    // Kbig=1e8 では EA/L の36%しかなく収束が100反復以上必要だった。
                    const double KBig = 1e8;
                    double kx = rxy.TieUx ? KBig : 0.0;
                    double ky = rxy.TieUy ? KBig : 0.0;
                    double kz = rxy.TieUz ? KBig : 0.0;
                    double kRz = rxy.TieRz ? KBig : 0.0;
                    // Rx/Ry は M–θ に基づき算出した kRx/kRy を用いる
                    // v28 問題 B: 2D Jacobian 有効時は Rx-Ry off-diagonal 付き K を使用
                    if (useRxRyCoupling)
                        rxy.SetKeWithRxRyCoupling(kx, ky, kz, kRx, kRxy, kRy, kRz, isTan);
                    else
                        rxy.SetKe(kx, ky, kz, kRx, kRy, kRz, isTan);
                }
            }

            springKMin = double.IsInfinity(springMin) ? double.NaN : springMin;
            springKMax = double.IsNegativeInfinity(springMax) ? double.NaN : springMax;
        }


        // ばね剛性の安全化ヘルパ
        //private static double SafeK(double v)
        //    => (double.IsFinite(v) && v > 0.0) ? v : 0.0;

        // 変更: ラッパーに model 引数を追加
        private void PrepareKTanMat(int iLC, AnaModel model) => PrepareKmat(iLC, true, model, out _, out _);
        private void PrepareKSecMat(int iLC, AnaModel model) => PrepareKmat(iLC, false, model, out _, out _);

        // 既存: K組立本体（model を受け取る版）
        //private void PrepareKMat(int iLC, bool isTan, AnaModel model)

    }
}
