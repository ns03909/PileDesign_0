using PileDesign.Models.Results;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.ConfirmDeleteAnalysisModel.cs
    ///
    /// 責任範囲:
    /// - 解析結果削除の確認ダイアログ処理
    /// - 解析モデルのリセット処理
    /// - 解析関連フラグの一括リセット
    /// </summary>
    public partial class MainWindowViewModel
    {
        // 共通ヘルパ: 解析モデル削除の確認と（必要なら）リセット実行
        private bool ConfirmDeleteAnalysisModel(
            string message = "解析結果を削除します。よろしいですか？",
            string caption = "確認",
            MessageBoxImage icon = MessageBoxImage.Question,
            bool resetModel = true)
        {
            var result = MessageService.Show(message, caption, MessageBoxButton.YesNo, icon);
            if (result != MessageBoxResult.Yes) return false;

            if (resetModel)
            {
                // 解析に由来する状態は 1 か所にまとめてある。
                // ここで個別に消していた頃は、経路ごとに消し漏らしが出ていた。
                ClearAllAnalysisState(includeElementSplit: true);
                UpdateWindowImmediate();
            }

            return true;
        }

        /// <summary>
        /// 入力を変更するコマンドの入口で呼ぶ。
        ///
        /// 解析結果は<b>破棄しない</b>。実務では結果を横目に見ながら入力を変えていくため、
        /// 少しでも触ると結果が消える運用は成り立たない。解析完了時に入力ごと複製して
        /// 切り離してあるので、入力を編集しても結果表示は解析時のままで整合が崩れない。
        /// 代わりに「入力が変更された = 再解析が必要」であることを記録する。
        ///
        /// 杭要素分割は解析結果ではなく入力側の状態なので、従来どおり確認のうえ無効化する。
        /// </summary>
        private bool CheckAndResetAnalysisResults()
        {
            MarkInputChangedSinceAnalysis();
            return ConfirmDiscardInvalidatedByInputChange(includeElementSplit: true);
        }

        /// <summary>
        /// 杭要素分割ウィンドウの「保存」で呼ぶ。
        ///
        /// 確認を<b>開くときではなく保存するとき</b>に出すためのもの。開くだけなら入力は
        /// 変わらない (編集はすべて複製に対して行う) ので、中を見たいだけの人に結果の破棄を聞くことになっていた。
        ///
        /// 杭要素分割そのものは破棄の対象ではない。ここで保存される分割が画面に出ている
        /// もので、捨てられるわけではないため、文面にも出さない。
        ///
        /// 水平解析の結果は残る (解析時の入力ごと切り離してある) が、分割が変われば
        /// 再解析が要る。<b>何も消えなくても、影響があるなら知らせる</b>。
        /// 分割の中身が実際に変わったかは見ていない。見落として黙るより、
        /// 保存を押したときに毎回知らせるほうが安全側。
        /// </summary>
        public bool ConfirmSaveElementDivision()
        {
            bool settlementGoesStale = HasSettlementResults();
            bool horizontalGoesStale = IsHorizontalAnalysisDone || HasAnalysisResultSet;

            // 影響を受ける結果が無ければ黙って通す
            if (settlementGoesStale || horizontalGoesStale)
            {
                var parts = new List<string>();
                if (horizontalGoesStale) parts.Add("・水平解析の結果は残りますが、再解析が必要になります");
                if (settlementGoesStale) parts.Add("・沈下解析の結果は残りますが、再解析が必要になります");

                string msg = "杭要素分割を保存すると、解析結果に次の影響があります。\n\n"
                           + string.Join("\n", parts)
                           + "\n\n保存しますか？";

                var result = MessageService.Show(msg, "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return false;
            }

            MarkInputChangedSinceAnalysis();
            UpdateWindowImmediate();
            return true;
        }

        /// <summary>
        /// 入力変更で無効になるものを確認し、破棄する。
        ///
        /// <b>解析結果は破棄しない。</b>解析完了時に入力ごと複製して切り離してあるので、
        /// 入力を編集しても結果表示は解析時のまま整合する。代わりに「再解析が必要」の
        /// 印が立つ (<see cref="MarkInputChangedSinceAnalysis()"/>)。
        ///
        /// <para>沈下の結果も 2026-09-20 から破棄しない。切り離しが済んで
        /// (<see cref="Models.Results.GroupSettlementResult"/> が持ち、スナップショットと同じ
        /// インスタンスを指す)、各杭の沈下量も結果から引く計算プロパティになり、傾斜角検定も
        /// スナップショットを読むようになったので、水平解析と同じ扱いにできる。
        /// 残すと古い値が入力系の表示に出る、という以前の理由は解消している。</para>
        ///
        /// <para><b>杭要素分割だけは従来どおり取り消す。</b>解析結果ではなく入力側の状態で、
        /// ジオメトリが変われば分割そのものが成り立たない。</para>
        ///
        /// 取り消すものが無ければダイアログを出さずに true を返す。
        /// </summary>
        /// <param name="includeElementSplit">杭要素分割も対象にするか（ジオメトリを変える編集で true）。</param>
        /// <param name="reason">「〜により、」として文頭に付ける理由（省略可）。</param>
        private bool ConfirmDiscardInvalidatedByInputChange(bool includeElementSplit, string? reason = null)
        {
            bool discardSplit = includeElementSplit && IsElementSplit;
            if (!discardSplit) return true;

            bool resultsGoStale = HasSettlementResults() || IsHorizontalAnalysisDone || HasAnalysisResultSet;

            string msg = (string.IsNullOrEmpty(reason) ? string.Empty : $"{reason}により、")
                       + "杭要素分割が取り消されます。続けますか？"
                       + (resultsGoStale ? "\n（解析結果は保持されますが、再解析が必要になります）" : string.Empty);

            var result = MessageService.Show(msg, "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return false;

            IsElementSplit = false;

            UpdateWindowImmediate();
            return true;
        }

        /// <summary>
        /// 入力モデル 1 つぶんの沈下の痕跡を消す。
        ///
        /// 結果そのもの (<c>PileGroupSettlement.Result</c>) は現在の入力とスナップショットで
        /// <b>同じインスタンス</b>なので 1 回消せば足りる。ここで消すのは、入力側に残っている
        /// 複製 (旧ファイル用の沈下グリッド・各杭の沈下量) と、土層-杭セットが持つ
        /// 単杭沈下の結果で、こちらはモデルごとに存在する。
        /// </summary>
        private static void ClearSettlementResultsIn(Models.InputData.InputModel? input)
        {
            if (input == null) return;

            // 群杭沈下と単杭沈下は別々に持たれているので、片方が無くても他方を消す。
            // 沈下の入れ物 (PileGroupSettlement) を持たないモデルもあるため、
            // ここで早期に return すると単杭沈下の結果が消え残る (2026-09-20)。
            var pgs = input.PileGroupSettlement;
            if (pgs != null)
            {
                // 中身を空にせず、空の結果に差し替える (前の実行の結果を持っている側を書き換えない)
                pgs.Result = new Models.Results.GroupSettlementResult();
                pgs.SettlementGridData = [];
                if (input.PileLayoutItems != null)
                    foreach (var pile in input.PileLayoutItems) pile.NotifyGroupPileSettlementChanged();
            }

            ClearSinglePileSettlementResultsIn(input);
        }

        /// <summary>
        /// 単杭沈下の結果 (土層-杭セットごとの荷重-沈下曲線と節点別の履歴) を消す。
        ///
        /// <para>2026-09-20 まで、ここを消していなかった。「沈下解析結果が削除されます」と
        /// 確認したうえで消えるのは群杭沈下の記録だけで、単杭沈下の曲線と履歴は残っていた。
        /// 画面・計算書・グラフは <c>IsVerticalAnalysisDone</c> が false になるので「未実行」と
        /// 扱うのに、<b>解析の入口は残った値をそのまま使っていた</b>。</para>
        ///
        /// <list type="bullet">
        /// <item>水平解析の杭節点 Z ばね (P-S ばね) は節点別履歴の有無だけを見る
        ///   (<c>AnalysisModelling.ShouldApplyVerticalSpringsToPile</c>)</item>
        /// <item>基礎梁考慮沈下の杭頭ばねは曲線の有無だけを見る (<c>VerticalBeamModelling</c>)</item>
        /// <item>保存して開き直すと、曲線の有無から <c>IsVerticalAnalysisDone</c> が true に戻り、
        ///   消したはずの単杭沈下が「実行済み」として復活する</item>
        /// </list>
        ///
        /// <para>曲線は「結果でありながら次の解析の入力でもある」ため置き場所は
        /// <c>SoilPile</c> のままにしてある (<see cref="Models.Results.SinglePileSettlementResult"/>)。
        /// 置き場所が入力側にあることと、消さなくてよいことは別の話。</para>
        ///
        /// <para>節点別履歴は <c>DeepCopy</c> が同じリストを共有するので、空のリストを
        /// 入れ直す (<c>Clear()</c> だと写し先にも及ぶ)。呼び出し側は現在の入力と
        /// スナップショットの両方に対してこれを呼ぶ。</para>
        /// </summary>
        private static void ClearSinglePileSettlementResultsIn(Models.InputData.InputModel input)
        {
            var soilPiles = input.ElementDivision?.SoilPiles;
            if (soilPiles == null) return;

            foreach (var sp in soilPiles)
            {
                if (sp == null) continue;
                sp.LoadDisplacements = [];
                sp.LoadDisplacementsLimit = [];
                sp.NodeDisplacements = [];
                sp.NodeReactions = [];
            }
        }

        /// <summary>
        /// 土層-杭セットを作り直したので、単杭沈下の結果が無くなったことを記録する。
        ///
        /// <para><see cref="Models.InputData.InputModel.GenerateSoilPiles"/> は
        /// <see cref="Models.InputData.SoilPile"/> を<b>新規構築</b>し、引き継ぐのは荷重面等価径と
        /// kh0 の手入力だけ。単杭沈下の荷重-沈下曲線と節点別履歴はそこに載っているので失われる。</para>
        ///
        /// <para>入力編集で沈下の結果を捨てなくなった (2026-09-20) ため、<b>旗だけが立ったまま
        /// 中身が無い</b>状態が起きた。グラフを開いても曲線が空、P-S ばねは無音で付かない、
        /// 保存すると次に開いたとき未実行に戻る。旗は中身に追従させる。</para>
        ///
        /// <para>鍵 (地盤番号・杭体番号・Z) が一致する曲線を貼り直すことはしない。鍵は
        /// 土層-杭セットの<b>形</b>を表さないので、杭長や土層が変わった曲線を「使える」と
        /// 誤認する。読込のときだけ貼り直してよいのは、そこでは形も一緒に読むため。</para>
        /// </summary>
        internal void NoteSinglePileSettlementResultsLost()
        {
            if (!IsVerticalAnalysisDone) return;

            IsVerticalAnalysisDone = false;
            Serilog.Log.Information(
                "[沈下] 土層-杭セットを作り直したため、単杭沈下の結果 (荷重-沈下曲線・節点別履歴) を無効にした");
        }

        /// <summary>
        /// 古い沈下の結果を<b>次の解析の入力として</b>使ってよいかを尋ねる。
        ///
        /// <para>入力編集で沈下の結果を捨てなくなった (2026-09-20、水平解析と同じ扱いに揃えた) ため、
        /// 単杭沈下の荷重-沈下曲線と節点別履歴が入力変更前のまま残る。これを読むのは表示だけでなく
        /// 次の解析でもある。</para>
        ///
        /// <list type="bullet">
        /// <item>水平解析の杭先端 P-S ばね (節点別履歴)</item>
        /// <item>基礎梁を考慮した沈下解析の杭頭ばね (荷重-沈下曲線)</item>
        /// </list>
        ///
        /// <para>捨てていた頃は「無ければ使わない」で済んでいたが、残すなら<b>入口で尋ねる</b>しかない。
        /// 黙って使うと、ばねだけ入力変更前という混ざった解析になる。解法には触らない。</para>
        /// </summary>
        /// <param name="what">尋ねる解析の名前 (「水平解析」など)。</param>
        /// <param name="uses">古い結果を何に使うか (「杭先端の P-S ばね」など)。</param>
        /// <returns>続けてよいなら true。</returns>
        public bool ConfirmUsingStaleSettlementResults(string what, string uses)
        {
            if (!SettlementResultsAreStale) return true;

            string msg = $"沈下解析の結果が入力変更前のものです。\n"
                       + $"{what}は{uses}にこの結果を使います。\n\n"
                       + "先に沈下解析を再実行することをおすすめします。\n"
                       + "このまま続けますか？";
            return MessageService.Show(msg, "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes;
        }

        // テスト用フック (内部ロジックをそのまま検証する)
        internal bool HasSettlementResultsForTest() => HasSettlementResults();
        internal void ClearSettlementResultsForTest() => ClearSettlementResults();

        /// <summary>破棄対象になる沈下解析の結果を持っているか。</summary>
        private bool HasSettlementResults()
        {
            // 複製 (SettlementGridData) は見ない。読込時のマイグレーションで
            // 旧ファイルからも記録が建つので、記録だけ見れば足りる。
            var pgs = CurrentInputModel?.PileGroupSettlement;
            return (pgs?.CaseRecords?.Count ?? 0) > 0
                || IsVerticalAnalysisDone
                || IsGroupPileSettlementAnalysisDone;
        }

        /// <summary>
        /// 沈下解析の結果を破棄する。
        ///
        /// 沈下の結果は入力モデルの中に格納されているので、現在の入力と
        /// <b>解析時のスナップショット</b>の両方から消す。結果表示はスナップショットを読むため、
        /// 現在の入力だけ消すと「削除しました」と言いながら結果テーブルに残る。
        /// </summary>
        private void ClearSettlementResults()
        {
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;

            ClearSettlementResultsIn(CurrentResultSet?.InputSnapshot);
            ClearSettlementResultsIn(CurrentInputModel);

            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(GroupSettlementRouteOptions));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));
        }

        /// <summary>
        /// 基本設定の Z=0 標高など、ジオメトリに影響する変更時に呼ぶ。
        /// 解析結果は破棄せず、杭要素分割のみキャンセル対象。
        /// 杭要素分割が無ければダイアログなしで true。
        /// </summary>
        public bool ConfirmResetAllForGeometryChange(string reason)
        {
            // 水平解析の結果は破棄しない (CheckAndResetAnalysisResults と同じ方針)。
            MarkInputChangedSinceAnalysis();
            return ConfirmDiscardInvalidatedByInputChange(includeElementSplit: true, reason: reason);
        }

        /// <summary>
        /// 荷重条件など、ジオメトリを変更しない編集で呼ぶヘルパ。
        ///
        /// 解析結果は<b>破棄しない</b>。荷重条件のように頻繁に触る入力でダイアログを出すと
        /// 「結果を見ながら条件を変える」使い方ができなくなる。解析完了時に入力ごと複製して
        /// 切り離してあるので、編集しても結果表示は解析時のまま整合する。
        /// </summary>
        public bool CheckAndResetAnalysisResultsKeepingSplit(string text)
        {
            MarkInputChangedSinceAnalysis();
            // 杭要素分割は保持する。沈下解析の結果は入力の中にあるので従来どおり破棄する
            // (沈下解析を使っていなければダイアログは出ない)。
            return ConfirmDiscardInvalidatedByInputChange(includeElementSplit: false, reason: text);
        }

        /// <summary>
        /// 材料・断面の変更時など、確認ダイアログなしで解析結果を自動削除する。
        /// 解析結果が存在する場合のみリセットを実行する。
        /// </summary>
        public void ResetAnalysisResultsSilently()
        {
            // フラグが全部 false でも、結果セットや入力モデル内の沈下結果が残っていることがある
            // (フラグだけ消す経路が過去にあったため)。残っていたら消す。
            if (!IsHorizontalAnalysisDone && !IsVerticalAnalysisDone
                && !IsGroupPileSettlementAnalysisDone && !IsVerticalBeamAnalysisDone
                && !HasAnalysisResultSet && !HasSettlementResults())
                return;

            ClearAllAnalysisState(includeElementSplit: true);
            UpdateWindowImmediate();
        }

        // 「解析結果を削除する」コマンドは置かない。結果は入力の編集で自動的に 「再解析が必要」になり、
        // 明示的に消す操作は画面に無かった (2026-09-19 に撤去)。消す必要があるときは ResetAnalysisResultsSilently を使う。

    }
}