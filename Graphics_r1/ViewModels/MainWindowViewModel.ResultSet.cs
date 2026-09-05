using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;

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
                if (!value) _horizontalInputChanged = false;
            }
        }

        /// <summary>
        /// 編集された入力が<b>どの解析に効くか</b>。
        ///
        /// 群杭沈下の入力 (矩形荷重・沈下用土層・荷重面) は水平解析がまったく読まない。
        /// それらを触っただけで「水平解析の再解析が必要です」と言うのは誤りで、
        /// 実際「水平解析 → 矩形荷重を入れて沈下解析」の順で必ず出ていた。
        /// </summary>
        [System.Flags]
        public enum AnalysisInputScope
        {
            None = 0,
            /// <summary>水平解析・鉛直解析など、モデル全体に効く入力。</summary>
            Model = 1,
            /// <summary>群杭沈下だけに効く入力。</summary>
            Settlement = 2,
            All = Model | Settlement,
        }

        /// <summary>水平解析の結果が陳腐化しているか (モデル側の入力が編集された)。</summary>
        private bool _horizontalInputChanged;

        /// <summary>
        /// 材料モデル化オプションが、表示中の解析結果を出したときから変わっているか。
        ///
        /// これが起きると<b>応答値は解析時、限界曲線は今のオプション</b>という混ざった図・表になる。
        /// オプションは静的 (<see cref="ConcreteModelOptions"/>) で、現在の入力から書き込まれるため、
        /// 解析後に変えると結果表示の限界側だけが追随してしまう。
        /// 計算書には同じ照合による注意書きがあるのに、画面には何も出ていなかった。
        /// </summary>
        public bool MaterialOptionsChangedSinceAnalysis =>
            CurrentModel?.ConcreteOptionsSignature is string recorded
            && recorded != ConcreteModelOptions.Signature();

        /// <summary>結果セットの状態表示（ステータスバー等）。</summary>
        public string ResultSetStatusText
        {
            get
            {
                if (_currentResultSet == null) return string.Empty;
                string stamp = _currentResultSet.CapturedAt.ToString("yyyy-MM-dd HH:mm");

                // 何が陳腐化したのかで言い分ける。沈下の入力しか触っていないのに
                // 「再解析が必要」と言われると、水平解析をやり直す話に読める。
                string baseText =
                    _horizontalInputChanged
                        ? $"表示中の解析結果は {stamp} 実行時の入力によるものです（入力が変更されています。再解析が必要です）"
                    : InputChangedSinceAnalysis
                        ? $"解析結果: {stamp} 実行／沈下解析の入力が変更されています（沈下解析の再実行が必要です）"
                        : $"解析結果: {stamp} 実行";

                // 応答値は解析時のもの、限界曲線は今のオプションで引かれる。混ざったまま読ませない
                return MaterialOptionsChangedSinceAnalysis
                    ? baseText + "／材料モデル化オプションが解析後に変更されています"
                        + "（限界曲線は変更後のオプションで描かれます。再解析が必要です）"
                    : baseText;
            }
        }

        /// <summary>
        /// 材料モデル化オプションを変えたあとに呼ぶ。ステータス表示を出し直す。
        /// </summary>
        internal void NotifyMaterialOptionsSignatureChanged()
        {
            OnPropertyChanged(nameof(MaterialOptionsChangedSinceAnalysis));
            OnPropertyChanged(nameof(ResultSetStatusText));
        }

        /// <summary>
        /// 読込の仕上げで、ファイルから復元した「編集された」記録へ戻す。
        ///
        /// 読込の最後に <c>SaveUndoState</c> を呼んで初期状態を積むが、あれは
        /// <b>全編集の集約点</b>なので <see cref="MarkInputChangedSinceAnalysis"/> も走る。
        /// 何も触っていないのに編集扱いになり、計算書を出すたびに確認が出ていた。
        /// <c>MarkWorkSaved</c> と同じ役目。
        /// </summary>
        internal void RestoreInputChangedSinceAnalysis(bool changed)
        {
            // ファイルには「どの範囲が変わったか」まで持たせていないので、
            // 変更ありなら安全側 (モデル全体) に倒す。
            InputChangedSinceAnalysis = changed;
            _horizontalInputChanged = changed;
        }

        /// <summary>
        /// 入力が編集されたことを記録する。結果は破棄しない。
        /// 入力を変更するコマンド／編集ハンドラから呼ぶ。
        /// </summary>
        public void MarkInputChangedSinceAnalysis() => MarkInputChangedSinceAnalysis(AnalysisInputScope.All);

        /// <summary>
        /// 入力が編集されたことを記録する。<paramref name="scope"/> で<b>どの解析に効くか</b>を伝える。
        ///
        /// 既定は <see cref="AnalysisInputScope.All"/>。効く範囲が狭いと分かっている経路
        /// (群杭沈下の入力だけを触る画面) からのみ狭い値を渡すこと。
        /// 誤って狭く申告すると、陳腐化した結果に「最新」の顔をさせてしまう。
        /// </summary>
        public void MarkInputChangedSinceAnalysis(AnalysisInputScope scope)
        {
            if (_currentResultSet == null) return;
            if (scope == AnalysisInputScope.None) return;

            if (scope.HasFlag(AnalysisInputScope.Model))
            {
                _horizontalInputChanged = true;
            }
            else if (!IsGroupPileSettlementAnalysisDone)
            {
                // 沈下の入力しか触っておらず、群杭沈下の結果もまだ無い。
                // 陳腐化するものが無いので何も言わない。水平解析だけ済ませて
                // 沈下の入力を用意している最中に「沈下解析の再実行が必要です」と
                // 出るのは意味を成さない。
                return;
            }

            InputChangedSinceAnalysis = true;
            OnPropertyChanged(nameof(ResultSetStatusText));
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

            // 沈下の入力しか触っていないなら、取り直しても水平解析の結果と食い違わない
            // (水平解析は PileGroupSettlement を読まない)。取り直すほうが、
            // コンタの格子など「入力側に置かれた解析の産物」も一緒に新しくなって都合がよい。
            if (_horizontalInputChanged && horizontalIsStillTheCapturedOne)
            {
                // 沈下の結果はスナップショットと同じインスタンスを共有しているので、
                // ここで写す必要はない (以前は入力モデルの中にあり、写し忘れると
                // 結果表示に沈下が出なかった)。念のため結び付けだけ確かめておく。
                EnsureSettlementResultSharedWithSnapshot();
                Serilog.Log.Information(
                    "[結果セット] 入力が編集済みのためスナップショットは取り直さない "
                    + "(水平解析結果は解析時のまま。沈下の結果は共有しているのでそのまま出る)");
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
        /// 沈下の結果を、スナップショットからも同じインスタンスで見えるようにする。
        ///
        /// 結果の実体は <see cref="GroupSettlementResult"/> 1 つで、現在の入力とスナップショットの
        /// どちらからも同じものを指す。<see cref="AnalysisResultSet.Capture"/> と読込の復元で
        /// 結び付けているので通常は何もしないが、旧いファイル由来のモデルでは
        /// 沈下の入れ物ごと無いことがあるため、ここで作って結び付ける。
        /// </summary>
        private void EnsureSettlementResultSharedWithSnapshot()
        {
            var live = CurrentInputModel?.PileGroupSettlement;
            var snapshot = _currentResultSet?.InputSnapshot;
            if (live == null || snapshot == null) return;

            snapshot.PileGroupSettlement ??= new PileGroupSettlement();
            if (!ReferenceEquals(snapshot.PileGroupSettlement.Result, live.Result))
                snapshot.PileGroupSettlement.Result = live.Result;
        }

        /// <summary>
        /// ファイルから復元した結果セットを設定する。
        /// 保存時点で入力が編集済みだった場合は「変更あり」の状態も引き継ぐ。
        /// </summary>
        internal void SetRestoredResultSet(AnalysisResultSet? set, bool changedSinceAnalysis)
        {
            CurrentResultSet = set;
            bool changed = set != null && changedSinceAnalysis;
            InputChangedSinceAnalysis = changed;
            _horizontalInputChanged = changed;   // 範囲は保存していないので安全側
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
