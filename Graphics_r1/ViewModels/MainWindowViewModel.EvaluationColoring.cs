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

            _evaluationSummaryCache = PileEvaluationSummary.Build(this);
            _evaluationSummaryKey = key;
            return _evaluationSummaryCache;
        }

        /// <summary>解析結果を捨てたときなど、次回は必ず作り直させる。</summary>
        public void InvalidateEvaluationSummary() => _evaluationSummaryCache = null;
    }
}
