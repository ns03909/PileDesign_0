using MathNet.Numerics.LinearAlgebra;

namespace PileDesign.FEM
{
    // 2節点ばね要素の共通実装
    public abstract class TwoNodeSpringElement
    {
        // シリアライズ/デザイナのため public set を許容
        public string Name { get; set; }
        public Node NodeI { get; set; }
        public Node NodeJ { get; set; }

        public Matrix<double> KeTan { get; protected set; }
        public Matrix<double> KeSec { get; protected set; }

        // シリアライザ用（後から Name/NodeI/NodeJ をセット可能）
        protected TwoNodeSpringElement() { }

        protected TwoNodeSpringElement(string name, Node nodeI, Node nodeJ)
        {
            Name = name ?? throw new System.ArgumentNullException(nameof(name));
            NodeI = nodeI ?? throw new System.ArgumentNullException(nameof(nodeI));
            NodeJ = nodeJ ?? throw new System.ArgumentNullException(nameof(nodeJ));
        }

        // 12x12 の [k -k; -k k] 形式で設定（Ux,Uy,Uz,Rx,Ry,Rz の6成分）
        public void SetKe(double kx, double ky, double kz, double kRx, double kRy, double kRz, bool isTan)
        {
            var ke = Matrix<double>.Build.DenseOfArray(new double[,]
            {
                { kx,  0,   0,   0,   0,   0,  -kx,  0,   0,   0,   0,   0 },
                { 0,   ky,  0,   0,   0,   0,   0,  -ky,  0,   0,   0,   0 },
                { 0,   0,   kz,  0,   0,   0,   0,   0,  -kz,  0,   0,   0 },
                { 0,   0,   0,  kRx,  0,   0,   0,   0,   0,  -kRx, 0,   0 },
                { 0,   0,   0,   0,  kRy,  0,   0,   0,   0,   0,  -kRy, 0 },
                { 0,   0,   0,   0,   0,  kRz,  0,   0,   0,   0,   0,  -kRz},
                { -kx,  0,   0,   0,   0,   0,   kx,  0,   0,   0,   0,   0 },
                { 0,  -ky,  0,   0,   0,   0,   0,   ky,  0,   0,   0,   0 },
                { 0,   0,  -kz,  0,   0,   0,   0,   0,   kz,  0,   0,   0 },
                { 0,   0,   0,  -kRx, 0,   0,   0,   0,   0,   kRx, 0,   0 },
                { 0,   0,   0,   0,  -kRy, 0,   0,   0,   0,   0,   kRy, 0 },
                { 0,   0,   0,   0,   0,  -kRz, 0,   0,   0,   0,   0,   kRz},
            });

            if (isTan) KeTan = ke; else KeSec = ke;
        }

        // v23 (B-1) 水平方向に 2×2 交差項を持つ剛性（杭地盤ばねの 2D Jacobian 用）
        // 水平地盤ばね（p–y 曲線）の力は f = p(|u|) × (u/|u|) で方向は相対変位に沿う。
        // 非線形領域では接線剛性 dp/d|u| と割線剛性 p/|u| が異なるため、真の Jacobian は
        //   df_x/du_x = K_tan × cos²θ + K_sec × sin²θ = kxx
        //   df_x/du_y = (K_tan − K_sec) × cos θ × sin θ = kxy
        //   df_y/du_y = K_tan × sin²θ + K_sec × cos²θ = kyy
        // という 2×2 対称ブロックになる（cos θ = u_x/|u|, sin θ = u_y/|u|）。
        // SetKe の対角 (K_tan, K_tan) 形式では off-axis 交差項がゼロとなり Newton 方向が不正確。
        // この関数では上記 2x2 ブロックを Ux/Uy 座標に適切に配置する。
        public void SetKeWithXYCoupling(double kxx, double kxy, double kyy, double kz, double kRx, double kRy, double kRz, bool isTan)
        {
            var ke = Matrix<double>.Build.Dense(12, 12, 0.0);

            // Ux-Uy ブロック（2x2 結合）: [[kxx, kxy], [kxy, kyy]]
            // 12x12 の i=0,1（NodeI の Ux,Uy） と i=6,7（NodeJ の Ux,Uy）に対して
            // [K -K; -K K] 構造を展開する
            ke[0, 0] = kxx; ke[0, 1] = kxy;
            ke[1, 0] = kxy; ke[1, 1] = kyy;
            ke[6, 6] = kxx; ke[6, 7] = kxy;
            ke[7, 6] = kxy; ke[7, 7] = kyy;
            ke[0, 6] = -kxx; ke[0, 7] = -kxy;
            ke[1, 6] = -kxy; ke[1, 7] = -kyy;
            ke[6, 0] = -kxx; ke[6, 1] = -kxy;
            ke[7, 0] = -kxy; ke[7, 1] = -kyy;

            // Uz, Rx, Ry, Rz は従来通り対角
            ke[2, 2] = kz; ke[8, 8] = kz; ke[2, 8] = -kz; ke[8, 2] = -kz;
            ke[3, 3] = kRx; ke[9, 9] = kRx; ke[3, 9] = -kRx; ke[9, 3] = -kRx;
            ke[4, 4] = kRy; ke[10, 10] = kRy; ke[4, 10] = -kRy; ke[10, 4] = -kRy;
            ke[5, 5] = kRz; ke[11, 11] = kRz; ke[5, 11] = -kRz; ke[11, 5] = -kRz;

            if (isTan) KeTan = ke; else KeSec = ke;
        }

        public Matrix<double> MapOnGlobalStiff(Matrix<double> K, bool isTan, bool isRowFree, bool isColFree)
        {
            var eq = Utils.GetEquationNumbers(NodeI, NodeJ);
            var ke = (isTan ? KeTan : KeSec) ?? throw new System.InvalidOperationException("SetKe が未実行です。");
            Utils.AddStiffnessToGlobal(K, ke, eq, isRowFree, isColFree, NodeI, NodeJ);
            return K;
        }

        public MathNet.Numerics.LinearAlgebra.Vector<double> CalcInternalForce(bool isTan)
        {
            var dI = NodeI.CumulativeDisp.GetVector();
            var dJ = NodeJ.CumulativeDisp.GetVector();
            var disp = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(12);
            disp.SetSubVector(0, 6, dI);
            disp.SetSubVector(6, 6, dJ);
            var ke = (isTan ? KeTan : KeSec) ?? throw new System.InvalidOperationException("SetKe が未実行です。");
            return ke * disp;
        }

        public void SetBeamDispAndForce(bool isTan = false, BeamDisp cumulativeDisp = null, BeamForce cumulativeForce = null)
        {
            var dI = NodeI.CumulativeDisp.GetVector();
            var dJ = NodeJ.CumulativeDisp.GetVector();
            var disp = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(12);
            disp.SetSubVector(0, 6, dI);
            disp.SetSubVector(6, 6, dJ);

            var ke = (isTan ? KeTan : KeSec) ?? throw new System.InvalidOperationException("SetKe が未実行です。");
            var f = ke * disp;

            cumulativeDisp?.SetVector(disp);
            cumulativeForce?.SetVector(f);
        }
    }
}