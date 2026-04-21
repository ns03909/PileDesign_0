using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PileDesign.FEM
{
    public enum RotationalDof { Rx, Ry, Rz }
    public enum RotationalSpringMode { SingleDof, CombinedXY }

    // M-θ曲線（タプル名の有無に依存しない実装）
    public sealed class MomentRotationCurve
    {
        public List<(double Theta, double Moment)> Points { get; } = [];

        public MomentRotationCurve() { }
        public MomentRotationCurve(IEnumerable<(double theta, double moment)> points)
        {
            if (points == null) return;
            foreach (var (t, m) in points) Points.Add((t, m));
            Points.Sort((a, b) => a.Theta.CompareTo(b.Theta));
        }

        public double EvaluateMoment(double theta)
        {
            if (Points.Count == 0) return 0.0;
            double t = Math.Abs(theta);
            // 杭頭 M-θ 曲線は原点対称（奇関数）を仮定。
            // counter-loading（βU=-1 等）で θ が負側へ振れたときに正しく -M を返すため
            // 入力の符号を保持し末尾で乗じる。
            double sign = theta < 0.0 ? -1.0 : 1.0;
            if (Points.Count == 1) return sign * InterpFromOrigin(Points[0], t);
            if (t <= Points[0].Theta) return sign * InterpFromOrigin(Points[0], t);
            if (t >= Points[^1].Theta) return sign * Points[^1].Moment;

            int idx = FindSegmentIndex(t);
            var a = (Theta: (idx == 0 ? 0.0 : Points[idx - 1].Theta), Moment: (idx == 0 ? 0.0 : Points[idx - 1].Moment));
            var b = Points[idx];
            return sign * Lerp(a, b, t);
        }

        public double EvaluateTangent(double theta)
        {
            if (Points.Count == 0) return 0.0;
            double t = Math.Abs(theta);
            double result;
            string region = "";

            if (Points.Count == 1)
            {
                result = SlopeFromOrigin(Points[0]);
                region = "single_point";
            }
            // 原点からの初期接線剛性: 最初の点が原点(0,0)の場合は次の点との傾きを使用
            else if (t <= Points[0].Theta)
            {
                if (Points[0].Theta <= 1e-12 && Points.Count >= 2)
                {
                    // 最初の点が原点の場合、原点と次の点との傾きを返す
                    result = SafeSlope(Points[0], Points[1]);
                    region = "origin→1";
                }
                else
                {
                    result = SlopeFromOrigin(Points[0]);
                    region = "slope_from_origin";
                }
            }
            else if (t >= Points[^1].Theta)
            {
                // EvaluateMoment は θ > θ_last で M = M_last（定数）を返すため、
                // 接線剛性もゼロでなければ K/F 不整合になる
                result = 0.0;
                region = "above_last_flat";
            }
            else
            {
                // 区分定数: M(θ) が区分線形なので dM/dθ はセグメントの傾き（定数）。
                // ブレンドすると EvaluateMoment（ブレンドなし）と不整合になり
                // K_tan ≠ dF/dd → Newton-Raphson 収束停滞の原因になる。
                int idx = FindSegmentIndex(t);
                var a = (Theta: (idx == 0 ? 0.0 : Points[idx - 1].Theta), Moment: (idx == 0 ? 0.0 : Points[idx - 1].Moment));
                var b = Points[idx];
                result = SafeSlope(a, b);
            }

            return result;
        }

        // 割線剛性: |M|/|θ|（内力計算用、常に正）
        // θ→0では初期接線剛性に収束
        public double EvaluateSecant(double theta)
        {
            double t = Math.Abs(theta);
            // θがゼロに近い場合は初期接線剛性を返す（ゼロ除算回避）
            // 閾値を1e-9に引き上げて数値安定性を向上
            if (t < 1e-9)
            {
                double tan0 = EvaluateTangent(0.0);
                return tan0;
            }
            // EvaluateMoment は奇関数として符号付きで返すため、セカント剛性として
            // 正のスカラー（EI_eff 相当）を維持するには絶対値を取る必要がある。
            double M = EvaluateMoment(theta);
            double secant = Math.Abs(M) / t;
            return secant;
        }

        private static double Lerp((double Theta, double Moment) a, (double Theta, double Moment) b, double t)
        {
            double dt = b.Theta - a.Theta;
            if (Math.Abs(dt) < 1e-20) return b.Moment;
            double u = (t - a.Theta) / dt;
            return a.Moment * (1.0 - u) + b.Moment * u;
        }
        private static double SafeSlope((double Theta, double Moment) a, (double Theta, double Moment) b)
        {
            double dt = b.Theta - a.Theta;
            if (Math.Abs(dt) < 1e-20) return 0.0;
            return (b.Moment - a.Moment) / dt;
        }
        private static double InterpFromOrigin((double Theta, double Moment) p, double t)
        {
            if (p.Theta <= 0.0) return 0.0;
            return (p.Moment / p.Theta) * t;
        }
        private static double SlopeFromOrigin((double Theta, double Moment) p)
        {
            if (p.Theta <= 0.0) return 0.0;
            return p.Moment / p.Theta;
        }
        private int FindSegmentIndex(double t)
        {
            int lo = 1, hi = Points.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (t <= Points[mid].Theta) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        // グラフ用に配列へ展開（θ>=0 の定義点のみ）
        public (double[] thetas, double[] moments) ToArrays()
        {
            if (Points.Count == 0) return (Array.Empty<double>(), Array.Empty<double>());
            var listTheta = new List<double>(Points.Count);
            var listMoment = new List<double>(Points.Count);
            foreach (var (t, m) in Points)
            {
                if (double.IsFinite(t) && double.IsFinite(m) && t >= 0.0)
                {
                    listTheta.Add(t);
                    listMoment.Add(m);
                }
            }
            return (listTheta.ToArray(), listMoment.ToArray());
        }
    }

    // 共通ベースを継承して HorizontalSoilSpring と同一経路でマッピング可能にする
    public class RotationalSpring : TwoNodeSpringElement
    {

        public List<RotationalSpringResult> RotationalSpringResults { get; set; } = [];

        public BeamDisp IncrementalDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamDisp CumulativeDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce IncrementalForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce CumulativeForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        // 並進+Rz を剛結したい場合に使用（Ke作成は PrepareKmat 側で行う想定）
        public bool TieUx { get; set; } = true;
        public bool TieUy { get; set; } = true;
        public bool TieUz { get; set; } = true;
        public bool TieRz { get; set; } = true;
        [System.Text.Json.Serialization.JsonIgnore]  // JSONから古い値が読み込まれないようにする
        public double Kbig { get; set; } = 1e8;  // CapNode-PileNode間のペナルティ剛性

        public RotationalSpringMode Mode { get; set; } = RotationalSpringMode.CombinedXY;
        public RotationalDof Dof { get; set; } = RotationalDof.Rx;

        public MomentRotationCurve? Curve { get; set; }     // SingleDof
        public MomentRotationCurve? CurveXY { get; set; }   // CombinedXY
        public double? Ktheta { get; set; }                 // SingleDof 線形K
        public double? KthetaXY { get; set; }               // CombinedXY 線形K

        public int? PileBodyNo { get; set; }

        public bool IsNonlinear =>
            (Mode == RotationalSpringMode.SingleDof && Curve is not null) ||
            (Mode == RotationalSpringMode.CombinedXY && CurveXY is not null);

        public RotationalSpring() { }

        // 既定（CombinedXY想定）
        public RotationalSpring(string name, Node nodeI, Node nodeJ) : base(name, nodeI, nodeJ) { }

        // CombinedXY: 線形剛性指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, double kthetaXY) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.CombinedXY;
            KthetaXY = kthetaXY;
        }

        // CombinedXY: 曲線指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, MomentRotationCurve curveXY) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.CombinedXY;
            CurveXY = curveXY ?? throw new ArgumentNullException(nameof(curveXY));
        }

        // SingleDof: 線形剛性指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, RotationalDof dof, double ktheta) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.SingleDof;
            Dof = dof;
            Ktheta = ktheta;
        }

        // SingleDof: 曲線指定
        public RotationalSpring(string name, Node nodeI, Node nodeJ, RotationalDof dof, MomentRotationCurve curve) : base(name, nodeI, nodeJ)
        {
            Mode = RotationalSpringMode.SingleDof;
            Dof = dof;
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
        }


        public void SetCurve(MomentRotationCurve? curve)
        {
            Curve = curve;
        }

        // 結果格納ラッパ
        public void SetBeamDispAndForce(bool isTan = false)
        {
            base.SetBeamDispAndForce(isTan, CumulativeDisp, CumulativeForce);
        }

        /// <summary>
        /// co-located ノード（CapNode と PileNode-0）前提の剛性アセンブリ。
        /// 基底クラスの T^T * K * T 変換をバイパスし、master chain を辿った
        /// resolved equation numbers で直接アセンブリする。
        /// これにより CapNode と PileNode-0 の TransferMatrix の非対称性に起因する
        /// スプリアス項（Kbig × arm² が ActionPoint の回転DOFに漏れる問題）を回避する。
        /// </summary>
        public new Matrix<double> MapOnGlobalStiff(Matrix<double> K, bool isTan, bool isRowFree, bool isColFree)
        {
            var ke = (isTan ? KeTan : KeSec)
                ?? throw new InvalidOperationException("SetKe が未実行です。");

            // ResolvedDofMap が利用可能なら scatter 方式を使用
            bool useResolvedMap = NodeI?.ResolvedDofMap != null && NodeJ?.ResolvedDofMap != null;
            if (useResolvedMap)
            {
                DofTerm[][] maps = new DofTerm[12][];
                for (int d = 0; d < 6; d++) maps[d] = NodeI.ResolvedDofMap[d] ?? [];
                for (int d = 0; d < 6; d++) maps[6 + d] = NodeJ.ResolvedDofMap[d] ?? [];

                for (int i = 0; i < 12; i++)
                {
                    var termsI = maps[i];
                    if (termsI.Length == 0) continue;
                    for (int j = 0; j < 12; j++)
                    {
                        double val = ke[i, j];
                        if (val == 0.0) continue;
                        var termsJ = maps[j];
                        if (termsJ.Length == 0) continue;
                        foreach (var ti in termsI)
                        {
                            if ((isRowFree && ti.Eq < 0) || (!isRowFree && ti.Eq >= 0)) continue;
                            foreach (var tj in termsJ)
                            {
                                if ((isColFree && tj.Eq < 0) || (!isColFree && tj.Eq >= 0)) continue;
                                K[ti.Eq, tj.Eq] += ti.Coeff * val * tj.Coeff;
                            }
                        }
                    }
                }
            }
            else
            {
                // フォールバック: 従来の直接アセンブリ
                var eq = ResolveEquationNumbers();
                for (int i = 0; i < 12; i++)
                {
                    int eqRow = eq[i];
                    if ((isRowFree && eqRow < 0) || (!isRowFree && eqRow >= 0)) continue;
                    for (int j = 0; j < 12; j++)
                    {
                        int eqCol = eq[j];
                        if ((isColFree && eqCol < 0) || (!isColFree && eqCol >= 0)) continue;
                        double val = ke[i, j];
                        if (val != 0.0) K[eqRow, eqCol] += val;
                    }
                }
            }

            return K;
        }

        /// <summary>
        /// PileNode.Uz(free) と AP.Rx/Ry の間接結合を K 行列に追加。
        /// ペナルティ→CapNode→RigidLink→AP チェーンの剛リンク近似。
        /// </summary>
        private void AddUzIndirectCouplingToK(Matrix<double> K, bool isTan)
        {
            // PileNode (NodeJ) の Uz が free (master 未設定) の場合のみ
            // NOTE: このクラスには `Dof` という名のプロパティ（RotationalDof 型）があるため、
            // Node 側の DOF 列挙子を参照するときは完全修飾 PileDesign.FEM.Dof を使う
            if (NodeJ.HasMasterAt(PileDesign.FEM.Dof.Uz)) return;

            // AP ノードを Ux/Uy/Rz いずれかの master から取得
            Node? ap = NodeJ.GetMaster(PileDesign.FEM.Dof.Ux)
                       ?? NodeJ.GetMaster(PileDesign.FEM.Dof.Uy)
                       ?? NodeJ.GetMaster(PileDesign.FEM.Dof.Rz);
            if (ap == null) return;

            var ke = isTan ? KeTan : KeSec;
            if (ke == null) return;
            double kz = ke[8, 8]; // Uz ペナルティ剛性
            if (kz == 0.0) return;

            double dY = NodeJ.SlaveArm.Y;
            double dX = NodeJ.SlaveArm.X;
            if (Math.Abs(dX) < 1e-10 && Math.Abs(dY) < 1e-10) return;

            int eqPileUz = NodeJ.EquationNumber[2]; // PileNode.Uz (free DOF)
            int eqApRx = ap.EquationNumber[3];
            int eqApRy = ap.EquationNumber[4];

            void AddSym(int r, int c, double v)
            {
                if (r >= 0 && c >= 0 && v != 0.0)
                {
                    K[r, c] += v;
                    if (r != c) K[c, r] += v;
                }
            }

            // d(f[PileNode.Uz])/d(AP.Rx) = -kz × ΔY
            AddSym(eqPileUz, eqApRx, -kz * dY);
            // d(f[PileNode.Uz])/d(AP.Ry) = kz × ΔX
            AddSym(eqPileUz, eqApRy, kz * dX);

            // AP.Rx/Ry 対角: d(f[AP.Rx])/d(AP.Rx) from penalty chain
            if (eqApRx >= 0) K[eqApRx, eqApRx] += kz * dY * dY;
            if (eqApRy >= 0) K[eqApRy, eqApRy] += kz * dX * dX;
            AddSym(eqApRx, eqApRy, -kz * dX * dY);
        }



        /// <summary>
        /// co-located ノード前提の内力アセンブリ。
        /// T^T * f 変換をバイパスし、resolved equation numbers で直接加算する。
        /// </summary>
        public void AssembleInternalForceToGlobal(Vector<double> vectorT)
        {
            var f = CumulativeForce.GetVector();

            // ResolvedDofMap が利用可能なら scatter 方式
            bool useResolvedMap = NodeI?.ResolvedDofMap != null && NodeJ?.ResolvedDofMap != null;
            if (useResolvedMap)
            {
                for (int i = 0; i < 12; i++)
                {
                    double fval = f[i];
                    if (fval == 0.0) continue;
                    int dof = i % 6;
                    var node = i < 6 ? NodeI : NodeJ;
                    var terms = node.ResolvedDofMap[dof];
                    if (terms == null) continue;
                    foreach (var term in terms)
                    {
                        if (term.Eq >= 0)
                            vectorT[term.Eq] += term.Coeff * fval;
                    }
                }
            }
            else
            {
                // フォールバック
                var eq = ResolveEquationNumbers();
                for (int i = 0; i < 12; i++)
                {
                    if (eq[i] >= 0)
                        vectorT[eq[i]] += f[i];
                }
            }
        }

        /// <summary>
        /// master chain を辿って 12 個の方程式番号を解決する。
        /// CapNode のように全DOFがスレーブの場合、master（ActionPoint）の方程式番号を返す。
        /// </summary>
        private List<int> ResolveEquationNumbers()
        {
            var eq = new List<int>(12);
            ResolveNodeDofs(eq, NodeI);
            ResolveNodeDofs(eq, NodeJ);
            return eq;
        }

        private static void ResolveNodeDofs(List<int> eq, Node node)
        {
            for (int dof = 0; dof < 6; dof++)
            {
                var master = node.MasterNodes[dof];
                if (master != null)
                    eq.Add(master.EquationNumber[dof]);
                else
                    eq.Add(node.EquationNumber[dof]);
            }
        }

        public RotationalSpring DeepCopy()
        {
            var nodeICopy = NodeI?.DeepCopy();
            var nodeJCopy = NodeJ?.DeepCopy();

            var copy = new RotationalSpring(Name, nodeICopy, nodeJCopy)
            {
                IncrementalDisp = this.IncrementalDisp?.Clone(),
                CumulativeDisp = this.CumulativeDisp?.Clone(),
                IncrementalForce = this.IncrementalForce?.Clone(),
                CumulativeForce = this.CumulativeForce?.Clone(),
                Mode = this.Mode,
                Dof = this.Dof,
                Curve = this.Curve,            // 参照そのまま（必要ならDeepCopyへ拡張）
                CurveXY = this.CurveXY,
                Ktheta = this.Ktheta,
                KthetaXY = this.KthetaXY,
                PileBodyNo = this.PileBodyNo,
                TieUx = this.TieUx,
                TieUy = this.TieUy,
                TieUz = this.TieUz,
                TieRz = this.TieRz,
                Kbig = this.Kbig,
            };

            if (this.KeTan != null) copy.SetKeFromMatrix(this.KeTan, isTan: true);
            if (this.KeSec != null) copy.SetKeFromMatrix(this.KeSec, isTan: false);

            return copy;
        }

        private void SetKeFromMatrix(Matrix<double> ke, bool isTan)
        {
            double kx = ke[0, 0];
            double ky = ke[1, 1];
            double kz = ke[2, 2];
            double kRx = ke[3, 3];
            double kRy = ke[4, 4];
            double kRz = ke[5, 5];
            base.SetKe(kx, ky, kz, kRx, kRy, kRz, isTan);
        }
    }
}