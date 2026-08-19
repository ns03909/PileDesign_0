using PileDesign.Constants;
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


using Serilog;
namespace PileDesign.ViewModels
{
    public partial class PileSectionViewModel : ObservableObject
    {
        public PileSectionWindow PileSectionWindowInstance { get; set; }

        public readonly UndoManager _undoManager = new();
        private WpfPlot? PlotNQ => PileSectionWindowInstance?.wpfPlotNQ;

        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private int _pileBodyNo;

        [ObservableProperty]
        private int _pileSegmentNo;

        // PileSection は変更時に非バインド属性 UltimateLimitAxialForceThresholds への
        // PropertyChanged 通知も発火するため手書き維持
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

        /// <summary>
        /// 節部側面図を描くキャンバス。断面図のキャンバス (300×300 固定) は円がほぼ埋めていて
        /// 余白が無いため、側面図はその隣に置いた専用キャンバスに描く。
        /// </summary>
        public Canvas NodeSideViewCanvas { get; set; }

        /// <summary>節部側面図の表示。節杭以外では畳んで場所を取らない。</summary>
        public System.Windows.Visibility NodeSideViewVisibility =>
            PileSection != null && PileSection.IsNodularPile && PileSection.NodeDiameter > PileSection.PileDiameter
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        private PathGeometry DrawingGeometry = new();
        private double Scale;

