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
    // 深度方向グラフ: 杭応力・杭変位・水平地盤反力の深度分布描画。GraphViewModel.cs からの物理分割 (純粋移動)。
    public partial class GraphViewModel
    {
        // 杭応力描画
        private void DrawPileForce(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText, string forceType, string unit)
        {
            IsPileOptionVisible = true;

            if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
            {
                wpfPlot.Plot.Clear();
                wpfPlot.Refresh();
                return;
            }

            // 土層背景色を scatter より先に追加
            AddSoilLayerBackground(wpfPlot);

            // 限界状態表示が有効かどうか
            bool showLimitState = SelectedLimitState != "なし" &&
                (forceType == "M" || forceType == "My" || forceType == "Mz" || forceType == "F" || forceType == "Fy" || forceType == "Fz");

            foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
            {
                var beams = pileLayoutDataItem.Beams;
                var soilPile = pileLayoutDataItem.SoilPile;

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析結果がない場合はスキップ
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep < 0) continue;

                            // 杭頭
                            List<double> beamZs = [beams[0]?.NodeI?.Coord.Z ?? 0];
                            List<double> beamForces = [0];

                            foreach (var beam in beams)
                            {
                                if (beam?.NodeI?.Coord == null || beam?.NodeJ?.Coord == null) continue;
                                // SegmentIndex未設定の梁（RigidLink等）はスキップ
                                if (beam.SegmentIndex is null) continue;

                                var result = beam.GetBeamResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                if (result?.CumulativeForce == null) continue;

                                beamZs.Add(beam.NodeI.Coord.Z);
                                beamZs.Add(beam.NodeJ.Coord.Z);

                                if (forceType == "Fy")
                                {
                                    beamForces.Add(result.CumulativeForce.Fyi);
                                    beamForces.Add(-result.CumulativeForce.Fyj);
                                }
                                else if (forceType == "Fz")
                                {
                                    beamForces.Add(result.CumulativeForce.Fzi);
                                    beamForces.Add(-result.CumulativeForce.Fzj);
                                }
                                else if (forceType == "F")
                                {
                                    beamForces.Add(result.CumulativeForce.Fi);
                                    beamForces.Add(result.CumulativeForce.Fj);
                                }
                                else if (forceType == "My")
                                {
                                    beamForces.Add(result.CumulativeForce.Myi);
                                    beamForces.Add(-result.CumulativeForce.Myj);
                                }
                                else if (forceType == "Mz")
                                {
                                    beamForces.Add(result.CumulativeForce.Mzi);
                                    beamForces.Add(-result.CumulativeForce.Mzj);
                                }
                                else if (forceType == "M")
                                {
                                    beamForces.Add(result.CumulativeForce.Mi);
                                    beamForces.Add(result.CumulativeForce.Mj);
                                }
                            }

                            // 杭先端: RigidLinkではなく、最後の杭要素のNodeJを使用
                            var lastPileBeam = beams.LastOrDefault(b => b.SegmentIndex != null);
                            if (lastPileBeam != null)
                            {
                                beamZs.Add(lastPileBeam.NodeJ.Coord.Z);
                            }
                            else
                            {
                                beamZs.Add(beams[^1].NodeJ.Coord.Z);
                            }
                            beamForces.Add(0);

                            var scatter = wpfPlot.Plot.Add.Scatter(beamForces.ToArray(), beamZs.ToArray());
                            scatter.LegendText = GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                            var stressColor = scatter.LineStyle.Color; // 応力ラインの色を取得

                            // ホバーポップアップ用詳細
                            double absMax = beamForces.Count > 0 ? beamForces.Max(Math.Abs) : 0;
                            _graphHoverMap[scatter] =
                                $"杭: #{pileLayoutDataItem.PileNo} (X={pileLayoutDataItem.X:N2}, Y={pileLayoutDataItem.Y:N2})\n"
                                + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                + $"組合せ: cmb{loadCombination.No} (αL={loadCombination.Alpha1:F2}/βU={loadCombination.Beta1:F2}/βL={loadCombination.Beta2:F2})\n"
                                + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                                + $"系列: {forceType} ({unit})\n"
                                + $"最大絶対値: {absMax:N2} {unit}\n"
                                + $"節点数: {beamZs.Count}";

                            // 限界状態の破線ステップライン描画（同じ色で描画）
                            if (showLimitState && soilPile?.PileBodySegments != null)
                            {
                                // 限界値のステップラインを構築
                                List<double> limitZs = [];
                                List<double> limitValues = [];

                                foreach (var beam in beams)
                                {
                                    if (beam?.NodeI?.Coord == null || beam?.NodeJ?.Coord == null) continue;

                                    var result = beam.GetBeamResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                    if (result?.CumulativeForce == null) continue;

                                    // 限界状態は設計軸力 (ケース別の入力地震時軸力) で評価する。
                                    // 以前は要素端力の平均 (Fxi + Fxj)/2 を使っていたが、梁要素の端力は
                                    // 軸方向で逆対称 (Fxj = -Fxi) なので恒等的に 0 になり、常に N=0 の
                                    // 耐力で限界線を描いていた。断面照査 (EvaluationWindowViewModel) と
                                    // 同じ軸力を使い、グラフと判定が食い違わないようにする。
                                    double axialForceN = pileLayoutDataItem.GetDesignAxialForce(
                                        loadCase.No, loadCase.Level);

                                    // 杭断面を取得（SoilPile.PileBodySegmentsを使用）
                                    int segmentIndex = beam.SegmentIndex ?? 0;
                                    if (segmentIndex < 0 || segmentIndex >= soilPile.PileBodySegments.Count) continue;

                                    var pileSection = soilPile.PileBodySegments[segmentIndex].PileSection;
                                    if (pileSection == null) continue;

                                    // 限界値を取得
                                    double limitValue;
                                    if (forceType == "M" || forceType == "My" || forceType == "Mz")
                                    {
                                        var (nValues, mValues) = GetLimitStateNMCurve(pileSection, SelectedLimitState);
                                        if (nValues == null || mValues == null || nValues.Count == 0) continue;
                                        limitValue = InterpolateLimitValue(nValues, mValues, axialForceN);
                                    }
                                    else // F, Fy, Fz
                                    {
                                        var (nValues, qValues) = GetLimitStateNQCurve(pileSection, SelectedLimitState);
                                        if (nValues == null || qValues == null || nValues.Count == 0) continue;
                                        limitValue = InterpolateLimitValue(nValues, qValues, axialForceN);
                                    }

                                    if (double.IsNaN(limitValue) || limitValue <= 0) continue;

                                    // ステップライン用のデータ（各要素で一定値、正側のみ）
                                    limitZs.Add(beam.NodeI.Coord.Z);
                                    limitZs.Add(beam.NodeJ.Coord.Z);
                                    limitValues.Add(limitValue);
                                    limitValues.Add(limitValue);
                                }

                                // 限界値ライン（正側のみ、同じ色で破線）
                                if (limitZs.Count > 0)
                                {
                                    var scatterLimit = wpfPlot.Plot.Add.Scatter(limitValues.ToArray(), limitZs.ToArray());
                                    scatterLimit.LineStyle.Pattern = LinePattern.Dashed;
                                    scatterLimit.LineStyle.Width = 1.5f;
                                    scatterLimit.LineStyle.Color = stressColor; // 応力と同じ色
                                    scatterLimit.MarkerStyle.IsVisible = false;
                                    scatterLimit.LegendText = SelectedLimitState;
                                }
                            }
                        }
                    }
                }
            }

            // せん断力の場合はFをQに置換（Q = Shear Force）
            string axisLabel = forceType.Replace("F", "Q");
            string axisX = axisLabel + " " + unit;

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, SelectedGraphOption, axisX, "Z(m)");
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // 杭変位描画
        private void DrawPileDisp(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText, string dispType, string unit)

        {
            IsPileOptionVisible = true;

            if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
            {
                wpfPlot.Plot.Clear();
                wpfPlot.Refresh();
                return;
            }

            // 土層背景色を scatter より先に追加
            AddSoilLayerBackground(wpfPlot);

            var selectedPiles = GetSelectedPileLayouts();
            if (AnaModel == null) return;

            foreach (PileLayoutDataItem pileLayoutDataItem in selectedPiles)
            {
                var beams = pileLayoutDataItem.Beams;
                var pileNodes = pileLayoutDataItem.PileNodes;
                var soilNodes = pileLayoutDataItem.SoilNodes;
                if (pileNodes == null || pileNodes.Count == 0)
                {
                    continue;
                }

                var loadCases = GetSelectedLoadCases();

                foreach (LoadCase loadCase in loadCases)
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析結果がない場合はスキップ
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep < 0) continue;

                            List<double> pileZs = [];
                            List<double> pileDisps = [];

                            List<double> soilZs = [];
                            List<double> soilDisps = [];

                            foreach (var pileNode in pileNodes)
                            {
                                pileZs.Add(pileNode.Coord.Z);
                                var result = pileNode.GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                if (result == null || result.CumulativeDisp == null)
                                {
                                    pileDisps.Add(0);
                                    continue;
                                }

                                if (dispType == "UX")
                                {
                                    pileDisps.Add(result.CumulativeDisp.Ux * 1000.0);
                                }
                                else if (dispType == "UY")
                                {
                                    pileDisps.Add(result.CumulativeDisp.Uy * 1000.0);
                                }
                                else if (dispType == "UH")
                                {
                                    pileDisps.Add(result.CumulativeDisp.Uh * 1000.0);
                                }
                                else
                                {
                                    pileDisps.Add(result.CumulativeDisp.Uz * 1000.0);
                                }
                            }

                            foreach (var soilNode in soilNodes)
                            {
                                soilZs.Add(soilNode.Coord.Z);
                                var result = soilNode.GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction);
                                if (result?.CumulativeDisp == null)
                                {
                                    soilDisps.Add(0.0);
                                    continue;
                                }
                                if (dispType == "UX")
                                {
                                    soilDisps.Add(result.CumulativeDisp.Ux * 1000.0);
                                }
                                else if (dispType == "UY")
                                {
                                    soilDisps.Add(result.CumulativeDisp.Uy * 1000.0);
                                }
                                else if (dispType == "UH")
                                {
                                    soilDisps.Add(result.CumulativeDisp.Uh * 1000.0);
                                }
                                else
                                {
                                    soilDisps.Add(result.CumulativeDisp.Uz * 1000.0);
                                }
                            }

                            var scatterPile = wpfPlot.Plot.Add.Scatter(pileDisps.ToArray(), pileZs.ToArray());
                            var pileColor = scatterPile.LineStyle.Color; // 杭変位の色を取得

                            var scatterSoil = wpfPlot.Plot.Add.Scatter(soilDisps.ToArray(), [.. soilZs]);
                            scatterSoil.LineStyle.Pattern = LinePattern.Dashed;
                            scatterSoil.LineStyle.Color = pileColor; // 杭変位と同じ色を適用
                            scatterSoil.MarkerStyle.FillColor = pileColor;

                            scatterPile.LegendText = "(PILE), " + GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                            scatterSoil.LegendText = "(SOIL), " + GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);

                            // ホバーポップアップ用詳細
                            string hoverHeader = $"杭: #{pileLayoutDataItem.PileNo} (X={pileLayoutDataItem.X:N2}, Y={pileLayoutDataItem.Y:N2})\n"
                                + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                + $"組合せ: cmb{loadCombination.No} (αL={loadCombination.Alpha1:F2}/βU={loadCombination.Beta1:F2}/βL={loadCombination.Beta2:F2})\n"
                                + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}";
                            double pileMax = pileDisps.Count > 0 ? pileDisps.Max(Math.Abs) : 0;
                            double soilMax = soilDisps.Count > 0 ? soilDisps.Max(Math.Abs) : 0;
                            _graphHoverMap[scatterPile] = hoverHeader
                                + $"\n系列: 杭変位 {dispType} ({unit})"
                                + $"\n最大絶対値: {pileMax:N2} {unit}"
                                + $"\n節点数: {pileZs.Count}";
                            _graphHoverMap[scatterSoil] = hoverHeader
                                + $"\n系列: 地盤変位 {dispType} ({unit})"
                                + $"\n最大絶対値: {soilMax:N2} {unit}"
                                + $"\n節点数: {soilZs.Count}";
                        }
                    }
                }
            }

            string axisX = dispType + " " + unit;

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, SelectedGraphOption, axisX, "Z(m)");
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // 水平地盤反力描画（相対変位、地盤反力、ばね割線剛性）
        private void DrawHorizontalSoilReaction(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText, string dataType, string unit)
        {
            IsPileOptionVisible = true;
            wpfPlot.Plot.Clear();

            if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
            {
                wpfPlot.Refresh();
                return;
            }

            // 土層背景色を scatter より先に追加 (背後に描画される)
            AddSoilLayerBackground(wpfPlot);

            foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
            {
                var horizontalSoilSprings = pileLayoutDataItem.HorizontalSoilSprings;
                if (horizontalSoilSprings == null || horizontalSoilSprings.Count == 0) continue;

                // 案 B: ノードごとのトリビュータリ長を求めるため、対応する HorizontalSoilReactions
                // (地盤セグメント定義) を取得。SoilPileAltNo が無効な場合は reactions を null にして
                // フォールバック (division スキップ、= 旧挙動の kN/kN/m を表示)
                List<Models.InputData.HorizontalSoilReactionItem>? reactions = null;
                if (pileLayoutDataItem.SoilPileAltNo > 0
                    && pileLayoutDataItem.SoilPileAltNo <= InputModel.ElementDivision.SoilPiles.Count)
                {
                    var sp = InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                    if (sp?.HorizontalSoilReactions != null && sp.HorizontalSoilReactions.Count > 0)
                        reactions = sp.HorizontalSoilReactions.ToList();
                }

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析結果がない場合はスキップ
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep < 0) continue;

                            List<double> springZs = [];
                            List<double> springValues = [];

                            int nSprings = horizontalSoilSprings.Count;

                            // 案 C (v3): 分布モード + 反力/反力係数 は セグメント単位で上下半分に分けて長方形分布を作る。
                            //   - 上半分 [Z_top, Z_mid] は ノード j の相対変位 y_j と セグメント j の top 側 py を使う
                            //   - 下半分 [Z_mid, Z_btm] は ノード j+1 の相対変位 y_{j+1} と セグメント j の bottom 側 py を使う
                            //   計算は HorizontalSoilReactionItem.GetP(y, py) を利用 (設計モデルと厳密一致)
                            bool useRectDist = IsDistributedMode && reactions != null
                                && (dataType == "Reaction" || dataType == "SecantStiffness");

                            if (useRectDist)
                            {
                                // 全節点の 相対変位 と FEM 実測ばね反力 をキャッシュ
                                double[] nodeRelDisps = new double[nSprings];
                                double[] nodeActualForces = new double[nSprings]; // |F|_FEM [kN]
                                for (int k = 0; k < nSprings; k++)
                                {
                                    var sp = horizontalSoilSprings[k];
                                    if (sp == null) continue;
                                    var res = sp.HorizontalSpringResults?
                                        .Where(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                 && r.LoadCombination?.No == loadCombination.No
                                                 && r.IsLiquefaction == isLiquefaction)
                                        .OrderByDescending(r => r.Step)
                                        .FirstOrDefault();
                                    if (res?.CumulativeDisp == null) continue;
                                    double dx = res.CumulativeDisp.Dxi - res.CumulativeDisp.Dxj;
                                    double dy = res.CumulativeDisp.Dyi - res.CumulativeDisp.Dyj;
                                    nodeRelDisps[k] = Math.Sqrt(dx * dx + dy * dy);
                                    if (res.CumulativeForce != null)
                                    {
                                        double fx = res.CumulativeForce.Fxi;
                                        double fy = res.CumulativeForce.Fyi;
                                        nodeActualForces[k] = Math.Sqrt(fx * fx + fy * fy);
                                    }
                                }

                                // isFront: 当該荷重ケースでのこの杭の前後判定 (p-y 計算に影響)
                                int iLC = loadCase.No - 1;
                                bool isFront = pileLayoutDataItem.IsFrontPiles != null
                                            && iLC >= 0
                                            && iLC < pileLayoutDataItem.IsFrontPiles.Count
                                            && pileLayoutDataItem.IsFrontPiles[iLC];

                                // 各節点 k の理論 上/下 寄与 (FEM と同じモデルで再計算) と、FEM 実測値に合わせた
                                // 比例スケール factor を計算
                                //   F_above_k: 節点 k の上方セグメント (k-1) の下半分寄与 (isTop=false)
                                //   F_below_k: 節点 k の下方セグメント k の上半分寄与   (isTop=true)
                                //   F_above_k + F_below_k = F_node_theory → scale_k = F_actual / F_theory
                                double[] fAboveScaled = new double[nSprings];
                                double[] fBelowScaled = new double[nSprings];
                                for (int k = 0; k < nSprings; k++)
                                {
                                    double y = nodeRelDisps[k];
                                    double fAboveTh = 0, fBelowTh = 0;
                                    // FEM (HorizontalCalculationViewModel) と同じ境界条件:
                                    //   上方寄与: k > 0 かつ セグメント k-1 が存在
                                    //   下方寄与: k が最終節点でない (k < nSprings - 1) かつ セグメント k が存在
                                    if (k > 0 && (k - 1) < reactions.Count)
                                        fAboveTh = Math.Abs(reactions[k - 1].GetSoilReaction(y, isTop: false, isFront, loadCase.SoilNonlinearityMode));
                                    if (k < nSprings - 1 && k < reactions.Count)
                                        fBelowTh = Math.Abs(reactions[k].GetSoilReaction(y, isTop: true, isFront, loadCase.SoilNonlinearityMode));

                                    // 純理論モード時は scale=1 (FEM スケールなし、理論値そのまま)
                                    double sum = fAboveTh + fBelowTh;
                                    double scale = IsPureTheoreticalMode
                                        ? 1.0
                                        : (sum > 1e-10 ? nodeActualForces[k] / sum : 1.0);
                                    fAboveScaled[k] = fAboveTh * scale;
                                    fBelowScaled[k] = fBelowTh * scale;
                                }

                                // セグメントごとに 4 隅の点を追加して長方形を作る
                                //   セグメント j の 上半分 [Z_top, Z_mid] = 節点 j の F_below (scaled)
                                //   セグメント j の 下半分 [Z_mid, Z_btm] = 節点 j+1 の F_above (scaled)
                                // 各半分の half-tributary area = L/2 × B でスケールして圧力/反力係数に
                                //
                                // 補足: ここで使う L = reactions[j].ZTop - reactions[j].ZBtm は
                                // InputModel.ElementDivision.SoilPiles[].HorizontalSoilReactions が
                                // 要素分割後の各 FEM 梁ごとに作られている (SoilPile.SetHorizontalSoilReaction)
                                // ため、FEM 上の実梁長と等価。Canvas 側の SoilReactionUtil (Beam.Length 集計) と
                                // 同じ「分割後の実長」ベースで計算している。
                                for (int j = 0; j < reactions.Count; j++)
                                {
                                    double zTop = reactions[j].ZTop;
                                    double zBtm = reactions[j].ZBtm;
                                    double zMid = 0.5 * (zTop + zBtm);
                                    double L = zTop - zBtm;
                                    if (L <= 0) continue;
                                    double B = reactions[j].B > 0 ? reactions[j].B : 1.0;
                                    double halfArea = 0.5 * L * B; // kN → kN/m² 変換用

                                    // 上半分: 節点 j の 下方寄与 (F_below_j) がこのセグメントの上半分に対応
                                    double fUpper = (j < nSprings) ? fBelowScaled[j] : 0;
                                    // 下半分: 節点 j+1 の 上方寄与 (F_above_{j+1}) がこのセグメントの下半分に対応
                                    double fLower = ((j + 1) < nSprings) ? fAboveScaled[j + 1] : 0;

                                    double pUpperPa = halfArea > 0 ? fUpper / halfArea : 0; // kN/m²
                                    double pLowerPa = halfArea > 0 ? fLower / halfArea : 0;

                                    double yUp = (j < nSprings) ? nodeRelDisps[j] : 0;
                                    double yLo = ((j + 1) < nSprings) ? nodeRelDisps[j + 1] : 0;

                                    double vUp, vLo;
                                    if (dataType == "Reaction")
                                    {
                                        vUp = pUpperPa;
                                        vLo = pLowerPa;
                                    }
                                    else // SecantStiffness
                                    {
                                        vUp = yUp > 1e-10 ? pUpperPa / yUp : 0; // kN/m³
                                        vLo = yLo > 1e-10 ? pLowerPa / yLo : 0;
                                    }

                                    // 長方形を作る 4 点
                                    springValues.Add(vUp); springZs.Add(zTop);
                                    springValues.Add(vUp); springZs.Add(zMid);
                                    springValues.Add(vLo); springZs.Add(zMid);
                                    springValues.Add(vLo); springZs.Add(zBtm);
                                }
                            }
                            else
                            {
                                // 従来の節点ベース処理 (RelativeDisp または IsDistributedMode=OFF)
                                for (int i = 0; i < nSprings; i++)
                                {
                                    var spring = horizontalSoilSprings[i];
                                    if (spring?.NodeI?.Coord == null) continue;

                                    // 深度（杭節点のZ座標）
                                    double z = spring.NodeI.Coord.Z;

                                    // 結果を取得（最終ステップ）
                                    var result = spring.HorizontalSpringResults?
                                        .Where(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                 && r.LoadCombination?.No == loadCombination.No
                                                 && r.IsLiquefaction == isLiquefaction)
                                        .OrderByDescending(r => r.Step)
                                        .FirstOrDefault();

                                    if (result?.CumulativeDisp == null || result?.CumulativeForce == null) continue;

                                    // 相対変位（杭節点 - 地盤節点）のX,Y合成
                                    double relDispX = result.CumulativeDisp.Dxi - result.CumulativeDisp.Dxj;
                                    double relDispY = result.CumulativeDisp.Dyi - result.CumulativeDisp.Dyj;
                                    double relDisp = Math.Sqrt(relDispX * relDispX + relDispY * relDispY);

                                    // ばね反力 (resultant) [kN]
                                    double forceX = result.CumulativeForce.Fxi;
                                    double forceY = result.CumulativeForce.Fyi;
                                    double force = Math.Sqrt(forceX * forceX + forceY * forceY);

                                    // ばね全体剛性 [kN/m] = 反力 [kN] / 変位 [m]
                                    double springStiffness = relDisp > 1e-10 ? force / relDisp : 0;

                                    springZs.Add(z);
                                    if (dataType == "RelativeDisp")
                                        springValues.Add(relDisp * 1000.0); // mm
                                    else if (dataType == "Reaction")
                                        springValues.Add(force); // kN
                                    else if (dataType == "SecantStiffness")
                                        springValues.Add(springStiffness); // kN/m
                                }
                            }

                            if (springZs.Count > 0)
                            {
                                var scatter = wpfPlot.Plot.Add.Scatter(springValues, springZs);
                                scatter.LegendText = GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                                // 案 C v3: 長方形分布モードでは 4 点/セグメントを直線で結ぶだけで長方形が描ける
                                // (ConnectStyle の調整は不要)

                                // ホバーポップアップ用詳細
                                double absMax = springValues.Count > 0 ? springValues.Max(Math.Abs) : 0;
                                string seriesLabel = dataType switch
                                {
                                    "RelativeDisp" => "相対変位",
                                    "Reaction" => "水平地盤反力",
                                    "SecantStiffness" => "水平地盤反力係数",
                                    _ => dataType
                                };
                                _graphHoverMap[scatter] =
                                    $"杭: #{pileLayoutDataItem.PileNo} (X={pileLayoutDataItem.X:N2}, Y={pileLayoutDataItem.Y:N2})\n"
                                    + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                    + $"組合せ: cmb{loadCombination.No} (αL={loadCombination.Alpha1:F2}/βU={loadCombination.Beta1:F2}/βL={loadCombination.Beta2:F2})\n"
                                    + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                                    + $"系列: {seriesLabel} ({unit})\n"
                                    + $"最大絶対値: {absMax:N2} {unit}\n"
                                    + $"節点数: {springZs.Count}";
                            }
                        }
                    }
                }
            }

            string title = dataType switch
            {
                "RelativeDisp" => "相対変位",
                "Reaction" => "水平地盤反力",
                "SecantStiffness" => "水平地盤反力係数",
                _ => dataType
            };
            string axisX = title + " " + unit;

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, title, axisX, "Z(m)");
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }
    }
}
