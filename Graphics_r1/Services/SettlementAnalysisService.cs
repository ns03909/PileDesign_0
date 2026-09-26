using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace PileDesign.Services
{
    /// <summary>
    /// 群杭沈下解析に関するサービスクラス
    /// Steinnbrenerの近似式を用いた沈下計算を担当
    /// </summary>
    public class SettlementAnalysisService
    {
        /// <summary>
        /// 群杭沈下解析の結果
        /// </summary>
        public class SettlementAnalysisResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public ObservableCollection<SettlementGridDataItem> SettlementGridData { get; set; }

            /// <summary>
            /// 各杭の沈下量 [mm]。Key = <c>PileLayoutDataItem.PileNo</c>。
            ///
            /// 以前は各杭の <c>GroupPileSettlement</c> に書き込むだけで、ケースの記録は
            /// そこから拾い直していた。結果の正が入力側にある状態だったので、ここで返す。
            /// </summary>
            public Dictionary<int, double> PileSettlements_mm { get; set; } = [];

            /// <summary>
            /// 計算は済んだが知らせたいこと (荷重を作らなかった杭、反力の無い杭など)。画面が警告として出す。
            /// 以前はこれらを黙って飛ばしていたので、沈下が小さく出ても理由が分からなかった。
            /// </summary>
            public List<string> Warnings { get; set; } = [];
        }

        /// <summary>
        /// 群杭沈下解析を実行
        /// </summary>
        /// <param name="pileGroupSettlement">群杭沈下解析設定</param>
        /// <param name="pileLayoutItems">杭配置アイテム</param>
        /// <param name="soilPiles">土バネ杭（ElementDivisionから）</param>
        /// <param name="gridXItems">グリッドX座標</param>
        /// <param name="gridYItems">グリッドY座標</param>
        /// <param name="xMin">X最小値</param>
        /// <param name="xMax">X最大値</param>
        /// <param name="yMin">Y最小値</param>
        /// <param name="yMax">Y最大値</param>
        /// <param name="xOffset">Xオフセット</param>
        /// <param name="yOffset">Yオフセット</param>
        /// <param name="xSpacing">X間隔</param>
        /// <param name="ySpacing">Y間隔</param>
        /// <returns>解析結果</returns>
        public SettlementAnalysisResult PerformSettlementAnalysis(
            PileGroupSettlement pileGroupSettlement,
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<SoilPile> soilPiles,
            ObservableCollection<GridDataItem> gridXItems,
            ObservableCollection<GridDataItem> gridYItems,
            double xMin,
            double xMax,
            double yMin,
            double yMax,
            double xOffset,
            double yOffset,
            double xSpacing,
            double ySpacing,
            ObservableCollection<VerticalBeamCaseResult> verticalBeamCaseResults = null)
        {
            // 土層が0の場合は警告を出して処理を中断
            if (pileGroupSettlement.SettlementSoilLayers == null ||
                pileGroupSettlement.SettlementSoilLayers.Count == 0)
            {
                return new SettlementAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "群杭沈下解析用の土層が1層以上必要です。"
                };
            }

            // 「個別十字（基礎梁反力）」は基礎梁考慮鉛直解析の常時 (VL) ケースの杭反力を荷重にする。
            // 以前は VL ケースが見つからないと先頭のケースを返していたので、別のケースの反力で沈下を求め得た。
            // 結果がまったく無いときは全杭の荷重が 0 になり、沈下 0 が正常な結果として出た。
            if (pileGroupSettlement.LoadingType == BeamReactionLoadingType
                && FindVBLongTermCase(verticalBeamCaseResults) == null)
            {
                return new SettlementAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "「個別十字（基礎梁反力）」は、単杭沈下解析（基礎梁考慮）の常時 (VL) ケースの杭反力を荷重にします。"
                        + "その結果がありません。単杭沈下解析（基礎梁考慮）を実行してから、群杭沈下解析をやり直してください。",
                };
            }

            // 矩形荷重の生成
            var notes = new List<string>();
            ObservableCollection<RectLoad> rectLoads = GenerateRectLoadsInternal(
                pileGroupSettlement,
                pileLayoutItems,
                soilPiles,
                verticalBeamCaseResults,
                notes);

            // 荷重が 1 つも無ければ沈下はどこも 0 になる。正常な結果として出さず、理由を返す
            if (rectLoads == null || rectLoads.Count == 0)
            {
                return new SettlementAnalysisResult
                {
                    Success = false,
                    ErrorMessage = pileGroupSettlement.LoadingType == "任意矩形"
                        ? "矩形荷重が 1 つもありません。荷重の表で矩形荷重を入力してください。"
                        : "矩形荷重を 1 つも作れませんでした。" + string.Join("", notes.Select(n => "\n" + n)),
                };
            }
            if (rectLoads.All(r => r.QA == 0))
                notes.Add("矩形荷重の荷重がすべて 0 です。沈下はどこも 0 になります (杭の常時軸力・荷重の入力を確認してください)。");

            // 荷重面が土層内にある場合は、最上層を荷重面で切り詰めた解析用レイヤを使用
            var effectiveLayers = PileGroupSettlement.GetEffectiveLayersForAnalysis(
                pileGroupSettlement.SoilLayersTopAltitude,
                pileGroupSettlement.LoadingPlaneAltitude,
                pileGroupSettlement.SettlementSoilLayers);

            // 各杭位置での沈下量を計算
            var pileSettlements = CalculatePileSettlements(pileLayoutItems, rectLoads, effectiveLayers);

            // グリッドの設定
            pileGroupSettlement.SetGridX(xMin, xMax, xOffset, xSpacing, gridXItems);
            pileGroupSettlement.SetGridY(yMin, yMax, yOffset, ySpacing, gridYItems);

            // グリッド上の沈下量を計算
            var settlementGridData = CalculateGridSettlements(
                pileGroupSettlement.SettlementGridX,
                pileGroupSettlement.SettlementGridY,
                rectLoads,
                effectiveLayers);

            return new SettlementAnalysisResult
            {
                Success = true,
                SettlementGridData = settlementGridData,
                PileSettlements_mm = pileSettlements,
                Warnings = notes,
            };
        }

        /// <summary>
        /// 「個別十字」系および「個別矩形」系の荷重タイプに対し、杭ごとの矩形荷重を自動生成する公開ヘルパー。
        /// 荷重タイプが自動生成系でない場合は既存 RectLoads を返す (任意矩形)。
        /// </summary>
        public static ObservableCollection<RectLoad> BuildAutoCrossRectLoads(
            PileGroupSettlement pileGroupSettlement,
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<SoilPile> soilPiles,
            ObservableCollection<VerticalBeamCaseResult> verticalBeamCaseResults)
        {
            return GenerateRectLoadsInternal(pileGroupSettlement, pileLayoutItems, soilPiles, verticalBeamCaseResults);
        }

        /// <summary>
        /// 荷重タイプに応じて矩形荷重を生成
        /// </summary>
        private ObservableCollection<RectLoad> GenerateRectLoads(
            PileGroupSettlement pileGroupSettlement,
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<SoilPile> soilPiles,
            ObservableCollection<VerticalBeamCaseResult> verticalBeamCaseResults)
        {
            return GenerateRectLoadsInternal(pileGroupSettlement, pileLayoutItems, soilPiles, verticalBeamCaseResults);
        }

        /// <summary>「個別十字（基礎梁反力）」の荷重タイプ名。</summary>
        private const string BeamReactionLoadingType = "個別十字（基礎梁反力）";

        /// <param name="notes">知らせたいこと (荷重を作らなかった杭など) を足す先。要らなければ null</param>
        private static ObservableCollection<RectLoad> GenerateRectLoadsInternal(
            PileGroupSettlement pileGroupSettlement,
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<SoilPile> soilPiles,
            ObservableCollection<VerticalBeamCaseResult> verticalBeamCaseResults,
            List<string>? notes = null)
        {
            ObservableCollection<RectLoad> rectLoads = [];
            // 荷重面等価径が未入力 (0 以下) で荷重を作らなかった杭
            var skipped = new List<int>();

            if (pileGroupSettlement.LoadingType == "任意矩形")
            {
                rectLoads = pileGroupSettlement.RectLoads;
            }
            else if (pileGroupSettlement.LoadingType == "個別矩形")
            {
                // 個別矩形: 杭ごとに 1 矩形 (一辺 = √π · GroupPileLoadDia/2、円と等価面積)
                // 自動生成後はユーザが DX/DY を変更可能 (DX/DY セッターは中心を保持)
                // 既存 RectLoads に LinkedPileNo が設定されたものがあれば再利用 (DX/DY 編集を温存)
                var existingByPileNo = pileGroupSettlement.RectLoads
                    .Where(r => r.LinkedPileNo > 0)
                    .ToDictionary(r => r.LinkedPileNo, r => r);

                foreach (PileLayoutDataItem pileLayoutDataItem in pileLayoutItems)
                {
                    SoilPile soilPile = soilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    double radius = soilPile.GroupPileLoadDia * 0.5;
                    if (radius <= 0) { skipped.Add(pileLayoutDataItem.No); continue; }
                    double qa = pileLayoutDataItem.AxialForceVL0 + pileLayoutDataItem.AxialForceVLAdditional;

                    if (existingByPileNo.TryGetValue(pileLayoutDataItem.PileNo, out var existing))
                    {
                        // 既存矩形: 中心を杭位置に追従、QA は更新、DX/DY (寸法) は維持
                        existing.CenterX = pileLayoutDataItem.Point3D.X;
                        existing.CenterY = pileLayoutDataItem.Point3D.Y;
                        existing.QA = qa;
                        rectLoads.Add(existing);
                    }
                    else
                    {
                        // 新規生成: 一辺 = √π · r (面積 = π·r² で円と等価)
                        double side = Math.Sqrt(Math.PI) * radius;
                        double half = side * 0.5;
                        rectLoads.Add(new RectLoad
                        {
                            X1 = pileLayoutDataItem.Point3D.X - half,
                            X2 = pileLayoutDataItem.Point3D.X + half,
                            Y1 = pileLayoutDataItem.Point3D.Y - half,
                            Y2 = pileLayoutDataItem.Point3D.Y + half,
                            QA = qa,
                            LinkedPileNo = pileLayoutDataItem.PileNo
                        });
                    }
                }
            }
            else if (pileGroupSettlement.LoadingType == "個別十字")
            {
                foreach (PileLayoutDataItem pileLayoutDataItem in pileLayoutItems)
                {
                    SoilPile soilPile = soilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    double radius = soilPile.GroupPileLoadDia * 0.5;
                    if (radius <= 0) { skipped.Add(pileLayoutDataItem.No); continue; } // 荷重面等価径未入力の杭はスキップ（NaN/重複点回避）
                    Point point = new() { X = pileLayoutDataItem.Point3D.X, Y = pileLayoutDataItem.Point3D.Y };
                    double qa = pileLayoutDataItem.AxialForceVL0 + pileLayoutDataItem.AxialForceVLAdditional;

                    ObservableCollection<RectLoad> eachRectLoads
                        = PileGroupSettlement.GetCrossRectLoads(point, radius, qa);

                    foreach (var rectLoad in eachRectLoads)
                        rectLoads.Add(rectLoad);
                }
            }
            else if (pileGroupSettlement.LoadingType == "個別矩形（基礎梁考慮）")
            {
                // 個別矩形（基礎梁考慮）: 反復アルゴリズム (Steinbrenner ↔ 線形ばね基礎梁) 実装までは
                // 個別矩形と同等の挙動。Pi 初期値は矩形荷重そのもの (既存 QA を維持)。
                //  - 既存矩形 (LinkedPileNo 付き): QA・DX・DY を維持、中心のみ杭位置に追従
                //  - 既存矩形なし: 一辺=√π·r、QA=AxialForceVL0+VLAdditional を初期値として生成
                var existingByPileNo = pileGroupSettlement.RectLoads
                    .Where(r => r.LinkedPileNo > 0)
                    .ToDictionary(r => r.LinkedPileNo, r => r);

                foreach (PileLayoutDataItem pileLayoutDataItem in pileLayoutItems)
                {
                    SoilPile soilPile = soilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    double radius = soilPile.GroupPileLoadDia * 0.5;
                    if (radius <= 0) { skipped.Add(pileLayoutDataItem.No); continue; }

                    if (existingByPileNo.TryGetValue(pileLayoutDataItem.PileNo, out var existing))
                    {
                        // QA は触らない (反復実装後は ki·S2 で更新される)
                        existing.CenterX = pileLayoutDataItem.Point3D.X;
                        existing.CenterY = pileLayoutDataItem.Point3D.Y;
                        rectLoads.Add(existing);
                    }
                    else
                    {
                        double side = Math.Sqrt(Math.PI) * radius;
                        double half = side * 0.5;
                        double qa = pileLayoutDataItem.AxialForceVL0 + pileLayoutDataItem.AxialForceVLAdditional;
                        rectLoads.Add(new RectLoad
                        {
                            X1 = pileLayoutDataItem.Point3D.X - half,
                            X2 = pileLayoutDataItem.Point3D.X + half,
                            Y1 = pileLayoutDataItem.Point3D.Y - half,
                            Y2 = pileLayoutDataItem.Point3D.Y + half,
                            QA = qa,
                            LinkedPileNo = pileLayoutDataItem.PileNo
                        });
                    }
                }
            }
            else if (pileGroupSettlement.LoadingType == BeamReactionLoadingType)
            {
                // 基礎梁考慮鉛直解析（VL ケース）の杭反力を荷重に適用。VL ケースが無ければ荷重を作らない
                // (別のケースの反力を使わない。解析の入口で理由を返して止める)
                var vbCase = FindVBLongTermCase(verticalBeamCaseResults);
                if (vbCase == null) return rectLoads;
                Dictionary<int, double> reactionByPileNo = vbCase.PileResults?.ToDictionary(r => r.PileNo, r => r.Reaction_kN)
                                                            ?? [];
                var noReaction = new List<int>();
                foreach (PileLayoutDataItem pileLayoutDataItem in pileLayoutItems)
                {
                    SoilPile soilPile = soilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    double radius = soilPile.GroupPileLoadDia * 0.5;
                    if (radius <= 0) { skipped.Add(pileLayoutDataItem.No); continue; } // 荷重面等価径未入力の杭はスキップ
                    Point point = new() { X = pileLayoutDataItem.Point3D.X, Y = pileLayoutDataItem.Point3D.Y };
                    // 反力が存在しない杭は 0 として扱う (黙らず知らせる)
                    double qa = reactionByPileNo.TryGetValue(pileLayoutDataItem.PileNo, out double r) ? r : 0.0;
                    if (!reactionByPileNo.ContainsKey(pileLayoutDataItem.PileNo)) noReaction.Add(pileLayoutDataItem.No);

                    ObservableCollection<RectLoad> eachRectLoads
                        = PileGroupSettlement.GetCrossRectLoads(point, radius, qa);

                    foreach (var rectLoad in eachRectLoads)
                        rectLoads.Add(rectLoad);
                }
                if (noReaction.Count > 0)
                    notes?.Add($"単杭沈下解析（基礎梁考慮）の {vbCase.LoadCaseName} ケースに反力の無い杭 {PileList(noReaction)} は、荷重 0 として扱いました。");
            }

            if (skipped.Count > 0)
                notes?.Add($"荷重面等価径が未入力 (0 以下) の杭 {PileList(skipped)} は、荷重を作っていません (群杭沈下の入力で荷重面等価径を入れてください)。");
            return rectLoads;

            static string PileList(List<int> nos)
                => "No." + string.Join(", ", nos.Take(10)) + (nos.Count > 10 ? $" ほか {nos.Count - 10} 本" : "");
        }

        /// <summary>
        /// 基礎梁鉛直解析結果から長期 (VL) ケースの結果を返す。<b>見つからなければ null。</b>
        /// LoadCaseName は "VL (常時+追加)" のような装飾があるため前方一致で判定。
        ///
        /// 以前は見つからないと先頭のケースを返していたので、選んだ荷重条件 (常時) と違うケースの反力で
        /// 沈下を求め得た。無ければ解析の入口で理由を返して止める。
        /// </summary>
        internal static VerticalBeamCaseResult? FindVBLongTermCase(
            ObservableCollection<VerticalBeamCaseResult>? verticalBeamCaseResults)
        {
            if (verticalBeamCaseResults == null || verticalBeamCaseResults.Count == 0) return null;
            return verticalBeamCaseResults.FirstOrDefault(c =>
                (c.LoadCaseName ?? string.Empty).TrimStart().StartsWith("VL"));
        }

        /// <summary>
        /// 各杭位置での沈下量を計算 (杭ごと独立 — 並列化)
        /// </summary>
        private Dictionary<int, double> CalculatePileSettlements(
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<RectLoad> rectLoads,
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers)
        {
            // 結果は double 配列に書き出し、UI スレッドで PileLayoutDataItem に反映
            int n = pileLayoutItems.Count;
            var pilesArr = pileLayoutItems.ToArray();
            var settlementsMm = new double[n];

            Parallel.For(0, n, i =>
            {
                Point point = new() { X = pilesArr[i].Point3D.X, Y = pilesArr[i].Point3D.Y };
                settlementsMm[i] = Steinnbrener.CalcSettlement(point, rectLoads, settlementSoilLayers) * 1000;
            });

            var byPileNo = new Dictionary<int, double>(n);
            for (int i = 0; i < n; i++)
            {
                // 入力側の複製にも書く (表示系は既にケースの記録を読むが、複製はまだ残っている)
                // 杭ごとの沈下量は結果 (CaseRecord.PileSettlements_mm) が持つ。
                // 入力側の杭へは書かない (表示は GroupPileSettlement が結果から引く)。
                byPileNo[pilesArr[i].PileNo] = settlementsMm[i];
            }
            return byPileNo;
        }

        /// <summary>
        /// 任意の RectLoads から沈下グリッドを計算する公開ヘルパー (基礎梁考慮反復用)。
        /// </summary>
        public static ObservableCollection<SettlementGridDataItem> CalculateGridSettlementsPublic(
            ObservableCollection<double> xs,
            ObservableCollection<double> ys,
            ObservableCollection<RectLoad> rectLoads,
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers)
        {
            int nx = xs?.Count ?? 0;
            int ny = ys?.Count ?? 0;
            var result = new ObservableCollection<SettlementGridDataItem>();
            if (nx == 0 || ny == 0 || rectLoads == null || settlementSoilLayers == null) return result;

            var xArray = xs.ToArray();
            var yArray = ys.ToArray();
            var settlementsMm = new double[nx * ny];

            Parallel.For(0, nx * ny, idx =>
            {
                int ix = idx / ny;
                int iy = idx % ny;
                Point point = new() { X = xArray[ix], Y = yArray[iy] };
                settlementsMm[idx] = Steinnbrener.CalcSettlement(point, rectLoads, settlementSoilLayers) * 1000;
            });

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iy = 0; iy < ny; iy++)
                {
                    result.Add(new SettlementGridDataItem
                    {
                        X = xArray[ix],
                        Y = yArray[iy],
                        Settlement = settlementsMm[ix * ny + iy]
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// グリッド上の沈下量を計算
        /// </summary>
        private ObservableCollection<SettlementGridDataItem> CalculateGridSettlements(
            ObservableCollection<double> xs,
            ObservableCollection<double> ys,
            ObservableCollection<RectLoad> rectLoads,
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers)
        {
            // グリッド点ごとの計算は独立 (Steinnbrener.CalcSettlement は static で外部状態を変更しない)。
            // 30×30 グリッド × 90 矩形荷重 × 10 層程度になると ~1M Steinbrenner 呼び出しになるため並列化。
            int nx = xs.Count;
            int ny = ys.Count;
            var xArray = xs.ToArray();
            var yArray = ys.ToArray();
            var settlementsMm = new double[nx * ny];

            Parallel.For(0, nx * ny, idx =>
            {
                int ix = idx / ny;
                int iy = idx % ny;
                Point point = new() { X = xArray[ix], Y = yArray[iy] };
                settlementsMm[idx] = Steinnbrener.CalcSettlement(point, rectLoads, settlementSoilLayers) * 1000;
            });

            // ObservableCollection は thread-safe ではないため UI スレッドで構築
            var settlementGridData = new ObservableCollection<SettlementGridDataItem>();
            for (int ix = 0; ix < nx; ix++)
            {
                for (int iy = 0; iy < ny; iy++)
                {
                    settlementGridData.Add(new SettlementGridDataItem
                    {
                        X = xArray[ix],
                        Y = yArray[iy],
                        Settlement = settlementsMm[ix * ny + iy]
                    });
                }
            }
            return settlementGridData;
        }
    }
}