        public static Crosshair MyCrosshair_MN { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_MN;

        public static Crosshair MyCrosshair_Mphi { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_Mphi;

        public static Crosshair MyCrosshair_Mtheta { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_Mtheta;

        public static Crosshair MyCrosshair_NQ { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_NQ;

        [ObservableProperty]
        private double _monQd = 1.0;

        partial void OnMonQdChanged(double value)
        {
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
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            ChartUpdate();
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MonQd change: failed to refresh NQ plot");
            }
        }

        // 追加: 杭種に応じて適切な N-Q グラフを描くヘルパー
        private void DrawNQForCurrentPile(double NMin, double NMax, int nDiv)
        {
            if (PileSection == null) return;

            if (PileSection.PileBodyType == PileTypeNames.InsituRc ||
                (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.RcSection))
            {
                DrawInsituReinforcedConcretePile_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.SteelPipeConcreteSection)
            {
                DrawInsituSteelPipeReinforcedConcretePile_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileBodyType == PileTypeNames.SteelPipe && PileSection.PileSectionType == PileTypeNames.CftSection)
            {
                // 鋼管杭+コンクリート充填鋼管部 は SPRC の鋼管コンクリート部 と同じ計算で描画
                DrawInsituSteelPipeReinforcedConcretePile_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileBodyType == PileTypeNames.SteelPipe && PileSection.PileSectionType == PileTypeNames.SteelPipeSection)
            {
                // 鋼管杭 (鋼管部、純鋼管区間) は SteelPipeSection の Middle ヘルパーで直接描画
                DrawSteelPipePileMiddle_NQ(nDiv);
            }
            // PHC節杭 の断面耐力は軸部基準で PHC杭 と同一
            else if (PileTypeNames.IsPhcLikeSection(PileSection.PileSectionType))
            {
                DrawPHC_NQ(NMin, NMax, nDiv);
            }
            else if (PileTypeNames.IsPrcLikeSection(PileSection.PileSectionType))
            {
                DrawPRC_NQ(NMin, NMax, nDiv);
            }
            else if (PileSection.PileSectionType == PileTypeNames.Sc)
            {
                if (PileSection.PipeTs > 0)
                    DrawSC_NQ(NMin, NMax, nDiv);
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSeismicLevel1))]
        [NotifyPropertyChangedFor(nameof(IsSeismicLevel2))]
        private int _seismicLevel = 2;

        partial void OnSeismicLevelChanged(int value)
        {
            // SeismicLevel 変更時に N-Q プロットと N-M プロット両方を再描画する
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
                            ChartUpdate(); // N-M プロットも損傷限界曲線の L1/L2 切替のため再描画
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
                Log.Warning(ex, "SeismicLevel change: failed to refresh NQ/NM plot");
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

            Serilog.Log.Debug($"Constructor: {PileSection.SelectedPrecastPile?.Name}");

            PrevPileSection = pileSection.DeepCopy(); // ShallowCopyメソッドを使用して値渡し

            PileSection.RecalculatePileDia();
        }

        [RelayCommand]
        public void Undo()
        {
            // Redo時に現在のライブ状態を復元できるよう、Undo前に履歴へ追加
            if (_undoManager.CurrentIndex == _undoManager.History.Count - 1)
            {
                _undoManager.SaveState(PileSection.DeepCopy());
            }
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is PileSection state)
            {
                PileSection = state.DeepCopy();
            }
        }

        [RelayCommand]
        public void Redo()
        {
            _undoManager.RedoSnapshot();
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

            double minN = double.MaxValue;
            UpdateSeries("(低減前)使用限界", serviceN, serviceM, NikkenSKColor.DeepBlue, isDashed: true);
            UpdateSeries("(低減前)損傷限界", damageN, damageM, NikkenSKColor.Green, isDashed: true);
            UpdateSeries("(低減前)安全限界", ultimateN, ultimateM, NikkenSKColor.PaleRed, isDashed: true);
            UpdateSeries("(低減後)使用限界", factoredServiceN, factoredServiceM, NikkenSKColor.DeepBlue);
            UpdateSeries("(低減後)損傷限界", factoredDamageN, factoredDamageM, NikkenSKColor.Green);
            UpdateSeries("(低減後)安全限界", factoredUltimateN, factoredUltimateM, NikkenSKColor.PaleRed);

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
                scatter.LegendText = ConcreteModelOptions.MapLimitStateText(legend);
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

            TryPlot(ultimateUnfactored, "(低減前) 安全限界 Q-N", NikkenSKColor.PaleRed, true, 1.5);
            TryPlot(ultimateFactored, "(低減後) 安全限界 Q-N", NikkenSKColor.PaleRed, false, 2.0);

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

        // 鋼管杭 (鋼管部) — 純鋼管区間用の SteelPipeSection を構築
        // CorrosionDepth は既に PileDiameter / CorrodedPipeTs で控除済み。
        private SteelPipeSection? CreateSteelPipeSectionMiddle()
        {
            if (PileSection == null || PileSection.PileBodyType != PileTypeNames.SteelPipe) return null;
            if (PileSection.PileDiameter <= 0 || PileSection.CorrodedPipeTs <= 0) return null;

            var (sigmaU, f) = SteelPipeGrades.GetProperties(PileSection.PipeGrade ?? "SKK400");
            // beta1 = 1.0 (低減なし)、fc = 0 (充填コンクリートなし)
            return new SteelPipeSection(
                PileSection.PileDiameter,
                PileSection.CorrodedPipeTs,
                f,
                _beta1: 1.0,
                fc: 0.0,
                sigmaB: sigmaU,
                e: 205000.0);
        }

        // 鋼管杭 (鋼管部) の N 範囲 (NMin = 引張容量負側、NMax = 圧縮容量、共に β1=1)
        // M-φ・N-Q を描画する際の軸力スウィープ範囲として用いる。
        // 軸力 100% で曲げ容量 0 となり M-φ は単点に縮退するが、GetMPhiRelationshipMiddle は
        // Md/Mu ≤ 0 で ([0.0], [0.0]) を返すため発散しない。
        private (double NMin, double NMax) ComputeSteelPipeMiddleNRange(SteelPipeSection section)
        {
            double nMax = section.NucMiddle;
            double nMin = -section.NutMiddle;
            return (nMin, nMax);
        }

        // 鋼管杭 (鋼管部) の M-φ
        private void DrawSteelPipePileMiddle_MPhiMThetaGraph(int nDiv)
        {
            var section = CreateSteelPipeSectionMiddle();
            if (section == null) return;

            var (NMin, NMax) = ComputeSteelPipeMiddleNRange(section);
            var nTargets = Enumerable.Range(0, nDiv + 1)
                .Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            // SteelPipeSection.GetMPhiRelationshipMiddle は (List<double>, List<double>) を返す
            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationshipMiddle(n));

            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: "鋼管杭の曲げモーメント-回転角関係は、杭頭部で定義されます");
        }

