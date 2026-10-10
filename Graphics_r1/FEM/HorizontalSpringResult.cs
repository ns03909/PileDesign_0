using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    public class HorizontalSpringResult
    {
        public LoadCase LoadCase { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public LoadCombination LoadCombination { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }

        public BeamDisp CumulativeDisp { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public BeamForce CumulativeForce { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る

        // パラメータなしコンストラクタ（必須）
        public HorizontalSpringResult() { }

        // 生成用コンストラクタ
        public HorizontalSpringResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, HorizontalSoilSpring horizontalSoilSpring)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
            CumulativeDisp = horizontalSoilSpring.CumulativeDisp?.Clone()!;
            CumulativeForce = horizontalSoilSpring.CumulativeForce?.Clone()!;
        }

        public HorizontalSpringResult DeepCopy()
        {
            var copy = new HorizontalSpringResult()
            {
                LoadCase = this.LoadCase?.DeepCopy()!,
                LoadCombination = this.LoadCombination?.DeepCopy()!,
                IsLiquefaction = this.IsLiquefaction,
                Step = this.Step,
                CumulativeDisp = this.CumulativeDisp?.Clone()!,
                CumulativeForce = this.CumulativeForce?.Clone()!
            };
            return copy;
        }
    }
}
