using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using ScottPlot.Plottables;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Fonts = ScottPlot.Fonts;
using WpfPlot = ScottPlot.WPF.WpfPlot;


namespace PileDesign.ViewModels
{
    public partial class PileSectionViewModel : ObservableObject
    {
        public PileSectionWindow PileSectionWindowInstance { get; set; }

        public readonly UndoManager _undoManager = new();
        private WpfPlot? PlotNQ => PileSectionWindowInstance?.wpfPlotNQ;

        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        private int _pileSegmentNo;
        public int PileSegmentNo
        {
            get => _pileSegmentNo;
            set => SetProperty(ref _pileSegmentNo, value);
        }

        private PileSection _pileSection;
        public PileSection PileSection
        {
            get => _pileSection;
            set
            {
                if (_pileSection != value)
                {
                    _pileSection = value;
                    OnPropertyChanged(nameof(PileSection));
                    // 必要なら個別プロパティも通知
                    OnPropertyChanged(nameof(PileSection.UltimateLimitAxialForceThresholds));
                }
            }
        }

        // Viewを閉じるためのイベント
        public event EventHandler<bool> RequestClose;

        private readonly PileSection PrevPileSection;

        public Canvas Canvas { get; set; }
        private PathGeometry DrawingGeometry = new();
        private double Scale;

        public static Crosshair MyCrosshair_MN { get; private set; }

        private string _crosshairPositionText_MN;
        public string CrosshairPositionText_MN
        {
            get => _crosshairPositionText_MN;
            set => SetProperty(ref _crosshairPositionText_MN, value);
        }

        public static Crosshair MyCrosshair_Mphi { get; private set; }

        private string _crosshairPositionText_Mphi;
        public string CrosshairPositionText_Mphi
        {
            get => _crosshairPositionText_Mphi;
            set => SetProperty(ref _crosshairPositionText_Mphi, value);
        }

        public static Crosshair MyCrosshair_Mtheta { get; private set; }

        private string _crosshairPositionText_Mtheta;
        public string CrosshairPositionText_Mtheta
        {
            get => _crosshairPositionText_Mtheta;
            set => SetProperty(ref _crosshairPositionText_Mtheta, value);
        }

        public static Crosshair MyCrosshair_NQ { get; private set; }

        private string _crosshairPositionText_NQ;
        public string CrosshairPositionText_NQ
        {
            get => _crosshairPositionText_NQ;
            set => SetProperty(ref _crosshairPositionText_NQ, value);
        }

