using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    /// <summary>
    /// 荷重ステップの収束状態。
    ///
    /// 水平解析は荷重ステップごとに Newton-Raphson を回す。反復が上限に達しても
    /// 残差が許容値まで下がらないことがあり、そのステップの変位は<b>釣り合っていない</b>。
    /// 解析はそのまま次のステップへ進むので、応答値は出るが解ではない。
    ///
    /// 以前この状態は解析ウィンドウのサマリー (メモリ上の StepSummary) にしか無く、
    /// 検定にも計算書にも保存ファイルにも伝わっていなかった。そのため
    /// <b>収束していないケースの応答値がそのまま OK / NG として表に並んでいた</b>。
    /// 結果と同じ場所に持たせて、結果を読むところすべてに届くようにする。
    /// </summary>
    public enum StepStatus
    {
        /// <summary>残差が許容値まで下がった。</summary>
        Converged,

        /// <summary>反復が上限に達しても残差が許容値まで下がらなかった。</summary>
        Unconverged,

        /// <summary>
        /// 収束基準を緩めても収束せず、ステップ分割を増やしても見込みが無いと判断した。
        /// 耐力を超えている (解が存在しない) 可能性がある。
        /// </summary>
        PhysicallyUnconverged,
    }

    public class AnalysisStepResult
    {
        public LoadCase LoadCase { get; set; }
        public LoadCombination LoadCombination { get; set; }
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }
        public int Iteration { get; set; }
        public double ResidualValue { get; set; }

        /// <summary>
        /// このステップの収束状態。
        ///
        /// 既定は <see cref="StepStatus.Converged"/>。この項目を持たない古い保存ファイルは
        /// 収束状態が分からないので、従来どおり (収束したものとして) 扱う。
        /// 新しく解析すれば正しい値が入る。
        /// </summary>
        public StepStatus Status { get; set; } = StepStatus.Converged;

        // デフォルトコンストラクタ（デシリアライズ用）
        public AnalysisStepResult() { }

        // 既存のコンストラクタ（アプリ内生成用）
        public AnalysisStepResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, int iteration, double residualValue)
            : this(loadCase, loadCombination, isLiquefaction, step, iteration, residualValue, StepStatus.Converged)
        {
        }

        public AnalysisStepResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, int iteration, double residualValue, StepStatus status)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
            Iteration = iteration;
            ResidualValue = residualValue;
            Status = status;
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
                this.ResidualValue,
                this.Status
            );
        }
    }
}
