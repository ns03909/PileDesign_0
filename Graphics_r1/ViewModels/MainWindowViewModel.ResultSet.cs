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

        private bool _resultSnapshotFailed;

        /// <summary>
        /// 直近の解析で、解析結果の控え (解析したときの入力の写し) を作れなかったか。
        ///
        /// 控えが無いと、結果表示・出力は<b>編集中の入力</b>を解析時の入力の代わりに見る。入力を編集した時点で
        /// 結果と入力が混ざるので、利用者に知らせ、編集後は混ざったことを状態表示に出し、計算書の出力を止める。
        /// 以前は複製の失敗をログに残すだけで、呼び出し側はそのまま続けていた。
        /// </summary>
        public bool ResultSnapshotFailed
        {
            get => _resultSnapshotFailed;
            private set
            {
                if (SetProperty(ref _resultSnapshotFailed, value))
                {
                    OnPropertyChanged(nameof(ResultSetStatusText));
                    OnPropertyChanged(nameof(ResultsMixedWithEditedInput));
                }
            }
        }

        // ── 入力の署名 (「再解析が必要」を入力の中身で判定する) ─────────────────
        //
        // 「再解析が必要」は編集のたびに印を立てる方式で、元に戻す・やり直しで解析時と同じ入力へ戻しても印が残り、
        // 逆に解析より前の入力へ戻しても印が立たなかった。元に戻す・やり直しのあとでは、解析したときの入力と
        // 今の入力の署名を比べて印を決め直す (RecheckInputAgainstAnalysis)。編集時に印を立てる仕組みはそのまま。

        /// <summary>解析結果の控えを取ったとき (= 最後に解析したとき) の入力の署名。分からなければ null。</summary>
        private string? _analysisInputSignature;

        /// <summary>沈下解析をしたときの入力の署名。沈下の結果は水平解析と別に陳腐化するので別に持つ。</summary>
        private string? _settlementInputSignature;

        private static readonly System.Text.Json.JsonSerializerOptions SignatureOptions = new()
        {
            // 参照の共有 ($id/$ref) を書かない。元に戻すは入力を DeepCopy で作り直し、共有していた参照が
            // 分かれることがある (保存ファイルの形が変わる)。中身が同じなら同じ署名になるよう、共有も全部書く
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        /// <summary>署名から除く、画面だけの状態・読むたびに振り直す番号。</summary>
        private static readonly System.Collections.Generic.HashSet<string> SignatureIgnoredNames = new(StringComparer.Ordinal)
        {
            "IsSelected", "IsVisible", "Id",
        };

        /// <summary>
        /// 入力の署名 (解析に効く中身が同じなら同じ値)。直列化できなければ null (判定できない — 印は変えない)。
        /// 選択・表示の有無と、実行時に振り直す節点の Id は含めない。
        /// </summary>
        internal static string? InputSignature(InputModel? input)
        {
            if (input == null) return null;
            try
            {
                var node = System.Text.Json.JsonSerializer.SerializeToNode(input, SignatureOptions);
                StripIgnored(node);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(node?.ToJsonString() ?? "");
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[結果セット] 入力の署名を作れませんでした (再解析が必要かの判定は編集の記録に任せます)");
                return null;
            }
        }

        private static void StripIgnored(System.Text.Json.Nodes.JsonNode? node)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).Where(SignatureIgnoredNames.Contains).ToList())
                        obj.Remove(key);
                    foreach (var (_, child) in obj) StripIgnored(child);
                    break;
                case System.Text.Json.Nodes.JsonArray arr:
                    foreach (var child in arr) StripIgnored(child);
                    break;
            }
        }

        /// <summary>
        /// 元に戻す・やり直しのあとに呼ぶ。今の入力を、解析したとき・沈下解析をしたときの入力と比べ、
        /// 「再解析が必要」の印を中身で決め直す。同じなら降ろし、違えば立てる (解析より前の入力へ戻したときも)。
        /// 署名が分からないものは今の印のまま。
        /// </summary>
        internal void RecheckInputAgainstAnalysis()
        {
            bool holdsResults = _currentResultSet != null || ResultSnapshotFailed;
            bool checkHorizontal = holdsResults && _analysisInputSignature != null;
            bool checkSettlement = _settlementInputSignature != null && HasSettlementResults();
            if (!checkHorizontal && !checkSettlement) return;

            string? current = InputSignature(CurrentInputModel);
            if (current == null) return;

            bool horizontalChanged = checkHorizontal ? current != _analysisInputSignature : _horizontalInputChanged;
            if (checkSettlement) _settlementInputChanged = current != _settlementInputSignature;

            bool stale = (holdsResults && horizontalChanged) || SettlementResultsAreStale;
            InputChangedSinceAnalysis = stale;          // false なら水平の印もここで降りる
            _horizontalInputChanged = horizontalChanged && stale;
            OnPropertyChanged(nameof(ResultSetStatusText));
        }

        /// <summary>
        /// 読込の仕上げで、解析したときの入力の署名を取る。ファイルが「解析後に編集していない」と言うなら
        /// 今の入力が解析時の入力。編集済みなら、別に持っている解析時の入力 (控え) から取る。どちらも無ければ分からない。
        /// </summary>
        internal void CaptureInputSignaturesAfterLoad()
        {
            if (!InputChangedSinceAnalysis)
            {
                string? current = InputSignature(CurrentInputModel);
                _analysisInputSignature = HasAnalysisResultSet ? current : null;
                _settlementInputSignature = _settlementInputChanged ? null : current;
            }
            else
            {
                var snapshot = _currentResultSet?.InputSnapshot;
                _analysisInputSignature = snapshot != null && !ReferenceEquals(snapshot, CurrentInputModel)
                    ? InputSignature(snapshot) : null;
                _settlementInputSignature = null;
            }
        }

        /// <summary>
        /// 表示中の沈下の結果が、水平解析とは別の時点の入力で解いたものか (水平解析の結果を持つときだけ)。
        /// 入力を編集したあと沈下解析だけをやり直すと、水平解析の控えは前の入力のまま、沈下は最新の実行を表示する。
        /// </summary>
        public bool SettlementSolvedFromOtherInput =>
            IsHorizontalAnalysisDone && _currentResultSet?.AnaModel != null
            && _analysisInputSignature != null && _settlementInputSignature != null
            && _analysisInputSignature != _settlementInputSignature
            && HasSettlementResults();

        /// <summary>控えが無いまま入力が編集され、表示・出力中の結果が編集後の入力と混ざっているか。</summary>
        public bool ResultsMixedWithEditedInput => ResultSnapshotFailed && InputChangedSinceAnalysis;

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
                {
                    OnPropertyChanged(nameof(ResultSetStatusText));
                    OnPropertyChanged(nameof(ResultsMixedWithEditedInput));
                }
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
        /// 沈下の結果が陳腐化しているか (沈下解析のあとに入力が編集された)。
        ///
        /// <para><b>水平解析とは別に持つ。</b> 沈下の結果は表示されるだけでなく、
        /// <b>次の解析の入力でもある</b> — 単杭沈下の荷重-沈下曲線を、水平解析の杭先端
        /// P-S ばねと基礎梁考慮沈下の杭頭ばねが読む。印を 1 つで兼ねると、水平解析を
        /// やり直した時点で沈下の陳腐化まで消え、入力変更前の曲線が「最新」の顔で
        /// 次の解析に入る。</para>
        ///
        /// <para>解析を実行したときではなく、<b>沈下解析を実行したとき</b>に降ろす
        /// (<see cref="MarkSettlementResultsCurrent"/>)。</para>
        /// </summary>
        private bool _settlementInputChanged;

        /// <summary>
        /// 表示・出力に使える沈下の結果があり、それが入力変更前のものか。
        /// 解析の入口で「古い沈下の結果を入力として使ってよいか」を尋ねるのに使う。
        /// </summary>
        public bool SettlementResultsAreStale => _settlementInputChanged && HasSettlementResults();

        /// <summary>
        /// 沈下解析が終わった (または読み込んだ結果が入力と整合している) ことを記録する。
        /// 沈下の陳腐化の印だけを降ろす。水平解析の印には触らない。
        /// </summary>
        public void MarkSettlementResultsCurrent()
        {
            // 沈下解析をしたときの入力 (元に戻したときに比べる相手)
            _settlementInputSignature = InputSignature(CurrentInputModel);
            if (!_settlementInputChanged) return;
            _settlementInputChanged = false;
            OnPropertyChanged(nameof(ResultSetStatusText));
        }

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
                if (_currentResultSet == null)
                    return ResultSnapshotFailed ? DescribeResultSnapshotStatus(InputChangedSinceAnalysis) : string.Empty;

                return BuildResultSetStatusText(
                    _currentResultSet.CapturedAt.ToString("yyyy-MM-dd HH:mm"),
                    horizontalStale: _horizontalInputChanged && IsHorizontalAnalysisDone,
                    settlementStale: SettlementResultsAreStale,
                    materialOptionsChanged: MaterialOptionsChangedSinceAnalysis,
                    settlementFromOtherInput: SettlementSolvedFromOtherInput);
            }
        }

        /// <summary>
        /// 状態表示の文を組む。<b>実際に持っている解析</b>だけを名指しする。
        /// </summary>
        /// <remarks>
        /// <para>2026-09-20 まで「水平解析の入力が編集された」だけで
        /// 「表示中の解析結果は … 実行時の入力によるものです（再解析が必要です）」と出していた。
        /// 単杭沈下だけを実行して入力を編集すると、<b>水平解析を実行していないのに</b>
        /// その結果が表示されている前提の文が出る (実機で確認)。</para>
        ///
        /// <para>沈下と水平解析は別に言う。沈下の結果は水平解析をやり直しても新しくならず、
        /// しかも次の解析の入力でもあるので、混ぜると「再解析した」つもりで古い曲線が使われる。</para>
        /// </remarks>
        /// <param name="stamp">結果を取った時刻の表示。</param>
        /// <param name="horizontalStale">水平解析の結果を持っていて、それが陳腐化しているか。</param>
        /// <param name="settlementStale">沈下の結果を持っていて、それが陳腐化しているか。</param>
        /// <param name="materialOptionsChanged">材料モデル化オプションが解析後に変わったか。</param>
        internal static string BuildResultSetStatusText(
            string stamp, bool horizontalStale, bool settlementStale, bool materialOptionsChanged,
            bool settlementFromOtherInput = false)
        {
            string baseText =
                horizontalStale && settlementStale
                    ? $"表示中の解析結果は {stamp} 実行時の入力によるものです（入力が変更されています。水平解析と沈下解析の再実行が必要です）"
                : horizontalStale
                    ? $"表示中の解析結果は {stamp} 実行時の入力によるものです（入力が変更されています。再解析が必要です）"
                : settlementStale
                    ? $"解析結果: {stamp} 実行／沈下解析の結果は入力変更前のものです（沈下解析の再実行が必要です）"
                    : $"解析結果: {stamp} 実行";

            // 沈下だけをやり直した (水平解析は前の入力のまま) ときは、表示中の沈下は最新の沈下解析の結果。
            // 水平解析とは解いた入力の時点が違うことを言う
            if (settlementFromOtherInput)
                baseText += "／沈下は水平解析と別の時点の入力で解いた最新の結果を表示しています";

            // 応答値は解析時のもの、限界曲線は今のオプションで引かれる。混ざったまま読ませない
            return materialOptionsChanged
                ? baseText + "／材料モデル化オプションが解析後に変更されています"
                    + "（限界曲線は変更後のオプションで描かれます。再解析が必要です）"
                : baseText;
        }

        /// <summary>控えを作れなかったときの状態表示。入力を編集したあとは、結果と入力が混ざっていることを言う。</summary>
        internal static string DescribeResultSnapshotStatus(bool inputEdited) => inputEdited
            ? "解析結果の控えが無いまま入力が編集されました。表示中の結果は編集後の入力と混ざっています（再解析が必要です）"
            : "解析結果の控えを作れませんでした（入力を編集すると、結果と編集後の入力が混ざります）";

        /// <summary>控えを作れなかったときに利用者へ知らせる文。</summary>
        internal static string DescribeResultSnapshotFailure() =>
            "解析結果の控え（解析したときの入力の写し）を作れませんでした。詳細はログに記録しています。\n\n"
            + "結果はこのまま表示できますが、このあと入力を編集すると、結果と編集後の入力が混ざって表示されます。"
            + "入力を編集する前に結果を確認・出力してください。編集した場合は、計算書を出す前に再解析してください。";

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
        /// <c>MarkProjectReplaced</c> と同じ役目。
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
            if (scope == AnalysisInputScope.None) return;

            // 沈下の結果は、モデル側の入力でも沈下側の入力でも陳腐化する
            // (地盤・杭配置が変われば曲線も沈下量も合わなくなる)。
            //
            // 下の 2 つの早期 return より前に置く。
            // ・結果セットの有無に縛らない: 沈下の結果は結果セットと独立に存在しうる
            //   (水平解析をしていない、結果セットを持たない旧いファイルを開いた等)。
            //   縛ると、その状態で入力を編集しても印が立たず、次の解析が古い曲線を黙って使う。
            // ・沈下の結果がまだ無い段階でも印だけ立てておき、沈下解析が終わったときに降ろす。
            //   実際に効くのは SettlementResultsAreStale (結果を持っているときだけ真)。
            _settlementInputChanged = true;

            // 控えを作れなかった結果は、編集中の入力を解析時の入力の代わりに見ている。
            // 編集した時点で結果と入力が混ざるので、そのことを記録する (結果セットが無くても)
            if (_currentResultSet == null && ResultSnapshotFailed)
            {
                _horizontalInputChanged = true;
                InputChangedSinceAnalysis = true;
                OnPropertyChanged(nameof(ResultSetStatusText));
                return;
            }

            if (_currentResultSet == null) return;

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

            if (set == null)
            {
                OnResultSnapshotFailed();
                return;
            }

            // スナップショット側の要素が VM 経由で「現在の入力」を見に行かないよう親を固定する
            set.InputSnapshot.AttachViewModel(this);

            CurrentResultSet = set;
            if (set.AnaModel != null) CurrentModel = set.AnaModel;
            InputChangedSinceAnalysis = false;
            ResultSnapshotFailed = false;
            _analysisInputSignature = InputSignature(CurrentInputModel);
        }

        /// <summary>
        /// 解析結果の控えを作れなかったときの後始末。
        ///
        /// 前の控えは、今の解析モデルと組でなければ捨てる。残すと、新しい解析の結果が<b>前の解析の入力</b>と
        /// 組になって表示される。今の解析は今の入力で解いたばかりなので、編集された印は降ろす
        /// (このあと編集すると <see cref="MarkInputChangedSinceAnalysis(AnalysisInputScope)"/> が立てる)。
        /// </summary>
        internal void OnResultSnapshotFailed()
        {
            if (_currentResultSet != null && !ReferenceEquals(_currentResultSet.AnaModel, CurrentModel))
                CurrentResultSet = null;
            InputChangedSinceAnalysis = false;
            ResultSnapshotFailed = true;
            // 控えは無いが、今の入力で解いたばかり。元に戻したときに比べる相手にはなる
            _analysisInputSignature = InputSignature(CurrentInputModel);
            Serilog.Log.Warning("[結果セット] 解析結果の控えを作れませんでした。結果表示は編集中の入力を見ます");
            PileDesign.Services.MessageService.Show(DescribeResultSnapshotFailure(), "解析結果の控え",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }

        /// <summary>
        /// 沈下の結果を、スナップショットからも同じインスタンスで見えるようにする。
        ///
        /// 結果の実体は <see cref="GroupSettlementResult"/> 1 つで、現在の入力とスナップショットの
        /// どちらからも同じものを指す。<see cref="AnalysisResultSet.Capture"/> と読込の復元で
        /// 結び付けているので通常は何もしないが、旧いファイル由来のモデルでは
        /// 沈下の入れ物ごと無いことがあるため、ここで作って結び付ける。
        /// </summary>
        internal void EnsureSettlementResultSharedWithSnapshot()
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
            ResultSnapshotFailed = false;
            bool changed = set != null && changedSinceAnalysis;
            InputChangedSinceAnalysis = changed;
            _horizontalInputChanged = changed;   // 範囲は保存していないので安全側
            _settlementInputChanged = changed;
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
            ResultSnapshotFailed = false;
            _analysisInputSignature = null;
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
        // コマンドとしては使っていない (画面に操作は無く、呼ぶのは内部とテストだけ)。2026-09-19 に属性を外した。
        public void DiscardAnalysisResults()
        {
            // 杭要素分割は入力側の状態なので残す (従来の挙動)。
            ClearAllAnalysisState(includeElementSplit: false);
            UpdateWindowImmediate();
        }
    }
}
