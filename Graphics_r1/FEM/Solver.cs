using MathNet.Numerics.LinearAlgebra;
using System;

namespace PileDesign.FEM
{
    internal class Solver
    {
        // ソルバ（緩和係数対応版）
        public static void SolveDisp(AnaModel anaModel, double relaxationFactor = 1.0)
        {
            anaModel.SetForcedDispOnLoadVectorAndStiffnessMatrix(true); // KAA_tanとVectorRを取得

            /* --- NaN診断: ソルバ入力を検査 ---
            bool kHasNaN = NaNDiagnostics.CheckMatrixDiag(anaModel.KAA_tan, "KAA_tan (pre-solve)", anaModel);
            bool rHasNaN = NaNDiagnostics.CheckVector(anaModel.VectorR, "VectorR (pre-solve)", anaModel);
            if (kHasNaN || rHasNaN)
            {
                int kNaNCount = NaNDiagnostics.CountMatrixNaN(anaModel.KAA_tan);
                NaNDiagnostics.LogNaN($"Solver input contaminated: K has {kNaNCount} NaN entries, R has NaN={rHasNaN}");
            } */

            // CSparse で一次方程式を解く（MathNetと同じく K Δd = R）
            Vector<double> incrementalDispVector;
            try
            {
                // SparseQR を用いるため一般行列として全成分を投入（isSpd: false）
                var x = CsparseLinearSolver.Solve(anaModel.KAA_tan, anaModel.VectorR, isSpd: false);
                incrementalDispVector = Vector<double>.Build.DenseOfArray(x);
            }
            catch (Exception csparseEx)
            {
                // フォールバック（MathNet と完全同等の経路）
                try
                {
                    incrementalDispVector = anaModel.KAA_tan.Solve(anaModel.VectorR);
                }
                catch (Exception mathnetEx)
                {
                    throw new InvalidOperationException(
                        $"連立方程式の求解に失敗しました。モデルが不安定な可能性があります。\n" +
                        $"CSparse: {csparseEx.Message}\nMathNet: {mathnetEx.Message}", mathnetEx);
                }
            }

            // 緩和係数を適用（1.0未満で収束を安定化）
            if (relaxationFactor < 1.0 && relaxationFactor > 0.0)
            {
                incrementalDispVector = incrementalDispVector * relaxationFactor;
            }

            // NaN/Infinity 検出: ソルバ出力にNaNが含まれる場合はゼロにクランプ
            // （特異行列や数値不安定時に発生しうる）
            bool hasNaN = false; // NaNDiagnostics.CheckVector(incrementalDispVector, "incrementalDispVector (post-solve)", anaModel);
            if (hasNaN)
            {
                // NaN成分をゼロにクランプ
                for (int idx = 0; idx < incrementalDispVector.Count; idx++)
                {
                    if (!double.IsFinite(incrementalDispVector[idx]))
                        incrementalDispVector[idx] = 0.0;
                }
            }

            // 変位増分制限（発散防止のためのライン検索簡易版）
            // 増分変位が大きすぎる場合はスケールダウン
            // 注意：制限が厳しすぎると収束を妨げる可能性あり
            const double maxDispIncrement = 0.05; // 最大増分 50mm = 0.05m に緩和
            double maxAbsIncrement = incrementalDispVector.AbsoluteMaximum();
            if (maxAbsIncrement > maxDispIncrement)
            {
                double scaleFactor = maxDispIncrement / maxAbsIncrement;
                incrementalDispVector = incrementalDispVector * scaleFactor;
            }

            anaModel.SetDispVector(incrementalDispVector);

            // 節点変位を結果用オブジェクトにセット
            foreach (var node in anaModel.Nodes)
            {
                //(Node[] masterNodes, Vector3D armVector) = anaModel.GetMasterNodesArmVector(node);

                double[] ddisp = new double[6];

                // クロス項の定義: (クロス先index, arm成分, 符号)
                (int crossIdx, Func<Vector3S, double> arm, double sign)[][] crossTerms =
                [
                    [(4, v => v.Z, 1.0), (5, v => v.Y, -1.0)], // 0: Ux
                    [(5, v => v.X, 1.0), (3, v => v.Z, -1.0)], // 1: Uy
                    [(3, v => v.Y, 1.0), (4, v => v.X, -1.0)], // 2: Uz
                    [], [], [], // 3: Rx // 4: Ry // 5: Rz
                ];

                for (int i = 0; i < 6; i++)
                {
                    int e_num = node.EquationNumber[i];
                    if (node.MasterNodes[i] != null)
                    {
                        int eq = node.MasterNodes[i].EquationNumber[i];
                        ddisp[i] = eq < 0 ? 0 : incrementalDispVector[eq];

                        // クロス項の加算（剛体運動学: Uz += Rx×ΔY - Ry×ΔX 等）
                        // クロス先DOFもslaveの場合のみ追加（TransferMatrixと整合）
                        foreach (var (crossIdx, arm, sign) in crossTerms[i])
                        {
                            if (node.MasterNodes[crossIdx] != null)
                            {
                                int eqCross = node.MasterNodes[crossIdx].EquationNumber[crossIdx];
                                if (eqCross >= 0)
                                    ddisp[i] += incrementalDispVector[eqCross] * arm(node.SlaveArm) * sign;
                            }
                        }
                    }
                    else if (e_num >= 0)
                    {
                        ddisp[i] = incrementalDispVector[e_num];
                    }
                    else
                    {
                        ddisp[i] = 0;
                    }
                }

                // ddisp: double[6]  (新しい増分変位)
                double[] cum =
                [
                    node.CumulativeDisp.Ux + ddisp[0],
                    node.CumulativeDisp.Uy + ddisp[1],
                    node.CumulativeDisp.Uz + ddisp[2],
                    node.CumulativeDisp.Rx + ddisp[3],
                    node.CumulativeDisp.Ry + ddisp[4],
                    node.CumulativeDisp.Rz + ddisp[5]
                ];

                node.IncrementalDisp = new NodeDisp(ddisp[0], ddisp[1], ddisp[2], ddisp[3], ddisp[4], ddisp[5]);
                node.CumulativeDisp = new NodeDisp(cum[0], cum[1], cum[2], cum[3], cum[4], cum[5]);
            }
        }

    }
}
