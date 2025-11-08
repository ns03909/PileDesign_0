//using MathNet.Numerics.LinearAlgebra;
//using System;
//using System.Collections.ObjectModel;
//using System.Linq;
//using System.Text.Json.Serialization;

//namespace PileDesign.FEM
//{
//    public class HorizontalSoilSpring
//    {
//        public string Name { get; set; }
//        public Node NodeI { get; set; }
//        public Node NodeJ { get; set; }

//        public BeamDisp IncrementalDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
//        public BeamDisp CumulativeDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
//        public BeamForce IncrementalForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
//        public BeamForce CumulativeForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

//        [JsonIgnore]
//        public Matrix<double> KeTan { get; private set; }
//        [JsonIgnore]
//        public Matrix<double> KeSec { get; private set; }

//        public ObservableCollection<HorizontalSpringResult> HorizontalSpringResults { get; set; } = [];

//        // パラメータなしコンストラクタ（必須）
//        public HorizontalSoilSpring() { }

//        // 生成用コンストラクタ
//        public HorizontalSoilSpring(string name, Node nodeI, Node nodeJ)
//        {
//            Name = name ?? throw new ArgumentNullException(nameof(name));
//            NodeI = nodeI ?? throw new ArgumentNullException(nameof(nodeI));
//            NodeJ = nodeJ ?? throw new ArgumentNullException(nameof(nodeJ));
//        }

//        // ばね剛性を取得する
//        public void SetKe(double kx, double ky, double kz, double kxx, double kyy, double kzz, bool isTan)
//        {
//            Matrix<double> ke = Matrix<double>.Build.DenseOfArray(new double[,]
//            {
//                { kx, 0.0, 0.0, 0.0, 0.0, 0.0, -kx, 0.0, 0.0, 0.0, 0.0, 0.0, }, // 0
//                { 0.0, ky, 0.0, 0.0, 0.0, 0.0, 0.0, -ky, 0.0, 0.0, 0.0, 0.0, }, // 1
//                { 0.0, 0.0, kz, 0.0, 0.0, 0.0, 0.0, 0.0, -kz, 0.0, 0.0, 0.0, }, // 2
//                { 0.0, 0.0, 0.0, kxx, 0.0, 0.0, 0.0, 0.0, 0.0, -kxx, 0.0, 0.0, }, // 3
//                { 0.0, 0.0, 0.0, 0.0, kyy, 0.0, 0.0, 0.0, 0.0, 0.0, -kyy, 0.0, }, // 4
//                { 0.0, 0.0, 0.0, 0.0, 0.0, kzz, 0.0, 0.0, 0.0, 0.0, 0.0, -kzz, }, // 5
//                { -kx, 0.0, 0.0, 0.0, 0.0, 0.0, kx, 0.0, 0.0, 0.0, 0.0, 0.0, }, // 6
//                { 0.0, -ky, 0.0, 0.0, 0.0, 0.0, 0.0, ky, 0.0, 0.0, 0.0, 0.0, }, // 7
//                { 0.0, 0.0, -kz, 0.0, 0.0, 0.0, 0.0, 0.0, kz, 0.0, 0.0, 0.0, }, // 8
//                { 0.0, 0.0, 0.0, -kxx, 0.0, 0.0, 0.0, 0.0, 0.0, kxx, 0.0, 0.0, }, // 9
//                { 0.0, 0.0, 0.0, 0.0, -kyy, 0.0, 0.0, 0.0, 0.0, 0.0, kyy, 0.0, }, // 10
//                { 0.0, 0.0, 0.0, 0.0, 0.0, -kzz, 0.0, 0.0, 0.0, 0.0, 0.0, kzz, }, // 11
//            });

//            if (isTan)
//            {
//                KeTan = ke;
//            }
//            else
//            {
//                KeSec = ke;
//            }
//        }


//        // 要素剛性を全体剛性にマップオンする

//        public Matrix<double> MapOnGlobalStiff(
//            Matrix<double> matrixGlobalStiffness, bool isTan, bool isRowFree, bool isColFree/*, AnaModel anaModel*/)
//        {
//            // eqリスト作成
//            var eq = Utils.GetEquationNumbers(NodeI, NodeJ) /*, anaModel)*/;
//            // 要素剛性マトリクス
//            Matrix<double> kMat = (isTan ? KeTan : KeSec) ?? throw new InvalidOperationException("Stiffness matrix is not initialized. Call SetKe() before MapOnGlobalStiff().");

//            // 全体剛性マトリクスへ加算
//            Utils.AddStiffnessToGlobal(matrixGlobalStiffness, kMat, eq, isRowFree, isColFree, NodeI, NodeJ/*, anaModel*/);

//            return matrixGlobalStiffness;
//        }

//        /// 水平地盤ばねの内力（ばね力）を計算するメソッド
//        public Vector<double> CalcInternalForce(bool isTan)
//        {
//            // 両端節点の変位ベクトル（全体座標系）
//            Vector<double> dispNodeI = NodeI.CumulativeDisp.GetVector(); // 6成分
//            Vector<double> dispNodeJ = NodeJ.CumulativeDisp.GetVector(); // 6成分

//            // 12成分の要素変位ベクトルを作成
//            Vector<double> disp = Vector<double>.Build.Dense(12);
//            disp.SetSubVector(0, 6, dispNodeI);
//            disp.SetSubVector(6, 6, dispNodeJ);

