using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    public class RotationalSpringResult
    {
        public LoadCase LoadCase { get; set; }
        public LoadCombination LoadCombination { get; set; }
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }

        public BeamDisp CumulativeDisp { get; set; }
        public BeamForce CumulativeForce { get; set; }

        // v28 アプローチ I: post-crack 方向ロック状態のスナップショット。
        // HasCracked=true のとき CrackNx/CrackNy は単位ベクトル (|n|=1)。
        // グラフ描画側で |θ|, |M| の代わりに n 方向ピーク値 (ThetaProjMax, M_peak) を使い、
        // 方向ロックと履歴 (hysteresis) で curve から外れて見える問題を防ぐ。
        public bool HasCracked { get; set; }
        public double? CrackNx { get; set; }
        public double? CrackNy { get; set; }
        public double ThetaProjMax { get; set; }

        // パラメータなしコンストラクタ（必須）
        public RotationalSpringResult() { }

        // 生成用コンストラクタ
        public RotationalSpringResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, RotationalSpring rotationalSpring)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
            CumulativeDisp = rotationalSpring.CumulativeDisp?.Clone();
            CumulativeForce = rotationalSpring.CumulativeForce?.Clone();
            HasCracked = rotationalSpring.HasCrackedXY;
            CrackNx = rotationalSpring.CrackNx;
            CrackNy = rotationalSpring.CrackNy;
            ThetaProjMax = rotationalSpring.ThetaProjMax;
        }

        public RotationalSpringResult DeepCopy()
        {
            var copy = new RotationalSpringResult()
            {
                LoadCase = this.LoadCase?.DeepCopy(),
                LoadCombination = this.LoadCombination?.DeepCopy(),
                IsLiquefaction = this.IsLiquefaction,
                Step = this.Step,
                CumulativeDisp = this.CumulativeDisp?.Clone(),
                CumulativeForce = this.CumulativeForce?.Clone(),
                HasCracked = this.HasCracked,
                CrackNx = this.CrackNx,
                CrackNy = this.CrackNy,
                ThetaProjMax = this.ThetaProjMax,
            };
            return copy;
        }
    }
}
