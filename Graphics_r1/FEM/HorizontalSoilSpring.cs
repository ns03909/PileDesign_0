using MathNet.Numerics.LinearAlgebra;
using System.Collections.Generic;
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

        public ResultList<HorizontalSpringResult> HorizontalSpringResults { get; set; } = [];

        private ResultLookup<HorizontalSpringResult>? _resultLookup;
        /// <summary><see cref="HorizontalSpringResults"/> を荷重条件とステップで引く索引 (<see cref="ResultLookup{T}"/>)。保存しない。</summary>
        [JsonIgnore]
        public ResultLookup<HorizontalSpringResult> ResultLookup
            => System.Threading.LazyInitializer.EnsureInitialized(ref _resultLookup, () => new ResultLookup<HorizontalSpringResult>(() => HorizontalSpringResults));

        // v28 問題 A 診断: p-y 降伏状態を反復ごとに追跡。
        // true = 降伏後 (|y| >= yy, K_tan = post-yield 定数), false = 弾性。
        // 反復間の状態変化を検出してログ出力する。
        [JsonIgnore]
        public bool IsYielded { get; set; } = false;
        [JsonIgnore]
        public bool PreviousIsYielded { get; set; } = false;

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

            var copy = new HorizontalSoilSpring(Name, nodeICopy!, nodeJCopy!)
            {
                IncrementalDisp = this.IncrementalDisp?.Clone()!,
                CumulativeDisp = this.CumulativeDisp?.Clone()!,
                IncrementalForce = this.IncrementalForce?.Clone()!,
                CumulativeForce = this.CumulativeForce?.Clone()!,
                // KeTan/KeSec はベースに保持されるのでクローン
                // null 許容
            };

            if (this.KeTan != null) copy.SetKeFromMatrix(this.KeTan, isTan: true);
            if (this.KeSec != null) copy.SetKeFromMatrix(this.KeSec, isTan: false);

            // 結果コピー
            copy.HorizontalSpringResults = new ResultList<HorizontalSpringResult>(
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