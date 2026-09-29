using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace PileDesign.ViewModels
{
    public partial class GroupPileFactorViewModel : BaseViewModel
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // Undo/Redo 用の UndoManager（Common.Undo 名前空間のもの）
        private readonly PileDesign.Common.Undo.UndoManager _undoManager = new();

        [ObservableProperty]
        private int? _totalPileCount;

        [ObservableProperty]
        private double? _pileSpacing;

        [ObservableProperty]
        private double? _pileDia;

        [ObservableProperty]
        private double? _pileSpacingDiaRatio;

        [ObservableProperty]
        private double? _pileGroupFactor;

        [ObservableProperty]
        private string _validationMessage = "";

        [ObservableProperty]
        private string _crosshairPositionText;

        public WpfPlot WpfPlot { get; set; }
        private Scatter MyScatter;
        public static Crosshair MyCrosshair { get; private set; }

        public IRelayCommand CloseCommand { get; }
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }

        public ICommand ApplyModelsPileNumberCommand { get; }
        public ICommand ApplyPileDistanceFactorToModelsAllPilesCommand { get; }
        public ICommand ApplyPileGroupFactorToModelsAllPilesCommand { get; }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;

        // コンストラクタ
        public GroupPileFactorViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            CloseCommand = new ToolkitRelayCommand(OnClose);
            UndoCommand = new ToolkitRelayCommand(OnUndo, CanUndo);
            RedoCommand = new ToolkitRelayCommand(OnRedo, CanRedo);

            ApplyModelsPileNumberCommand =
                new ToolkitRelayCommand(OnApplyModelsPileNumber);
            ApplyPileDistanceFactorToModelsAllPilesCommand =
                new ToolkitRelayCommand(OnApplyPileDistanceFactorToModelsAllPiles);
            ApplyPileGroupFactorToModelsAllPilesCommand =
                new ToolkitRelayCommand(OnApplyPileGroupFactorToModelsAllPiles);

            //チャート初期化
            UpdateGraph();
        }

        // Undo 実行
        private void OnUndo()
        {
            _undoManager.Undo();
            ComputePileGroupFactor();
            ChartUpdate();
            NotifyUndoRedoChanged();
        }

        // Redo 実行
        private void OnRedo()
        {
            _undoManager.Redo();
            ComputePileGroupFactor();
            ChartUpdate();
            NotifyUndoRedoChanged();
        }

        // Undo 可能かどうか
        private bool CanUndo() => _undoManager.CanUndo;

        // Redo 可能かどうか
        private bool CanRedo() => _undoManager.CanRedo;

        // Undo/Redo ボタンの有効状態を更新
        private void NotifyUndoRedoChanged()
        {
            (UndoCommand as ToolkitRelayCommand)?.NotifyCanExecuteChanged();
            (RedoCommand as ToolkitRelayCommand)?.NotifyCanExecuteChanged();
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
                List<double> pileGroupFactors = [];
                foreach (double rOnB in rOnBs)
                {
                    // 式は Services.PileGroupFactor に 1 つだけ置く (テストが守っているのはそちら)
                    double pileGroupFactor = PileDesign.Services.PileGroupFactor.GetPileGroupFactor(totalPileCounts[i], rOnB);
                    pileGroupFactors.Add(pileGroupFactor);
                }

                var scatter = WpfPlot.Plot.Add.Scatter(rOnBs, pileGroupFactors);
                scatter.LegendText = $"N={totalPileCounts[i]}";
                scatter.MarkerSize = 0;
            }

            string title = "杭間隔比R/Bと群杭係数ξの関係（基礎指針'19　図6.6.16）";
            string xLabel = "杭間隔比R/B";
            string yLabel = "群杭係数ξ";
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
            if (!TotalPileCount.HasValue || TotalPileCount.Value <= 0)
            {
                PileGroupFactor = null;
                ValidationMessage = "杭総本数を正の整数で入力してください。";
                return;
            }
            if (!PileSpacingDiaRatio.HasValue ||
                !PileDesign.Services.PileGroupFactor.IsValidSpacingRatio(PileSpacingDiaRatio.Value))
            {
                PileGroupFactor = null;
                ValidationMessage = "杭間隔比 R/B を正の有限値で入力してください。";
                return;
            }
            if (!PileDesign.Services.PileGroupFactor.TryGetPileGroupFactor(
                TotalPileCount.Value, PileSpacingDiaRatio.Value, out double factor))
            {
                PileGroupFactor = null;
                ValidationMessage = "群杭係数を計算できません。杭総本数と R/B を確認してください。";
                return;
            }
            PileGroupFactor = factor;
            ValidationMessage = "";
        }

        public void UpdateRatioFromDimensions()
        {
            PileSpacingDiaRatio = PileSpacing is double spacing && PileDia is double diameter
                && PileDesign.Services.PileGroupFactor.IsValidSpacingRatio(spacing)
                && PileDesign.Services.PileGroupFactor.IsValidSpacingRatio(diameter)
                ? spacing / diameter : null;
            if (PileSpacingDiaRatio.HasValue &&
                !PileDesign.Services.PileGroupFactor.IsValidSpacingRatio(PileSpacingDiaRatio.Value))
                PileSpacingDiaRatio = null;
            ComputePileGroupFactor();
            ChartUpdate();
        }

        // チャート更新
        public void ChartUpdate()
        {
            // PileSpacingDiaRatioとTotalPileCountがnullでないことを確認
            if (WpfPlot == null) return;

            if (!PileSpacingDiaRatio.HasValue || !PileDesign.Services.PileGroupFactor.IsValidSpacingRatio(PileSpacingDiaRatio.Value)
                || !PileGroupFactor.HasValue || !PileDesign.Services.PileGroupFactor.IsValidFactor(PileGroupFactor.Value))
            {
                if (MyScatter != null)
                {
                    WpfPlot.Plot.Remove(MyScatter);
                    MyScatter = null;
                    WpfPlot.Refresh();
                }
                return;
            }

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
            //TotalPileCount = InputModel.PileLayoutItems.Count;
            // Undo 用に変更前の値を保存
            int? oldValue = TotalPileCount;
            int newValue = InputModel.PileLayoutItems.Count;

            // 値が同じ場合は何もしない
            if (oldValue == newValue) return;

            // 変更を適用
            TotalPileCount = newValue;
            ComputePileGroupFactor();
            ChartUpdate();

            // Undo アクションを登録
            var undoAction = new DelegateUndoAction(
                "モデルの杭総本数を適用",
                () => TotalPileCount = oldValue,
                () => TotalPileCount = newValue);

            _undoManager.Push(undoAction);
            NotifyUndoRedoChanged();
        }

        public void OnApplyPileDistanceFactorToModelsAllPiles()
        {
            if (!PileSpacingDiaRatio.HasValue ||
                !PileDesign.Services.PileGroupFactor.IsValidSpacingRatio(PileSpacingDiaRatio.Value))
            {
                ValidationMessage = "杭間隔比 R/B を正の有限値で入力してから適用してください。";
                return;
            }
            ValidationMessage = "";
            double newValue = PileSpacingDiaRatio.Value;
            _mainWindowViewModel.TryApplyInputEdit(InputModel.PileLayoutItems.Any(p => p.PileSpacingFactor != newValue), () =>
            {
                foreach (var item in InputModel.PileLayoutItems) item.PileSpacingFactor = newValue;
            });
        }

        public void OnApplyPileGroupFactorToModelsAllPiles()
        {
            if (!PileGroupFactor.HasValue ||
                !PileDesign.Services.PileGroupFactor.IsValidFactor(PileGroupFactor.Value))
            {
                ValidationMessage = "群杭係数は 0 より大きく 1 以下の有限値にしてください。";
                return;
            }
            ValidationMessage = "";
            double newValue = PileGroupFactor.Value;
            _mainWindowViewModel.TryApplyInputEdit(InputModel.PileLayoutItems.Any(p => p.GroupPileFactor != newValue), () =>
            {
                foreach (var item in InputModel.PileLayoutItems) item.GroupPileFactor = newValue;
            });
        }
    }

    /// <summary>
    /// デリゲートベースの Undo アクション
    /// </summary>
    public class DelegateUndoAction : IUndoAction
    {
        private readonly Action _undoAction;
        private readonly Action _redoAction;

        public string Description { get; }

        public DelegateUndoAction(string description, Action undoAction, Action redoAction)
        {
            Description = description;
            _undoAction = undoAction ?? throw new ArgumentNullException(nameof(undoAction));
            _redoAction = redoAction ?? throw new ArgumentNullException(nameof(redoAction));
        }

        public void Undo() => _undoAction();
        public void Redo() => _redoAction();
    }
}
