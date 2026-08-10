using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Serilog;
using PileDesign.Services;
//using System.Windows.Forms;

namespace PileDesign.ViewModels
{
    // 沈下系グラフ: 沈下曲線 (X/Y 軸)・土層背景・荷重-沈下曲線の描画。GraphViewModel.cs からの物理分割 (純粋移動)。
    public partial class GraphViewModel
    {
        // 変位描画
        private void DrawSettlement(GridDataItem gridItem, string axis)
        {
            string xTitle = $"{axis}(m)";
            string yTitle = "沈下量(mm)";

            List<double> xs = [];
            List<double> ys = [];
            double xOnGrid = 0;
            double yOnGrid = 0;
            foreach (var pile in gridItem.Piles)
            {
                if (axis == "X") // X平行
                {
                    xOnGrid = pile.X;
                    xs.Add(pile.Y); // Y座標追加
                }
                else // axis ="Y"
                {
                    yOnGrid = pile.Y;
                    xs.Add(pile.X); // X座標追加
                }
                if (SelectedGraphOption == "沈下 単杭" ||
                    SelectedGraphOption == "沈下 単杭+群杭")
                {
                    if (SelectedLoadCaseOption == "VL")
                    {
                        if (SelectedGraphOption == "沈下 単杭")
                        {
                            ys.Add(pile.SinglePileSettlementVL * 1000); // m → mm
                        }
                        else // if (SelectedGraphOption == "沈下 単杭+群杭")
                        {
                            ys.Add(pile.SinglePileSettlementVL * 1000 + pile.GroupPileSettlement); // m→mm + mm
                        }

                    }
                    else // VL以外
                    {
                        for (int i = 0; i < InputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                        {
                            if (InputModel.LoadCasesInput.LoadCasesLevel1[i].LoadName == SelectedLoadCaseOption)
                            {
                                if (SelectedGraphOption == "沈下 単杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel1s[i] * 1000); // m → mm
                                    break;
                                }
                                else // if (SelectedGraphOption == "沈下 単杭+群杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel1s[i] * 1000 + pile.GroupPileSettlement); // m→mm + mm
                                    break;
                                }
                            }
                        }
                        for (int i = 0; i < InputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                        {
                            if (InputModel.LoadCasesInput.LoadCasesLevel2[i].LoadName == SelectedLoadCaseOption)
                            {
                                if (SelectedGraphOption == "沈下 単杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel2s[i] * 1000); // m → mm
                                    break;
                                }
                                else // if (SelectedGraphOption == "沈下 単杭+群杭")
                                {
                                    ys.Add(pile.SinglePileSettlementLevel2s[i] * 1000 + pile.GroupPileSettlement); // m→mm + mm
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (SelectedGraphOption == "沈下 群杭")
                {
                    ys.Add(pile.GroupPileSettlement);
                }
                else if (SelectedGraphOption == "沈下 基礎梁考慮単杭" ||
                         SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭")
                {
                    double vbSettle = GetVBSettlement(pile.No, SelectedLoadCaseOption);
                    if (SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭")
                        vbSettle += pile.GroupPileSettlement;
                    ys.Add(vbSettle);
                }
                else if (SelectedGraphOption == "沈下 個別矩形(基礎梁考慮)")
                {
                    ys.Add(GetBeamAwareCaseSettlement(pile, SelectedLoadCaseOption));
                }
            }

            /*var scatter = */
            WpfPlot.Plot.Add.Scatter(xs, ys);

            List<double> midxs = [];
            List<double> midys = [];
            for (int i = 0; i < xs.Count - 1; i++)
            {
                midxs.Add((xs[i] + xs[i + 1]) * 0.5);
                midys.Add((ys[i] + ys[i + 1]) * 0.5);
                double angle = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
                double[] xArray = [midxs[^1]];
                double[] yArray = [midys[^1]];
                var scatterAngle = WpfPlot.Plot.Add.Scatter(xArray, yArray);
                scatterAngle.LegendText = GetSettlementAngleLegendText(angle);
                scatterAngle.MarkerSize = 0;
                scatterAngle.LineWidth = 0;
            }

            if (SelectedGraphOption == "沈下 群杭" || SelectedGraphOption == "沈下 単杭+群杭" ||
                SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭" ||
                SelectedGraphOption == "沈下 個別矩形(基礎梁考慮)")
            {
                List<double> xsGround = [];
                List<double> ysGround = [];
                var groundSource = GetSettlementGridForCurrentOption();
                foreach (var settlementGridDataItem in groundSource)
                {
                    if (axis == "X" && xOnGrid == settlementGridDataItem.X) // X平行
                    {
                        xsGround.Add(settlementGridDataItem.Y); // Y座標追加
                        ysGround.Add(settlementGridDataItem.Settlement);
                    }
                    else if (axis == "Y" && yOnGrid == settlementGridDataItem.Y) // 
                    {
                        xsGround.Add(settlementGridDataItem.X); // X座標追加
                        ysGround.Add(settlementGridDataItem.Settlement);
                    }
                }

                /*var scatterGround = */
                WpfPlot.Plot.Add.Scatter(xsGround, ysGround);

                List<double> midxsGround = [];
                List<double> midysGround = [];
                for (int i = 0; i < xsGround.Count - 1; i++)
                {
                    midxsGround.Add((xsGround[i] + xsGround[i + 1]) / 2);
                    midysGround.Add((ysGround[i] + ysGround[i + 1]) / 2);
                    double angle = (ysGround[i + 1] - ysGround[i]) / (xsGround[i + 1] - xsGround[i]);
                    double[] xArrayGround = [midxsGround[^1]];
                    double[] yArrayGround = [midysGround[^1]];
                    var scatterAngleGround = WpfPlot.Plot.Add.Scatter(xArrayGround, yArrayGround);
                    scatterAngleGround.LegendText = GetSettlementAngleLegendText(angle);
                    scatterAngleGround.MarkerSize = 0;
                    scatterAngleGround.LineWidth = 0;
                }
            }

            ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", SelectedGraphOption, xTitle, yTitle);

            WpfPlot.Plot.Axes.InvertY();
            WpfPlot.Plot.ShowLegend();
            WpfPlot.Refresh();
        }

        // 土層背景色 (2026-04-24): 地盤ウィンドウと同じ配色
        //   粘性土: 薄茶  (210, 180, 140, 64)
        //   砂質土: 薄橙  (255, 165,   0, 64)
        //   礫質土: 薄緑  (144, 238, 144, 64)
        //   他    : 薄灰  (200, 200, 200, 32)
        private static ScottPlot.Color GetSoilTypeBackgroundColor(string? soilType) => soilType switch
        {
            "粘性土" => new ScottPlot.Color(210, 180, 140, 64),
            "砂質土" => new ScottPlot.Color(255, 165, 0, 64),
            "礫質土" => new ScottPlot.Color(144, 238, 144, 64),
            _ => new ScottPlot.Color(200, 200, 200, 32),
        };

        /// <summary>
        /// 最初に選択された杭の HorizontalSoilReactions から土層境界を推定し、
        /// 連続する同 SoilType セグメントを 1 層としてまとめて VerticalSpan で背景色を付ける。
        /// GraphWindow の杭周地盤変位反力 / 杭変位応力 グラフで呼ぶ。
        /// </summary>
        private void AddSoilLayerBackground(WpfPlot wpfPlot)
        {
            var piles = GetSelectedPileLayouts();
            if (piles == null) return;
            PileLayoutDataItem? pile = piles.FirstOrDefault();
            if (pile == null) return;
            if (pile.SoilPileAltNo <= 0 || pile.SoilPileAltNo > InputModel.ElementDivision.SoilPiles.Count) return;
            var sp = InputModel.ElementDivision.SoilPiles[pile.SoilPileAltNo - 1];
            if (sp?.HorizontalSoilReactions == null || sp.HorizontalSoilReactions.Count == 0) return;

            var reactions = sp.HorizontalSoilReactions;

            // 連続する同 SoilType セグメントを 1 層としてまとめる
            int i = 0;
            while (i < reactions.Count)
            {
                string? currentType = reactions[i].SoilType;
                double zTop = reactions[i].ZTop;
                int j = i;
                while (j + 1 < reactions.Count && reactions[j + 1].SoilType == currentType)
                    j++;
                double zBtm = reactions[j].ZBtm;
                var color = GetSoilTypeBackgroundColor(currentType);
                // VerticalSpan(yMin, yMax, color): Z(深さ)軸は Y 軸、zBtm が yMin (下方)、zTop が yMax
                wpfPlot.Plot.Add.VerticalSpan(zBtm, zTop, color);
                i = j + 1;
            }
        }
        /// <summary>
        /// 荷重-杭頭沈下曲線・荷重-杭先端沈下曲線を描画
        /// SoilPile.LoadDisplacementsから杭頭荷重(PileTopLoad) vs 沈下量(DD0s/DDns)をプロット
        /// </summary>
        private void DrawLoadSettlementCurve()
        {
            var soilPiles = InputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return;

            // 選択杭の SoilPileAltNo を取得
            var selectedPiles = GetSelectedPileLayouts();
            var soilPileIndices = new HashSet<int>();
            foreach (var pile in selectedPiles)
            {
                int idx = pile.SoilPileAltNo - 1;
                if (idx >= 0 && idx < soilPiles.Count)
                    soilPileIndices.Add(idx);
            }
            if (soilPileIndices.Count == 0)
            {
                // All の場合は全SoilPile
                for (int i = 0; i < soilPiles.Count; i++)
                    soilPileIndices.Add(i);
            }

            var colors = new[] {
                new ScottPlot.Color(0, 114, 189),    // 青
                new ScottPlot.Color(217, 83, 25),     // オレンジ
                new ScottPlot.Color(119, 172, 48),    // 緑
                new ScottPlot.Color(126, 47, 142),    // 紫
                new ScottPlot.Color(162, 20, 47),     // 赤
                new ScottPlot.Color(77, 190, 238),    // 水色
            };

            int colorIdx = 0;
            foreach (int spIdx in soilPileIndices)
            {
                var sp = soilPiles[spIdx];
                if (sp.LoadDisplacements == null || sp.LoadDisplacements.Count == 0) continue;

                var sorted = sp.LoadDisplacements.OrderBy(ld => ld.PileTopLoad).ToList();
                double[] loads = [.. sorted.Select(ld => ld.PileTopLoad)];
                double[] headSettlements = [.. sorted.Select(ld => ld.DD0s)];
                double[] toeSettlements = [.. sorted.Select(ld => ld.DDns)];

                var color = colors[colorIdx % colors.Length];
                string label = $"杭セット{spIdx + 1}";

                // 杭頭沈下曲線（実線）
                var scatterHead = WpfPlot.Plot.Add.Scatter(headSettlements, loads);
                scatterHead.Color = color;
                scatterHead.LegendText = $"{label} 杭頭";
                scatterHead.MarkerSize = 5;

                // 杭先端沈下曲線（破線）
                var scatterToe = WpfPlot.Plot.Add.Scatter(toeSettlements, loads);
                scatterToe.Color = color;
                scatterToe.LegendText = $"{label} 杭先端";
                scatterToe.MarkerSize = 3;
                scatterToe.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;

                colorIdx++;
            }

            ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "荷重沈下曲線", "沈下量 (mm)", "荷重 (kN)");
        }
    }
}
