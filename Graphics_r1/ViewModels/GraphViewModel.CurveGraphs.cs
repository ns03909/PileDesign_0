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
    // 特性曲線グラフ: 土圧合力ばね・M-φ・杭頭 M-θ・P-y 曲線の描画。GraphViewModel.cs からの物理分割 (純粋移動)。
    public partial class GraphViewModel
    {
        // 土圧合力ばね: 1つのグラフに最上点・最下点の相対変位をX軸とした2系列を描画
        private void DrawDoatsuGoryokuBane(WpfPlot wpfPlot, Crosshair crosshair, string crosshairPositionText)
        {
            wpfPlot.Plot.Clear();

            var doatsuSprings = AnaModel.HorizontalSoilSprings
                .Where(s => s.NodeI?.Name == "根入部節点").ToList();
            if (doatsuSprings.Count == 0) return;

            // 最上点・最下点のばねを特定（Z座標で判定）
            var topSpring = doatsuSprings.OrderByDescending(s => s.NodeI.Coord.Z).FirstOrDefault();
            var btmSpring = doatsuSprings.OrderBy(s => s.NodeI.Coord.Z).FirstOrDefault();
            if (topSpring == null || btmSpring == null) return;

            double maxDispMm = 0; // 全系列の最大相対変位を追跡
            int caseIndex = 0; // 同一ケース (LC/Comb/Liq) の 3 系列を同色にするためのカウンタ

            foreach (LoadCase loadCase in GetSelectedLoadCases())
            {
                foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                {
                    foreach (var isLiquefaction in SelectedLiquefactionCases)
                    {
                        int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                        if (lastStep < 0) continue;

                        var caseColor = GetCaseColor(caseIndex++);

                        List<double> topRelDisps = [0];
                        List<double> btmRelDisps = [0];
                        List<double> totalForcesTop = [0];
                        List<double> totalForcesBtm = [0];

                        for (int step = 0; step <= lastStep; step++)
                        {
                            // 全土圧合力ばねの水平反力合計
                            double sumFx = 0, sumFy = 0;
                            foreach (var spring in doatsuSprings)
                            {
                                var result = spring.HorizontalSpringResults?
                                    .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                      && r.LoadCombination?.No == loadCombination.No
                                                      && r.IsLiquefaction == isLiquefaction
                                                      && r.Step == step);
                                if (result?.CumulativeForce != null)
                                {
                                    sumFx += result.CumulativeForce.Fxi;
                                    sumFy += result.CumulativeForce.Fyi;
                                }
                            }
                            // 載荷方向への射影 (P-y 曲線慣用に合わせ符号反転)
                            //
                            // FEM 規約: NodeI(杭側) 内部力 Fxi は杭の動きと逆向き
                            //   - 杭が +X に動く → Fxi < 0 (杭を引き戻す)
                            // ここでは「土圧合力ばねが杭に与える抵抗力」(P-y 曲線の P と同じ向き) を
                            // 表示したいので、内部力を符号反転して "soil resistance toward load direction"
                            // として扱う。これで X (相対変位の大きさ) と Y (反力合計) が
                            // 通常の P-y 曲線と同じ Q1 (両方正) に来る。
                            double radA = loadCase.LoadAngle * Math.PI / 180.0;
                            double cosA = Math.Cos(radA);
                            double sinA = Math.Sin(radA);
                            double totalForce = -(sumFx * cosA + sumFy * sinA);
                            totalForcesTop.Add(totalForce);
                            totalForcesBtm.Add(totalForce);

                            // 最上点の相対変位
                            var topResult = topSpring.HorizontalSpringResults?
                                .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                  && r.LoadCombination?.No == loadCombination.No
                                                  && r.IsLiquefaction == isLiquefaction
                                                  && r.Step == step);
                            if (topResult?.CumulativeDisp != null)
                            {
                                double dx = topResult.CumulativeDisp.Dxi - topResult.CumulativeDisp.Dxj;
                                double dy = topResult.CumulativeDisp.Dyi - topResult.CumulativeDisp.Dyj;
                                topRelDisps.Add(Math.Sqrt(dx * dx + dy * dy) * 1000.0);
                            }
                            else topRelDisps.Add(0);

                            // 最下点の相対変位
                            var btmResult = btmSpring.HorizontalSpringResults?
                                .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                  && r.LoadCombination?.No == loadCombination.No
                                                  && r.IsLiquefaction == isLiquefaction
                                                  && r.Step == step);
                            if (btmResult?.CumulativeDisp != null)
                            {
                                double dx = btmResult.CumulativeDisp.Dxi - btmResult.CumulativeDisp.Dxj;
                                double dy = btmResult.CumulativeDisp.Dyi - btmResult.CumulativeDisp.Dyj;
                                btmRelDisps.Add(Math.Sqrt(dx * dx + dy * dy) * 1000.0);
                            }
                            else btmRelDisps.Add(0);
                        }

                        string legend = GetGeneralLegendText(loadCase, loadCombination, isLiquefaction);

                        // 最大相対変位を更新
                        double seriesMax = Math.Max(topRelDisps.Max(), btmRelDisps.Max());
                        if (seriesMax > maxDispMm) maxDispMm = seriesMax;

                        // 軸の意味を説明するホバー (X = 相対変位、Y = 抵抗反力合計)
                        string axisExplain =
                            "X 軸 (相対変位): 根入部節点 (NodeI, 杭側) の変位 − 地盤側 (NodeJ) の変位 の大きさ [mm]。\n"
                            + "        ‖ (Dx_i − Dx_j, Dy_i − Dy_j) ‖ で計算。NodeJ は固定なので実質的に\n"
                            + "        「杭の根入部の絶対変位」と等価。\n"
                            + "Y 軸 (反力合計): 土圧合力ばね N 本の合計反力を載荷方向に射影 [kN]。\n"
                            + "        P-y 曲線慣用に合わせ、ばねが杭を押し戻す向き (= 抵抗) を正としている。";

                        var scatterTop = wpfPlot.Plot.Add.Scatter(topRelDisps, totalForcesTop);
                        scatterTop.LegendText = $"最上点 {legend}";
                        scatterTop.Color = caseColor;
                        scatterTop.MarkerStyle.FillColor = caseColor;
                        scatterTop.MarkerStyle.LineColor = caseColor;
                        _graphHoverMap[scatterTop] =
                            $"系列: 最上点 (最大 Z の土圧合力ばね NodeI 位置)\n"
                            + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}° / 組合せ cmb{loadCombination.No}\n"
                            + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                            + $"ステップ数: {lastStep}\n"
                            + axisExplain;

                        var scatterBtm = wpfPlot.Plot.Add.Scatter(btmRelDisps, totalForcesBtm);
                        scatterBtm.LegendText = $"最下点 {legend}";
                        scatterBtm.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                        scatterBtm.Color = caseColor;
                        scatterBtm.MarkerStyle.FillColor = caseColor;
                        scatterBtm.MarkerStyle.LineColor = caseColor;
                        _graphHoverMap[scatterBtm] =
                            $"系列: 最下点 (最小 Z の土圧合力ばね NodeI 位置)\n"
                            + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}° / 組合せ cmb{loadCombination.No}\n"
                            + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                            + $"ステップ数: {lastStep}\n"
                            + axisExplain;

                        // 等変形時の理論曲線（loadCase依存、最大変位×1.5まで描画）
                        var dgb = InputModel.ElementDivision?.DoatsuGoryokuBane;
                        if (dgb != null && dgb.Items.Count > 0 && dgb.DeltaP > 0 && seriesMax > 0)
                        {
                            double radT = loadCase.LoadAngle * Math.PI / 180.0;
                            double cosT = Math.Cos(radT);
                            double sinT = Math.Sin(radT);
                            double theorMaxM = seriesMax * 1.5 / 1000.0; // mm→m、1.5倍

                            var theorDisps = new List<double>();
                            var theorForces = new List<double>();
                            int nPoints = 100;
                            for (int i = 0; i <= nPoints; i++)
                            {
                                double d = theorMaxM * i / nPoints; // m単位
                                double dx = d * cosT;
                                double dy = d * sinT;
                                double dgbFx = 0, dgbFy = 0;
                                foreach (var item in dgb.Items)
                                {
                                    double dz = item.ZTop - item.ZBtm;
                                    dgbFx += item.GetPressure(dx) * dz * Math.Abs(item.Y1 - item.Y2);
                                    dgbFy += item.GetPressure(dy) * dz * Math.Abs(item.X1 - item.X2);
                                }
                                theorDisps.Add(d * 1000.0); // mm
                                theorForces.Add(dgbFx * cosT + dgbFy * sinT);
                            }
                            var scatterTheor = wpfPlot.Plot.Add.Scatter(theorDisps, theorForces);
                            scatterTheor.LegendText = $"等変形時（理論） {legend}";
                            scatterTheor.LineStyle.Pattern = ScottPlot.LinePattern.Dotted;
                            scatterTheor.MarkerSize = 0;
                            scatterTheor.Color = caseColor;
                            _graphHoverMap[scatterTheor] =
                                $"系列: 等変形時 理論曲線 (全土圧合力ばねが同一相対変位の場合)\n"
                                + $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                + axisExplain;
                        }
                    }
                }
            }

            ConfigurePlot(wpfPlot, crosshair, crosshairPositionText,
                "土圧合力ばね反力",
                "相対変位 (mm)", "土圧合力ばね反力合計 (kN)");

            // X軸の最大値を最終ステップの最大変位の1.5倍に制限
            if (maxDispMm > 0)
            {
                wpfPlot.Plot.Axes.SetLimitsX(0, maxDispMm * 1.5);
            }

            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // M-φ関係描画（任意の杭の任意の要素について軸力に応じたM-φ曲線と最終ステップマーカー）
        private void DrawMPhiCurves(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            var model = AnaModel;
            wpfPlot.Plot.Clear();
            _graphHoverMap.Clear();

            var targetPiles = GetSelectedPileLayouts();
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();


            foreach (var pileLayout in targetPiles)
            {
                // 杭体取得
                if (pileLayout.PileBodyNo <= 0 || pileLayout.PileBodyNo > InputModel.PileBodies.Count)
                {
                    continue;
                }
                var pileBody = InputModel.PileBodies[pileLayout.PileBodyNo - 1];
                if (pileBody.PileBodyRef != SelectedPileBodyRef)
                {
                    continue;
                }

                // 対応するBeam要素を見つける
                // SoilPileの杭要素分割（地層境界・0.5D分割）でBeam数 > 入力セグメント数のため、
                // SegmentIndexからSoilPileのセグメント番号で逆引きする
                SoilPile soilPile = null;
                {
                    int soilPileAltNo = pileLayout.SoilPileAltNo;
                    if (InputModel.ElementDivision?.SoilPiles != null
                        && soilPileAltNo - 1 >= 0
                        && soilPileAltNo - 1 < InputModel.ElementDivision.SoilPiles.Count)
                    {
                        soilPile = InputModel.ElementDivision.SoilPiles[soilPileAltNo - 1];
                    }
                }

                bool isAllSegments = SelectedPileSegmentNo <= 0;
                var matchedBeams = new List<Beam>();

                foreach (var beam in pileLayout.Beams)
                {
                    if (beam.SegmentIndex is not int seg) continue;

                    if (isAllSegments)
                    {
                        // All: すべてのbeamを追加
                        matchedBeams.Add(beam);
                        continue;
                    }

                    // SoilPileのPileBodySegments[seg].No は入力セグメント番号と一致（DeepCopy由来）
                    int inputSegNo = -1;
                    if (soilPile != null && seg >= 0 && seg < soilPile.PileBodySegments.Count)
                        inputSegNo = soilPile.PileBodySegments[seg].No;

                    if (inputSegNo == SelectedPileSegmentNo)
                    {
                        matchedBeams.Add(beam);
                    }
                }
                if (matchedBeams.Count == 0)
                {
                    continue;
                }

                foreach (var targetBeam in matchedBeams)
                {
                int segLabel = targetBeam.SegmentIndex.HasValue ? targetBeam.SegmentIndex.Value + 1 : 0;

                foreach (var loadCase in selectedLoadCases)
                {
                    foreach (var loadCombination in selectedCombinations)
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            // 解析未実行の (LoadCase, LoadCombination, Liquefaction) はこの組合せ全てスキップ
                            int lastStepForSet = model.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStepForSet < 0) continue;

                            // 軸力推定
                            double axialN = 0.0;
                            var prop = loadCase.GetType().GetProperty("NonlinearAxialForceN");
                            if (prop?.GetValue(loadCase) is double nlc && double.IsFinite(nlc) && nlc != 0.0)
                            {
                                axialN = nlc;
                            }
                            else
                            {
                                try
                                {
                                    double nSeis = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                    if (double.IsFinite(nSeis) && nSeis != 0.0)
                                        axialN = nSeis;
                                }
                                catch (Exception ex) { Log.Warning(ex, "[GraphVM] GetSeismicAxialForce"); }
                                if (axialN == 0.0 && double.IsFinite(pileLayout.AxialForce))
                                    axialN = pileLayout.AxialForce;
                            }

                            // M-φ曲線取得（解析で使用したものを優先）
                            List<double> phis = null;
                            List<double> moments = null;
                            string curveSource = "none";

                            // 解析結果からM-φ曲線を取得（解析で実際に使用したもの）
                            int lastStep = lastStepForSet;
                            BeamResult beamResultForCurve = null;
                            if (lastStep >= 0)
                            {
                                beamResultForCurve = targetBeam.GetBeamResult(model, loadCase, loadCombination, isLiquefaction, lastStep);

                                // 方法0: BeamResultに保存されたM-φ曲線を使用（最優先 - 解析で実際に使用したもの）
                                if (beamResultForCurve?.MPhiCurve_Phis != null && beamResultForCurve.MPhiCurve_Phis.Count >= 2)
                                {
                                    phis = beamResultForCurve.MPhiCurve_Phis;
                                    moments = beamResultForCurve.MPhiCurve_Moments;
                                    curveSource = "BeamResult";
                                }
                            }

                            // 方法1: 解析時に解決済みのキャッシュ曲線を使用（BeamResultに保存されていない場合）
                            if ((phis == null || phis.Count < 2) && targetBeam.ResolvedCombinedCurve?.Points != null)
                            {
                                var cachedCurve = targetBeam.ResolvedCombinedCurve;
                                if (cachedCurve.Points.Count >= 2)
                                {
                                    phis = [.. cachedCurve.Points.Select(p => p.Phi)];
                                    moments = [.. cachedCurve.Points.Select(p => p.Moment)];
                                    curveSource = "ResolvedCombinedCurve";
                                }
                            }

                            // 方法2: フォールバック - PileSectionから新規取得（解析結果がない場合のみ）
                            if ((phis == null || phis.Count < 2) && targetBeam.SegmentIndex is int fallbackSeg
                                && soilPile != null && fallbackSeg >= 0 && fallbackSeg < soilPile.PileBodySegments.Count)
                            {
                                var pileSegment = soilPile.PileBodySegments[fallbackSeg];
                                var pileSection = pileSegment.PileSection;
                                if (pileSection != null)
                                {
                                    try
                                    {
                                        var mPhi = pileSection.GetMPhiRelationship(axialN);
                                        var rawPhis = mPhi.Phis?.ToList();
                                        var rawMoments = mPhi.Moments?.ToList();
                                        if (rawPhis != null && rawMoments != null && rawPhis.Count >= 2)
                                        {
                                            phis = rawPhis;
                                            moments = rawMoments;
                                            curveSource = "PileSection(fallback)";
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Serilog.Log.Debug($"[GraphViewModel] M-φ fallback 取得失敗: {ex.GetType().Name}: {ex.Message}");
                                    }
                                }
                            }

                            if (phis == null || moments == null || phis.Count < 2)
                            {
                                continue;
                            }

                            // 曲線プロット
                            bool isEIMode = SelectedGraphOption == "杭体EI-φ";
                            double[] plotXValues;
                            double[] plotYValues;
                            if (isEIMode)
                            {
                                // EI = M/φ は M-φ 区間内で双曲線状に変化する。
                                // ScatterLine は直線近似なので、分割を細かくして描画ポリラインを真の曲線に近づける。
                                // nDiv=100 で M-φ 折点付近の湾曲もほぼ折線で追従する（計算コスト微小）。
                                var eiPhis = new List<double>();
                                var eiValues = new List<double>();
                                for (int seg2 = 0; seg2 < phis.Count - 1; seg2++)
                                {
                                    double phi0 = phis[seg2], phi1 = phis[seg2 + 1];
                                    double m0 = moments[seg2], m1 = moments[seg2 + 1];
                                    const int nDiv = 100;
                                    int jStart = (seg2 == 0) ? 1 : 0; // φ=0はスキップ
                                    for (int j = jStart; j <= nDiv; j++)
                                    {
                                        double t = (double)j / nDiv;
                                        double phi = phi0 + t * (phi1 - phi0);
                                        double m = m0 + t * (m1 - m0);
                                        if (phi > 1e-15)
                                        {
                                            eiPhis.Add(phi);
                                            eiValues.Add(m / phi);
                                        }
                                    }
                                }
                                plotXValues = eiPhis.ToArray();
                                plotYValues = eiValues.ToArray();
                            }
                            else
                            {
                                plotXValues = phis.ToArray();
                                plotYValues = [.. moments];
                            }

                            string legend = $"LC:{loadCase.LoadName}|Comb:{loadCombination.No}|LIQ:{isLiquefaction}|N:{axialN:F0}|Pile:{pileLayout.No}|Seg:{segLabel}";
                            var scatter = wpfPlot.Plot.Add.Scatter(plotXValues, plotYValues);
                            scatter.LineStyle.Width = 2;
                            scatter.MarkerSize = isEIMode ? 0 : 5; // EIモードはマーカー不要
                            scatter.LegendText = legend;

                            // ホバー詳細: 杭/要素/入力杭区間/荷重条件/軸力/曲線出典
                            int inputSegForDetails = -1;
                            string sectionDesc = "";
                            if (targetBeam.SegmentIndex is int segForDetails
                                && soilPile != null && segForDetails >= 0 && segForDetails < soilPile.PileBodySegments.Count)
                            {
                                var pbSeg = soilPile.PileBodySegments[segForDetails];
                                inputSegForDetails = pbSeg.No;
                                sectionDesc = pbSeg.PileSection?.PileDescription ?? "";
                            }
                            string mphiDetails =
                                $"杭 No: {pileLayout.No} / 要素 Seg{segLabel}\n" +
                                $"入力杭区間 No: {(inputSegForDetails > 0 ? inputSegForDetails.ToString() : "—")}\n" +
                                $"杭断面: {(string.IsNullOrEmpty(sectionDesc) ? "—" : sectionDesc)}\n" +
                                $"LC: {loadCase.LoadName} / Comb: {loadCombination.No} / LIQ: {isLiquefaction}\n" +
                                $"軸力 N: {axialN:F1} kN\n" +
                                $"曲線出典: {curveSource}";
                            _graphHoverMap[scatter] = mphiDetails;

                            // 最終ステップの曲率・モーメント取得（lastStepとbeamResultForCurveは上で取得済み）
                            if (lastStep >= 0 && beamResultForCurve != null)
                            {
                                // 曲率：解析で保存した値を使用
                                double phiFinal = beamResultForCurve.Curvature;

                                // フォールバック：Curvatureが0以下の場合、回転角差から計算
                                if (phiFinal <= 0.0 && beamResultForCurve.CumulativeDisp != null)
                                {
                                    double length = targetBeam.Length;
                                    if (length > 0)
                                    {
                                        // 正しい曲率計算: 各成分の差から合成
                                        double dRyi = beamResultForCurve.CumulativeDisp.Ryj - beamResultForCurve.CumulativeDisp.Ryi;
                                        double dRzi = beamResultForCurve.CumulativeDisp.Rzj - beamResultForCurve.CumulativeDisp.Rzi;
                                        phiFinal = Math.Sqrt(dRyi * dRyi + dRzi * dRzi) / length;
                                    }
                                }

                                // モーメント：BeamResultに保存されたM-φ曲線から評価した値を使用
                                // 梁要素の剛性マトリクスから計算される端部モーメント(CumulativeForce)は
                                // M-φ曲線の断面モーメントとは異なるため、曲線から直接評価した値を使用
                                double mFinal = beamResultForCurve.Moment;
                                // フォールバック: Momentが0以下の場合、曲線から補間
                                if (mFinal <= 0.0)
                                {
                                    mFinal = InterpolateMomentFromCurve(phis, moments, phiFinal);
                                }
                                double mFem = beamResultForCurve.CumulativeForce?.MabsMax ?? 0;

                                // マーカープロット
                                double markerY = isEIMode && phiFinal > 1e-15 ? mFinal / phiFinal : mFinal;
                                if (double.IsFinite(phiFinal) && double.IsFinite(markerY) && markerY > 0)
                                {
                                    Scatter marker = wpfPlot.Plot.Add.Scatter([phiFinal], new[] { markerY });
                                    marker.LineStyle.Width = 0;
                                    marker.MarkerSize = 12;
                                    marker.MarkerStyle.Shape = ScottPlot.MarkerShape.FilledCircle;
                                    marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                    marker.LegendText = $"最終:{legend}";
                                    _graphHoverMap[marker] =
                                        mphiDetails + "\n" +
                                        $"最終 φ: {phiFinal:F6} rad/m\n" +
                                        $"最終 M: {mFinal:F1} kN·m" +
                                        (isEIMode ? $"\n最終 EI: {markerY:F0} kN·m²" : "");
                                }
                            }
                        }
                    }
                }
                } // foreach targetBeam in matchedBeams
            }

            bool isEIGraph = SelectedGraphOption == "杭体EI-φ";
            string plotTitle = isEIGraph ? "EI-φ関係" : "M-φ関係";
            string yLabel = isEIGraph ? "EI (kN·m²)" : "M (kN·m)";
            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, plotTitle, "φ (rad/m)", yLabel);
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        // M-θ関係描画（任意の杭の杭頭について軸力に応じたM-θ曲線と最終ステップマーカー）
        /// <summary>
        /// FT-Pile / キャプテンパイル M-θ グラフ描画
        /// N値ごとの曲線群を描画し、最終ステップ位置にマーカーを配置
        /// </summary>
        private void DrawPileHeadTypeMTheta(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            wpfPlot.Plot.Clear();
            var model = AnaModel;
            if (model == null) { wpfPlot.Refresh(); return; }

            bool isFTPile = SelectedGraphOption == "FTPileM-θ";
            string targetType = isFTPile ? "FT-Pile構法" : "キャプテンパイル工法";

            var targetPiles = GetSelectedPileLayouts();
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();

            double maxThetaMarker = 0; // マーカーのθ最大値を追跡

            foreach (var pileLayout in targetPiles)
            {
                if (pileLayout.PileBodyNo <= 0 || pileLayout.PileBodyNo > InputModel.PileBodies.Count) continue;
                var pileBody = InputModel.PileBodies[pileLayout.PileBodyNo - 1];
                if (pileBody.PileTopType?.Contains(targetType) != true) continue;

                var pileTop = pileBody.PileTop;
                if (pileTop == null) continue;

                // 各荷重ケースごとに、その杭の軸力に応じた1本のM-θ曲線を描画
                foreach (var loadCase in selectedLoadCases)
                {
                    // この杭・荷重ケースの軸力を取得（kN）
                    double axialN_kN;
                    try { axialN_kN = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level); }
                    catch { axialN_kN = pileLayout.AxialForce; }
                    double axialN_N = axialN_kN * 1000.0; // kN → N

                    // その軸力に対応するM-θ曲線を1本取得
                    ObservableCollection<double> thetas = null;
                    ObservableCollection<double> ms = null;

                    if (isFTPile && pileTop.FTPile != null)
                    {
                        var result = pileTop.FTPile.GetMThetaRelationship(axialN_N);
                        thetas = result.Item1;
                        ms = result.Item2;
                    }
                    else if (!isFTPile && pileTop.CaptainPile != null)
                    {
                        var result = pileTop.CaptainPile.GetMThetaRelationship(axialN_N);
                        thetas = result.Item1;
                        ms = result.Item2;
                    }

                    if (thetas == null || ms == null || thetas.Count < 2) continue;

                    // 曲線描画（N·mm → kN·m: 1 kN·m = 1e6 N·mm、マーカーなしライン）
                    var scatter = wpfPlot.Plot.Add.Scatter(
                        thetas.Select(t => (double)t).ToArray(),
                        ms.Select(m => m / 1e6).ToArray());
                    scatter.MarkerSize = 0;
                    scatter.LegendText = $"杭{pileLayout.No} {loadCase.LoadName} N={axialN_kN:N0}kN";

                    // 最終ステップマーカー
                    foreach (var loadCombination in selectedCombinations)
                    {
                        foreach (var isLiq in SelectedLiquefactionCases)
                        {
                            int lastStep = model.GetAnalysisLastStep(loadCase, loadCombination, isLiq);
                            if (lastStep < 0) continue;

                            var rs = model.RotationalSprings?.FirstOrDefault(r =>
                                r.Name == $"RθXY-{pileLayout.No}");
                            if (rs == null) continue;

                            var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                                r.LoadCase?.No == loadCase.No &&
                                r.LoadCombination?.No == loadCombination.No &&
                                r.IsLiquefaction == isLiq &&
                                r.Step == lastStep);
                            if (rsResult?.CumulativeDisp == null || rsResult.CumulativeForce == null) continue;

                            double dRx = rsResult.CumulativeDisp.Rxj - rsResult.CumulativeDisp.Rxi;
                            double dRy = rsResult.CumulativeDisp.Ryj - rsResult.CumulativeDisp.Ryi;
                            double thetaFinal = Math.Sqrt(dRx * dRx + dRy * dRy);
                            double mFinal = Math.Sqrt(
                                rsResult.CumulativeForce.Mxi * rsResult.CumulativeForce.Mxi +
                                rsResult.CumulativeForce.Myi * rsResult.CumulativeForce.Myi);

                            if (double.IsFinite(thetaFinal) && double.IsFinite(mFinal) && thetaFinal > 0)
                            {
                                var marker = wpfPlot.Plot.Add.Scatter(new[] { thetaFinal }, new[] { mFinal });
                                marker.LineStyle.Width = 0;
                                marker.MarkerSize = 12;
                                marker.MarkerStyle.Shape = ScottPlot.MarkerShape.FilledCircle;
                                marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                marker.LegendText = $"解析結果 杭{pileLayout.No} {loadCase.LoadName}|{(isLiq ? "LIQ" : "非LIQ")}";
                                if (thetaFinal > maxThetaMarker) maxThetaMarker = thetaFinal;
                            }
                        }
                    }
                }
            }

            string title = isFTPile ? "FT-Pile M-θ関係" : "キャプテンパイル M-θ関係";
            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, title, "θ (rad)", "M (kN·m)", decimalPlacesX: 3);

            // X軸の表示範囲: 0 ～ マーカー最大θの1.5倍
            if (maxThetaMarker > 1e-10)
            {
                wpfPlot.Plot.Axes.SetLimitsX(0, maxThetaMarker * 1.5);
            }

            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }

        private void DrawMThetaCurvesWithMarker(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            var model = AnaModel;
            if (model?.RotationalSprings == null || model.RotationalSprings.Count == 0)
            {
                wpfPlot.Plot.Clear();
                _graphHoverMap.Clear();
                wpfPlot.Refresh();
                return;
            }

            var targetPiles = GetSelectedPileLayouts();
            var targetPileNos = new HashSet<int>(targetPiles.Select(p => p.No));
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();

            wpfPlot.Plot.Clear();
            _graphHoverMap.Clear();

            // 剛結扱いで M-θ 曲線が描画されない杭をユーザーに通知するためのセット
            // (杭体タイプ × 杭頭タイプの組合せが剛結となるケース。例: 場所打ち鋼管コンクリート杭 + 鉄筋定着工法)
            var rigidPileInfos = new HashSet<string>();

            foreach (var loadCase in selectedLoadCases)
            {
                foreach (var loadCombination in selectedCombinations)
                {
                    foreach (var isLiquefaction in SelectedLiquefactionCases)
                    {
                        // 解析未実行の (LoadCase, LoadCombination, Liquefaction) はこの組合せ全てスキップ
                        int lastStepForSet = model.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                        if (lastStepForSet < 0) continue;

                        foreach (var rs in model.RotationalSprings)
                        {
                            // 対応杭レイアウト探索
                            // バネ名形式: "RθXY-{pileNo}" から杭番号を抽出
                            PileLayoutDataItem pileLayout = null;
                            if (rs.Name != null && rs.Name.Contains('-'))
                            {
                                var parts = rs.Name.Split('-');
                                if (parts.Length >= 2 && int.TryParse(parts[^1], out int pileNo))
                                {
                                    pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.No == pileNo);
                                }
                            }
                            // フォールバック: NodeJから探索
                            if (pileLayout == null && rs.NodeJ != null)
                            {
                                pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.PileNodes.Count > 0 && ReferenceEquals(pl.PileNodes[0], rs.NodeJ));
                            }
                            // フォールバック: PileBodyNoから探索（最初の杭のみ）
                            if (pileLayout == null && rs.PileBodyNo is int pb && pb > 0 && pb <= InputModel.PileBodies.Count)
                            {
                                pileLayout = InputModel.PileLayoutItems.FirstOrDefault(pl => pl.PileBodyNo == pb);
                            }

                            if (pileLayout == null) continue;
                            if (SelectedPileOption != "All" && !targetPileNos.Contains(pileLayout.No)) continue;

                            // 軸力推定
                            double axialN = 0.0;
                            var prop = loadCase.GetType().GetProperty("NonlinearAxialForceN");
                            if (prop?.GetValue(loadCase) is double nlc && double.IsFinite(nlc) && nlc != 0.0)
                            {
                                axialN = nlc;
                            }
                            else
                            {
                                try
                                {
                                    double nSeis = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                    if (double.IsFinite(nSeis) && nSeis != 0.0)
                                        axialN = nSeis;
                                }
                                catch (Exception ex) { Log.Warning(ex, "[GraphVM] GetSeismicAxialForce"); }
                                if (axialN == 0.0 && double.IsFinite(pileLayout.AxialForce))
                                    axialN = pileLayout.AxialForce;
                            }

                            // Y 案: 表示中ケースに対応するスナップショットから曲線取得 (解析実体と整合)
                            // 無ければ rs 直接 (旧経路、後方互換)、それも無ければ K·θ 線形外挿。
                            string snapKey = RotationalSpring.MakeCaseKey(
                                loadCase?.LoadName, loadCombination?.No ?? 0, isLiquefaction);
                            MomentRotationCurve? snapCurveXY = null;
                            MomentRotationCurve? snapCurveSingle = null;
                            RotationalSpringMode snapMode = rs.Mode;
                            double? snapKxy = rs.KthetaXY;
                            double? snapKsingle = rs.Ktheta;
                            if (rs.CaseMThetaSnapshots.TryGetValue(snapKey, out var snap))
                            {
                                snapCurveXY = snap.CurveXY;
                                snapCurveSingle = snap.Curve;
                                snapMode = snap.Mode;
                                snapKxy = snap.KthetaXY;
                                snapKsingle = snap.Ktheta;
                            }
                            else
                            {
                                snapCurveXY = rs.CurveXY;
                                snapCurveSingle = rs.Curve;
                            }

                            double[] thetas;
                            double[] moments;
                            string modeTag;
                            if (snapMode == RotationalSpringMode.CombinedXY && snapCurveXY != null)
                            {
                                (thetas, moments) = snapCurveXY.ToArrays();
                                modeTag = "XY";
                            }
                            else if (snapMode == RotationalSpringMode.SingleDof && snapCurveSingle != null)
                            {
                                (thetas, moments) = snapCurveSingle.ToArrays();
                                modeTag = rs.Dof.ToString();
                            }
                            else
                            {
                                double? k = snapMode == RotationalSpringMode.CombinedXY ? snapKxy : snapKsingle;
                                if (!k.HasValue || k.Value <= 0.0) continue;

                                // 剛体相当 (HorizontalCalculationViewModel.SetupNonlinearMThetaForLoadCase で
                                // K=1e10 を「剛」マーカーとして使っている) の場合は曲線をプロットしない。
                                // K·θ で θ=0.02 まで描くと M=2×10^8 kN·m という非物理値の直線になり、
                                // 「鋼管杭 (plain) で M-θ がおかしい」ように見える誤解を防ぐため。
                                // 杭頭は剛結扱いの設計なので M-θ 関係そのものが存在しない、と表示する。
                                const double KRigidThreshold = 1e9; // KBig=1e10 の 1 桁下を閾値に
                                if (k.Value >= KRigidThreshold)
                                {
                                    // 凡例のみ追加 (空シリーズ) して「剛結」表示
                                    string rigidLegend =
                                        $"LC:{loadCase.LoadName}|Comb:{loadCombination.No}|LIQ:{isLiquefaction}|Pile:{pileLayout.No}|剛結 (M-θ 関係なし)";
                                    var marker = wpfPlot.Plot.Add.Marker(0, 0);
                                    marker.LegendText = rigidLegend;
                                    marker.MarkerSize = 0;
                                    // 説明用に杭体/杭頭タイプ組合せを蓄積 (Window 下部にまとめて表示)
                                    if (pileLayout.PileBodyNo > 0 && pileLayout.PileBodyNo <= InputModel.PileBodies.Count)
                                    {
                                        var pbody = InputModel.PileBodies[pileLayout.PileBodyNo - 1];
                                        string combo = $"{pbody.PileBodyType} + {pbody.PileTopType}";
                                        rigidPileInfos.Add($"杭 #{pileLayout.No} ({combo})");
                                    }
                                    else
                                    {
                                        rigidPileInfos.Add($"杭 #{pileLayout.No}");
                                    }
                                    continue;
                                }

                                const double thetaMax = 0.02;
                                int nDiv = 50;
                                thetas = [.. Enumerable.Range(0, nDiv).Select(i => i * thetaMax / (nDiv - 1))];
                                moments = [.. thetas.Select(t => k.Value * t)];
                                modeTag = snapMode == RotationalSpringMode.CombinedXY ? "XY" : rs.Dof.ToString();
                            }
                            if (thetas.Length == 0 || moments.Length == 0) continue;

                            // 曲線プロット
                            string legend = $"LC:{loadCase.LoadName}|Comb:{loadCombination.No}|LIQ:{isLiquefaction}|N:{axialN:F0}|Pile:{pileLayout.No}|Mode:{modeTag}";
                            var scatter = wpfPlot.Plot.Add.Scatter(thetas, moments);
                            scatter.LegendText = legend;

                            // 杭頭詳細: 杭No、断面、回転ばね構成、軸力、荷重条件
                            double pileHeadZ = pileLayout.PileNodes != null && pileLayout.PileNodes.Count > 0
                                ? pileLayout.PileNodes[0].Coord.Z : double.NaN;
                            string headSectionDesc = "";
                            if (pileLayout.PileBodyNo > 0 && pileLayout.PileBodyNo <= InputModel.PileBodies.Count)
                            {
                                var pbody = InputModel.PileBodies[pileLayout.PileBodyNo - 1];
                                if (pbody?.PileBodySegments != null && pbody.PileBodySegments.Count > 0)
                                    headSectionDesc = pbody.PileBodySegments[0].PileSection?.PileDescription ?? "";
                            }
                            // Y 案: snapshot 側の K を優先表示 (ケース別実体)
                            double kUsed = snapMode == RotationalSpringMode.CombinedXY ? (snapKxy ?? 0.0) : (snapKsingle ?? 0.0);
                            string mthetaDetails =
                                $"杭 No: {pileLayout.No}  (X={pileLayout.X:F3}, Y={pileLayout.Y:F3})\n" +
                                $"杭頭 Z: {(double.IsFinite(pileHeadZ) ? pileHeadZ.ToString("F3") + " m" : "—")}\n" +
                                $"杭頭断面: {(string.IsNullOrEmpty(headSectionDesc) ? "—" : headSectionDesc)}\n" +
                                $"回転ばね: {rs.Name} / Mode: {modeTag} / Kθ: {kUsed:0.###E+0}\n" +
                                $"LC: {loadCase.LoadName} / Comb: {loadCombination.No} / LIQ: {isLiquefaction}\n" +
                                $"軸力 N: {axialN:F1} kN";
                            _graphHoverMap[scatter] = mthetaDetails;

                            // 最終ステップの回転角・モーメント取得
                            int lastStep = model.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            if (lastStep >= 0)
                            {
                                // RotationalSpringResultから該当する結果を取得（Beam.GetBeamResultと同様のパターン）
                                var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                                    r.LoadCase?.No == loadCase.No &&
                                    r.LoadCombination?.No == loadCombination.No &&
                                    r.IsLiquefaction == isLiquefaction &&
                                    r.Step == lastStep);

                                if (rsResult?.CumulativeDisp != null && rsResult.CumulativeForce != null)
                                {
                                    // 回転角（回転ばねの相対回転量から直接取得）
                                    // CumulativeDispはNodeI,NodeJの変位を格納（Rxi=NodeI.Rx, Rxj=NodeJ.Rx）
                                    double dRx = rsResult.CumulativeDisp.Rxj - rsResult.CumulativeDisp.Rxi;
                                    double dRy = rsResult.CumulativeDisp.Ryj - rsResult.CumulativeDisp.Ryi;
                                    double dRz = rsResult.CumulativeDisp.Rzj - rsResult.CumulativeDisp.Rzi;
                                    double mxi = rsResult.CumulativeForce.Mxi;
                                    double myi = rsResult.CumulativeForce.Myi;
                                    double mzi = rsResult.CumulativeForce.Mzi;

                                    double thetaFinal;
                                    double mFinal;
                                    bool isPeakPlot = false;
                                    if (rs.Mode == RotationalSpringMode.CombinedXY)
                                    {
                                        // v28 アプローチ I: post-crack で方向ロック + ヒステリシスされた杭は
                                        // **ピーク履歴値 (ThetaProjMax, curve(ThetaProjMax))** をプロット。
                                        // 現在値 (θ_proj, M_proj) は線形除荷経路上の点で、monotonic loading
                                        // curve 上には乗らない。設計的にはピーク時の最大 demand を包絡線で
                                        // 示す方が意味のある可視化。
                                        // Y 案: ピーク投影は snapshot 側の CurveXY を優先
                                        var peakCurve = snapCurveXY ?? rs.CurveXY;
                                        if (rsResult.HasCracked
                                            && rsResult.CrackNx.HasValue
                                            && rsResult.CrackNy.HasValue
                                            && rsResult.ThetaProjMax > 0.0
                                            && peakCurve != null)
                                        {
                                            thetaFinal = rsResult.ThetaProjMax;
                                            mFinal = Math.Abs(peakCurve.EvaluateMoment(thetaFinal));
                                            isPeakPlot = true;
                                        }
                                        else
                                        {
                                            thetaFinal = Math.Sqrt(dRx * dRx + dRy * dRy);
                                            mFinal = Math.Sqrt(mxi * mxi + myi * myi);
                                        }
                                    }
                                    else
                                    {
                                        thetaFinal = rs.Dof == RotationalDof.Rx ? Math.Abs(dRx)
                                            : rs.Dof == RotationalDof.Ry ? Math.Abs(dRy)
                                            : Math.Abs(dRz);
                                        mFinal = rs.Dof == RotationalDof.Rx ? Math.Abs(mxi)
                                            : rs.Dof == RotationalDof.Ry ? Math.Abs(myi)
                                            : Math.Abs(mzi);
                                    }

                                    // マーカープロット
                                    if (double.IsFinite(thetaFinal) && double.IsFinite(mFinal) && thetaFinal > 0)
                                    {
                                        var marker = wpfPlot.Plot.Add.Scatter([thetaFinal], new[] { mFinal });
                                        marker.LineStyle.Width = 0;
                                        marker.MarkerSize = 12;
                                        marker.MarkerStyle.Shape = ScottPlot.MarkerShape.FilledCircle;
                                        marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                        marker.LegendText = $"最終:{legend}";

                                        if (isPeakPlot)
                                        {
                                            // ピーク表示: 現在値 (θ_proj, M_proj) もホバーに併記
                                            double dRxH = rsResult.CumulativeDisp.Rxj - rsResult.CumulativeDisp.Rxi;
                                            double dRyH = rsResult.CumulativeDisp.Ryj - rsResult.CumulativeDisp.Ryi;
                                            double thetaProjNow = dRxH * rsResult.CrackNx.Value + dRyH * rsResult.CrackNy.Value;
                                            double mProjNow = mxi * rsResult.CrackNx.Value + myi * rsResult.CrackNy.Value;
                                            _graphHoverMap[marker] =
                                                mthetaDetails + "\n" +
                                                $"ピーク θ_proj_max (n=({rsResult.CrackNx:F3},{rsResult.CrackNy:F3})): {thetaFinal:F6} rad\n" +
                                                $"ピーク M: {mFinal:F1} kN·m\n" +
                                                $"現在 θ_proj: {thetaProjNow:F6} rad / M_proj: {mProjNow:F1} kN·m\n" +
                                                "(post-crack 方向ロック: n 方向ピーク履歴値を表示)";
                                        }
                                        else
                                        {
                                            _graphHoverMap[marker] =
                                                mthetaDetails + "\n" +
                                                $"最終 θ: {thetaFinal:F6} rad\n" +
                                                $"最終 M: {mFinal:F1} kN·m";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, "M-θ関係", "θ (rad)", "M (kN·m)", decimalPlacesX: 3);
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();

            // 剛結杭がある場合は説明文を出力
            // (場所打ち鋼管コンクリート杭+鉄筋定着工法 等は剛結のため M-θ 関係が存在しない)
            if (rigidPileInfos.Count > 0)
            {
                var sortedInfos = rigidPileInfos.OrderBy(s => s, StringComparer.Ordinal);
                GraphInfoMessage =
                    "杭頭が剛結扱いのため M-θ 関係が存在しない杭があります（M-θ 曲線は描画されません）: "
                    + string.Join(", ", sortedInfos);
            }
        }

        // 水平地盤反力度p-y関係描画（理論P-y曲線 + 最終ステップマーカー）
        private void DrawPyCurvesWithMarker(WpfPlot wpfPlot, Crosshair crosshair, string CrosshairPositionText)
        {
            wpfPlot.Plot.Clear();
            _graphHoverMap.Clear();

            var targetPiles = GetSelectedPileLayouts();
            var selectedLoadCases = GetSelectedLoadCases();
            var selectedCombinations = GetSelectedLoadCombinations();
            bool isAllSegments = SelectedPileSegmentOption == "All";
            int singleSegIdx = SelectedPileSegmentNo - 1; // 0-based（All以外）

            // 理論 P-y 曲線は荷重ケースの地盤非線形モードごとに形が変わるため、
            // 選択ケースに含まれるモードの種類だけ曲線を描く（通常は全ケース同一で 1 本）。
            var curveModes = selectedLoadCases.Select(lc => lc.SoilNonlinearityMode).Distinct().ToList();
            if (curveModes.Count == 0) curveModes.Add(SoilNonlinearityMode.KhReductionWithPy);

            double maxMarkerDisp = 0;

            foreach (var pileLayout in targetPiles)
            {
                int altNo = pileLayout.SoilPileAltNo;
                if (altNo <= 0 || altNo > InputModel.ElementDivision.SoilPiles.Count) continue;
                var soilPile = InputModel.ElementDivision.SoilPiles[altNo - 1];
                var reactions = soilPile.HorizontalSoilReactions;
                if (reactions == null || reactions.Count == 0) continue;

                // All: 全区間、それ以外: 選択区間のみ
                var segIndices = isAllSegments
                    ? Enumerable.Range(0, reactions.Count).ToList()
                    : (singleSegIdx >= 0 && singleSegIdx < reactions.Count ? new List<int> { singleSegIdx } : new List<int>());

                bool isFront = pileLayout.IsFrontPiles?.FirstOrDefault() ?? true;

                foreach (int segIdx in segIndices)
                {
                var reaction = reactions[segIdx];

                // 理論P-y曲線（Top/Btm）を描画
                double pyTop = isFront ? reaction.PyFrontTop : reaction.PyRearTop;
                double pyBtm = isFront ? reaction.PyFrontBtm : reaction.PyRearBtm;

                // P-y曲線のサンプリング点（小変位域を細かく、大変位域は粗く）
                var yValues = new List<double>();
                for (double y = 0.0; y < 0.01; y += 0.0001) yValues.Add(y);   // 0-10mm: 0.1mm刻み
                for (double y = 0.01; y < 0.05; y += 0.001) yValues.Add(y);   // 10-50mm: 1mm刻み
                for (double y = 0.05; y < 0.50; y += 0.005) yValues.Add(y);   // 50-500mm: 5mm刻み

                // ホバー詳細文字列（共通部分）
                string pyDetails =
                    $"杭 No: {pileLayout.No} / 要素 #{segIdx + 1}\n" +
                    $"地盤層: {reaction.Name}\n" +
                    $"土質: {reaction.SoilType}\n" +
                    $"標高: {reaction.ZTop:F3} ~ {reaction.ZBtm:F3} m\n" +
                    $"杭径 B: {reaction.B * 1000.0:F0} mm\n" +
                    $"N 値: {reaction.NValue:F1}";

                var xsT = yValues.Select(y => y * 1000.0).ToArray();
                foreach (var curveMode in curveModes)
                {
                    // モードが 1 種類のときは従来通り凡例を簡潔に保つ
                    string modeSuffix = curveModes.Count > 1
                        ? $"|{SoilNonlinearityModes.ToShortText(curveMode)}" : "";

                    // Top曲線
                    var ysT = yValues.Select(y => reaction.GetP(y, pyTop, curveMode)).ToArray();
                    var curveT = wpfPlot.Plot.Add.ScatterLine(xsT, ysT);
                    curveT.LegendText = $"P{pileLayout.No}|Seg{segIdx + 1}|Top{modeSuffix}";
                    _graphHoverMap[curveT] = pyDetails;

                    // Btm曲線（X値は同じ）
                    var ysB = yValues.Select(y => reaction.GetP(y, pyBtm, curveMode)).ToArray();
                    var curveB = wpfPlot.Plot.Add.ScatterLine(xsT, ysB);
                    curveB.LegendText = $"P{pileLayout.No}|Seg{segIdx + 1}|Btm{modeSuffix}";
                    curveB.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                    _graphHoverMap[curveB] = pyDetails;
                }

                // 最終ステップのマーカーを描画（i端・j端）
                // X軸: 解析結果の相対変位、Y軸: 理論P-y曲線上の値（必ず曲線上に乗る）
                var springs = pileLayout.HorizontalSoilSprings;
                if (springs == null) continue;

                // i端 = node segIdx, j端 = node segIdx+1
                var endNodes = new List<(int nodeIdx, string label, double py, ScottPlot.MarkerShape shape)>();
                if (segIdx < springs.Count)
                    endNodes.Add((segIdx, "i端", pyTop, ScottPlot.MarkerShape.FilledCircle));
                if (segIdx + 1 < springs.Count)
                    endNodes.Add((segIdx + 1, "j端", pyBtm, ScottPlot.MarkerShape.FilledSquare));

                foreach (var (nodeIdx, endLabel, py, shape) in endNodes)
                {
                    var spring = springs[nodeIdx];

                    foreach (var loadCase in selectedLoadCases)
                    {
                        foreach (var loadCombination in selectedCombinations)
                        {
                            foreach (var isLiquefaction in SelectedLiquefactionCases)
                            {
                                int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                if (lastStep < 0) continue;

                                var result = spring.HorizontalSpringResults?
                                    .Where(r => r.LoadCase?.LoadName == loadCase.LoadName
                                             && r.LoadCombination?.No == loadCombination.No
                                             && r.IsLiquefaction == isLiquefaction)
                                    .OrderByDescending(r => r.Step)
                                    .FirstOrDefault();

                                if (result?.CumulativeDisp == null) continue;

                                double relDispX = result.CumulativeDisp.Dxi - result.CumulativeDisp.Dxj;
                                double relDispY = result.CumulativeDisp.Dyi - result.CumulativeDisp.Dyj;
                                double relDisp = Math.Sqrt(relDispX * relDispX + relDispY * relDispY);
                                double relDispMm = relDisp * 1000.0;

                                // Y軸は理論値（P-y曲線上の値）
                                double pTheory = reaction.GetP(relDisp, py, loadCase.SoilNonlinearityMode);

                                string legend = $"LC:{loadCase.LoadName}|LIQ:{isLiquefaction}|P{pileLayout.No}|{endLabel}";

                                if (double.IsFinite(relDispMm) && relDispMm > 0 && double.IsFinite(pTheory))
                                {
                                    maxMarkerDisp = Math.Max(maxMarkerDisp, relDispMm);

                                    var marker = wpfPlot.Plot.Add.Scatter(new[] { relDispMm }, new[] { pTheory });
                                    marker.LineStyle.Width = 0;
                                    marker.MarkerSize = 12;
                                    marker.MarkerStyle.Shape = shape;
                                    marker.Color = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
                                    marker.LegendText = $"最終:{legend}";
                                    _graphHoverMap[marker] =
                                        pyDetails + "\n" +
                                        $"LC: {loadCase.LoadName} / Comb: {loadCombination.No} / LIQ: {isLiquefaction}\n" +
                                        $"{endLabel}: 相対変位 {relDispMm:F2} mm, p = {pTheory:F1} kN/m²";
                                }
                            }
                        }
                    }
                }
                } // foreach segIdx
            }

            ConfigurePlot(wpfPlot, crosshair, CrosshairPositionText, "水平地盤反力度p-y関係", "相対変位 (mm)", "反力度 p (kN/m²)");
            // X軸の定義域をマーカー最大値 × 1.5 に設定
            if (maxMarkerDisp > 0)
            {
                wpfPlot.Plot.Axes.SetLimitsX(0, maxMarkerDisp * 1.5);
            }
            wpfPlot.Plot.ShowLegend();
            wpfPlot.Refresh();
        }
    }
}