        // 鋼管杭 (鋼管部) の N-Q
        // 使用限界・損傷限界せん断は軸力非依存 (水平直線)。安全限界のみ Nud に依存。
        private void DrawSteelPipePileMiddle_NQ(int nDiv)
        {
            var section = CreateSteelPipeSectionMiddle();
            if (section == null) return;

            var (NMin, NMax) = ComputeSteelPipeMiddleNRange(section);
            const int iCount = 100;

            var ns = new List<double>(iCount + 1);
            var qsService = new List<double>(iCount + 1);
            var qsDamage = new List<double>(iCount + 1);
            var qsUltimate = new List<double>(iCount + 1);

            double qSvc = section.GetServiceLimitShear();
            double qDmg = section.GetDamageLimitShear();

            for (int i = 0; i <= iCount; i++)
            {
                double n = NMin + (NMax - NMin) * i / iCount;
                ns.Add(n);
                qsService.Add(qSvc);
                qsDamage.Add(qDmg);
                qsUltimate.Add(section.GetUltimateLimitShearMiddle(n));
            }

            // 鋼管杭 (鋼管部) は β1 既定 1.0 で低減前/後同一カーブ。
            PlotNQCurves(
                (qsService, ns), (qsService, ns),
                (qsDamage, ns), (qsDamage, ns),
                (qsUltimate, ns), (qsUltimate, ns));
        }

        // 共通: M-φ 曲線を描画するヘルパー
        private void PlotMPhiCurves(
            IEnumerable<double> nTargets,
            Func<double, (List<double> phis, List<double> Ms)> getMPhi,
            Func<List<double>, List<double>, List<double>?>? buildMiddlePhis = null,
            string? middleLegendPrefix = null,
            Func<double, (List<double> phis, List<double> Ms)?>? getFiberOverlay = null)
        {
            var wpf = PileSectionWindowInstance?.wpfPlotMphi;
            if (wpf is null) return;

            wpf.Plot.Clear();

            // 追加: 凡例フォントを日本語/φを含むテキストで検出して設定
            // middleLegendPrefix が "杭中間部" など日本語を含む場合を優先し、無ければ M-φ のタイトル文字列で検出
            string legendProbe = (middleLegendPrefix ?? "M-φ関係") + " φ";
            wpf.Plot.Legend.FontName = Fonts.Detect(legendProbe);

            bool fiberLegendShown = false;
            foreach (var n in nTargets)
            {
                var (phis, Ms) = getMPhi(n);
                if (phis is null || Ms is null || phis.Count == 0 || Ms.Count == 0) continue;

                // 単位変換: M [Nmm] -> [kNm]
                var Ms_kNm = Ms.Select(m => m * UnitConversion.NMM_TO_KNM).ToArray();
                var phis_1_m = phis.ToArray();

                var scatter = wpf.Plot.Add.Scatter(phis_1_m, Ms_kNm);
                scatter.LegendText = $"N={(n * UnitConversion.N_TO_KN):N0}kN";
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
                        scatterMiddle.LegendText = $"{(middleLegendPrefix ?? "中間部")} N={(n * UnitConversion.N_TO_KN):N0}kN";
                        scatterMiddle.LineWidth = 2;
                        scatterMiddle.Color = scatter.Color;
                        scatterMiddle.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                    }
                }

