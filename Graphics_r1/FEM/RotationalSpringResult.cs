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
                CumulativeForce = this.CumulativeForce?.Clone()
            };
            return copy;
        }
    }
}
