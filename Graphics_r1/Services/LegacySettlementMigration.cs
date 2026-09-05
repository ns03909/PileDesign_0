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
        /// </summary>
        public static void AttachResultAndMigrate(
            Models.InputData.InputModel? input,
            Models.Results.GroupSettlementResult? loadedResult)
        {
            var pgs = input?.PileGroupSettlement;
            if (pgs == null) return;

            if (loadedResult != null) pgs.Result = loadedResult;
            Apply(pgs, input!.PileLayoutItems);
        }

        /// 旧ファイル互換マイグレーション:
        /// (1) "個別十字（基礎梁考慮）" → "個別十字（基礎梁反力）" の名称変更
        /// (2) CaseRecord.LoadingType が空文字のレコードを IsBeamAware から推定して補完
        /// (3) ActiveLoadingType が空ならアクティブレコード or 先頭レコードから推定
        /// (4) LoadingPlaneAltitudeNonBeam / BeamAware が NaN (新フィールド未設定) なら旧 LoadingPlaneAltitude をコピー
        /// (5) 入力の中に入っていたケース記録 (旧 "CaseRecords") を結果の型へ移す
        /// </summary>
        public static void Apply(
            PileGroupSettlement pgs,
            IEnumerable<PileLayoutDataItem>? piles = null)
        {
            if (pgs == null) return;

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
            if ((pgs.CaseRecords == null || pgs.CaseRecords.Count == 0)
                && (pgs.LegacySettlementGridData?.Count ?? 0) > 0)
            {
                pgs.CaseRecords =
                [
                    new GroupSettlementCaseRecord
                    {
                        LoadCaseName = "VL",
                        LoadingType = string.IsNullOrEmpty(pgs.LoadingType) ? "任意矩形" : pgs.LoadingType,
                        IsBeamAware = false,
                        IsConverged = true,
                        RectLoads = [.. (pgs.RectLoads ?? []).Select(r => r.Clone())],
                        SettlementGridData = [.. pgs.LegacySettlementGridData.Select(g => g.Clone())],
                        // 杭ごとの沈下量も複製から拾う。これが無いと SettlementOf() が
                        // 常に 0 を返し、旧ファイルでは杭配置グリッドの沈下量が空になる。
                        PileSettlements_mm = piles?
                            .Where(p => p != null && p.LegacyGroupPileSettlement != 0)
                            .ToDictionary(p => p.PileNo, p => p.LegacyGroupPileSettlement) ?? [],
                    }
                ];
                pgs.ActiveCaseIndex = 0;
            }

            // 旧ファイルの杭ごとの沈下量は結果へ移し終えたので受け取り口を空にする
            if (piles != null)
            {
                foreach (var p in piles)
                {
                    if (p != null) p.LegacyGroupPileSettlement = 0;
                }
            }

            if (pgs.CaseRecords == null || pgs.CaseRecords.Count == 0) return;

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
        }
    }
}
