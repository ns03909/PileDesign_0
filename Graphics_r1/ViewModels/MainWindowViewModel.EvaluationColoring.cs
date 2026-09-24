using PileDesign.Services;
using System;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel partial: 検定サマリー (杭ごとの最大検定比) の保持と、キャンバスの色分け。
    ///
    /// 検定の項目は荷重ケース × 部位 × 限界状態で数百件になり、
    /// テーブルを開いて並べ替えないと「どの杭が厳しいか」が分からなかった。
    /// ここで畳んだ値をダッシュボードとキャンバスに同じ経路で配る。
    /// </summary>
    public partial class MainWindowViewModel
    {
        // ── 杭配置の検定比色分け ──────────────────────────────────────

        private bool _isEvaluationColoringVisible;

        /// <summary>
        /// 杭配置を検定比で色分けするか。
        /// 緑 (余裕あり) / 黄 (余裕小) / 赤 (NG) / 灰 (未収束)。検定の無い杭は通常の塗り。
        /// </summary>
        public bool IsEvaluationColoringVisible
        {
            get => _isEvaluationColoringVisible;
            set
            {
                if (SetProperty(ref _isEvaluationColoringVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        private bool _isEvaluationDetailVisible;

        /// <summary>
        /// 検定比の詳細をマウスオーバーで出すか。
        /// 色分けした杭にカーソルを近づけると、その部位の検定項目と検定比を出す
        /// (色だけでは「何の検定比がいくつか」が分からないため)。
        ///
        /// ON の間は<b>他の結果ツールチップより優先する</b>。
        /// 色を見ながら値を確かめるための表示なので、応力や変位のツールチップが
        /// 割り込むと目的を果たせない。
        /// </summary>
        public bool IsEvaluationDetailVisible
        {
            get => _isEvaluationDetailVisible;
            set => SetProperty(ref _isEvaluationDetailVisible, value);
        }

        // ── 検定サマリーのキャッシュ ─────────────────────────────────

        /// <summary>
        /// 入力が編集されるたびに進む番号。<see cref="SaveUndoState(AnalysisInputScope, string?)"/> で進める。
        /// 支持力の検定は入力 (杭軸力・杭体) だけから決まるので、解析結果が変わらなくても
        /// 編集があれば検定サマリーを作り直す必要がある。その検知に使う。
        /// </summary>
        public int InputEditVersion { get; private set; }

        private PileEvaluationSummary? _evaluationSummaryCache;
        private (object? Model, object? Input, DateTime? Time, bool Done, int Edit) _evaluationSummaryKey;

        /// <summary>
        /// 検定サマリーを返す。
        /// キャンバスは回転・ズームのたびに描き直すので、検定 (数百件の N-M 補間) を
        /// 毎回やり直さないよう、解析結果と入力が同じ間は前回の値を返す。
        /// </summary>
        /// <param name="force">true なら必ず作り直す (ダッシュボードの再計算)。</param>
        public PileEvaluationSummary GetEvaluationSummary(bool force = false)
        {
            var key = (
                (object?)CurrentModel,
                (object?)ResultInputModel,
                LastAnalysisTime,
                IsHorizontalAnalysisDone,
                InputEditVersion);

            if (!force && _evaluationSummaryCache != null && _evaluationSummaryKey.Equals(key))
                return _evaluationSummaryCache;

            AdoptEvaluationSummary(PileEvaluationSummary.Build(this));
            _evaluationSummaryKey = key;
            return _evaluationSummaryCache!;
        }

        /// <summary>
        /// 作り直したまとめを受け取る。凡例の注意書き (<see cref="EvaluationColoringWarning"/>) もここで決める
        /// (まとめを持つ所と注意書きを決める所を 1 つにして、片方だけ更新されないようにする)。
        /// </summary>
        internal void AdoptEvaluationSummary(PileEvaluationSummary summary)
        {
            _evaluationSummaryCache = summary;
            EvaluationColoringWarning = DescribeColoringFailure(
                summary, horizontalDone: CurrentModel != null && IsHorizontalAnalysisDone);
        }

        private string? _evaluationColoringWarning;

        /// <summary>
        /// 色分けの凡例に出す注意書き。検定の組み立てに失敗した部分があれば、その検定が色に入っていないこと。
        /// 無ければ null。
        ///
        /// 失敗した検定はまとめに入らないので、色分けは組めた検定 (たとえば支持力) だけで塗られる。
        /// 以前は失敗をダッシュボードにしか出さず、キャンバスでは杭頭の緑の印などがそのまま残り、
        /// 水平解析も含めて OK と読めた。
        /// </summary>
        public string? EvaluationColoringWarning
        {
            get => _evaluationColoringWarning;
            private set
            {
                if (SetProperty(ref _evaluationColoringWarning, value))
                    OnPropertyChanged(nameof(HasEvaluationColoringWarning));
            }
        }

        /// <summary>凡例の注意書きを出すか。</summary>
        public bool HasEvaluationColoringWarning => !string.IsNullOrEmpty(EvaluationColoringWarning);

        /// <summary>
        /// 検定の組み立てに失敗した部分を、色分けを見る人向けに書く。失敗が無ければ null。
        /// 水平解析をしていないときの水平解析の失敗は数えない (組もうとしていない)。
        /// </summary>
        internal static string? DescribeColoringFailure(PileEvaluationSummary summary, bool horizontalDone)
        {
            bool horizontal = horizontalDone && summary.HorizontalFailed;
            bool bearing = summary.BearingFailed;
            if (!horizontal && !bearing) return null;

            string parts = horizontal && bearing
                ? $"{PileEvaluationSummary.HorizontalPart}と{PileEvaluationSummary.BearingPart}"
                : horizontal ? PileEvaluationSummary.HorizontalPart : PileEvaluationSummary.BearingPart;
            return $"{parts}の検定を組めませんでした。色に入っていないので、色の無い所も OK ではありません"
                   + "（解析結果ダッシュボードで確認してください）。";
        }

        /// <summary>解析結果を捨てたときなど、次回は必ず作り直させる。</summary>
        public void InvalidateEvaluationSummary() => _evaluationSummaryCache = null;
    }
}
