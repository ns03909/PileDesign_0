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

        /// <summary>解析結果を明示的に破棄する（メニュー等から呼ぶ）。</summary>
        [RelayCommand]
        public void DiscardAnalysisResults()
        {
            CurrentResultSet = null;
            CurrentModel = null;
            InputChangedSinceAnalysis = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;
            IsAnalysisResultVisible = false;
            UpdateWindowImmediate();
        }
    }
}
