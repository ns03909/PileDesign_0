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

            double xOnGrid = 0;
            double yOnGrid = 0;
            foreach (var pile in gridItem.Piles)
            {
                if (axis == "X") xOnGrid = pile.X;   // X平行
                else yOnGrid = pile.Y;               // Y平行
            }

            // 杭ごとの値は BuildSettlementLine が組む (単杭沈下＋群杭沈下は PileSettlementComposition)。
            // 群杭沈下は画面で表示中のケースの記録を使い、凡例にそのケースを書く
            var line = BuildSettlementLine(InputModel, gridItem.Piles, axis, SelectedGraphOption, SelectedLoadCaseOption,
                PileSettlementComposition.DisplayedGroupRecord(InputModel),
                pile => GetVBSettlement(pile.No, SelectedLoadCaseOption),
                pile => GetBeamAwareCaseSettlement(pile, SelectedLoadCaseOption));
            List<double> xs = line.Xs;
            List<double> ys = line.Ys;

            // 値の無い杭 (NaN) は線を切って描かない。以前は 0 として描き、沈下していない杭に見えた
            bool labeled = false;
            int runStart = 0;
            for (int i = 0; i <= ys.Count; i++)
            {
                if (i < ys.Count && double.IsFinite(ys[i])) continue;
                if (i > runStart)
                {
                    var run = WpfPlot.Plot.Add.Scatter(xs.GetRange(runStart, i - runStart), ys.GetRange(runStart, i - runStart));
                    if (!labeled && !string.IsNullOrEmpty(line.Label)) { run.LegendText = line.Label; labeled = true; }
                }
                runStart = i + 1;
            }
            foreach (var note in line.Notes)
            {
                var noteMark = WpfPlot.Plot.Add.Scatter(Array.Empty<double>(), Array.Empty<double>());
                noteMark.LegendText = note;
                noteMark.MarkerSize = 0;
                noteMark.LineWidth = 0;
            }

            List<double> midxs = [];
            List<double> midys = [];
            for (int i = 0; i < xs.Count - 1; i++)
            {
                if (!double.IsFinite(ys[i]) || !double.IsFinite(ys[i + 1])) continue;   // 欠けた杭をまたぐ傾きは出さない
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
                SelectedGraphOption == "沈下 杭矩形(基礎梁考慮)")
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
            var sp = SoilPileOrNote(pile);
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

        /// <summary>沈下グラフ 1 本ぶん (通り心上の杭の位置と沈下量 [mm])。値の無い杭は NaN。</summary>
        internal sealed record SettlementLine(List<double> Xs, List<double> Ys, string Label, List<string> Notes);

        /// <summary>
        /// 沈下グラフの点を組む。単杭沈下＋群杭沈下は <see cref="PileSettlementComposition"/> で組み、
        /// 群杭沈下は <paramref name="groupRecord"/> (画面で表示中のケース) を使う。
        ///
        /// <para>以前は表示中のケースの群杭沈下を <c>SettlementOf</c> で引き、記録が無い・記録にその杭が無いときは
        /// <b>黙って 0</b> を足していた。単杭沈下の結果が無い杭も 0 として描いた。
        /// 群杭沈下を解いていない (または記録に杭が足りない) と、その杭だけ沈下していないように見える。
        /// どのケースの群杭沈下を足したかもグラフから分からなかった (地震時のケースを表示中なら地震時の値を足す)。
        /// 値の無い杭は NaN にして描かず、凡例に杭と理由を書く。足したケースは凡例に書く。</para>
        /// </summary>
        internal static SettlementLine BuildSettlementLine(InputModel input, IEnumerable<PileLayoutDataItem> piles, string axis,
            string graphOption, string loadCaseOption, PileDesign.Models.Results.GroupSettlementCaseRecord? groupRecord,
            Func<PileLayoutDataItem, double> vbSettlement, Func<PileLayoutDataItem, double> beamAwareCaseSettlement)
        {
            List<double> xs = [];
            List<double> ys = [];
            var missingSingle = new List<int>();
            var missingGroup = new List<int>();
            string groupLabel = "群杭" + GroupSettlementCaseLabel.Describe(groupRecord);
            string label = graphOption switch
            {
                "沈下 単杭" => $"単杭 ({loadCaseOption})",
                "沈下 単杭+群杭" => $"単杭 ({loadCaseOption}) + {groupLabel}",
                "沈下 群杭" => groupLabel,
                "沈下 基礎梁考慮単杭" => $"基礎梁考慮単杭 ({loadCaseOption})",
                "沈下 基礎梁考慮単杭+群杭" => $"基礎梁考慮単杭 ({loadCaseOption}) + {groupLabel}",
                _ => "",
            };
            bool usesGroup = graphOption is "沈下 単杭+群杭" or "沈下 群杭" or "沈下 基礎梁考慮単杭+群杭";

            foreach (var pile in piles)
            {
                xs.Add(axis == "X" ? pile.Y : pile.X);   // X平行の通り心なら Y 座標、Y平行なら X 座標

                double y;
                switch (graphOption)
                {
                    case "沈下 単杭":
                    case "沈下 単杭+群杭":
                    {
                        bool withGroup = graphOption == "沈下 単杭+群杭";
                        var parts = PileSettlementComposition.Compose(input, pile, groupRecord, withGroup, loadCaseOption);
                        if (!parts.HasSingle) missingSingle.Add(pile.PileNo);
                        else if (withGroup && parts.GroupOrNull_mm == null) missingGroup.Add(pile.PileNo);
                        y = parts.Total_mm ?? double.NaN;
                        break;
                    }
                    case "沈下 群杭":
                    {
                        double? g = PileSettlementComposition.GroupOnly_mm(groupRecord, pile.PileNo);
                        if (g == null) missingGroup.Add(pile.PileNo);
                        y = g ?? double.NaN;
                        break;
                    }
                    case "沈下 基礎梁考慮単杭":
                        y = vbSettlement(pile);
                        break;
                    case "沈下 基礎梁考慮単杭+群杭":
                    {
                        double? g = PileSettlementComposition.GroupOnly_mm(groupRecord, pile.PileNo);
                        if (g == null) missingGroup.Add(pile.PileNo);
                        y = g.HasValue ? vbSettlement(pile) + g.Value : double.NaN;
                        break;
                    }
                    case "沈下 杭矩形(基礎梁考慮)":
                        y = beamAwareCaseSettlement(pile);
                        break;
                    default:
                        y = double.NaN;
                        break;
                }
                ys.Add(y);
            }

            var notes = new List<string>();
            if (missingSingle.Count > 0)
                notes.Add($"杭No.{string.Join(", ", missingSingle)}: 単杭沈下 ({loadCaseOption}) の結果なし (描かない)");
            if (missingGroup.Count > 0)
                notes.Add(groupRecord == null
                    ? "群杭沈下の結果なし (描かない)"
                    : $"杭No.{string.Join(", ", missingGroup)}: 群杭沈下の結果なし (描かない)");
            if (usesGroup && groupRecord != null)
            {
                if (groupRecord.IsConvergenceUnknown) notes.Add("群杭沈下: 収束状態が不明な結果 (旧形式から復元)");
                else if (groupRecord is { IsBeamAware: true, IsConverged: false }) notes.Add("群杭沈下: 反復が収束していない結果");
            }
            return new SettlementLine(xs, ys, label, notes);
        }
    }
}