                // ファイバーモデル M-φ の重ね描き（同色破線）。凡例は先頭の 1 本のみに付ける（凡例爆発防止）
                if (getFiberOverlay != null)
                {
                    (List<double> phis, List<double> Ms)? fiber = null;
                    try { fiber = getFiberOverlay(n); }
                    catch { /* 安全側で無視（重ね描きのみの機能のため） */ }

                    if (fiber is { } fc && fc.phis.Count > 1 && fc.phis.Count == fc.Ms.Count)
                    {
                        var scatterFiber = wpf.Plot.Add.Scatter(
                            fc.phis.ToArray(), fc.Ms.Select(m => m * UnitConversion.NMM_TO_KNM).ToArray());
                        scatterFiber.LineWidth = 1.5f;
                        scatterFiber.Color = scatter.Color;
                        scatterFiber.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                        scatterFiber.MarkerShape = ScottPlot.MarkerShape.None;
                        scatterFiber.LegendText = fiberLegendShown ? string.Empty : "ファイバーモデル（破線）";
                        fiberLegendShown = true;
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
            // 軸ラベルと同じ単位（φ [1/mm]）で表示する（旧表記 "φ(1/m)" は誤り）
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Mphi", "φ(1/mm)", "M(kNm)", 1, 1);
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

                var Ms_kNm = Ms.Select(m => m * UnitConversion.NMM_TO_KNM).ToArray();
                var thetasArr = thetas.ToArray();

                var scatter = wpf.Plot.Add.Scatter(thetasArr, Ms_kNm);
                scatter.LegendText = $"N={(n * UnitConversion.N_TO_KN):N0}kN";
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

        // M-φ グラフにファイバーモデル曲線（破線）を重ね描きするか（ファイバー対応断面のみ）
        [ObservableProperty]
        private bool _showFiberMPhiOverlay;

        // ファイバー重ね描きトグル時に M-φ のみ再描画するため、最後に描画した軸力範囲(kN)を記憶
        // （場所打ちRC系の軽量再描画用。他杭種は ChartUpdate で再描画）
        private double? _lastMPhiNMin;
        private double? _lastMPhiNMax;
        private int _lastMPhiNDiv = 10;

        // ファイバー重ね描きチェックボックスの表示可否。
        // 対象: 場所打ちRC / 場所打ち鋼管コンクリート / 既製コンクリート杭 (PHC/PRC/SC)。
        // 鋼管杭は SteelPipeSection（別系統、M-φ が既に厳密な折線）のため対象外。
        public bool IsFiberMPhiOverlayAvailable =>
            PileSection != null &&
            (PileSection.PileBodyType == PileTypeNames.InsituRc ||
             PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete ||
             PileSection.PileBodyType == PileTypeNames.PrecastConcrete);

        partial void OnShowFiberMPhiOverlayChanged(bool value)
        {
            try
            {
                if (PileSection == null || PileSectionWindowInstance == null) return;
                if (!IsFiberMPhiOverlayAvailable) return;

                bool isInsituRc =
                    PileSection.PileBodyType == PileTypeNames.InsituRc ||
                    (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.RcSection);

                if (isInsituRc && _lastMPhiNMin is double nMin && _lastMPhiNMax is double nMax)
                {
                    // 場所打ちRC系は M-φ グラフのみ軽量再描画
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        DrawInsituReinforcedConcretePile_MPhiMThetaGraph(nMin, nMax, _lastMPhiNDiv);
                    }));
                }
                else
                {
                    // その他の対応杭種は全体再描画（SeismicLevel 変更時と同方針）
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() => ChartUpdate()));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ShowFiberMPhiOverlay change: failed to refresh M-φ plot");
            }
        }

        // 場所打ち鉄筋コンクリート杭
        public void DrawInsituReinforcedConcretePile_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            // ファイバー重ね描きトグル時の再描画用に軸力範囲(kN)を記憶（単位変換前に保存）
            _lastMPhiNMin = NMin;
            _lastMPhiNMax = NMax;
            _lastMPhiNDiv = nDiv;

            var insituConcrete = new InsituConcrete(PileSection.ConcreteOutDia, PileSection.ConcreteGsi, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var section = new InsituReinforcedConcreteSection(insituConcrete, mainBars);

            // kN -> N
            NMin *= UnitConversion.KN_TO_N;
            NMax *= UnitConversion.KN_TO_N;
            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            // M-φ: 場所打ちRC（オプション時はファイバーモデル曲線を破線で重ね描き）
            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n),
                getFiberOverlay: ShowFiberMPhiOverlay ? (n => section.GetMPhiRelationshipFiber(n)) : null);

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

            // M-φ（杭頭部 + 杭中間部(破線)。オプション時はファイバーモデル曲線も重ね描き）
            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n), BuildMiddlePhis, "杭中間部",
                getFiberOverlay: ShowFiberMPhiOverlay ? (n => section.GetMPhiRelationshipFiber(n)) : null);

            // M-θ は未定義メッセージ。鋼管杭+コンクリート充填鋼管部 は M-θ が杭頭部側 (PileTop) で
            // Kθ 線形ばねとして定義されるため、その旨を表示する。
            string mThetaMessage = PileSection?.PileBodyType == PileTypeNames.SteelPipe
                ? "鋼管杭の曲げモーメント-回転角関係は、杭頭部で定義されます"
                : "場所打ち鋼管コンクリート杭の曲げモーメント-回転角関係の定義はありません";
            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: mThetaMessage);
        }

        // 鋼管杭+コンクリート充填鋼管部 専用の M-φ (杭頭接合部)
        // SPRC のファイバー積分 (ひび割れ MCr 概念を含む) ではなく、SteelPipeSection の
        // GetMPhiRelationshipHead を使用する。これにより M-N 図 (Md, Mu) と完全に整合した
        // 4 点折れ線 (0,0) → (φMd, Md) → (φMu', Mu) → (φu, Mu) を描く。
        // 鋼管杭ではコンクリートのひび割れモーメント MCr の概念は用いない。
        // M-θ は杭頭部 PileTop 側で Kθ 線形ばねとして定義されるためメッセージを表示する。
        private void DrawSteelPipePileTopComposite_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            var sps = CreateSteelPipeSectionHead();
            if (sps == null) return;

            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            PlotMPhiCurves(nTargets, n => sps.GetMPhiRelationshipHead(n));

            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: "鋼管杭の曲げモーメント-回転角関係は、杭頭部で定義されます");
        }

        // 鋼管杭+コンクリート充填鋼管部 (杭頭) の SteelPipeSection を構築。
        // CreateSteelPipeSectionMiddle と異なり Fc を渡す (杭頭部 Md/Mu 計算に必要)。
        private SteelPipeSection? CreateSteelPipeSectionHead()
        {
            if (PileSection == null || PileSection.PileBodyType != PileTypeNames.SteelPipe) return null;
            if (PileSection.PileSectionType != PileTypeNames.CftSection) return null;
            if (PileSection.PileDiameter <= 0 || PileSection.CorrodedPipeTs <= 0) return null;

            var (sigmaU, f) = SteelPipeGrades.GetProperties(PileSection.PipeGrade ?? "SKK400");
            return new SteelPipeSection(
                PileSection.PileDiameter,
                PileSection.CorrodedPipeTs,
                f,
                _beta1: 1.0,
                fc: PileSection.ConcreteFc,
                sigmaB: sigmaU,
                e: 205000.0);
        }

        // PHC杭
        private void DrawPHC_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            var precastConcrete = new PrecastPHCConcrete(PileSection.PileDiameter, PileSection.PileDiameter - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var tendons = new Tendons(PileSection.TendonDp, PileSection.TendonAp, PileSection.TendonSigmaPy, PileSection.TendonSigmaPu);
            var section = new PHCSection(precastConcrete, tendons, PileSection.Prestress);

            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            // M-φ（beta1 はデフォルト利用。オプション時はファイバーモデル曲線も重ね描き）
            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n),
                getFiberOverlay: ShowFiberMPhiOverlay ? (n => section.GetMPhiRelationshipFiber(n)) : null);

            // M-θ は未定義メッセージ
            PlotMThetaCurves(nTargets, getMTheta: null, canPlotPredicate: null,
                notDefinedMessageIfNull: "PHC杭の曲げモーメント-回転角関係の定義はありません");
        }

        // PRC杭
        private void DrawPRC_MPhiMThetaGraph(double NMin, double NMax, int nDiv)
        {
            if (PileSection.MainBarDr <= 0 || PileSection.MainBarNum <= 0 || PileSection.MainBarSize == null)
            {
                Serilog.Log.Debug("Invalid MainBars properties. Skipping graph generation.");
                return;
            }

            var precastConcrete = new PrecastPRCConcrete(PileSection.PileDiameter, PileSection.PileDiameter - 2 * PileSection.ConcreteThickness, PileSection.ConcreteFc);
            var mainBars = new MainBars(PileSection.MainBarDr, PileSection.MainBarNum, PileSection.MainBarSpec, PileSection.MainBarSize);
            var tendons = new Tendons(PileSection.TendonDp, PileSection.TendonAp, PileSection.TendonSigmaPy, PileSection.TendonSigmaPu);
            var section = new PRCSection(precastConcrete, mainBars, tendons, PileSection.Prestress);

            var nTargets = Enumerable.Range(0, nDiv + 1).Select(i => NMin + (NMax - NMin) * i / nDiv).ToList();

            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n),
                getFiberOverlay: ShowFiberMPhiOverlay ? (n => section.GetMPhiRelationshipFiber(n)) : null);

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

            PlotMPhiCurves(nTargets, n => section.GetMPhiRelationship(n),
                getFiberOverlay: ShowFiberMPhiOverlay ? (n => section.GetMPhiRelationshipFiber(n)) : null);

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
            scatter.LegendText = ConcreteModelOptions.MapLimitStateText(title);

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

        // ===== 杭断面 ひずみ度・応力度分布グラフ =====
        // N-M 曲線上でクリックされた (N,M) に最も近い曲線上の点の (εc, φ) を、
        // 共有サービス StrainStressProfileService 経由でプロファイル表示する (全杭種対応)。
        public void ShowStrainStressAtMNPoint(double clickN_kN, double clickM_kNm)
        {
            if (PileSectionWindowInstance == null || PileSection == null) return;
            StrainStressProfileService.ShowAtClick(PileSectionWindowInstance, PileSection, clickN_kN, clickM_kNm);
        }

        public void ChartUpdate()
        {
            //スペックのセット
            PileSection.SetSpecs();

            // 杭種変更でファイバー重ね描きチェックボックスの表示可否が変わり得るため通知
            OnPropertyChanged(nameof(IsFiberMPhiOverlayAvailable));

            List<double> ns;

            if (PileSection.PileBodyType == PileTypeNames.InsituRc ||
                (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.RcSection))
            {
                var (serviceN, serviceM) = PileSection.UnfactoredServiceNM;
                var (damageN, damageM) = PileSection.UnfactoredDamageNM;
                var (ultimateN, ultimateM) = PileSection.UnfactoredUltimateNM;
                var (factoredServiceN, factoredServiceM) = PileSection.FactoredServiceNM;
                var (factoredDamageN, factoredDamageM) = PileSection.GetFactoredDamageNM(SeismicLevel);
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
                var (factoredDamageN, factoredDamageM) = PileSection.GetFactoredDamageNM(SeismicLevel);
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

            if (PileSection.PileBodyType == PileTypeNames.InsituRc ||
                (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.RcSection))
            {
                // M-φグラフの描画
                double NMin = ns[0];
                double NMax = ns[^1];

                DrawInsituReinforcedConcretePile_MPhiMThetaGraph(NMin, NMax, 10);
                DrawInsituReinforcedConcretePile_NQ(NMin, NMax, 10);
            }

            else if (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.SteelPipeConcreteSection)
            {
                // M-φグラフの描画
                double NMin = ns[0]; // kN -> N
                double NMax = ns[^1]; // kN -> N

                DrawInsituSteelPipeReinforcedConcretePile_MPhiMThetaGraph(NMin, NMax, 10);
                DrawInsituSteelPipeReinforcedConcretePile_NQ(NMin, NMax, 10);
            }

            else if (PileSection.PileBodyType == PileTypeNames.SteelPipe && PileSection.PileSectionType == PileTypeNames.CftSection)
            {
                // 鋼管杭+コンクリート充填鋼管部 は接合部 (杭頭) のみに存在する部位なので、
                // 杭中間部 (破線) 曲線は描かない。SPRC のように杭全長に存在する部位ではない。
                double NMin = ns[0];
                double NMax = ns[^1];

                DrawSteelPipePileTopComposite_MPhiMThetaGraph(NMin, NMax, 10);
                DrawInsituSteelPipeReinforcedConcretePile_NQ(NMin, NMax, 10);
            }

            else if (PileSection.PileBodyType == PileTypeNames.SteelPipe && PileSection.PileSectionType == PileTypeNames.SteelPipeSection)
            {
                // 鋼管杭 (鋼管部、純鋼管区間) は SteelPipeSection の Middle ヘルパーで直接描画。
                // UltimateLimitAxialForceThresholds は CreateSectionCalculator が null を返すため
                // 利用できないので、N 範囲は描画ヘルパー内で sNut/NucMiddle から自動計算する。
                DrawSteelPipePileMiddle_MPhiMThetaGraph(10);
                DrawSteelPipePileMiddle_NQ(10);
            }

            // PHC節杭 の断面耐力は軸部基準で PHC杭 と同一
            else if (PileTypeNames.IsPhcLikeSection(PileSection.PileSectionType))
            {
                // M-φグラフの描画
                double NMin = ns[0]; // kN -> N
                double NMax = ns[^1]; // kN -> N

                DrawPHC_MPhiMThetaGraph(NMin, NMax, 10);
                DrawPHC_NQ(NMin, NMax, 10);
            }


            else if (PileTypeNames.IsPrcLikeSection(PileSection.PileSectionType))
            {
                if (PileSection.MainBarDr <= 0 || PileSection.MainBarNum <= 0 || PileSection.PileDiameter <= 0)
                {
                    Serilog.Log.Debug("ChartUpdate skipped due to incomplete PileSection properties.");
                    return;
                }

                // M-φグラフの描画
                double NMin = ns[0]; // kN -> N
                double NMax = ns[^1]; // kN -> N

                DrawPRC_MPhiMThetaGraph(NMin, NMax, 10);
                DrawPRC_NQ(NMin, NMax, 10);
            }

            else if (PileSection.PileSectionType == PileTypeNames.Sc)
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

        // Canvasの内容を画像としてクリップボードにコピーするメソッド (共通ヘルパーへ委譲)
        public static void CopyCanvasToClipboard(Canvas canvas)
        {
            Common.ClipboardHelper.TrySetCanvasImage(canvas);
        }

        // スケール取得メソッド
        private double GetScale()
        {
            if (Canvas == null)
            { return 1.00; }
            double canvasWidth = Canvas.ActualWidth;
            double canvasHeight = Canvas.ActualHeight;
            canvasHeight = Math.Max(canvasHeight, 100.0); /// 仮
            // 節杭では節部径が軸部径より大きいので、節部径の円が切れないよう基準寸法に含める
            double baseDimension = NodularSectionDrawing.BaseDimension(PileSection);
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

            // 節杭は断面（軸部の切り口）だけでは節が分からないので、節部径の円を描き足す
            NodularSectionDrawing.DrawNodeDiameterCircle(Canvas, PileSection, Scale);

            // 節の形とピッチは軸方向にしか現れないため、隣の専用キャンバスに側面図を描く
            NodularSectionDrawing.DrawNodeSideView(NodeSideViewCanvas, PileSection);
            OnPropertyChanged(nameof(NodeSideViewVisibility));


            if (PileSection.PileBodyType == PileTypeNames.InsituRc ||
                (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.RcSection))
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
            else if (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSection.PileSectionType == PileTypeNames.SteelPipeConcreteSection)
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
            // PHC節杭 の断面は軸部そのものなので PHC杭 と同一 (ドーナツ + PC鋼棒)。
            // ※ このメソッドは画面用。docx 出力用の同一ロジックが
            //    ShapeDrawer.RedrawShapes() にあり、片方だけ直すと描画が食い違う。
            else if (PileTypeNames.IsPhcLikeSection(PileSection.PileSectionType))
            {
                double outdia = PileSection.ConcreteOutDia;
                double india = PileSection.ConcreteOutDia - 2 * PileSection.ConcreteThickness;
                DrawDonut("concrete", outdia, india);
                double tendonPCD = PileSection.TendonDp;
                DrawTendons(tendonPCD);
            }
            else if (PileTypeNames.IsPrcLikeSection(PileSection.PileSectionType))
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
            else if (PileSection.PileSectionType == PileTypeNames.Sc)
            {
                double outdia = PileSection.PipeDia;
                double india = PileSection.PipeDia - 2 * PileSection.PipeTs;
                DrawDonut("steelPipe", outdia, india);
                double concreteOutDia = PileSection.ConcreteOutDia;
                double concreteIndia = concreteOutDia - 2 * PileSection.ConcreteThickness;
                DrawDonut("concrete", concreteOutDia, concreteIndia);
            }
            else if (PileSection.PileBodyType == PileTypeNames.SteelPipe)
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
            // 外径と内径の 2 つの楕円を既定の FillRule.EvenOdd で塗ると、
            // 内径側が抜けてちょうどドーナツ (中空断面) になる。
            Path donutPath = new()
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                Data = geometry,
                // 帯筋 (hoop) は配筋を示す線なので塗らない。塗るとコンクリートを覆ってしまう。
                Fill = type switch
                {
                    "concrete" => NikkenBrush.PileConcreteFill,
                    "steelPipe" => NikkenBrush.PileSteelFill,
                    _ => null,
                },
            };
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

