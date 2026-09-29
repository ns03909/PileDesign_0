using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 群杭沈下の旧ファイルを、いまの形 (ケース記録) へ移す。
    ///
    /// 結果は本来 <see cref="GroupSettlementCaseRecord"/> が持つ。入力モデルの中にある
    /// 複製 (<c>PileGroupSettlement.SettlementGridData</c> /
    /// <c>PileLayoutDataItem.GroupPileSettlement</c>) は移行のなごりで、
    /// <b>複製を読んでよいのはここだけ</b>。他所で読むと、ケースを切り替えたのに
    /// そこだけ古い値が出る。ソース走査テストがこのファイル以外での読みを検出する。
    /// </summary>
    internal static class LegacySettlementMigration
    {
        /// <summary>
        /// 読込直後の入力モデルに、保存されていた沈下の結果を結び付けて移行まで済ませる。
        ///
        /// 結果は入力とは別の節 (<c>ProjectData.GroupSettlementResult</c>) にあり、
        /// 旧ファイルでは入力の中 ("CaseRecords") にある。読込の経路が 1 つではないので、
        /// <b>この 2 つをまとめてここで行う</b> (片方だけ呼ぶと沈下の結果が出ない)。
        /// 戻り値は利用者に知らせること (無ければ空)。
        /// </summary>
        public static IReadOnlyList<string> AttachResultAndMigrate(
            Models.InputData.InputModel? input,
            Models.Results.GroupSettlementResult? loadedResult)
        {
            var pgs = input?.PileGroupSettlement;
            if (pgs == null) return [];

            if (loadedResult != null) pgs.Result = loadedResult;
            return Apply(pgs, input!.PileLayoutItems);
        }

        /// <summary>
        /// 旧形式から復元した結果で、収束状態を確かめられないときの理由 (検定不能の理由・計算書の注記に使う)。
        /// </summary>
        internal const string UnknownConvergenceReason =
            "群杭沈下の結果は旧形式のファイルから復元したもので、収束状態と解析条件を確認できません (収束状態: 不明)。"
            + "群杭沈下解析を再実行してください";

        /// <summary>旧形式の複製から結果を復元したことを知らせる文面。</summary>
        internal static string DescribeRestoredWithUnknownConvergence()
            => "旧形式のファイルから群杭沈下の結果 (コンタ図・杭ごとの沈下量) を復元しました。\n"
             + "旧形式には収束状態と解析条件の記録が無いため、収束状態は「不明」として扱い、"
             + "この結果による沈下量・変形角の検定は行いません (検定不能)。群杭沈下解析を再実行してください。";

        /// <summary>杭番号が重なっていて沈下量を移さなかった杭を知らせる文面。</summary>
        internal static string DescribeDuplicatePileNos(IReadOnlyList<(int PileNo, int Count)> duplicates)
            => "旧形式の群杭沈下量を移すときに、同じ杭番号が複数の杭に付いていました: "
             + string.Join("・", duplicates.Select(d => $"No.{d.PileNo} ({d.Count} 本)")) + "。\n"
             + "どの杭の値か決められないため、これらの番号の杭の沈下量は移していません (0 としても扱いません)。"
             + "ほかの杭の沈下量とコンタ図は移しています。";

        /// 旧ファイル互換マイグレーション:
        /// (1) "個別十字（基礎梁考慮）" → "個別十字（基礎梁反力）" の名称変更
        /// (2) CaseRecord.LoadingType が空文字のレコードを IsBeamAware から推定して補完
        /// (3) ActiveLoadingType が空ならアクティブレコード or 先頭レコードから推定
        /// (4) LoadingPlaneAltitudeNonBeam / BeamAware が NaN (新フィールド未設定) なら旧 LoadingPlaneAltitude をコピー
        /// (5) 入力の中に入っていたケース記録 (旧 "CaseRecords") を結果の型へ移す
        /// </summary>
        public static IReadOnlyList<string> Apply(
            PileGroupSettlement pgs,
            IEnumerable<PileLayoutDataItem>? piles = null)
        {
            var notices = new List<string>();
            if (pgs == null) return notices;

            // (0) 旧ファイルは結果 (ケース記録) を入力の中に持っている。
            //     いまは結果を別の型 (GroupSettlementResult) が持ち、保存も別の節なので、
            //     受け取り口から結果側へ移して<b>空にする</b>。
            //     空にしないと、開いて保存し直したファイルに結果が二重に入る。
            if (pgs.LegacyCaseRecords is { Count: > 0 } legacy)
            {
                if (!pgs.Result.HasResults)
                {
                    foreach (var rec in legacy) pgs.Result.CaseRecords.Add(rec);
                }
                pgs.LegacyCaseRecords = [];
            }

            // (1) 名称変更マイグレーション
            const string oldName = "個別十字（基礎梁考慮）";
            const string newName = "個別十字（基礎梁反力）";
            if (pgs.LoadingType == oldName) pgs.LoadingType = newName;
            if (pgs.ActiveLoadingType == oldName) pgs.ActiveLoadingType = newName;

            // (4) 荷重面標高の per-route フィールド初期化 (旧データ互換)
            if (double.IsNaN(pgs.LoadingPlaneAltitudeNonBeam))
                pgs.LoadingPlaneAltitudeNonBeam = pgs.LoadingPlaneAltitude;
            if (double.IsNaN(pgs.LoadingPlaneAltitudeBeamAware))
                pgs.LoadingPlaneAltitudeBeamAware = pgs.LoadingPlaneAltitude;

            // (5) CaseRecords を持たない旧ファイル: 複製しか残っていないので、そこから 1 件復元する。
            //     表示系は ActiveRecord を読むようになったため、これが無いと旧ファイルの
            //     沈下コンタが出なくなる。
            //     複製には収束状態も解析条件も残っていないので、収束したとは書かない (「不明」)。
            if ((pgs.CaseRecords == null || pgs.CaseRecords.Count == 0)
                && (pgs.LegacySettlementGridData?.Count ?? 0) > 0)
            {
                var (pileSettlements, duplicates) = CollectLegacyPileSettlements(piles);
                pgs.CaseRecords =
                [
                    new GroupSettlementCaseRecord
                    {
                        LoadCaseName = "VL",
                        LoadingType = string.IsNullOrEmpty(pgs.LoadingType) ? "任意矩形" : pgs.LoadingType,
                        IsBeamAware = false,
                        IsConverged = false,
                        IsConvergenceUnknown = true,
                        RectLoads = [.. (pgs.RectLoads ?? []).Select(r => r.Clone())],
                        SettlementGridData = [.. pgs.LegacySettlementGridData.Select(g => g.Clone())],
                        // 杭ごとの沈下量も複製から拾う。これが無いと SettlementOf() が
                        // 常に 0 を返し、旧ファイルでは杭配置グリッドの沈下量が空になる。
                        PileSettlements_mm = pileSettlements,
                    }
                ];
                pgs.ActiveCaseIndex = 0;
                notices.Add(DescribeRestoredWithUnknownConvergence());
                if (duplicates.Count > 0)
                {
                    Serilog.Log.Warning("[読込] 旧形式の群杭沈下量: 杭番号が重なっているため移さなかった杭 {Duplicates}",
                        string.Join(", ", duplicates.Select(d => $"No.{d.PileNo}×{d.Count}")));
                    notices.Add(DescribeDuplicatePileNos(duplicates));
                }
            }

            // 旧ファイルの杭ごとの沈下量は結果へ移し終えたので受け取り口を空にする
            if (piles != null)
            {
                foreach (var p in piles)
                {
                    if (p != null) p.LegacyGroupPileSettlement = 0;
                }
            }

            if (pgs.CaseRecords == null || pgs.CaseRecords.Count == 0) return notices;

            // 表示するケースが決まっていない旧ファイルは先頭を選ぶ
            if (pgs.ActiveCaseIndex < 0 || pgs.ActiveCaseIndex >= pgs.CaseRecords.Count)
                pgs.ActiveCaseIndex = 0;

            string fallback = string.IsNullOrEmpty(pgs.LoadingType) ? "任意矩形" : pgs.LoadingType;
            foreach (var rec in pgs.CaseRecords)
            {
                if (rec.LoadingType == oldName) rec.LoadingType = newName;
                if (string.IsNullOrEmpty(rec.LoadingType))
                {
                    rec.LoadingType = rec.IsBeamAware ? "個別矩形（基礎梁考慮）" : fallback;
                }
            }

            // (3) ActiveLoadingType の推定
            if (string.IsNullOrEmpty(pgs.ActiveLoadingType))
            {
                int idx = pgs.ActiveCaseIndex;
                if (idx >= 0 && idx < pgs.CaseRecords.Count)
                    pgs.ActiveLoadingType = pgs.CaseRecords[idx].LoadingType;
                else
                    pgs.ActiveLoadingType = pgs.CaseRecords[0].LoadingType;
            }
            return notices;
        }

        /// <summary>
        /// 杭ごとの旧い沈下量を杭番号で引ける形にする。
        ///
        /// 同じ杭番号が複数の杭に付いていると、番号からどの杭の値か決められない。以前は
        /// <c>ToDictionary</c> がそこで例外を投げ、ファイル全体が開けなかった。重なった番号の値は移さずに返し
        /// (呼び出し側が知らせる)、ほかの杭の値は移す。値が 0 の杭も番号の重なりには数える
        /// (0 の杭と値のある杭が同じ番号なら、どちらの値かやはり決められない)。
        /// </summary>
        internal static (Dictionary<int, double> Settlements, List<(int PileNo, int Count)> Duplicates)
            CollectLegacyPileSettlements(IEnumerable<PileLayoutDataItem>? piles)
        {
            var settlements = new Dictionary<int, double>();
            var duplicates = new List<(int PileNo, int Count)>();
            if (piles == null) return (settlements, duplicates);

            foreach (var group in piles.Where(p => p != null).GroupBy(p => p.PileNo).OrderBy(g => g.Key))
            {
                var members = group.ToList();
                if (members.Count > 1)
                {
                    if (members.Any(p => p.LegacyGroupPileSettlement != 0))
                        duplicates.Add((group.Key, members.Count));
                    continue;
                }
                double value = members[0].LegacyGroupPileSettlement;
                if (value != 0) settlements[group.Key] = value;
            }
            return (settlements, duplicates);
        }
    }
}
