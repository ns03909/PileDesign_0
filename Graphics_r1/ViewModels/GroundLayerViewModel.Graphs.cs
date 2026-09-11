#nullable enable
using AvalonDock.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PileDesign.Services;
//using System.Windows.Media;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// GroundLayerViewModel — 地盤ウィンドウのグラフ描画。
    ///
    /// N 値・Cu・Vs・Es・変位・FL・応答スペクトルの各グラフと、
    /// 背景に敷く土層の色分けを描く。マウスを乗せたときの十字線と読み値もここ。
    ///
    /// <b>十字線は静的に持っている。</b> ScottPlot のプロットに 1 つずつ紐づくので、
    /// ウィンドウを開き直しても同じものを使う。ここを実体ごとに持つと、
    /// 開き直したあとに古いプロットを指したまま動かなくなる。
    ///
    /// <b>マウス移動の購読は 1 回だけ。</b> <c>_hooked...MouseMove</c> で見張っている。
    /// 描き直すたびに購読すると、同じ位置で何本も十字線が動く。
    ///
    /// 以前は <c>GroundLayerViewModel.cs</c> (3,508 行) の中にあった。
    /// </summary>
    public partial class GroundLayerViewModel
    {
        public static Crosshair? MyCrosshair_NValue { get; private set; }

        private string _crosshairPositionText_NValue;
        public string CrosshairPositionText_NValue
        {
            get => _crosshairPositionText_NValue;
            set => SetProperty(ref _crosshairPositionText_NValue, value);
        }

        public static Crosshair? MyCrosshair_Cu { get; private set; }

        private string _crosshairPositionText_Cu;
        public string CrosshairPositionText_Cu
        {
            get => _crosshairPositionText_Cu;
            set => SetProperty(ref _crosshairPositionText_Cu, value);
        }

        public static Crosshair? MyCrosshair_Vs { get; private set; }

        private string _crosshairPositionText_Vs;
        public string CrosshairPositionText_Vs
        {
            get => _crosshairPositionText_Vs;
            set => SetProperty(ref _crosshairPositionText_Vs, value);
        }

        public static Crosshair? MyCrosshair_Es { get; private set; }

        private string _crosshairPositionText_Es;
        public string CrosshairPositionText_Es
        {
            get => _crosshairPositionText_Es;
            set => SetProperty(ref _crosshairPositionText_Es, value);
        }

        public static Crosshair? MyCrosshair_Disp { get; private set; }

        private string _crosshairPositionText_Disp;
        public string CrosshairPositionText_Disp
        {
            get => _crosshairPositionText_Disp;
            set => SetProperty(ref _crosshairPositionText_Disp, value);
        }

        public static Crosshair? MyCrosshair_FL { get; private set; }

        private string _crosshairPositionText_FL;
        public string CrosshairPositionText_FL
        {
            get => _crosshairPositionText_FL;
            set => SetProperty(ref _crosshairPositionText_FL, value);
        }

        public static Crosshair? MyCrosshair_Sa { get; private set; }

        private string _crosshairPositionText_Sa;
        public string CrosshairPositionText_Sa
        {
            get => _crosshairPositionText_Sa;
            set => SetProperty(ref _crosshairPositionText_Sa, value);
        }

        private bool _hookedDispMouseMove, _hookedFLMouseMove, _hookedNMouseMove;

        private void DrawGroundDisplacementGraph()
        {
            if (GroundWindowInstance == null)
            { return; }
            if (GroundInput?.GroundMassesData == null) return;

            // 土質点の変位は層の上端 (地表から層厚 H を積んだ位置) に描く。解析と同じ (GroundInput.MassTopAltitudes)
            double topAltitude = GroundInput.GroundTopAltitude;
            List<double> gLDepths = [.. GroundInput.MassTopAltitudes().Select(a => a - topAltitude)];

            var wpf = GroundWindowInstance.wpfPlotDisplacement;

            if (wpf?.Plot == null) return;
            wpf.Plot.Clear();
            DrawSoilLayer(wpf);

            // 任意入力モードの場合はカスタムプロファイルを描画
            var custom = GroundInput.CustomDisplacementProfile;
            if (custom != null && custom.IsEnabled)
            {
                var caseProfiles = new (ObservableCollection<DisplacementPoint> profile, string label, SKColor color)[]
                {
                    (custom.Level1NonLiq, "L1 非液状化", NikkenSKColor.Green),
                    (custom.Level1Liq, "L1 液状化", NikkenSKColor.SkyBlue),
                    (custom.Level2NonLiq, "L2 非液状化", NikkenSKColor.PaleRed),
                    (custom.Level2Liq, "L2 液状化", NikkenSKColor.LineOrange),
                };

                foreach (var (profile, label, color) in caseProfiles)
                {
                    if (profile == null || profile.Count < 2) continue;

                    var disps = profile.Select(p => p.Displacement).ToArray();
                    var depths = profile.Select(p => GroundInput.ToGLDepth(p.Z)).ToArray();

                    var scatter = wpf.Plot.Add.Scatter(disps, depths);
                    scatter.Color = Color.FromSKColor(color);
                    scatter.LineWidth = 2;
                    scatter.LegendText = label;
                    scatter.MarkerSize = 5;
                }
            }
            else if (GroundInput.IsGroundDisplacementIgnored)
            {
                // 「考慮しない」モード: 地盤変位は全層 0 として扱うため、グラフには何も描画しない
                // (空のグラフを表示。凡例も非表示)
            }
            else if (GroundInput.GroundLayers.Count != 0)
            {
                if (ChartDispContent.Contains("(レベル1)") || ChartDispContent.Contains("(レベル1, 2)"))
                {
                    List<double> dMaxU1s = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU1s.Add(data.DmaxUStar[0]);
                    }
                    //if (dMaxU1s.Any(double.IsNaN))
                    //{ hasData=false; }
                    /*else */
                    if (dMaxU1s.Any(double.IsNaN) == false && dMaxU1s.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter([.. dMaxU1s], gLDepths.ToArray());
                        scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                        scatter.LineWidth = 2;

                        double xMax1 = dMaxU1s.Max();
                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            PlotHelper.AddText(wpf.Plot, $"{dMaxU1s[i]:N1}", dMaxU1s[i], gLDepths[i], xMax1);
                        }
                    }
                }
                if (ChartDispContent.Contains("(レベル2)") || ChartDispContent.Contains("(レベル1, 2)"))
                {
                    List<double> dMaxU2s = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU2s.Add(data.DmaxUStar[1]);
                    }

                    if (dMaxU2s.Any(double.IsNaN) == false &&
                        dMaxU2s.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter([.. dMaxU2s], gLDepths.ToArray());
                        scatter.Color = Color.FromSKColor(NikkenSKColor.DeepBlue);
                        scatter.LineWidth = 2;

                        double xMax2 = dMaxU2s.Max();
                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            PlotHelper.AddText(wpf.Plot, $"{dMaxU2s[i]:N1}", dMaxU2s[i], gLDepths[i], xMax2);
                        }
                    }
                }
                if (ChartDispContent.Contains("∑γcyH") && (ChartDispContent.Contains("(レベル1)") || ChartDispContent.Contains("(レベル1, 2)")))
                {
                    List<double> dMaxU1Pluss = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU1Pluss.Add(data.DmaxUStarSigmaGammaCyH[0]);
                    }

                    if (dMaxU1Pluss.Any(double.IsNaN) == false &&
                        dMaxU1Pluss.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter([.. dMaxU1Pluss], gLDepths.ToArray());
                        scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                        scatter.LineWidth = 2;

                        double xMax1p = dMaxU1Pluss.Max();
                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            PlotHelper.AddText(wpf.Plot, $"{dMaxU1Pluss[i]:N1}", dMaxU1Pluss[i], gLDepths[i], xMax1p);
                        }
                    }
                }
                if (ChartDispContent.Contains("∑γcyH") && (ChartDispContent.Contains("(レベル2)") || ChartDispContent.Contains("(レベル1, 2)")))
                {
                    List<double> dMaxU2Pluss = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU2Pluss.Add(data.DmaxUStarSigmaGammaCyH[1]);
                    }

                    if (dMaxU2Pluss.Any(double.IsNaN) == false &&
                        dMaxU2Pluss.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter(dMaxU2Pluss.ToArray(), [.. gLDepths]);
                        scatter.Color = Color.FromSKColor(NikkenSKColor.DeepBlue);
                        scatter.LineWidth = 2;

                        double xMax2p = dMaxU2Pluss.Max();
                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            PlotHelper.AddText(wpf.Plot, $"{dMaxU2Pluss[i]:N1}", dMaxU2Pluss[i], gLDepths[i], xMax2p);
                        }
                    }
                }
            }
            wpf.Plot.Legend.IsVisible = true;
            wpf.Plot.Legend.FontName = Fonts.Detect("日本語");

            string title = "地盤変位";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "地盤変位 (mm)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "GL基準深さ(m)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            // クロスヘアの初期化

            MyCrosshair_Disp ??= PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedDispMouseMove)
            {
                wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Disp", "変位(mm)", "GL基準深さ(m)", 1, 3);
                _hookedDispMouseMove = true;
            }

            wpf.Refresh();
        }

        private void DrawFLScatter(List<double> gLDepths, int index, WpfPlot wpf, SKColor skColor)
        {
            if (GroundInput?.GroundMassesData == null) return;
            List<List<double>> fL1ss = [];
            List<double> fL1s = [];
            List<List<double>> gLDepth1ss = [];
            List<double> gLDepth1s = [];

            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                if (GroundInput.GroundMassesData[i].FL[index] == null)
                {
                    fL1ss.Add(fL1s);
                    fL1s = [];
                    gLDepth1ss.Add(gLDepth1s);
                    gLDepth1s = [];
                }
                else
                {
                    fL1s.Add(GroundInput.GroundMassesData[i].FL[index].GetValueOrDefault());
                    gLDepth1s.Add(gLDepths[i]);
                }
            }

            if (fL1s.Count > 0)
            {
                fL1ss.Add(fL1s);
                gLDepth1ss.Add(gLDepth1s);
            }

            for (int i = 0; i < gLDepth1ss.Count; i++)
            {
                var scatter = wpf.Plot.Add.Scatter(fL1ss[i].ToArray(), [.. gLDepth1ss[i]]);
                scatter.Color = Color.FromSKColor(skColor);
                scatter.LineWidth = 2;
                double xMaxFL = fL1ss[i].Count > 0 ? fL1ss[i].Max() : 1.0;
                for (int j = 0; j < gLDepth1ss[i].Count; j++)
                {
                    PlotHelper.AddText(wpf.Plot, $"{fL1ss[i][j]:N2}", fL1ss[i][j], gLDepth1ss[i][j], xMaxFL);
                }
            }
        }

        private void DrawFLGraph()
        {
            if (GroundWindowInstance == null) return;
            if (GroundInput?.GroundMassesData == null) return;

            List<double> gLDepths = [];
            foreach (var data in GroundInput.GroundMassesData)
            {
                double _factor = data == GroundInput.GroundMassesData.First() ? 1.0 :
                                 data == GroundInput.GroundMassesData.Last() ? 0.0 : 0.5;
                double gLDepth = data.GLDepth + data.Spacing * _factor;
                gLDepths.Add(gLDepth);
            }

            var wpf = GroundWindowInstance.wpfPlotFL;
            wpf.Plot.Clear();
            DrawSoilLayer(wpf);

            if (ChartFLContent.Contains("FL"))
            {
                if (ChartFLContent.Contains("FL(レベル1)") || ChartFLContent.Contains("FL(レベル1,2)"))
                    DrawFLScatter(gLDepths, 0, wpf, NikkenSKColor.SkyBlue);
                if (ChartFLContent.Contains("FL(レベル2)") || ChartFLContent.Contains("FL(レベル1,2)"))
                    DrawFLScatter(gLDepths, 1, wpf, NikkenSKColor.DeepBlue);
            }

            string title = "液状化安全率 FL値分布";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "FL値";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "GL基準深さ(m)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandY();
            wpf.Plot.Axes.Bottom.Min = 0.0;
            wpf.Plot.Axes.Bottom.Max = 1.0;

            MyCrosshair_FL ??= PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedFLMouseMove)
            {
                wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_FL", "FL", "GL基準深さ(m)", 1, 3);
                _hookedFLMouseMove = true;
            }

            wpf.Refresh();
        }

        // 応答スペクトル法のための加速度応答スペクトル描画メソッド
        // 基盤 Sa_b(T) = L·Sa0(T) と地表 Sa_s(T) = Gs(T)·L·Sa0(T) を Level1/Level2 ずつ表示
        private bool _hookedSaMouseMove;
        private void DrawResponseSpectrumGraph()
        {
            if (GroundWindowInstance == null) return;
            if (GroundInput == null) return;

            var wpf = GroundWindowInstance.wpfPlotResponseSpectrum;
            if (wpf == null) return;
            wpf.Plot.Clear();

            // 周期サンプリング (0.02〜5s, 対数等間隔 200 点) — X 軸を log10 表示するため
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

            // 算定法が応答スペクトル法以外のときは Gs1=Gs2=0 のため地表は描けない。
            // その場合は基盤 Sa0 のみ表示。
            bool hasSurface = GroundInput.Gs1Levels[0] > 0 || GroundInput.Gs1Levels[1] > 0;

            // Level 1 基盤 (薄水色)
            {
                double[] sa = new double[nPts];
                for (int i = 0; i < nPts; i++) sa[i] = PileDesign.Services.GroundResponseSpectrumCalc.SaBedrock(Ts[i], 0.2);
                var s = wpf.Plot.Add.ScatterLine(logTs, sa);
                s.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                s.LineWidth = 1.5f;
                s.LineStyle.Pattern = LinePattern.Dashed;
                s.LegendText = "L1 基盤";
            }
            // Level 2 基盤 (濃水色)
            {
                double[] sa = new double[nPts];
                for (int i = 0; i < nPts; i++) sa[i] = PileDesign.Services.GroundResponseSpectrumCalc.SaBedrock(Ts[i], 1.0);
                var s = wpf.Plot.Add.ScatterLine(logTs, sa);
                s.Color = Color.FromSKColor(NikkenSKColor.DeepBlue);
                s.LineWidth = 1.5f;
                s.LineStyle.Pattern = LinePattern.Dashed;
                s.LegendText = "L2 基盤";
            }

            if (hasSurface)
            {
                // Level 1 地表
                {
                    double gs1 = GroundInput.Gs1Levels[0];
                    double gs2 = GroundInput.Gs2Levels[0];
                    double t1 = GroundInput.NaturalPeriods[0];
                    double t2 = GroundInput.T2Levels[0];
                    double[] sa = new double[nPts];
                    for (int i = 0; i < nPts; i++) sa[i] = PileDesign.Services.GroundResponseSpectrumCalc.SaSurface(Ts[i], t1, t2, gs1, gs2, 0.2);
                    var s = wpf.Plot.Add.ScatterLine(logTs, sa);
                    s.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                    s.LineWidth = 2.5f;
                    s.LegendText = $"L1 地表 (Gs1={gs1:F2}, Gs2={gs2:F2}, T1={t1:F2}s)";
                }
                // Level 2 地表
                {
                    double gs1 = GroundInput.Gs1Levels[1];
                    double gs2 = GroundInput.Gs2Levels[1];
                    double t1 = GroundInput.NaturalPeriods[1];
                    double t2 = GroundInput.T2Levels[1];
                    double[] sa = new double[nPts];
                    for (int i = 0; i < nPts; i++) sa[i] = PileDesign.Services.GroundResponseSpectrumCalc.SaSurface(Ts[i], t1, t2, gs1, gs2, 1.0);
                    var s = wpf.Plot.Add.ScatterLine(logTs, sa);
                    s.Color = Color.FromSKColor(NikkenSKColor.DeepBlue);
                    s.LineWidth = 2.5f;
                    s.LegendText = $"L2 地表 (Gs1={gs1:F2}, Gs2={gs2:F2}, T1={t1:F2}s)";
                }
            }

            string title = "加速度応答スペクトル (h=0.05)";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "周期 T (s)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "Sₐ (m/s²)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            // X 軸を log10 表示: 値は log10(T) でプロットし、目盛は実周期で表示
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
            wpf.Plot.Axes.Bottom.TickGenerator = tickGen;

            wpf.Plot.ShowLegend();
            wpf.Plot.Legend.FontName = Fonts.Detect("凡例");
            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.Bottom.Min = logTMin;
            wpf.Plot.Axes.Bottom.Max = logTMax;
            wpf.Plot.Axes.Left.Min = 0.0;

            MyCrosshair_Sa ??= PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedSaMouseMove)
            {
                // X 軸が log10(T) なので、Crosshair 表示用に実 T へ逆変換するインライン版を使用
                wpf.MouseMove += (s, e) => HandleResponseSpectrumMouseMove(s, e);
                _hookedSaMouseMove = true;
            }

            wpf.Refresh();
        }

        private void HandleResponseSpectrumMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is not ScottPlot.WPF.WpfPlot wpfPlot) return;

            var scatters = wpfPlot.Plot.GetPlottables().OfType<ScottPlot.Plottables.Scatter>().ToList();
            if (scatters.Count == 0) return;

            var p = e.GetPosition(wpfPlot);
            var mousePixel = new ScottPlot.Pixel(p.X * wpfPlot.DisplayScale, p.Y * wpfPlot.DisplayScale);
            var mouseLocation = wpfPlot.Plot.GetCoordinates(mousePixel);

            double minDist = double.MaxValue;
            ScottPlot.DataPoint? nearest = null;
            ScottPlot.Plottables.Scatter? nearestScatter = null;
            foreach (var sc in scatters)
            {
                var pt = sc.Data.GetNearest(mouseLocation, wpfPlot.Plot.LastRender);
                double dx = pt.Coordinates.X - mouseLocation.X;
                double dy = pt.Coordinates.Y - mouseLocation.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (pt.IsReal && dist < minDist)
                {
                    minDist = dist;
                    nearest = pt;
                    nearestScatter = sc;
                }
            }

            if (wpfPlot.Plot.GetPlottables().OfType<ScottPlot.Plottables.Crosshair>().FirstOrDefault() is ScottPlot.Plottables.Crosshair crosshair)
            {
                if (nearest is { IsReal: true })
                {
                    crosshair.IsVisible = true;
                    crosshair.Position = nearest.Value.Coordinates;
                    wpfPlot.Refresh();
                    // X 軸は log10(T) なので 10^x で実周期を求める
                    double tActual = Math.Pow(10, nearest.Value.Coordinates.X);
                    string legend = nearestScatter?.LegendText ?? "";
                    CrosshairPositionText_Sa = $"{legend} || T(s)={tActual:F3}, Sa(m/s²)={nearest.Value.Coordinates.Y:F3}";
                }
                else if (crosshair.IsVisible)
                {
                    crosshair.IsVisible = false;
                    wpfPlot.Refresh();
                }
            }
        }

        // N値グラフ描画メソッド
        // IsLayerNValueGraphVisible: 土層の平均N値（階段グラフ + 矩形）
        // IsMassPointNValueGraphVisible: 土質点のN値（折れ線 + 値ラベル）
        private void DrawNValueGraph()
        {
            if (GroundWindowInstance == null) return;
            if (GroundInput == null) return;

            var wpfNValue = GroundWindowInstance.wpfPlotNValue;
            wpfNValue.Plot.Clear();
            DrawSoilLayer(wpfNValue);

            double xMaxN = 60.0; // 値ラベル配置の上限基準

            // 土層の平均N値（階段グラフ + 半透明矩形）
            if (IsLayerNValueGraphVisible && GroundInput.GroundLayers != null && GroundInput.GroundLayers.Count > 0)
            {
                var layerNs = GroundInput.GroundLayers.Select(l => l.NValue).ToList();
                var layerBottomDepths = GroundInput.GroundLayers.Select(l => l.BottomGLDepth).ToList();
                (var steppedX, var steppedY) = GetSteppedData(layerNs, layerBottomDepths);
                if (steppedX.Count > 0)
                {
                    var layerScatter = wpfNValue.Plot.Add.Scatter(steppedX.ToArray(), steppedY.ToArray());
                    layerScatter.Color = Color.FromSKColor(NikkenSKColor.LineOrange);
                    layerScatter.LineWidth = 2;
                    foreach (var coord in GetRectangleGeometry(layerNs, layerBottomDepths))
                    {
                        var rect = wpfNValue.Plot.Add.Rectangle(coord);
                        rect.FillColor = new(255, 165, 0, 48); // 半透明オレンジ
                        rect.LineColor = Color.FromSKColor(NikkenSKColor.LineOrange);
                        rect.LineWidth = 1;
                    }
                    xMaxN = Math.Max(xMaxN, steppedX.Max());
                }
            }

            // 土質点のN値（折れ線 + 値ラベル）
            if (IsMassPointNValueGraphVisible && GroundInput.GroundMassesData != null && GroundInput.GroundMassesData.Count > 0)
            {
                List<double> ns = [];
                List<double> depths = [];
                for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
                {
                    ns.Add(GroundInput.GroundMassesData[i].NValue);
                    depths.Add(GroundInput.GroundMassesData[i].GLDepth);
                }
                var scatter = wpfNValue.Plot.Add.Scatter(ns, depths);
                scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                scatter.LineWidth = 2;
                if (ns.Count > 0) xMaxN = Math.Max(xMaxN, ns.Max());
                for (int i = 0; i < depths.Count; i++)
                    PlotHelper.AddText(wpfNValue.Plot, $"{ns[i]:N0}", ns[i], depths[i], xMaxN);
            }

            string title = "N値分布";
            wpfNValue.Plot.Axes.Title.Label.Text = title;
            wpfNValue.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "N値";
            wpfNValue.Plot.Axes.Bottom.Label.Text = xLabel;
            wpfNValue.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "GL基準深さ(m)";
            wpfNValue.Plot.Axes.Left.Label.Text = yLabel;
            wpfNValue.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpfNValue.Plot.Axes.AutoScale();
            wpfNValue.Plot.Axes.AutoScaleExpandY();
            wpfNValue.Plot.Axes.Bottom.Min = 0.0;
            wpfNValue.Plot.Axes.Bottom.Max = 60.0;

            MyCrosshair_NValue ??= PlotHelper.InitCrosshair(wpfNValue, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedNMouseMove)
            {
                wpfNValue.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_NValue", "N値", "GL基準深さ(m)", 1, 3);
                _hookedNMouseMove = true;
            }

            wpfNValue.Refresh();
        }


        // 土層描画メソッド
        private void DrawSoilLayer(WpfPlot wpf)
        {
            //Color color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            Color color0 = Color.FromSKColor(NikkenSKColor.Yellow);
            Color grayColor = new(128, 128, 128, 255); // グレー色

            LinePattern linePattern = LinePattern.Solid;
            // 地表
            wpf.Plot.Add.HorizontalLine(0, 2, grayColor, LinePattern.Solid);

            // 土層境界ライン
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                wpf.Plot.Add.HorizontalLine(GroundInput.GroundLayers[i].BottomGLDepth, 1, color0, linePattern);
            }

            // 塗りつぶし（層ごとの背景）
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                double y1 = i == 0 ? 0 : GroundInput.GroundLayers[i - 1].BottomGLDepth;
                double y2 = GroundInput.GroundLayers[i].BottomGLDepth;

                Color fillColor = new(200, 200, 200, 32); // デフォルト: 半透明の薄いグレー
                if (GroundInput.GroundLayers[i].GranularityClass == "粘性土")
                { fillColor = new(210, 180, 140, 64); } // 半透明の薄い茶色 R G B alpha
                else if (GroundInput.GroundLayers[i].GranularityClass == "砂質土")
                { fillColor = new(255, 165, 0, 64); } // 半透明の薄いオレンジ R G B alpha
                else if (GroundInput.GroundLayers[i].GranularityClass == "礫質土")
                { fillColor = new(144, 238, 144, 64); } // 半透明の薄い緑 R G B alpha

                wpf.Plot.Add.VerticalSpan(y1, y2, fillColor);
            }

            // Y=0 の基準縦線
            Color blackColor = new(0, 0, 0, 255); // 黒色
            wpf.Plot.Add.VerticalLine(0, 1, blackColor);

            // ---- 地下水位表示追加ここから ----
            double gwDepth = GroundInput.GroundWaterGLDepth; // (多くの場合 0 か負値)

            // 地下水位ライン（青）
            Color waterColor = Color.FromSKColor(NikkenSKColor.DeepBlue);
            wpf.Plot.Add.HorizontalLine(gwDepth, 2, waterColor, LinePattern.Solid);

            // 「▽GWT」ラベル (Y軸位置 x=0、線の少し上)
            // ▽ (U+25BD) を描画できるフォントを Fonts.Detect で取得
            const string gwtText = "▽GWT";
            var gwtLabel = wpf.Plot.Add.Text(gwtText, new ScottPlot.Coordinates(0, gwDepth));
            gwtLabel.LabelFontColor = waterColor;
            gwtLabel.LabelFontSize = 10;
            gwtLabel.LabelBold = true;
            gwtLabel.LabelFontName = Fonts.Detect(gwtText);
            gwtLabel.LabelAlignment = ScottPlot.Alignment.LowerLeft;

            // Y軸レンジから線間隔を決定：(maxY - minY) / 50 を使用。データ不足時は従来の 0.12 を使用
            double yMax = 0.0;
            double yMin = 0.0;
            bool hasDepthData = false;

            // 地層底を候補にする
            if (GroundInput.GroundLayers != null && GroundInput.GroundLayers.Count > 0)
            {
                yMin = GroundInput.GroundLayers.Min(l => l.BottomGLDepth);
                hasDepthData = true;
            }

            // 地質点深さも考慮
            if (GroundInput.GroundMassesData != null && GroundInput.GroundMassesData.Count > 0)
            {
                double minMassDepth = GroundInput.GroundMassesData.Min(m => m.GLDepth);
                if (!hasDepthData)
                {
                    yMin = minMassDepth;
                    hasDepthData = true;
                }
                else
                {
                    yMin = Math.Min(yMin, minMassDepth);
                }
            }

            double range = hasDepthData ? Math.Abs(yMax - yMin) : 0.0;
            double lineGap = range > 0.0 ? range / 100.0 : 0.12;

            // 直下 3 本の水平ライン（下ほど透過度を高く＝より薄く）
            //double lineGap = 0.12;
            byte[] alphas = [200, 130, 70]; // 上→下
            for (int i = 0; i < alphas.Length; i++)
            {
                double y = gwDepth - (i + 1) * lineGap;
                Color transLineColor = new(waterColor.Red, waterColor.Green, waterColor.Blue, alphas[i]);
                wpf.Plot.Add.HorizontalLine(y, 1, transLineColor, LinePattern.Solid);
            }
        }

        // 階段状グラフ描画メソッド
        private void DrawSteppedGraph(List<double> originalX, List<double> originalY, WpfPlot wpf, string title, string xLabel, string yLabel)
        {
            if (GroundWindowInstance == null)
            { return; }

            (List<double> steppedVss, List<double> steppedGLDepths) = GetSteppedData(originalX, originalY);

            var dataX1 = steppedVss.ToArray();
            var dataY1 = steppedGLDepths.ToArray();

            wpf.Plot.Clear();
            DrawSoilLayer(wpf);

            var scatter = wpf.Plot.Add.Scatter(dataX1, dataY1);

            scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;

            double xMaxStepped = dataX1.Length > 0 ? dataX1.Max() : 1.0;
            for (int i = 0; i < dataX1.Length; i++)
            {
                PlotHelper.AddText(wpf.Plot, $"{dataX1[i]:N0}", dataX1[i], dataY1[i], xMaxStepped);
            }

            List<CoordinateRect> coordinateRects = GetRectangleGeometry(originalX, originalY);
            foreach (CoordinateRect coordinate in coordinateRects)
            {
                var rectangle = wpf.Plot.Add.Rectangle(coordinate);
                rectangle.FillColor = Color.FromSKColor(NikkenSKColor.SkyBlue);
                rectangle.LineColor = new(0, 0, 0, 255); // 黒色
                rectangle.LineWidth = 1;
            }

            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();
            wpf.Plot.Axes.Bottom.Min = 0.0;

            wpf.Refresh();
        }

        // 粘着力グラフ描画メソッド
        private void DrawCuGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> cus = [];
            List<double> _bottomGLDepths = [];

            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                cus.Add(GroundInput.GroundLayers[i].Cohesive);
                _bottomGLDepths.Add(GroundInput.GroundLayers[i].BottomGLDepth);
            }
            DrawSteppedGraph(cus, _bottomGLDepths, GroundWindowInstance.wpfPlotCu, "粘着力分布", "粘着力Cu (kN/m2)", "GL基準深さ(m)");

            WpfPlot wpf = GroundWindowInstance.wpfPlotCu;

            // クロスヘアの初期化
            MyCrosshair_Cu = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

            // 例: グラフ初期化時
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Cu", "Cu(kN/m2)", "GL基準深さ(m)", 1, 3);
        }

        // せん断速度グラフ描画メソッド
        private void DrawVsGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> vss = [];
            List<double> _bottomGLDepths = [];

            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                vss.Add(GroundInput.GroundLayers[i].Vs);
                _bottomGLDepths.Add(GroundInput.GroundLayers[i].BottomGLDepth);
            }
            DrawSteppedGraph(vss, _bottomGLDepths, GroundWindowInstance.wpfPlotVs, "せん断波速度分布", "せん断波速度 Vs(m/s)", "GL基準深さ(m)");

            WpfPlot wpf = GroundWindowInstance.wpfPlotVs;

            // クロスヘアの初期化
            MyCrosshair_Vs = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

            // 例: グラフ初期化時
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Vs", "Vs(m/s)", "GL基準深さ(m)", 1, 3);
        }

        // 変形係数グラフ描画メソッド
        private void DrawEsGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> ess = [];
            List<double> _bottomGLDepths = [];

            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                ess.Add(GroundInput.GroundLayers[i].Es);
                _bottomGLDepths.Add(GroundInput.GroundLayers[i].BottomGLDepth);
            }
            DrawSteppedGraph(ess, _bottomGLDepths, GroundWindowInstance.wpfPlotEs, "変形係数分布", "変形係数 Es(kN/m2)", "GL基準深さ(m)");

            WpfPlot wpf = GroundWindowInstance.wpfPlotEs;

            // クロスヘアの初期化
            MyCrosshair_Es = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

            // 例: グラフ初期化時
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Es", "Es(kN/m2)", "GL基準深さ(m)", 1, 3);
        }
    }
}