//            // 剛性マトリクス
//            Matrix<double> k = (isTan ? KeTan : KeSec) ?? throw new InvalidOperationException("Stiffness matrix is not initialized. Call SetKe() before CalcInternalForce().");

//            // 内力計算
//            Vector<double> f = k * disp;

//            return f;
//        }

//        public void SetBeamDispAndForce(bool isTan = false)
//        {
//            // 節点の変位ベクトル（全体座標系）

//            Vector<double> dispNodeI = NodeI.CumulativeDisp.GetVector(); //double[] dispNodeI = nodeI.OutNode.Disp;
//            Vector<double> dispNodeJ = NodeJ.CumulativeDisp.GetVector(); //double[] dispNodeJ = nodeJ.OutNode.Disp;

//            // dispNodeIとdispNodeJを組み合わせて2n行のベクトルを作成
//            Vector<double> disp = Vector<double>.Build.Dense(dispNodeI.Count + dispNodeJ.Count); //double[] disp = [.. dispNodeI, .. dispNodeJ];
//            disp.SetSubVector(0, dispNodeI.Count, dispNodeI);
//            disp.SetSubVector(dispNodeI.Count, dispNodeJ.Count, dispNodeJ);

//            // 節点の変位ベクトル（要素座標系）

//            //Matrix<double> t = Utils.GetTransformMatrix(NodeI, NodeJ); //double[,] t = Utils.GetTransformMatrix(nodeI, nodeJ);
//            Vector<double> d = disp; //double[] d = Utils.MatrixVectorMultiply(t, disp);
//            // 要素応力 f = k * d
//            Matrix<double> k = (isTan ? KeTan : KeSec) ?? throw new InvalidOperationException("Stiffness matrix is not initialized. Call SetKe() before CalcInternalForce().");
//            // 内力計算
//            Vector<double> f = k * d; //double[] f = Utils.MatrixVectorMultiply(k, d);
//                                      // 要素応力を結果用オブジェクトにセット
//            CumulativeDisp.SetVector(d);
//            CumulativeForce.SetVector(f);
//        }

//        public HorizontalSoilSpring DeepCopy()
//        {
//            // NodeI, NodeJもDeepCopyする
//            var nodeICopy = NodeI?.DeepCopy();
//            var nodeJCopy = NodeJ?.DeepCopy();

//            var copy = new HorizontalSoilSpring(Name, nodeICopy, nodeJCopy)
//            {
//                IncrementalDisp = this.IncrementalDisp?.Clone(),
//                CumulativeDisp = this.CumulativeDisp?.Clone(),
//                IncrementalForce = this.IncrementalForce?.Clone(),
//                CumulativeForce = this.CumulativeForce?.Clone(),
//                KeTan = this.KeTan?.Clone(),
//                KeSec = this.KeSec?.Clone(),
//                HorizontalSpringResults = new ObservableCollection<HorizontalSpringResult>(
//                    this.HorizontalSpringResults.Select(r => r.DeepCopy()))
//            };
//            return copy;
//        }

//    }
//}
using MathNet.Numerics.LinearAlgebra;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.FEM
{
    public class HorizontalSoilSpring : TwoNodeSpringElement
    {
        public BeamDisp IncrementalDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamDisp CumulativeDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce IncrementalForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce CumulativeForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        [JsonIgnore]
        public new Matrix<double> KeTan => base.KeTan;
        [JsonIgnore]
        public new Matrix<double> KeSec => base.KeSec;

        public ObservableCollection<HorizontalSpringResult> HorizontalSpringResults { get; set; } = [];

        // パラメータなしコンストラクタ（シリアライザ用）
        public HorizontalSoilSpring() : base() { }

        // 生成用コンストラクタ
        public HorizontalSoilSpring(string name, Node nodeI, Node nodeJ) : base(name, nodeI, nodeJ) { }

        // 既存呼び出し互換: 自身の累積変位/力に結果を格納
        public void SetBeamDispAndForce(bool isTan = false)
        {
            base.SetBeamDispAndForce(isTan, CumulativeDisp, CumulativeForce);
        }

        public HorizontalSoilSpring DeepCopy()
        {
            // NodeI, NodeJもDeepCopyする
            var nodeICopy = NodeI?.DeepCopy();
            var nodeJCopy = NodeJ?.DeepCopy();

            var copy = new HorizontalSoilSpring(Name, nodeICopy, nodeJCopy)
            {
                IncrementalDisp = this.IncrementalDisp?.Clone(),
                CumulativeDisp = this.CumulativeDisp?.Clone(),
                IncrementalForce = this.IncrementalForce?.Clone(),
                CumulativeForce = this.CumulativeForce?.Clone(),
                // KeTan/KeSec はベースに保持されるのでクローン
                // null 許容
            };

            if (this.KeTan != null) copy.SetKeFromMatrix(this.KeTan, isTan: true);
            if (this.KeSec != null) copy.SetKeFromMatrix(this.KeSec, isTan: false);

            // 結果コピー
            copy.HorizontalSpringResults = new ObservableCollection<HorizontalSpringResult>(
                this.HorizontalSpringResults.Select(r => r.DeepCopy()));

            return copy;
        }

        // 既存Keをそのままコピーしたい時の補助（SetKeの公開APIはスカラー引数のため）
        private void SetKeFromMatrix(Matrix<double> ke, bool isTan)
        {
            // 対角（ペナルティ形式 [k -k; -k k]）から剛性成分を復元
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