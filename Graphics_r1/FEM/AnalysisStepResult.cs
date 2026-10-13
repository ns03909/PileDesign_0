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

        /// <summary>
        /// 残差が許容値 (1e-6) まで下がらず、停滞・振動のため緩めた基準 (最大 1e-2) で受理した。
        /// 構成則の折れ点 (ひび割れ・降伏境界) で Newton 方向が反転するリミットサイクルでは
        /// 刻みを細かくしても抜けられないため、受理して次のステップへ進む。
        /// 「収束」と同じ印にすると見えなくなるので区別する (2026-09-12)。
        /// 検定は未収束としては扱わないが、緩和した旨を表示する。
        /// </summary>
        ConvergedRelaxed,

        /// <summary>反復が上限に達しても残差が許容値まで下がらなかった。</summary>
        Unconverged,

        /// <summary>
        /// 収束基準を緩めても収束せず、ステップ分割を増やしても見込みが無いと判断した。
        /// 耐力を超えている (解が存在しない) 可能性がある。
        /// </summary>
        PhysicallyUnconverged,

        /// <summary>
        /// 収束状態の記録が無い (この項目を持たない古い保存ファイルの結果)。収束した証拠が無いので、
        /// 数値は見られるが、検定は「収束不明」として OK / NG に数えない (未収束と同じ扱い)。再解析すれば正しい値が入る。
        /// 以前は既定の「収束」として扱い、残差が大きくても収束したケースと区別できなかった (2026-10-14)。
        /// 値は保存ファイルの数値と重ならないよう末尾に置く (並びは「悪いほど大きい」の順で、未収束より後ろ)。
        /// </summary>
        Unknown,
    }

    public class AnalysisStepResult : ICaseStepResult
    {
        public LoadCase LoadCase { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public LoadCombination LoadCombination { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }
        public int Iteration { get; set; }
        public double ResidualValue { get; set; }

        /// <summary>
        /// このステップの収束状態。
        ///
        /// 読込 (引数なしのコンストラクタ) の既定は <see cref="StepStatus.Unknown"/>。この項目を持たない古い保存ファイルは
        /// 収束状態が分からないので、収束したものとしては扱わない。解析が作る結果は必ず状態を入れる (下のコンストラクタ)。
        /// </summary>
        public StepStatus Status { get; set; } = StepStatus.Unknown;

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
                this.LoadCase?.DeepCopy()!,
                this.LoadCombination?.DeepCopy()!,
                this.IsLiquefaction,
                this.Step,
                this.Iteration,
                this.ResidualValue,
                this.Status
            );
        }
    }
}
