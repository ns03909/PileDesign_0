using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
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
            double ySpacing)
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
                soilPiles);

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
        /// 荷重タイプに応じて矩形荷重を生成
        /// </summary>
        private ObservableCollection<RectLoad> GenerateRectLoads(
            PileGroupSettlement pileGroupSettlement,
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            ObservableCollection<SoilPile> soilPiles)
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

            return rectLoads;
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
