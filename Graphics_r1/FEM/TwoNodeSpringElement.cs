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