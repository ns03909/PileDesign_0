using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    public class HorizontalSpringResult
    {
        public LoadCase LoadCase { get; set; }
        public LoadCombination LoadCombination { get; set; }
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }

        public BeamDisp CumulativeDisp { get; set; }
        public BeamForce CumulativeForce { get; set; }

        // パラメータなしコンストラクタ（必須）
        public HorizontalSpringResult() { }

        // 生成用コンストラクタ
        public HorizontalSpringResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, HorizontalSoilSpring horizontalSoilSpring)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
            CumulativeDisp = horizontalSoilSpring.CumulativeDisp?.Clone();
            CumulativeForce = horizontalSoilSpring.CumulativeForce?.Clone();
        }

        public HorizontalSpringResult DeepCopy()
        {
            var copy = new HorizontalSpringResult()
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
