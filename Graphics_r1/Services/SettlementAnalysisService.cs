using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

            // 矩形荷重の生成
            ObservableCollection<RectLoad> rectLoads = GenerateRectLoads(
                pileGroupSettlement,
                pileLayoutItems,
                soilPiles,
                verticalBeamCaseResults);

            // 各杭位置での沈下量を計算
            CalculatePileSettlements(pileLayoutItems, rectLoads, pileGroupSettlement.SettlementSoilLayers);

            // グリッドの設定
            pileGroupSettlement.SetGridX(xMin, xMax, xOffset, xSpacing, gridXItems);
            pileGroupSettlement.SetGridY(yMin, yMax, yOffset, ySpacing, gridYItems);

            // グリッド上の沈下量を計算
            var settlementGridData = CalculateGridSettlements(
                pileGroupSettlement.SettlementGridX,
                pileGroupSettlement.SettlementGridY,
                rectLoads,
                pileGroupSettlement.SettlementSoilLayers);

            return new SettlementAnalysisResult
            {
                Success = true,
                SettlementGridData = settlementGridData
            };
        }

        /// <summary>
        /// 「個別十字」系の荷重タイプに対し、杭ごとの十字形矩形荷重を自動生成する公開ヘルパー。
        /// 荷重タイプが個別十字系でない場合は空のコレクションを返す。
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

        private static ObservableCollection<RectLoad> GenerateRectLoadsInternal(
            PileGroupSettlement pileGroupSettlement,
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<SoilPile> soilPiles,
            ObservableCollection<VerticalBeamCaseResult> verticalBeamCaseResults)
        {
            ObservableCollection<RectLoad> rectLoads = [];

            if (pileGroupSettlement.LoadingType == "任意矩形")
            {
                rectLoads = pileGroupSettlement.RectLoads;
            }
            else if (pileGroupSettlement.LoadingType == "個別十字")
            {
                foreach (PileLayoutDataItem pileLayoutDataItem in pileLayoutItems)
                {
                    SoilPile soilPile = soilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    double radius = soilPile.GroupPileLoadDia * 0.5;
                    Point point = new() { X = pileLayoutDataItem.Point3D.X, Y = pileLayoutDataItem.Point3D.Y };
                    double qa = pileLayoutDataItem.AxialForceVL0 + pileLayoutDataItem.AxialForceVLAdditional;

                    ObservableCollection<RectLoad> eachRectLoads
                        = PileGroupSettlement.GetCrossRectLoads(point, radius, qa);

                    foreach (var rectLoad in eachRectLoads)
                        rectLoads.Add(rectLoad);
                }
            }
            else if (pileGroupSettlement.LoadingType == "個別十字（基礎梁考慮）")
            {
                // 基礎梁考慮鉛直解析（VL ケース）の杭反力を荷重に適用
                var vbCase = FindVBLongTermCase(verticalBeamCaseResults);
                Dictionary<int, double> reactionByPileNo = vbCase?.PileResults?.ToDictionary(r => r.PileNo, r => r.Reaction_kN)
                                                            ?? [];
                foreach (PileLayoutDataItem pileLayoutDataItem in pileLayoutItems)
                {
                    SoilPile soilPile = soilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    double radius = soilPile.GroupPileLoadDia * 0.5;
                    Point point = new() { X = pileLayoutDataItem.Point3D.X, Y = pileLayoutDataItem.Point3D.Y };
                    // 反力が存在しない杭は 0 として扱う
                    double qa = reactionByPileNo.TryGetValue(pileLayoutDataItem.PileNo, out double r) ? r : 0.0;

                    ObservableCollection<RectLoad> eachRectLoads
                        = PileGroupSettlement.GetCrossRectLoads(point, radius, qa);

                    foreach (var rectLoad in eachRectLoads)
                        rectLoads.Add(rectLoad);
                }
            }

            return rectLoads;
        }

        /// <summary>
        /// 基礎梁鉛直解析結果から長期 (VL) ケースの結果を返す。見つからない場合は先頭を返す。
        /// LoadCaseName は "VL (常時+追加)" のような装飾があるため前方一致で判定。
        /// </summary>
        private static VerticalBeamCaseResult FindVBLongTermCase(
            ObservableCollection<VerticalBeamCaseResult> verticalBeamCaseResults)
        {
            if (verticalBeamCaseResults == null || verticalBeamCaseResults.Count == 0) return null;
            var vl = verticalBeamCaseResults.FirstOrDefault(c =>
                (c.LoadCaseName ?? string.Empty).TrimStart().StartsWith("VL"));
            return vl ?? verticalBeamCaseResults[0];
        }

        /// <summary>
        /// 各杭位置での沈下量を計算
        /// </summary>
        private void CalculatePileSettlements(
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<RectLoad> rectLoads,
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers)
        {
            foreach (PileLayoutDataItem pileLayoutDataItem in pileLayoutItems)
            {
                Point point = new() { X = pileLayoutDataItem.Point3D.X, Y = pileLayoutDataItem.Point3D.Y };
                pileLayoutDataItem.GroupPileSettlement = Steinnbrener.CalcSettlement(
                    point, rectLoads, settlementSoilLayers) * 1000;
            }
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
            var settlementGridData = new ObservableCollection<SettlementGridDataItem>();

            foreach (var x in xs)
            {
                foreach (var y in ys)
                {
                    Point point = new() { X = x, Y = y };
                    var settlement = Steinnbrener.CalcSettlement(
                        point, rectLoads, settlementSoilLayers) * 1000;

                    settlementGridData.Add(new SettlementGridDataItem
                    {
                        X = x,
                        Y = y,
                        Settlement = settlement
                    });
                }
            }

            return settlementGridData;
        }
    }
}