        private double _monQd = 1.0;
        public double MonQd
        {
            get => _monQd;
            set
            {
                double v = value;
                if (SetProperty(ref _monQd, v))
                {
                    try
                    {
                        // 必要条件を満たす場合、N-Q グラフを再描画する
                        if (PileSection != null && PileSectionWindowInstance != null)
                        {
                            var thresholds = PileSection.UltimateLimitAxialForceThresholds;
                            if (thresholds != null && thresholds.Count >= 2)
                            {
                                double NMin = thresholds[0];
                                double NMax = thresholds[^1];
                                // UI スレッドで安全に描画メソッドを呼ぶ
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    DrawNQForCurrentPile(NMin, NMax, 10);
                                }));
                            }
                            else
                            {
                                // 閾値が取れない場合は ChartUpdate にフォールバックして全体更新
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    ChartUpdate();
                                }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"MonQd change: failed to refresh NQ plot: {ex.Message}");
                    }
                }
            }
        }

        // 追加: 杭種に応じて適切な N-Q グラフを描くヘルパー
        private void DrawNQForCurrentPile(double NMin, double NMax, int nDiv)
        {
            if (PileSection == null) return;

            if (PileSection.PileBodyType == "場所打ち鉄筋コンクリート杭" ||
                (PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && PileSection.PileSectionType == "鉄筋コンクリート部"))
            {
                DrawInsituReinforcedConcretePile_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && PileSection.PileSectionType == "鋼管コンクリート部")
            {
                DrawInsituSteelPipeReinforcedConcretePile_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileSectionType == "PHC杭")
            {
                DrawPHC_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileSectionType == "PRC杭")
            {
                DrawPRC_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileSectionType == "SC杭")
            {
                if (PileSection.PipeTs > 0)
                    DrawSC_NQ(NMin, NMax, nDiv);
            }
        }

        private int _seismicLevel = 2;
        public int SeismicLevel
        {
            get => _seismicLevel;
            set
            {
                if (SetProperty(ref _seismicLevel, value))
                {
                    // SeismicLevel 変更時に N-Q プロットを再描画する（UI スレッドで安全に呼ぶ）
                    try
                    {
                        if (PileSection != null && PileSectionWindowInstance != null)
                        {
                            var thresholds = PileSection.UltimateLimitAxialForceThresholds;
                            if (thresholds != null && thresholds.Count >= 2)
                            {
                                double NMin = thresholds[0];
                                double NMax = thresholds[^1];
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    DrawNQForCurrentPile(NMin, NMax, 10);
                                }));
                            }
                            else
                            {
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() => ChartUpdate()));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SeismicLevel change: failed to refresh NQ plot: {ex.Message}");
                    }

                    // IsSeismicLevel1/2 のバインディング更新通知
                    OnPropertyChanged(nameof(IsSeismicLevel1));
                    OnPropertyChanged(nameof(IsSeismicLevel2));
                }
            }
        }

        // XAML の RadioButton とバインドするヘルパー bool プロパティ
        public bool IsSeismicLevel1
        {
            get => SeismicLevel == 1;
            set
            {
                if (value) SeismicLevel = 1;
                OnPropertyChanged(nameof(IsSeismicLevel1));
                OnPropertyChanged(nameof(IsSeismicLevel2));
            }
        }
        public bool IsSeismicLevel2
        {
            get => SeismicLevel == 2;
            set
            {
                if (value) SeismicLevel = 2;
                OnPropertyChanged(nameof(IsSeismicLevel1));
                OnPropertyChanged(nameof(IsSeismicLevel2));
            }
        }


        // コンストラクタ
        public PileSectionViewModel(
            MainWindowViewModel mainWindowViewModel,
            PileSection pileSection,
            int pileBodyNo,
            int segmentNo
            )
        {
            _mainWindowViewModel = mainWindowViewModel;
            PileBodyNo = pileBodyNo;
            PileSegmentNo = segmentNo;
            PileSection = pileSection;

            System.Diagnostics.Debug.WriteLine($"Constructor: {PileSection.SelectedPrecastPile?.Name}");

            PrevPileSection = pileSection.DeepCopy(); // ShallowCopyメソッドを使用して値渡し

            PileSection.RecalculatePileDia();
        }

        [RelayCommand]
        public void Undo()
        {
            _undoManager.Undo();
            if (_undoManager.CurrentState is PileSection state)
            {
                PileSection = state.DeepCopy();
            }
        }

        [RelayCommand]
        public void Redo()
        {
            _undoManager.Redo();
            if (_undoManager.CurrentState is PileSection state)
            {
                PileSection = state.DeepCopy();
            }
        }

        [RelayCommand]
        private void OnOk()
        {
            if (!_mainWindowViewModel.CheckAndResetElementSplit("杭断面"))
                return;
            RequestClose?.Invoke(this, true);
        }

        [RelayCommand]
        private void OnCancel()
        {
            // PileSectionの変更を元に戻す
            PileSection.PileBodyNo = PrevPileSection.PileBodyNo;
            PileSection.PileBodyType = PrevPileSection.PileBodyType;
            PileSection.PileSectionType = PrevPileSection.PileSectionType;
            PileSection.PileDiameter = PrevPileSection.PileDiameter;
            PileSection.ConcreteOutDia = PrevPileSection.ConcreteOutDia;
            PileSection.ConcreteThickness = PrevPileSection.ConcreteThickness;
            PileSection.ConcreteFc = PrevPileSection.ConcreteFc;
            PileSection.ConcreteGamma = PrevPileSection.ConcreteGamma;
            PileSection.ConcreteGsi = PrevPileSection.ConcreteGsi;
            PileSection.ConcreteE = PrevPileSection.ConcreteE;
            PileSection.SelectedPrecastPile = PrevPileSection.SelectedPrecastPile;
            PileSection.SelectedSteelPipePile = PrevPileSection.SelectedSteelPipePile;
            PileSection.SelectedSteelPipePileName = PrevPileSection.SelectedSteelPipePileName;
            PileSection.MainBarSize = PrevPileSection.MainBarSize;
            PileSection.MainBarNum = PrevPileSection.MainBarNum;
            PileSection.MainBarCenterCover = PrevPileSection.MainBarCenterCover;
            PileSection.HoopSize = PrevPileSection.HoopSize;
            PileSection.PipeDia = PrevPileSection.PipeDia;
            PileSection.PipeGrade = PrevPileSection.PipeGrade;
            PileSection.SelectedPileSectionSpecification = PrevPileSection.SelectedPileSectionSpecification;

            RequestClose?.Invoke(this, false);
        }


        public void ReplaceSeries(
            List<double> serviceN, List<double> serviceM,
            List<double> damageN, List<double> damageM,
            List<double> ultimateN, List<double> ultimateM,
            List<double> factoredServiceN, List<double> factoredServiceM,
            List<double> factoredDamageN, List<double> factoredDamageM,
            List<double> factoredUltimateN, List<double> factoredUltimateM
            )
        {
            var wpf = PileSectionWindowInstance.wpfPlotMN;
            wpf.Plot.Clear();
            string title = "軸力と曲げモーメントの耐力曲線";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "N (kN)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "M (kNm)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Legend.FontName = Fonts.Detect(yLabel);

            double maxN = double.MinValue, minN = double.MaxValue;
            UpdateSeries("(低減前)使用限界", serviceN, serviceM, NikkenSKColor.DeepBlue, isDashed: true);
            UpdateSeries("(低減前)損傷限界", damageN, damageM, NikkenSKColor.Green, isDashed: true);
            UpdateSeries("(低減前)安全限界", ultimateN, ultimateM, NikkenSKColor.Red, isDashed: true);
            UpdateSeries("(低減後)使用限界", factoredServiceN, factoredServiceM, NikkenSKColor.DeepBlue);
            UpdateSeries("(低減後)損傷限界", factoredDamageN, factoredDamageM, NikkenSKColor.Green);
            UpdateSeries("(低減後)安全限界", factoredUltimateN, factoredUltimateM, NikkenSKColor.Red);

            var black = new ScottPlot.Color(0, 0, 0);
            wpf.Plot.Add.VerticalLine(0, 1, black);
            wpf.Plot.Add.HorizontalLine(0, 1, black);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            wpf.Plot.Axes.Left.Min = 0.0;

            wpf.Refresh();

            // クロスヘアの初期化
            MyCrosshair_MN = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_MN", "N(kN)", "M(kNm)", 1, 1);

            bool shouldBreak = false;
            for (int i = 0; i <= 5; i++)
            {
                for (int j = 0; j <= 5; j++)
                {
                    double minScaleValue = -j * 2 * Math.Pow(10, i);
                    if (minScaleValue < minN)
                    {
                        shouldBreak = true;
                        break;
                    }
                }
                if (shouldBreak)
                {
                    break;
                }
            }
        }

        // 共通: N-Q 曲線を描画するヘルパー
        private void PlotNQCurves(
            (List<double> qs, List<double> ns) serviceUnfactored,
            (List<double> qs, List<double> ns) serviceFactored,
            (List<double> qs, List<double> ns) damageUnfactored,
            (List<double> qs, List<double> ns) damageFactored,
            (List<double> qs, List<double> ns) ultimateUnfactored,
            (List<double> qs, List<double> ns) ultimateFactored)
        {
            var wpf = PlotNQ;
            if (wpf is null) return;

            wpf.Plot.Clear();

            void TryPlot((List<double> qs, List<double> ns) data, string legend, SKColor? color = null, bool dashed = false, double lineWidth = 1.5)
            {
                var (qs, ns) = data;
                if (ns == null || qs == null) return;
                if (ns.Count == 0 || ns.Count != qs.Count) return;

                string detectedFont = Fonts.Detect(legend ?? "メイリオ");
                wpf.Plot.Legend.FontName = detectedFont;

                double[] xs = [.. ns.Select(x => x * 1e-3)]; // N -> kN
                double[] ys = [.. qs.Select(q => q * 1e-3)]; // Q -> kN
                var scatter = wpf.Plot.Add.Scatter(xs, ys);
                scatter.LegendText = legend;
                if (color.HasValue) scatter.Color = ScottPlot.Color.FromSKColor(color.Value);
                scatter.LineWidth = (float)lineWidth;
                scatter.MarkerSize = 0;
                if (dashed) scatter.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
            }

            // プロット順：使用限界・損傷限界・安全限界（それぞれ低減前/後）
            TryPlot(serviceUnfactored, "(低減前) 使用限界 Q-N", NikkenSKColor.DeepBlue, true, 1.5);
            TryPlot(serviceFactored, "(低減後) 使用限界 Q-N", NikkenSKColor.DeepBlue, false, 2.0);

            TryPlot(damageUnfactored, "(低減前) 損傷限界 Q-N", NikkenSKColor.Green, true, 1.5);
            TryPlot(damageFactored, "(低減後) 損傷限界 Q-N", NikkenSKColor.Green, false, 2.0);

            TryPlot(ultimateUnfactored, "(低減前) 安全限界 Q-N", NikkenSKColor.Red, true, 1.5);
            TryPlot(ultimateFactored, "(低減後) 安全限界 Q-N", NikkenSKColor.Red, false, 2.0);

            var blackNQ = new ScottPlot.Color(0, 0, 0);
            wpf.Plot.Add.VerticalLine(0, 1, blackNQ);
            wpf.Plot.Add.HorizontalLine(0, 1, blackNQ);

            wpf.Plot.Axes.Bottom.Label.Text = "N (kN)";
            wpf.Plot.Axes.Left.Label.Text = "Q (kN)";
            wpf.Plot.Legend.IsVisible = true;
            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.Left.Min = 0.0; // Y軸の最小値を0に固定
            wpf.Refresh();

            // クロスヘア初期化（既存仕様に合わせて毎回登録）
            MyCrosshair_NQ = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_NQ", "N(kN)", "Q(kN)", 1, 1);
        }

        // 場所打ち鉄筋コンクリート杭せん断力
        private void DrawInsituReinforcedConcretePile_NQ(double NMin, double NMax, int nDiv)
        {
            var insituConcrete = new InsituConcrete(PileSection.ConcreteOutDia, PileSection.ConcreteGsi, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var section = new InsituReinforcedConcreteSection(insituConcrete, mainBars);

            double monQd = MonQd;
            double pw = PileSection.HoopPw;
            double sigmaWy = PileSection.HoopSigmay;

            var svcUnf = section.GetServiceLimitQNInteraction(monQd, false);
            var svcFac = section.GetServiceLimitQNInteraction(monQd, true);

            var dmgUnf = section.GetDamageLimitQNInteraction(monQd, false, SeismicLevel);
            var dmgFac = section.GetDamageLimitQNInteraction(monQd, true, SeismicLevel);

            var ultUnf = section.GetUltimateQNInteraction(monQd, pw, sigmaWy, false);
            var ultFac = section.GetUltimateQNInteraction(monQd, pw, sigmaWy, true);

            PlotNQCurves(svcUnf, svcFac, dmgUnf, dmgFac, ultUnf, ultFac);
        }

        // 場所打ち鉄筋コンクリート杭せん断力
        private void DrawInsituSteelPipeReinforcedConcretePile_NQ(double NMin, double NMax, int nDiv)
        {
            var insituSteelPipe = new InsituSteelPipe(PileSection.PipeGrade, PileSection.PipeDia, PileSection.PipeTs, PileSection.CorrosionDepth);
            var insituConcrete = new InsituConcrete(PileSection.ConcreteOutDia, PileSection.ConcreteGsi, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var section = new InsituSteelPipeReinforcedConcreteSection(insituSteelPipe, insituConcrete, mainBars);

            // ライブラリ側の仕様により同一取得を2度呼んでいたが、ここではそのまま尊重
            var svcUnf = section.GetServiceLimitQNInteraction();
            var svcFac = section.GetServiceLimitQNInteraction();

            var dmgUnf = section.GetDamageLimitQNInteraction();
            var dmgFac = section.GetDamageLimitQNInteraction();

            var ultUnf = section.GetUltimateQNInteraction();
            var ultFac = section.GetUltimateQNInteraction();

            PlotNQCurves(svcUnf, svcFac, dmgUnf, dmgFac, ultUnf, ultFac);
        }

        // PHC杭せん断力
        private void DrawPHC_NQ(double NMin, double NMax, int nDiv)
        {
            var precastConcrete = new PrecastPHCConcrete(PileSection.PileDiameter, PileSection.PileDiameter - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var tendons = new Tendons(PileSection.TendonDp, PileSection.TendonAp, PileSection.TendonSigmaPy, PileSection.TendonSigmaPu);
            var section = new PHCSection(precastConcrete, tendons, PileSection.Prestress);

            double monQd = MonQd;

            var svcUnf = section.GetServiceLimitQNInteraction(monQd, false);
            var svcFac = section.GetServiceLimitQNInteraction(monQd, true);

            var dmgUnf = section.GetDamageLimitQNInteraction(monQd, false, SeismicLevel);
            var dmgFac = section.GetDamageLimitQNInteraction(monQd, true, SeismicLevel);

            var ultUnf = section.GetUltimateQNInteraction(monQd, false);
            var ultFac = section.GetUltimateQNInteraction(monQd, true);

            PlotNQCurves(svcUnf, svcFac, dmgUnf, dmgFac, ultUnf, ultFac);
        }

        // PRC杭せん断力
        private void DrawPRC_NQ(double NMin, double NMax, int nDiv)
        {
            var precastConcrete = new PrecastPRCConcrete(PileSection.PileDiameter, PileSection.PileDiameter - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var tendons = new Tendons(PileSection.TendonDp, PileSection.TendonAp, PileSection.TendonSigmaPy, PileSection.TendonSigmaPu);
            var section = new PRCSection(precastConcrete, mainBars, tendons, PileSection.Prestress);

            double monQd = MonQd;

            var svcUnf = section.GetServiceLimitQNInteraction(monQd, false);
            var svcFac = section.GetServiceLimitQNInteraction(monQd, true);

            var dmgUnf = section.GetDamageLimitQNInteraction(monQd, false, SeismicLevel);
            var dmgFac = section.GetDamageLimitQNInteraction(monQd, true, SeismicLevel);

            var ultUnf = section.GetUltimateQNInteraction(monQd, false);
            var ultFac = section.GetUltimateQNInteraction(monQd, true);

            PlotNQCurves(svcUnf, svcFac, dmgUnf, dmgFac, ultUnf, ultFac);
        }

        // SC杭せん断力
        private void DrawSC_NQ(double NMin, double NMax, int nDiv)
        {
            var precastConcrete = new PrecastSCConcrete(PileSection.PileDiameter - 2 * PileSection.PipeTs, PileSection.PileDiameter - 2 * PileSection.PipeTs - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var steelPipe = new PrecastSteelPipe(PileSection.PipeGrade, PileSection.PipeDia, PileSection.PipeTs, PileSection.CorrosionDepth);
            var section = new SCSection(precastConcrete, steelPipe);

            double monQd = MonQd;

            var svcUnf = section.GetServiceLimitQNInteraction(monQd, false);
            var svcFac = section.GetServiceLimitQNInteraction(monQd, true);

            var dmgUnf = section.GetDamageLimitQNInteraction(monQd, false, SeismicLevel);
            var dmgFac = section.GetDamageLimitQNInteraction(monQd, true, SeismicLevel);

            var ultUnf = section.GetUltimateQNInteraction(monQd, false);
            var ultFac = section.GetUltimateQNInteraction(monQd, true);

            PlotNQCurves(svcUnf, svcFac, dmgUnf, dmgFac, ultUnf, ultFac);
        }

        // 共通: M-φ 曲線を描画するヘルパー
        private void PlotMPhiCurves(
            IEnumerable<double> nTargets,
            Func<double, (List<double> phis, List<double> Ms)> getMPhi,
            Func<List<double>, List<double>, List<double>>? buildMiddlePhis = null,
            string? middleLegendPrefix = null)
        {
            var wpf = PileSectionWindowInstance?.wpfPlotMphi;
            if (wpf is null) return;

            wpf.Plot.Clear();

            // 追加: 凡例フォントを日本語/φを含むテキストで検出して設定
            // middleLegendPrefix が "杭中間部" など日本語を含む場合を優先し、無ければ M-φ のタイトル文字列で検出
            string legendProbe = (middleLegendPrefix ?? "M-φ関係") + " φ";
            wpf.Plot.Legend.FontName = Fonts.Detect(legendProbe);

            foreach (var n in nTargets)
            {
                var (phis, Ms) = getMPhi(n);
                if (phis is null || Ms is null || phis.Count == 0 || Ms.Count == 0) continue;

                // 単位変換: M [Nmm] -> [kNm]
                var Ms_kNm = Ms.Select(m => m * 1e-6).ToArray();
                var phis_1_m = phis.ToArray();

                var scatter = wpf.Plot.Add.Scatter(phis_1_m, Ms_kNm);
                scatter.LegendText = $"N={(n * 0.001):N0}kN";
                scatter.LineWidth = 2;

                // 中間部（破線）を描く場合
                if (buildMiddlePhis != null)
                {
                    List<double>? phisMiddle = null;
                    try
                    {
                        phisMiddle = buildMiddlePhis(phis, Ms);
                    }
                    catch { /* 安全側で無視 */ }

                    if (phisMiddle != null && phisMiddle.Count > 0)
                    {
                        var scatterMiddle = wpf.Plot.Add.Scatter(phisMiddle.ToArray(), Ms_kNm);
                        scatterMiddle.LegendText = $"{(middleLegendPrefix ?? "中間部")} N={(n * 0.001):N0}kN";
                        scatterMiddle.LineWidth = 2;
                        scatterMiddle.Color = scatter.Color;
                        scatterMiddle.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                    }
                }
            }

            string title = "M-φ関係";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "曲率 φ [1/mm]";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "曲げモーメント M [kNm]";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            var blackMPhi = new ScottPlot.Color(0, 0, 0);
            wpf.Plot.Add.VerticalLine(0, 1, blackMPhi);
            wpf.Plot.Add.HorizontalLine(0, 1, blackMPhi);

            wpf.Plot.Legend.IsVisible = true;
            wpf.Plot.Axes.AutoScale();
            wpf.Refresh();

            // クロスヘアの初期化
            MyCrosshair_Mphi = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Mphi", "φ(1/m)", "M(kNm)", 1, 1);
        }

        // 共通: M-θ 曲線を描画するヘルパー（未定義メッセージにも対応）
        private void PlotMThetaCurves(
            IEnumerable<double> nTargets,
            Func<double, (List<double> thetas, List<double> Ms)>? getMTheta,
            Func<double, bool>? canPlotPredicate,
            string? notDefinedMessageIfNull)
        {
            var wpf = PileSectionWindowInstance?.wpfPlotMtheta;
            if (wpf is null) return;

            wpf.Plot.Clear();

            // 追加: 凡例フォントも日本語/θを含む文字列で検出して設定（予防）
            wpf.Plot.Legend.FontName = Fonts.Detect("M-θ関係 θ");

            if (getMTheta is null)
            {
                // 未定義メッセージのみ表示
                if (!string.IsNullOrWhiteSpace(notDefinedMessageIfNull))
                {
                    wpf.Plot.Axes.Title.Label.Text = notDefinedMessageIfNull;
                    wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect("適用なし");
                }
                wpf.Refresh();
                return;
            }

            foreach (var n in nTargets)
            {
                if (canPlotPredicate != null && !canPlotPredicate(n))
                    continue;

                var (thetas, Ms) = getMTheta(n);
                if (thetas is null || Ms is null || thetas.Count == 0 || Ms.Count == 0) continue;

                var Ms_kNm = Ms.Select(m => m * 1e-6).ToArray();
                var thetasArr = thetas.ToArray();

                var scatter = wpf.Plot.Add.Scatter(thetasArr, Ms_kNm);
                scatter.LegendText = $"N={(n * 0.001):N0}kN";
                scatter.LineWidth = 2;
            }

            string title = "M-θ関係";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "回転角 θ [rad]";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "曲げモーメント M [kNm]";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            var blackMTheta = new ScottPlot.Color(0, 0, 0);
            wpf.Plot.Add.VerticalLine(0, 1, blackMTheta);
            wpf.Plot.Add.HorizontalLine(0, 1, blackMTheta);

            wpf.Plot.Legend.IsVisible = true;
            wpf.Plot.Axes.AutoScale();
            wpf.Refresh();

            // クロスヘアの初期化
            MyCrosshair_Mtheta = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Mtheta", "θ(rad)", "M(kNm)", 1, 1);
        }

        // 場所打ち鉄筋コンクリート杭
        public void DrawInsituReinforcedConcretePile_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            var insituConcrete = new InsituConcrete(PileSection.ConcreteOutDia, PileSection.ConcreteGsi, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var section = new InsituReinforcedConcreteSection(insituConcrete, mainBars);

            // kN -> N
            NMin *= 1000;
            NMax *= 1000;
            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            // M-φ: 場所打ちRC
            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n));

            // M-θ: 場所打ちRC（安全側にラムダで明示）
            PlotMThetaCurves(
                nTargets,
                n => section.GetMThetaRelationship(n),
                n => n / section.Ae <= 0.25 * insituConcrete.Gsi * insituConcrete.Fc,
                notDefinedMessageIfNull: null);
        }

        // 場所打ち鋼管コンクリート杭
        private void DrawInsituSteelPipeReinforcedConcretePile_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            var insituSteelPipe = new InsituSteelPipe(PileSection.PipeGrade, PileSection.PipeDia, PileSection.PipeTs, PileSection.CorrosionDepth);
            var insituConcrete = new InsituConcrete(PileSection.ConcreteOutDia, PileSection.ConcreteGsi, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var section = new InsituSteelPipeReinforcedConcreteSection(insituSteelPipe, insituConcrete, mainBars);

            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            // 杭中間部 φ の算出（既存式を安全化）
            static List<double>? BuildMiddlePhis(List<double> phis, List<double> Ms)
            {
                if (phis.Count < 2 || Ms.Count < 2) return null;

                // 4点の場合（通常ケース: 原点→ひび割れ→降伏→終局）
                if (phis.Count >= 4 && Ms.Count >= 4)
                {
                    double denom = (Ms[2] - Ms[1]);
                    if (Math.Abs(denom) < 1e-12) return null;
                    double beta1 = 1.0;
                    var list = new List<double> { phis[0], phis[1], phis[2] };
                    list.Add(phis[1] + (phis[2] - phis[1]) * (beta1 * Ms[3] - Ms[1]) / denom);
                    return list;
                }

                // 3点の場合（降伏前に終局到達: 原点→ひび割れ→終局）
                if (phis.Count == 3 && Ms.Count == 3)
                {
                    return [phis[0], phis[1], phis[2]];
                }

                return null;
            }

            // M-φ（杭頭部 + 杭中間部(破線)）
            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n), BuildMiddlePhis, "杭中間部");

            // M-θ は未定義メッセージ
            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: "場所打ち鋼管コンクリート杭の曲げモーメント-回転角関係の定義はありません");
        }

        // PHC杭
        private void DrawPHC_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            var precastConcrete = new PrecastPHCConcrete(PileSection.PileDiameter, PileSection.PileDiameter - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var tendons = new Tendons(PileSection.TendonDp, PileSection.TendonAp, PileSection.TendonSigmaPy, PileSection.TendonSigmaPu);
            var section = new PHCSection(precastConcrete, tendons, PileSection.Prestress);

            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            // M-φ（beta1 はデフォルト利用）
            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n));

            // M-θ は未定義メッセージ
            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: "PHC杭の曲げモーメント-回転角関係の定義はありません");
        }

        // PRC杭
        private void DrawPRC_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            if (PileSection.MainBarDr <= 0 || PileSection.MainBarNum <= 0 || PileSection.MainBarSize == null)
            {
                System.Diagnostics.Debug.WriteLine("Invalid MainBars properties. Skipping graph generation.");
                return;
            }

            var precastConcrete = new PrecastPRCConcrete(PileSection.PileDiameter, PileSection.PileDiameter - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var tendons = new Tendons(PileSection.TendonDp, PileSection.TendonAp, PileSection.TendonSigmaPy, PileSection.TendonSigmaPu);
            var section = new PRCSection(precastConcrete, mainBars, tendons, PileSection.Prestress);

            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n));

            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: "PRC杭の曲げモーメント-回転角関係の定義はありません");
        }

        // SC杭
        private void DrawSC_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            var precastConcrete = new PrecastSCConcrete(PileSection.PileDiameter - 2 * PileSection.PipeTs, PileSection.PileDiameter - 2 * PileSection.PipeTs - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var steelPipe = new PrecastSteelPipe(PileSection.PipeGrade, PileSection.PipeDia, PileSection.PipeTs, PileSection.CorrosionDepth);
            var section = new SCSection(precastConcrete, steelPipe);

            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n));

            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: "SC杭の曲げモーメント-回転角関係の定義はありません");
        }

        private void UpdateSeries(string title, List<double> nValues, List<double> mValues,
            SKColor? color = null, bool isDashed = false)
        {
            var wpf = PileSectionWindowInstance.wpfPlotMN;
            var scatter = wpf.Plot.Add.Scatter(nValues, mValues);
            if (color.HasValue)
                scatter.Color = ScottPlot.Color.FromSKColor(color.Value);
            scatter.LineWidth = isDashed ? 1.5f : 2.0f;
            if (isDashed)
                scatter.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
            scatter.MarkerSize = 0;
            scatter.MarkerShape = ScottPlot.MarkerShape.None;
            scatter.LegendText = title;

            wpf.Plot.Legend.FontName = Fonts.Detect(title);
        }

        // List<double>型データの構成要素すべてに係数を乗ずるメソッド
        internal static List<double> GetMultipliedListValues(List<double> originalList, double multiplier)
        {
            List<double> result = [];
            for (int i = 0; i < originalList.Count; i++)
            {
                result.Add(originalList[i] * multiplier);
            }
            return result;
        }

        public void ChartUpdate()
        {
            //スペックのセット
            PileSection.SetSpecs();
            List<double> ns;

            if (PileSection.PileBodyType == "場所打ち鉄筋コンクリート杭" ||
                (PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && PileSection.PileSectionType == "鉄筋コンクリート部"))
            {
                var (serviceN, serviceM) = PileSection.UnfactoredServiceNM;
                var (damageN, damageM) = PileSection.UnfactoredDamageNM;
                var (ultimateN, ultimateM) = PileSection.UnfactoredUltimateNM;
                var (factoredServiceN, factoredServiceM) = PileSection.FactoredServiceNM;
                var (factoredDamageN, factoredDamageM) = PileSection.FactoredDamageNM;
                var (factoredUltimateN, factoredUltimateM) = PileSection.FactoredUltimateNM;

                // 追加: 鋼材降伏開始NM（GetYieldMomentベース）
                var (steelYieldN, steelYieldM) = PileSection.SteelYieldNM;

                var (crackN, crackM) = PileSection.CrackNM;

                // 以降はReplaceSeries(...)でグラフ描画
                ReplaceSeries(
                    serviceN, serviceM,
                    damageN, damageM,
                    ultimateN, ultimateM,
                    factoredServiceN, factoredServiceM,
                    factoredDamageN, factoredDamageM,
                    factoredUltimateN, factoredUltimateM
                    );

                // 追加で降伏開始曲線を重ね描き
                if (steelYieldN != null && steelYieldM != null && steelYieldN.Count > 1 && steelYieldN.Count == steelYieldM.Count)
                {
                    var wpfMN = PileSectionWindowInstance.wpfPlotMN;
                    var scatterYield = wpfMN.Plot.Add.Scatter(steelYieldN, steelYieldM);
                    scatterYield.LegendText = "引張鉄筋降伏開始";
                    scatterYield.LineWidth = 4;
                    //scatterYield.LineStyle.Pattern = ScottPlot.LinePattern.Dash;
                    scatterYield.MarkerShape = ScottPlot.MarkerShape.None;

                    wpfMN.Plot.Axes.AutoScale();
                    wpfMN.Refresh();

                    ns = steelYieldN;
                }
                else
                {
                    // フォールバック（鋼材降伏曲線が未計算/空の場合）
                    ns = PileSection.UltimateLimitAxialForceThresholds;
                }

                // 追加でひび割れ開始曲線を重ね描き
                if (crackN != null && crackM != null && crackN.Count > 1 && crackN.Count == crackM.Count)
                {
                    var wpfMN = PileSectionWindowInstance.wpfPlotMN;
                    var scattercrack = wpfMN.Plot.Add.Scatter(crackN, crackM);
                    scattercrack.LegendText = "ひび割れ開始";
                    scattercrack.LineWidth = 4;
                    //scatterYield.LineStyle.Pattern = ScottPlot.LinePattern.Dash;
                    scattercrack.MarkerShape = ScottPlot.MarkerShape.None;

                    wpfMN.Plot.Axes.AutoScale();
                    wpfMN.Refresh();

                    //ns = steelYieldN;
                }
                else
                {
                    // フォールバック（鋼材降伏曲線が未計算/空の場合）
                    ns = PileSection.UltimateLimitAxialForceThresholds;
                }

            }
            else
            {
                var (serviceN, serviceM) = PileSection.UnfactoredServiceNM;
                var (damageN, damageM) = PileSection.UnfactoredDamageNM;
                var (ultimateN, ultimateM) = PileSection.UnfactoredUltimateNM;
                var (factoredServiceN, factoredServiceM) = PileSection.FactoredServiceNM;
                var (factoredDamageN, factoredDamageM) = PileSection.FactoredDamageNM;
                var (factoredUltimateN, factoredUltimateM) = PileSection.FactoredUltimateNM;
                // 以降はReplaceSeries(...)でグラフ描画
                ReplaceSeries(
                    serviceN, serviceM,
                    damageN, damageM,
                    ultimateN, ultimateM,
                    factoredServiceN, factoredServiceM,
                    factoredDamageN, factoredDamageM,
                    factoredUltimateN, factoredUltimateM
                    );

                ns = PileSection.UltimateLimitAxialForceThresholds;
            }

            if (ns == null || ns.Count == 0)
                ns = PileSection.UltimateLimitAxialForceThresholds;

            if (PileSection.PileBodyType == "場所打ち鉄筋コンクリート杭" ||
                (PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && PileSection.PileSectionType == "鉄筋コンクリート部"))
            {
                // M-φグラフの描画
                double NMin = ns[0];
                double NMax = ns[^1];

                DrawInsituReinforcedConcretePile_MPhiMThetaGraph(NMin, NMax, 10);
                DrawInsituReinforcedConcretePile_NQ(NMin, NMax, 10);
            }

            else if (PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && PileSection.PileSectionType == "鋼管コンクリート部")
            {
                // M-φグラフの描画
                double NMin = ns[0]; // kN -> N
                double NMax = ns[^1]; // kN -> N

                DrawInsituSteelPipeReinforcedConcretePile_MPhiMThetaGraph(NMin, NMax, 10);
                DrawInsituSteelPipeReinforcedConcretePile_NQ(NMin, NMax, 10);
            }

            else if (PileSection.PileSectionType == "PHC杭")
            {
                // M-φグラフの描画
                double NMin = ns[0]; // kN -> N
                double NMax = ns[^1]; // kN -> N

                DrawPHC_MPhiMThetaGraph(NMin, NMax, 10);
                DrawPHC_NQ(NMin, NMax, 10);
            }


            else if (PileSection.PileSectionType == "PRC杭")
            {
                if (PileSection.MainBarDr <= 0 || PileSection.MainBarNum <= 0 || PileSection.PileDiameter <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("ChartUpdate skipped due to incomplete PileSection properties.");
                    return;
                }

                // M-φグラフの描画
                double NMin = ns[0]; // kN -> N
                double NMax = ns[^1]; // kN -> N

                DrawPRC_MPhiMThetaGraph(NMin, NMax, 10);
                DrawPRC_NQ(NMin, NMax, 10);
            }

            else if (PileSection.PileSectionType == "SC杭")
            {
                if (PileSection.PipeTs > 0)
                {
                    // M-φグラフの描画
                    double NMin = ns[0]; // kN -> N
                    double NMax = ns[^1]; // kN -> N

                    DrawSC_MPhiMThetaGraph(NMin, NMax, 10);
                    DrawSC_NQ(NMin, NMax, 10);
                }
            }
        }

        // Canvas画像を保存するメソッド
        public static void SaveImage(Canvas canvas)
        {
            // ファイル保存ダイアログを作成し、デフォルトの保存場所をデスクトップに設定します
            SaveFileDialog saveFileDialog = new()
            {
                Filter = "PNGファイル (*.png)|*.png|すべてのファイル (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            // ユーザーがダイアログでOKを選択した場合の処理を定義します
            if (saveFileDialog.ShowDialog() == true)
            {
                // RenderTargetBitmapを作成し、Canvasを描画します
                RenderTargetBitmap renderBitmap = new((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96d, 96d, PixelFormats.Default);
                renderBitmap.Render(canvas);

                // BitmapEncoderを使用してRenderTargetBitmapを画像ファイルに書き込みます
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                // ファイルに書き込みます
                using System.IO.FileStream fileStream = new(saveFileDialog.FileName, System.IO.FileMode.Create);
                encoder.Save(fileStream);
            }
        }

        // Canvasの内容を画像としてクリップボードにコピーするメソッド
        public static void CopyCanvasToClipboard(Canvas canvas)
        {
            // RenderTargetBitmapを作成し、Canvasを描画します
            RenderTargetBitmap renderBitmap = new((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96d, 96d, PixelFormats.Default);
            renderBitmap.Render(canvas);

            // Clipboard.SetImage()はStringMetadata非対応で例外になる環境があるため
            // BitmapSourceを一切渡さず、生バイトストリームのみでクリップボードに設定
            var pngEnc = new PngBitmapEncoder();
            pngEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
            var pngStream = new System.IO.MemoryStream();
            pngEnc.Save(pngStream);

            var bmpEnc = new BmpBitmapEncoder();
            bmpEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
            var bmpStream = new System.IO.MemoryStream();
            bmpEnc.Save(bmpStream);
            bmpStream.Position = 14;
            var dibBytes = new byte[bmpStream.Length - 14];
            bmpStream.Read(dibBytes, 0, dibBytes.Length);

            var dataObject = new DataObject();
            dataObject.SetData("PNG", pngStream, false);
            dataObject.SetData(DataFormats.Dib, new System.IO.MemoryStream(dibBytes), false);
            Clipboard.SetDataObject(dataObject, true);
        }

        // スケール取得メソッド
        private double GetScale()
        {
            if (Canvas == null)
            { return 1.00; }
            double canvasWidth = Canvas.ActualWidth;
            double canvasHeight = Canvas.ActualHeight;
            canvasHeight = Math.Max(canvasHeight, 100.0); /// 仮
            double baseDimension = Math.Max(PileSection.PileDiameter + 150.0, 1200.0);
            return Math.Min(canvasWidth, canvasHeight) / baseDimension;
        }

        // 断面描画メソッド
        public void RedrawShapes()
        {
            // 描画用パスをクリア
            DrawingGeometry = new PathGeometry();
            if (Canvas == null)
            { return; }

            Scale = GetScale();

            Canvas.Children.Clear();

            DrawGauge();


            if (PileSection.PileBodyType == "場所打ち鉄筋コンクリート杭" ||
                (PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && PileSection.PileSectionType == "鉄筋コンクリート部"))
            {
                double concreteOutDia = PileSection.ConcreteOutDia;
                DrawDonut("concrete", concreteOutDia, 0.0);
                int number = PileSection.MainBarNum;
                double dia = ExtractNumber(PileSection.MainBarSize);
                double pcd = concreteOutDia - PileSection.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
                double hoopsize = ExtractNumber(PileSection.HoopSize);
                double outDia = concreteOutDia - PileSection.HoopCenterCover * 2.0 + hoopsize;
                double inDia = outDia - 2 * hoopsize;
                DrawDonut("hoop", outDia, inDia);
            }
            else if (PileSection.PileBodyType == "場所打ち鋼管コンクリート杭" && PileSection.PileSectionType == "鋼管コンクリート部")
            {
                double concreteOutDia = PileSection.ConcreteOutDia;
                double outdia = PileSection.PipeDia;
                double india = PileSection.PipeDia - 2 * PileSection.PipeTs;
                DrawDonut("steelPipe", outdia, india);

                DrawDonut("concrete", concreteOutDia, 0.0);

                int number = PileSection.MainBarNum;
                double dia = ExtractNumber(PileSection.MainBarSize);
                double pcd = concreteOutDia - PileSection.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
            }
            else if (PileSection.PileSectionType == "PHC杭")
            {
                double outdia = PileSection.ConcreteOutDia;
                double india = PileSection.ConcreteOutDia - 2 * PileSection.ConcreteThickness;
                DrawDonut("concrete", outdia, india);
                double tendonPCD = PileSection.TendonDp;
                DrawTendons(tendonPCD);
            }
            else if (PileSection.PileSectionType == "PRC杭")
            {
                double outdia = PileSection.ConcreteOutDia;
                double india = PileSection.ConcreteOutDia - 2 * PileSection.ConcreteThickness;
                DrawDonut("concrete", outdia, india);
                int number = PileSection.MainBarNum;
                double dia = ExtractNumber(PileSection.MainBarSize);
                double pcd = outdia - PileSection.MainBarCenterCover * 2.0;
                DrawMainBars(number, dia, pcd);
                double tendonPCD = PileSection.TendonDp;
                DrawTendons(tendonPCD);
            }
            else if (PileSection.PileSectionType == "SC杭")
            {
                double outdia = PileSection.PipeDia;
                double india = PileSection.PipeDia - 2 * PileSection.PipeTs;
                DrawDonut("steelPipe", outdia, india);
                double concreteOutDia = PileSection.ConcreteOutDia;
                double concreteIndia = concreteOutDia - 2 * PileSection.ConcreteThickness;
                DrawDonut("concrete", concreteOutDia, concreteIndia);
            }
            else if (PileSection.PileBodyType == "鋼管杭")
            {
                double outdia = PileSection.PipeDia;
                double india = PileSection.PipeDia - 2 * PileSection.PipeTs;
                DrawDonut("steelPipe", outdia, india);
            }

            Path path1 = new()
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                Data = DrawingGeometry
            };
            Path path = path1;

            Canvas.Children.Add(path);
        }

        // 目盛描画メソッド
        private void DrawGauge()
        {
            // 目盛
            int minor = 100;
            int major = 500;

            LineGeometry line0 = new()
            {
                StartPoint = new Point(0.0, Canvas.ActualHeight * 0.5),
                EndPoint = new Point(50 * Scale, Canvas.ActualHeight * 0.5),
            };
            DrawingGeometry.AddGeometry(line0);
            int i = 1;

            while (i * minor * Scale <= Canvas.ActualHeight * 0.5)
            {
                double linelength = i % (major / minor) == 0 ? 50 : 25;
                LineGeometry line1 = new()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight * 0.5 + minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight * 0.5 + minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line1);

                LineGeometry line2 = new()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight * 0.5 - minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight * 0.5 - minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line2);
                i += 1;
            }
        }

        // ドーナツ描画メソッド
        private void DrawDonut(string type, double outdia, double india)
        {
            PathGeometry geometry = new();
            EllipseGeometry outerCircle = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), outdia * 0.5 * Scale, outdia * 0.5 * Scale);
            EllipseGeometry innerCircle = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), india * 0.5 * Scale, india * 0.5 * Scale);
            geometry.AddGeometry(outerCircle);
            geometry.AddGeometry(innerCircle);
            Path donutPath = new();
            if (type == "concrete")
            {
                donutPath.Stroke = NikkenBrush.SkyBlue; // 線の色
                //donutPath.Fill = Brushes.NavajoWhite;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            if (type == "steelPipe")
            {
                donutPath.Stroke = NikkenBrush.SkyBlue; // 線の色
                //donutPath.Fill = Brushes.WhiteSmoke;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }

            if (type == "hoop")
            {
                donutPath.Stroke = NikkenBrush.SkyBlue;
                //donutPath.Fill = Brushes.AntiqueWhite;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            Canvas.Children.Add(donutPath); // Canvasに追加
        }

        // PC鋼材描画メソッド
        private void DrawTendons(double dia)
        {
            EllipseGeometry circleGeometry = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), dia * 0.5 * Scale, dia * 0.5 * Scale);

            Path dashedCirclePath = new()
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection([4, 2]), // 破線パターン
                Data = circleGeometry
            };
            Canvas.Children.Add(dashedCirclePath); // Canvasに追加
        }

        // 主筋描画メソッド
        private void DrawMainBars(double number, double dia, double pcd)
        {
            PathGeometry geo = new();

            for (int i = 0; i < number; i++)
            {
                double X = pcd * 0.5 * Math.Cos(i / number * 2 * Math.PI) * Scale;
                double Y = pcd * 0.5 * Math.Sin(i / number * 2 * Math.PI) * Scale;
                EllipseGeometry ellipse = new(new Point(Canvas.ActualWidth * 0.5 + X, Canvas.ActualHeight * 0.5 + Y), dia * 0.5 * Scale, dia * 0.5 * Scale);
                geo.AddGeometry(ellipse);
            }
            Path barPath = new()
            {
                Stroke = NikkenBrush.SkyBlue,
                //Fill = Brushes.DarkBlue,
                StrokeThickness = 1,
                //StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // 破線パターン
                Data = geo
            };
            Canvas.Children.Add(barPath); // Canvasに追加
        }

        // 数字を抽出して double に変換するメソッド
        private static double ExtractNumber(string input)
        {
            if (input == null)
            {
                return 0;
            }

            // 数字以外の文字を削除してから double に変換
            string numericPart = Regex.Replace(input, "[^0-9.]", "");
            if (double.TryParse(numericPart, out double result))
            {
                return result;
            }
            // 変換できない場合は例外をスローするか、適切な処理を行う
            throw new ArgumentException("入力文字列に数字が含まれていません。");
        }
    }
}

