using DocumentFormat.OpenXml.Packaging;
using PileDesign.Common;
using PileDesign.Models.InputData;
using ScottPlot;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    // 地盤グラフ (N値分布 / 粘着力 Cu / S波速度 Vs / 変形係数 Es / 液状化安全率 FL) の docx 出力
    // GroundLayerViewModel の Draw*Graph に依存せず、ScottPlot.Plot を直接組み立てて PNG 化する
    internal partial class WordDocument
    {
        private const double GroundGraphWidthMm = 120;
        private const double GroundGraphHeightMm = 150;

        private enum GroundGraphType { NValue, Cu, Vs, Es, FL }

        // エントリポイント: チェックされている地盤グラフを順次出力
        private void AddGroundGraphsSection(MainDocumentPart mainPart, Body body)
        {
            if (inputModel?.GroundsInput == null || mainWindowViewModel == null) return;

            bool any = mainWindowViewModel.DocxOutput.IncludeNValueGraph
                    || mainWindowViewModel.DocxOutput.IncludeCuGraph
                    || mainWindowViewModel.DocxOutput.IncludeVsGraph
                    || mainWindowViewModel.DocxOutput.IncludeEsGraph
                    || mainWindowViewModel.DocxOutput.IncludeFLGraph
                    || mainWindowViewModel.DocxOutput.IncludeGroundDisplacementGraph
                    || mainWindowViewModel.DocxOutput.IncludeResponseSpectrumGraph;
            if (!any) return;

            AddHeader1(body, "地盤グラフ", 1);

            for (int gIdx = 0; gIdx < inputModel.GroundsInput.Count; gIdx++)
            {
                var ground = inputModel.GroundsInput[gIdx];
                if (ground == null) continue;

                // 地盤識別子: 地盤符号 (GroundRef) があれば優先、なければ「地盤 N」
                string groundLabel = !string.IsNullOrWhiteSpace(ground.GroundRef)
                    ? $"地盤 {gIdx + 1}: {ground.GroundRef}"
                    : $"地盤 {gIdx + 1}";

                if (mainWindowViewModel.DocxOutput.IncludeNValueGraph)
                    AddGroundGraph(mainPart, body, ground, GroundGraphType.NValue, $"N値分布 ({groundLabel})", groundLabel);
                if (mainWindowViewModel.DocxOutput.IncludeCuGraph)
                    AddGroundGraph(mainPart, body, ground, GroundGraphType.Cu, $"粘着力 Cu 分布 ({groundLabel})", groundLabel);
                if (mainWindowViewModel.DocxOutput.IncludeVsGraph)
                    AddGroundGraph(mainPart, body, ground, GroundGraphType.Vs, $"S波速度 Vs 分布 ({groundLabel})", groundLabel);
                if (mainWindowViewModel.DocxOutput.IncludeEsGraph)
                    AddGroundGraph(mainPart, body, ground, GroundGraphType.Es, $"変形係数 Es 分布 ({groundLabel})", groundLabel);
                if (mainWindowViewModel.DocxOutput.IncludeFLGraph)
                    AddGroundGraph(mainPart, body, ground, GroundGraphType.FL, $"液状化安全率 FL 分布 ({groundLabel})", groundLabel);
                if (mainWindowViewModel.DocxOutput.IncludeGroundDisplacementGraph)
                    AddGroundDisplacementGraph(mainPart, body, ground, groundLabel);
                if (mainWindowViewModel.DocxOutput.IncludeResponseSpectrumGraph)
                    AddResponseSpectrumGraph(mainPart, body, ground, groundLabel);
            }
        }

        // ---- 任意地盤変位グラフ (DmaxUStar L1/L2) -------------------------------------

        private void AddGroundDisplacementGraph(MainDocumentPart mainPart, Body body, GroundInput ground, string groundLabel)
        {
            try
            {
                if (ground.GroundMassesData == null || ground.GroundMassesData.Count == 0) return;

                // 「考慮しない」モードの場合: グラフは出力しない
                if (ground.IsGroundDisplacementIgnored)
                {
                    Serilog.Log.Information("[GroundDispGraph] {Label}: 地盤変位『考慮しない』モードのためグラフ出力をスキップ", groundLabel);
                    return;
                }

                bool isCustom = ground.CustomDisplacementProfile?.IsEnabled == true;

                // 各 mass の中央深さ
                double[] depths = new double[ground.GroundMassesData.Count];
                for (int i = 0; i < ground.GroundMassesData.Count; i++)
                {
                    var data = ground.GroundMassesData[i];
                    double factor = (i == 0) ? 1.0 : (i == ground.GroundMassesData.Count - 1) ? 0.0 : 0.5;
                    depths[i] = data.GLDepth + data.Spacing * factor;
                }

                var plot = new Plot();
                DrawSoilLayersOnPlot(plot, ground);

                bool hasAny;
                if (isCustom)
                {
                    // 任意入力モード: CustomDisplacementProfile から L1/L2 (液状化あり/なし) を描画
                    hasAny  = AddCustomDispScatter(plot, ground, isLiq: false, level: 0, NikkenSKColor.SkyBlue,   "L1 非液状化");
                    hasAny |= AddCustomDispScatter(plot, ground, isLiq: true,  level: 0, NikkenSKColor.SkyBlue,   "L1 液状化");
                    hasAny |= AddCustomDispScatter(plot, ground, isLiq: false, level: 1, NikkenSKColor.DeepBlue,  "L2 非液状化");
                    hasAny |= AddCustomDispScatter(plot, ground, isLiq: true,  level: 1, NikkenSKColor.DeepBlue,  "L2 液状化");
                }
                else
                {
                    // 自動計算モード: DmaxU* から L1/L2 を描画 (旧挙動)
                    hasAny  = AddDispScatter(plot, ground, depths, 0, NikkenSKColor.SkyBlue, "L1");
                    hasAny |= AddDispScatter(plot, ground, depths, 1, NikkenSKColor.DeepBlue, "L2");
                }
                if (!hasAny) return;

                string title = isCustom
                    ? $"地盤変位 (任意入力) — {groundLabel}"
                    : $"地盤変位 — {groundLabel}";
                plot.Axes.Title.Label.Text = title;
                plot.Axes.Title.Label.FontName = Fonts.Detect(title);
                plot.Axes.Bottom.Label.Text = "地盤変位 (mm)";
                plot.Axes.Bottom.Label.FontName = Fonts.Detect("地盤変位");
                plot.Axes.Left.Label.Text = "GL基準深さ (m)";
                plot.Axes.Left.Label.FontName = Fonts.Detect("GL基準深さ");
                plot.Legend.IsVisible = true;
                plot.Legend.FontName = Fonts.Detect("日本語");
                plot.Axes.AutoScale();
                plot.Axes.AutoScaleExpandY();

                string captionMode = isCustom ? "任意地盤変位 (任意入力)" : "任意地盤変位";
                SavePlotAndAddToBody(mainPart, body, plot, $"{captionMode} ({groundLabel})");
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "地盤変位グラフ ({Label}) の docx 出力中にエラー", groundLabel);
            }
        }

        private static bool AddDispScatter(Plot plot, GroundInput ground, double[] depths, int level, SKColor skColor, string legend)
        {
            if (ground.GroundMassesData == null) return false;
            var xs = new List<double>();
            var ys = new List<double>();
            for (int i = 0; i < ground.GroundMassesData.Count; i++)
            {
                var data = ground.GroundMassesData[i];
                if (data.DmaxUStar == null || data.DmaxUStar.Count <= level) continue;
                double v = data.DmaxUStar[level];
                if (double.IsNaN(v)) continue;
                xs.Add(v);
                ys.Add(depths[i]);
            }
            if (xs.Count == 0) return false;

            var scatter = plot.Add.Scatter(xs.ToArray(), ys.ToArray());
            scatter.Color = ScottPlot.Color.FromSKColor(skColor);
            scatter.LineWidth = 2;
            scatter.LegendText = legend;
            return true;
        }

        // 任意入力モード用: CustomDisplacementProfile から該当ケース (L1/L2 × 液状化あり/なし) のプロファイルを描画。
        // 値は 標高 Z [m] / 変位 [mm] のプロットを GL基準深さ [m] / 変位 [mm] に変換 (深さ = GroundTopAltitude - Z)。
        private static bool AddCustomDispScatter(Plot plot, GroundInput ground, bool isLiq, int level, SKColor skColor, string legend)
        {
            var custom = ground.CustomDisplacementProfile;
            if (custom == null) return false;
            var profile = (level == 0)
                ? (isLiq ? custom.Level1Liq : custom.Level1NonLiq)
                : (isLiq ? custom.Level2Liq : custom.Level2NonLiq);
            if (profile == null || profile.Count == 0) return false;

            double topAlt = ground.GroundTopAltitude;
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var p in profile)
            {
                if (double.IsNaN(p.Displacement)) continue;
                xs.Add(p.Displacement);
                ys.Add(topAlt - p.Z); // 標高 Z → GL 基準深さ
            }
            if (xs.Count == 0) return false;

            var scatter = plot.Add.Scatter(xs.ToArray(), ys.ToArray());
            scatter.Color = ScottPlot.Color.FromSKColor(skColor);
            scatter.LineWidth = 2;
            scatter.LegendText = legend;
            return true;
        }

        // ---- 応答スペクトルグラフ (基盤/地表 × L1/L2) ----------------------------------

        private void AddResponseSpectrumGraph(MainDocumentPart mainPart, Body body, GroundInput ground, string groundLabel)
        {
            Serilog.Log.Warning("[ResponseSpectrum] start ({Label})", groundLabel);
            try
            {
                // 周期サンプリング (0.02〜5s, 対数等間隔 200 点)
                const double tMin = 0.02;
                const double tMax = 5.0;
                const int nPts = 200;
                double logTMin = Math.Log10(tMin);
                double logTMax = Math.Log10(tMax);
                double[] Ts = new double[nPts];
                double[] logTs = new double[nPts];
                for (int i = 0; i < nPts; i++)
                {
                    logTs[i] = logTMin + (logTMax - logTMin) * i / (nPts - 1);
                    Ts[i] = Math.Pow(10, logTs[i]);
                }

                var plot = new Plot();

                // 基盤 L1
                {
                    double[] sa = new double[nPts];
                    for (int i = 0; i < nPts; i++)
                        sa[i] = PileDesign.Services.GroundResponseSpectrumCalc.SaBedrock(Ts[i], 0.2);
                    var s = plot.Add.ScatterLine(logTs, sa);
                    s.Color = ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue);
                    s.LineWidth = 1.5f;
                    s.LineStyle.Pattern = LinePattern.Dashed;
                    s.LegendText = "L1 基盤";
                }
                // 基盤 L2
                {
                    double[] sa = new double[nPts];
                    for (int i = 0; i < nPts; i++)
                        sa[i] = PileDesign.Services.GroundResponseSpectrumCalc.SaBedrock(Ts[i], 1.0);
                    var s = plot.Add.ScatterLine(logTs, sa);
                    s.Color = ScottPlot.Color.FromSKColor(NikkenSKColor.DeepBlue);
                    s.LineWidth = 1.5f;
                    s.LineStyle.Pattern = LinePattern.Dashed;
                    s.LegendText = "L2 基盤";
                }

                // 地表 (Gs1 > 0 の場合のみ)
                bool hasSurface = ground.Gs1Levels != null && ground.Gs1Levels.Count >= 2
                    && (ground.Gs1Levels[0] > 0 || ground.Gs1Levels[1] > 0);
                if (hasSurface && ground.Gs2Levels != null && ground.NaturalPeriods != null && ground.T2Levels != null)
                {
                    for (int level = 0; level < 2; level++)
                    {
                        if (level >= ground.Gs1Levels.Count) break;
                        double gs1 = ground.Gs1Levels[level];
                        if (gs1 <= 0) continue;
                        double gs2 = ground.Gs2Levels[level];
                        double t1 = ground.NaturalPeriods[level];
                        double t2 = ground.T2Levels[level];
                        double s0 = level == 0 ? 0.2 : 1.0;
                        double[] sa = new double[nPts];
                        for (int i = 0; i < nPts; i++)
                            sa[i] = PileDesign.Services.GroundResponseSpectrumCalc.SaSurface(Ts[i], t1, t2, gs1, gs2, s0);
                        var s = plot.Add.ScatterLine(logTs, sa);
                        s.Color = ScottPlot.Color.FromSKColor(level == 0 ? NikkenSKColor.SkyBlue : NikkenSKColor.DeepBlue);
                        s.LineWidth = 2.5f;
                        s.LegendText = $"L{level + 1} 地表 (Gs1={gs1:F2}, T1={t1:F2}s)";
                    }
                }

                string title = $"加速度応答スペクトル (h=0.05) — {groundLabel}";
                plot.Axes.Title.Label.Text = title;
                plot.Axes.Title.Label.FontName = Fonts.Detect(title);
                plot.Axes.Bottom.Label.Text = "周期 T (s)";
                plot.Axes.Bottom.Label.FontName = Fonts.Detect("周期");
                plot.Axes.Left.Label.Text = "Sa (m/s²)";
                plot.Axes.Left.Label.FontName = Fonts.Detect("Sa");

                // X 軸を log10(T) で表示
                var minorTickGen = new ScottPlot.TickGenerators.LogMinorTickGenerator();
                var tickGen = new ScottPlot.TickGenerators.NumericAutomatic
                {
                    MinorTickGenerator = minorTickGen,
                    IntegerTicksOnly = true,
                    LabelFormatter = (y) =>
                    {
                        double t = Math.Pow(10, y);
                        if (t >= 1) return t.ToString("0.##");
                        if (t >= 0.1) return t.ToString("0.0");
                        return t.ToString("0.00");
                    }
                };
                plot.Axes.Bottom.TickGenerator = tickGen;
                plot.ShowLegend();
                plot.Legend.FontName = Fonts.Detect("凡例");
                plot.Axes.AutoScale();
                plot.Axes.Bottom.Min = logTMin;
                plot.Axes.Bottom.Max = logTMax;
                plot.Axes.Left.Min = 0.0;

                SavePlotAndAddToBody(mainPart, body, plot, $"応答スペクトル ({groundLabel})");
                Serilog.Log.Warning("[ResponseSpectrum] success ({Label}, hasSurface={HS})", groundLabel, hasSurface);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[ResponseSpectrum] FAIL ({Label}): {Message}", groundLabel, ex.Message);
            }
        }

        // ---- Plot → PNG → docx 共通ヘルパ ---------------------------------------------

        private void SavePlotAndAddToBody(MainDocumentPart mainPart, Body body, Plot plot, string caption)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            int pxW = MmToPx(GroundGraphWidthMm, Dpi, Layout.HiResScale);
            int pxH = MmToPx(GroundGraphHeightMm, Dpi, Layout.HiResScale);
            plot.SavePng(tempFile, pxW, pxH);
            WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, GroundGraphWidthMm, GroundGraphHeightMm);
            try { File.Delete(tempFile); } catch { }
            AddAutoFigureCaption(body, caption, "図");
        }

        // 単一の地盤グラフを Plot で組み立てて docx に追加
        private void AddGroundGraph(MainDocumentPart mainPart, Body body,
            GroundInput ground, GroundGraphType type, string caption, string groundLabel)
        {
            try
            {
                var plot = new Plot();
                DrawSoilLayersOnPlot(plot, ground);

                (string baseTitle, string xLabel, double? xMin, double? xMax) = type switch
                {
                    GroundGraphType.NValue => ("N値分布", "N値", (double?)0, (double?)60),
                    GroundGraphType.Cu     => ("粘着力分布", "粘着力 Cu (kN/m²)", (double?)0, null),
                    GroundGraphType.Vs     => ("S波速度分布", "Vs (m/s)", (double?)0, null),
                    GroundGraphType.Es     => ("変形係数分布", "Es (kN/m²)", (double?)0, null),
                    GroundGraphType.FL     => ("液状化安全率 FL", "FL", (double?)0, (double?)1),
                    _                      => ("", "", null, null)
                };
                // タイトルに地盤ラベルを併記
                string title = $"{baseTitle} — {groundLabel}";

                bool hasData = type switch
                {
                    GroundGraphType.NValue => AddMassPointDataToPlot(plot, ground, m => m.NValue),
                    GroundGraphType.Cu     => AddSteppedLayerDataToPlot(plot, ground, l => l.Cohesive),
                    GroundGraphType.Vs     => AddSteppedLayerDataToPlot(plot, ground, l => l.Vs),
                    GroundGraphType.Es     => AddSteppedLayerDataToPlot(plot, ground, l => l.Es),
                    GroundGraphType.FL     => AddFLDataToPlot(plot, ground),
                    _                      => false
                };

                if (!hasData) return;

                plot.Axes.Title.Label.Text = title;
                plot.Axes.Title.Label.FontName = Fonts.Detect(title);
                plot.Axes.Bottom.Label.Text = xLabel;
                plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);
                plot.Axes.Left.Label.Text = "GL基準深さ (m)";
                plot.Axes.Left.Label.FontName = Fonts.Detect("GL基準深さ");
                plot.Axes.AutoScale();
                plot.Axes.AutoScaleExpandY();
                if (xMin.HasValue) plot.Axes.Bottom.Min = xMin.Value;
                if (xMax.HasValue) plot.Axes.Bottom.Max = xMax.Value;

                string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                int pxW = MmToPx(GroundGraphWidthMm, Dpi, Layout.HiResScale);
                int pxH = MmToPx(GroundGraphHeightMm, Dpi, Layout.HiResScale);
                plot.SavePng(tempFile, pxW, pxH);
                WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, GroundGraphWidthMm, GroundGraphHeightMm);
                try { File.Delete(tempFile); } catch { }

                AddAutoFigureCaption(body, caption, "図");
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "地盤グラフ ({Type}) の docx 出力中にエラー", type);
            }
        }

        // ScottPlot.Plot に土層背景・水位線を描画 (GroundLayerViewModel.DrawSoilLayer の docx 用簡易版)
        private static void DrawSoilLayersOnPlot(Plot plot, GroundInput ground)
        {
            if (ground.GroundLayers == null || ground.GroundLayers.Count == 0) return;

            var grayColor = new ScottPlot.Color(128, 128, 128, 255);
            plot.Add.HorizontalLine(0, 2, grayColor, LinePattern.Solid);

            for (int i = 0; i < ground.GroundLayers.Count; i++)
            {
                var layer = ground.GroundLayers[i];
                double y1 = i == 0 ? 0 : ground.GroundLayers[i - 1].BottomGLDepth;
                double y2 = layer.BottomGLDepth;
                ScottPlot.Color fillColor = layer.GranularityClass switch
                {
                    "粘性土" => new ScottPlot.Color(210, 180, 140, 64),
                    "砂質土" => new ScottPlot.Color(255, 165, 0, 64),
                    "礫質土" => new ScottPlot.Color(144, 238, 144, 64),
                    _        => new ScottPlot.Color(200, 200, 200, 32)
                };
                plot.Add.VerticalSpan(y1, y2, fillColor);
                // 層境界線
                var yellowColor = new ScottPlot.Color(247, 181, 21, 255);
                plot.Add.HorizontalLine(layer.BottomGLDepth, 1, yellowColor, LinePattern.Solid);
            }

            // X=0 基準縦線
            var blackColor = new ScottPlot.Color(0, 0, 0, 255);
            plot.Add.VerticalLine(0, 1, blackColor);

            // 地下水位 (Nikken DeepBlue #3271AD + ▽GWT ラベル)
            double gwDepth = ground.GroundWaterGLDepth;
            var waterColor = new ScottPlot.Color(0x32, 0x71, 0xAD, 255);
            plot.Add.HorizontalLine(gwDepth, 2, waterColor, LinePattern.Solid);

            double yMin = ground.GroundLayers.Min(l => l.BottomGLDepth);
            double range = Math.Abs(yMin);
            double lineGap = range > 0 ? range / 100.0 : 0.12;
            byte[] alphas = [200, 130, 70];
            for (int i = 0; i < alphas.Length; i++)
            {
                double y = gwDepth - (i + 1) * lineGap;
                var transColor = new ScottPlot.Color(waterColor.Red, waterColor.Green, waterColor.Blue, alphas[i]);
                plot.Add.HorizontalLine(y, 1, transColor, LinePattern.Solid);
            }

            const string gwtText = "▽GWT";
            var gwtLabel = plot.Add.Text(gwtText, new Coordinates(0, gwDepth));
            gwtLabel.LabelFontColor = waterColor;
            gwtLabel.LabelFontSize = 10;
            gwtLabel.LabelBold = true;
            gwtLabel.LabelFontName = Fonts.Detect(gwtText);
            gwtLabel.LabelAlignment = Alignment.LowerLeft;
        }

        // 土質点ごとのデータ (N値) を Scatter として追加
        private static bool AddMassPointDataToPlot(Plot plot, GroundInput ground, Func<GroundMassDataInput, double> selector)
        {
            if (ground.GroundMassesData == null || ground.GroundMassesData.Count == 0) return false;

            double[] xs = ground.GroundMassesData.Select(m => selector(m)).ToArray();
            double[] ys = ground.GroundMassesData.Select(m => m.GLDepth).ToArray();

            var scatter = plot.Add.Scatter(xs, ys);
            scatter.Color = ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;

            double xMax = xs.Length > 0 ? Math.Max(xs.Max(), 1.0) : 1.0;
            for (int i = 0; i < xs.Length; i++)
                PlotHelper.AddText(plot, $"{xs[i]:N0}", xs[i], ys[i], xMax);

            return true;
        }

        // 土層ごとの階段状データ (Cu / Vs / Es) を追加
        private static bool AddSteppedLayerDataToPlot(Plot plot, GroundInput ground, Func<GroundLayerInput, double> selector)
        {
            if (ground.GroundLayers == null || ground.GroundLayers.Count == 0) return false;

            // 階段状: 各層の上端→下端で同値、層境界で値ジャンプ
            var xList = new List<double>();
            var yList = new List<double>();
            for (int i = 0; i < ground.GroundLayers.Count; i++)
            {
                double v = selector(ground.GroundLayers[i]);
                double yTop = i == 0 ? 0 : ground.GroundLayers[i - 1].BottomGLDepth;
                double yBtm = ground.GroundLayers[i].BottomGLDepth;
                xList.Add(v); yList.Add(yTop);
                xList.Add(v); yList.Add(yBtm);
            }

            double[] xs = [.. xList];
            double[] ys = [.. yList];
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.Color = ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;

            double xMax = xs.Length > 0 ? Math.Max(xs.Max(), 1.0) : 1.0;
            for (int i = 0; i < ground.GroundLayers.Count; i++)
            {
                double v = selector(ground.GroundLayers[i]);
                double yMid = ((i == 0 ? 0 : ground.GroundLayers[i - 1].BottomGLDepth)
                              + ground.GroundLayers[i].BottomGLDepth) * 0.5;
                PlotHelper.AddText(plot, $"{v:N0}", v, yMid, xMax);
            }

            return true;
        }

        // FL データ (Level1 / Level2) を Scatter で追加。null の地点は系列を分割
        private static bool AddFLDataToPlot(Plot plot, GroundInput ground)
        {
            if (ground.GroundMassesData == null || ground.GroundMassesData.Count == 0) return false;

            // 各 mass の中央深さ
            double[] depths = new double[ground.GroundMassesData.Count];
            for (int i = 0; i < ground.GroundMassesData.Count; i++)
            {
                var data = ground.GroundMassesData[i];
                double factor = (i == 0) ? 1.0 : (i == ground.GroundMassesData.Count - 1) ? 0.0 : 0.5;
                depths[i] = data.GLDepth + data.Spacing * factor;
            }

            bool hasAny = false;
            for (int level = 0; level < 2; level++)
            {
                var skColor = level == 0 ? NikkenSKColor.SkyBlue : NikkenSKColor.DeepBlue;
                hasAny |= AddFLScatterForLevel(plot, ground, depths, level, skColor);
            }
            return hasAny;
        }

        private static bool AddFLScatterForLevel(Plot plot, GroundInput ground, double[] depths, int levelIndex, SKColor skColor)
        {
            // null の地点で系列を分割
            var fl1ss = new List<List<double>>();
            var fl1s = new List<double>();
            var dep1ss = new List<List<double>>();
            var dep1s = new List<double>();

            for (int i = 0; i < ground.GroundMassesData.Count; i++)
            {
                var fl = ground.GroundMassesData[i].FL;
                double? v = (fl != null && fl.Count > levelIndex) ? fl[levelIndex] : null;
                if (v == null)
                {
                    if (fl1s.Count > 0) { fl1ss.Add(fl1s); dep1ss.Add(dep1s); fl1s = []; dep1s = []; }
                }
                else
                {
                    fl1s.Add(v.Value);
                    dep1s.Add(depths[i]);
                }
            }
            if (fl1s.Count > 0) { fl1ss.Add(fl1s); dep1ss.Add(dep1s); }

            bool any = false;
            for (int seg = 0; seg < fl1ss.Count; seg++)
            {
                if (fl1ss[seg].Count == 0) continue;
                var xs = fl1ss[seg].ToArray();
                var ys = dep1ss[seg].ToArray();
                var scatter = plot.Add.Scatter(xs, ys);
                scatter.Color = ScottPlot.Color.FromSKColor(skColor);
                scatter.LineWidth = 2;
                double xMax = xs.Length > 0 ? Math.Max(xs.Max(), 1.0) : 1.0;
                for (int j = 0; j < xs.Length; j++)
                    PlotHelper.AddText(plot, $"{xs[j]:N2}", xs[j], ys[j], xMax);
                any = true;
            }
            return any;
        }
    }
}
