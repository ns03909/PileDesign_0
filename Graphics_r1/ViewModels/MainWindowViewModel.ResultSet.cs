using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models;
using PileDesign.Models.InputData;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.ResultSet.cs
    ///
    /// 責任範囲:
    /// - 解析結果と「解析時の入力」を 1 組で保持する <see cref="AnalysisResultSet"/> の管理
    /// - 結果表示系が参照すべき入力 (<see cref="ResultInputModel"/>) の提供
    /// - 解析後に入力が変更されたかどうかの表示
    ///
    /// 従来は入力を変更するたびに解析結果を破棄していた。実務では結果を横目に見ながら
    /// 入力を変えていくため、この運用は成り立たない。解析完了時に入力ごと複製して
    /// 切り離しておけば、以降の編集は結果に影響しない。
    /// </summary>
    public partial class MainWindowViewModel
    {
        private AnalysisResultSet? _currentResultSet;

        /// <summary>現在保持している解析結果セット（入力スナップショット + 結果）。未解析なら null。</summary>
        public AnalysisResultSet? CurrentResultSet
        {
            get => _currentResultSet;
            private set
            {
                if (SetProperty(ref _currentResultSet, value))
                {
                    OnPropertyChanged(nameof(ResultInputModel));
                    OnPropertyChanged(nameof(HasAnalysisResultSet));
                    OnPropertyChanged(nameof(ResultSetStatusText));
                }
            }
        }

        public bool HasAnalysisResultSet => _currentResultSet != null;

        /// <summary>
        /// 結果表示系（グラフ・結果テーブル・結果キャンバス・計算書・評価）が参照すべき入力。
        ///
        /// 解析結果があるときは「解析を実行した時点の入力」を返す。結果と整合するのはこれだけで、
        /// 現在の入力を混ぜると「変位は解析時・断面は編集後」という読み手が区別できない図になる。
        /// 結果が無いときは現在の入力を返すので、呼び出し側は分岐不要。
        /// </summary>
        public InputModel ResultInputModel => _currentResultSet?.InputSnapshot ?? CurrentInputModel;

        private bool _inputChangedSinceAnalysis;

        /// <summary>解析後に入力が編集されたか（表示中の結果が現在の入力と一致しない）。</summary>
        public bool InputChangedSinceAnalysis
        {
            get => _inputChangedSinceAnalysis;
            private set
            {
                if (SetProperty(ref _inputChangedSinceAnalysis, value))
                    OnPropertyChanged(nameof(ResultSetStatusText));
            }
        }

        /// <summary>結果セットの状態表示（ステータスバー等）。</summary>
        public string ResultSetStatusText
        {
            get
            {
                if (_currentResultSet == null) return string.Empty;
                string stamp = _currentResultSet.CapturedAt.ToString("yyyy-MM-dd HH:mm");
                return InputChangedSinceAnalysis
                    ? $"表示中の解析結果は {stamp} 実行時の入力によるものです（入力が変更されています。再解析が必要です）"
                    : $"解析結果: {stamp} 実行";
            }
        }

        /// <summary>
        /// 入力が編集されたことを記録する。結果は破棄しない。
        /// 入力を変更するコマンド／編集ハンドラから呼ぶ。
        /// </summary>
        public void MarkInputChangedSinceAnalysis()
        {
            if (_currentResultSet == null) return;
            InputChangedSinceAnalysis = true;
        }

        /// <summary>
        /// 解析完了時に、現在の入力と結果を 1 組に複製して切り離す。
        /// 以降 <see cref="CurrentModel"/> は切り離された複製を指し、
        /// 結果表示系は <see cref="ResultInputModel"/> を見る。
        /// </summary>
        public void CaptureAnalysisResultSet()
        {
            if (CurrentInputModel == null) return;

            // 水平解析の結果が前回のスナップショットのままで、そのあと入力が編集されている場合は
            // 取り直さない。取り直すと「編集後の入力」と「解析時の結果」が 1 組に組み直され、
            // 変位は解析時・断面は編集後という混在に戻ってしまう。この仕組みが防ぐはずのものそのもの。
            //
            // 沈下だけ再実行したときにこれが起きていた。沈下の結果は入力モデルの中にあり、
            // 表示も現在の入力を見るので、取り直さなくても沈下の結果は出る。
            // 併せて「入力が変更された」の記録も残るので、陳腐化した水平解析結果の警告が消えない。
            bool horizontalIsStillTheCapturedOne =
                _currentResultSet?.AnaModel != null
                && ReferenceEquals(CurrentModel, _currentResultSet.AnaModel);

            if (InputChangedSinceAnalysis && horizontalIsStillTheCapturedOne)
            {
                Serilog.Log.Information(
                    "[結果セット] 入力が編集済みのためスナップショットは取り直さない (水平解析結果は解析時のまま)");
                return;
            }

            var set = AnalysisResultSet.Capture(
                CurrentInputModel,
                CurrentModel,
                VerticalBeamCaseResults?.ToList(),
                IsHorizontalAnalysisDone,
                IsVerticalAnalysisDone,
                IsGroupPileSettlementAnalysisDone,
                IsVerticalBeamAnalysisDone,
                IsElementSplit);

            if (set == null) return;   // 複製に失敗したときは従来どおり live を参照する

            // スナップショット側の要素が VM 経由で「現在の入力」を見に行かないよう親を固定する
            set.InputSnapshot.AttachViewModel(this);

            CurrentResultSet = set;
            if (set.AnaModel != null) CurrentModel = set.AnaModel;
            InputChangedSinceAnalysis = false;
        }

        /// <summary>
        /// ファイルから復元した結果セットを設定する。
        /// 保存時点で入力が編集済みだった場合は「変更あり」の状態も引き継ぐ。
        /// </summary>
        internal void SetRestoredResultSet(AnalysisResultSet? set, bool changedSinceAnalysis)
        {
            CurrentResultSet = set;
            InputChangedSinceAnalysis = set != null && changedSinceAnalysis;
        }

        /// <summary>
        /// 結果セットと陳腐化の記録だけを破棄する。
        /// 解析結果を消すすべての経路から呼ぶこと。残すと ResultInputModel が
        /// 解析時の入力を返し続け、消したはずの結果の痕跡が表示に残る。
        /// </summary>
        internal void ClearAnalysisResultSetState()
        {
            CurrentResultSet = null;
            InputChangedSinceAnalysis = false;
        }

        /// <summary>
        /// 解析に由来する状態をすべて捨てる。<b>解析結果を無効にするすべての経路から呼ぶこと。</b>
        ///
        /// 捨てるものが 3 か所に分かれている。
        /// <list type="bullet">
        /// <item>VM のフラグ (<see cref="IsHorizontalAnalysisDone"/> ほか) と <see cref="CurrentModel"/></item>
        /// <item>解析結果セット (入力スナップショット + AnaModel)</item>
        /// <item><b>入力モデルの中に格納された沈下の結果</b>
        ///   (<c>PileGroupSettlement</c> の CaseRecords / SettlementGridData、各杭の GroupPileSettlement)</item>
        /// </list>
        /// 1 か所でも漏らすと「消したはずの結果が残る」。経路ごとに部分集合しか消していなかったため、
        /// 新規作成や計算例の読み込みでは前のモデルの結果セットが残り、
        /// 破棄したはずの沈下結果は保存 → 再読込で復活していた
        /// (解析済みかどうかは入力モデル内のデータから推定するため)。
        /// </summary>
        /// <param name="includeElementSplit">
        /// 杭要素分割も取り消すか。分割は解析結果ではなく入力側の状態なので、呼び出し側が決める。
        /// </param>
        internal void ClearAllAnalysisState(bool includeElementSplit)
        {
            if (includeElementSplit) IsElementSplit = false;

            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;
            IsAnalysisResultVisible = false;

            VerticalBeamCaseResults = null;
            CurrentModel = null;

            ClearAnalysisResultSetState();
            ClearSettlementResults();
        }

        /// <summary>解析結果を明示的に破棄する（メニュー等から呼ぶ）。</summary>
        [RelayCommand]
        public void DiscardAnalysisResults()
        {
            // 杭要素分割は入力側の状態なので残す (従来の挙動)。
            ClearAllAnalysisState(includeElementSplit: false);
            UpdateWindowImmediate();
        }
    }
}
