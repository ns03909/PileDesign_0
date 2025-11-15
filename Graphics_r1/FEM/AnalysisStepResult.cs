using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    public class AnalysisStepResult
    {
        public LoadCase LoadCase { get; set; }
        public LoadCombination LoadCombination { get; set; }
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }
        public int Iteration { get; set; }
        public double ResidualValue { get; set; }

        // デフォルトコンストラクタ（デシリアライズ用）
        public AnalysisStepResult() { }

        // 既存のコンストラクタ（アプリ内生成用）
        public AnalysisStepResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, int iteration, double residualValue)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
            Iteration = iteration;
            ResidualValue = residualValue;
        }

        public int GetLastStep()
        { return Step; }

        public AnalysisStepResult DeepCopy()
        {
            return new AnalysisStepResult(
                this.LoadCase?.DeepCopy(),
                this.LoadCombination?.DeepCopy(),
                this.IsLiquefaction,
                this.Step,
                this.Iteration,
                this.ResidualValue
            );
        }
    }
}
