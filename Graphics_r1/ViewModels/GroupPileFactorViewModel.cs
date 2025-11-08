using CommunityToolkit.Mvvm.Input;
using LiveChartsCore.Defaults;
using PileDesign.Common;
using PileDesign.Models.InputData;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    public class GroupPileFactorViewModel : BaseViewModel
    {
        //public static InputModel InputModel => InputModel.Instance;
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        private int? _totalPileCount;
        public int? TotalPileCount
        {
            get => _totalPileCount;
            set => SetProperty(ref _totalPileCount, value);
        }

        private double? _pileSpacing;
        public double? PileSpacing
        {
            get => _pileSpacing;
            set => SetProperty(ref _pileSpacing, value);
        }

        private double? _pileDia;
        public double? PileDia
        {
            get => _pileDia;
            set => SetProperty(ref _pileDia, value);
        }

        private double? _pileSpacingDiaRatio;
        public double? PileSpacingDiaRatio
        {
            get => _pileSpacingDiaRatio;
            set => SetProperty(ref _pileSpacingDiaRatio, value);
        }

        private double? _pileGroupFactor;
        public double? PileGroupFactor
        {
            get => _pileGroupFactor;
            set => SetProperty(ref _pileGroupFactor, value);
        }

        public WpfPlot WpfPlot { get; set; }
        private Scatter MyScatter;
        public static Crosshair MyCrosshair { get; private set; }

        private string _crosshairPositionText;
        public string CrosshairPositionText
        {
            get => _crosshairPositionText;
            set => SetProperty(ref _crosshairPositionText, value);
        }

        public IRelayCommand CloseCommand { get; }

        public ICommand ApplyModelsPileNumberCommand { get; }
        public ICommand ApplyPileDistanceFactorToModelsAllPilesCommand { get; }
        public ICommand ApplyPileGroupFactorToModelsAllPilesCommand { get; }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;

        // コンストラクタ
        public GroupPileFactorViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            CloseCommand = new RelayCommand(OnClose);

            ApplyModelsPileNumberCommand = new RelayCommand(OnApplyModelsPileNumber);
            ApplyPileDistanceFactorToModelsAllPilesCommand
                = new RelayCommand(OnApplyPileDistanceFactorToModelsAllPiles);
            ApplyPileGroupFactorToModelsAllPilesCommand
                = new RelayCommand(OnApplyPileGroupFactorToModelsAllPiles);

            //チャート初期化
            UpdateGraph();
        }

        // ダイアログを閉じる
        private void OnClose()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        //チャート初期化
        public void UpdateGraph()
        {
            if (WpfPlot == null) return;

            WpfPlot.Plot.Clear();

            List<int> totalPileCounts = [4, 9, 16, 25, 36, 49, 64, 81, 100, 121, 169];
            List<double> rOnBs = [.. Enumerable.Range(0, 201).Select(x => 1.5 + x * 0.1)];

            for (int i = 0; i < totalPileCounts.Count; i++)
            {
                List<ObservablePoint> newValues = [];

                List<double> pileGroupFactors = [];
                foreach (double rOnB in rOnBs)
                {
                    double e = 1.2 / Math.Pow(totalPileCounts[i], 0.65 / rOnB);
                    double pileGroupFactor = Math.Min(Math.Pow(e, 4.0 / 3.0), 1);

                    pileGroupFactors.Add(pileGroupFactor);
                    newValues.Add(new ObservablePoint(rOnB, pileGroupFactor)); // 新しいリストに追加
                }

                var scatter = WpfPlot.Plot.Add.Scatter(rOnBs, pileGroupFactors);
                scatter.LegendText = $"N={totalPileCounts[i]}";
                scatter.MarkerSize = 0;
            }
            // 更新用Scatterを1つだけ作成
            //MyScatter = WpfPlot.Plot.Add.Scatter(new double[] { }, new double[] { });
            //MyScatter.MarkerSize = 8;
            ////MyScatter.Color = System.Drawing.Color.Red;
            //MyScatter.LegendText = "Update";

            string title = "杭間隔比R/Bと群杭係数ξの関係（基礎指針'19　図6.6.16）";
            string xLabel = "杭間隔比R/B";
            string yLabel = "群杭係数ξ";
            //WpfPlot.Plot.Axes.Left.Label.Text = "群杭係数ξ";
            WpfPlot.Plot.Axes.Title.Label.Text = title;
            WpfPlot.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);
            WpfPlot.Plot.Axes.Bottom.Label.Text = xLabel;
            WpfPlot.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);
            WpfPlot.Plot.Axes.Left.Label.Text = yLabel;
            WpfPlot.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);
            WpfPlot.Plot.ShowLegend();

            // クロスヘアの初期化
            MyCrosshair = PlotHelper.InitCrosshair(WpfPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

            // 例: グラフ初期化時
            WpfPlot.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText", "R/B", "ξ");

            WpfPlot.Refresh();
        }

        // 群杭係数をセットするメソッド
        public void ComputePileGroupFactor()
        {
            if (!PileSpacingDiaRatio.HasValue || !TotalPileCount.HasValue)
            {
                return;
            }
            double e = 1.2 / Math.Pow(TotalPileCount.Value, 0.65 / PileSpacingDiaRatio.Value);
            PileGroupFactor = Math.Min(Math.Pow(e, 4.0 / 3.0), 1);
        }

        // チャート更新
        public void ChartUpdate()
        {
            // PileSpacingDiaRatioとTotalPileCountがnullでないことを確認
            if (!PileSpacingDiaRatio.HasValue || !TotalPileCount.HasValue) return;
            if (WpfPlot == null) return;

            if (PileSpacingDiaRatio.HasValue && PileGroupFactor != null)
            {
                double x = PileSpacingDiaRatio.Value;
                double y = (double)PileGroupFactor;

                if (MyScatter != null)
                {
                    WpfPlot.Plot.Remove(MyScatter);
                }
                var color = NikkenSKColor.SkyBlue;
                MyScatter = WpfPlot.Plot.Add.Scatter([x], new double[] { y });
                MyScatter.MarkerSize = 8;
                MyScatter.MarkerColor = ScottPlot.Color.FromSKColor(color); ;
                MyScatter.MarkerFillColor = ScottPlot.Color.FromSKColor(color);
                MyScatter.LineWidth = 0;
                MyScatter.LegendText = "Update";
                WpfPlot.Refresh();
            }
        }

        public void OnApplyModelsPileNumber()
        {
            TotalPileCount = InputModel.PileLayoutItems.Count;
        }

        public void OnApplyPileDistanceFactorToModelsAllPiles()
        {
            if (PileSpacingDiaRatio.HasValue)
            {
                foreach (var item in InputModel.PileLayoutItems)
                {
                    item.PileSpacingFactor = PileSpacingDiaRatio.Value;
                }
            }
        }

        public void OnApplyPileGroupFactorToModelsAllPiles()
        {
            if (PileGroupFactor.HasValue)
            {
                foreach (var item in InputModel.PileLayoutItems)
                {
                    item.GroupPileFactor = PileGroupFactor.Value;
                }
            }
        }
    }
}
