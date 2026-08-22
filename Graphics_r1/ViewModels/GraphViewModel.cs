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
    public partial class GraphViewModel : ObservableObject
    {
        private readonly UndoManager _undoManager = new();

        private readonly MainWindowViewModel _mainWindowViewModel;

        /// <summary>
        /// グラフが基準にする入力。
        ///
        /// 既定は「解析を実行した時点の入力」。現在の入力を混ぜると、
        /// 変位は解析時・断面は編集後という読み手が区別できない図になるため。
        /// 断面性状グラフ (N-M 相関・M-φ・M-θ) に限り「現在の入力」へ切替できる。
        /// その場合、重ねられなくなる解析結果の点は描かない
        /// (<see cref="ShowAnalysisOverlay"/> が false になる)。
        /// </summary>
        public InputModel InputModel => UseCurrentInputForCurves
            ? _mainWindowViewModel.CurrentInputModel
            : _mainWindowViewModel.ResultInputModel;

        private bool _useCurrentInputForCurves;

        /// <summary>断面性状グラフを「現在の入力」で描くか（既定 false = 解析時の入力）。</summary>
        public bool UseCurrentInputForCurves
        {
            get => _useCurrentInputForCurves;
            set
            {
                if (SetProperty(ref _useCurrentInputForCurves, value))
                {
                    OnPropertyChanged(nameof(ShowAnalysisOverlay));
                    OnPropertyChanged(nameof(GraphBasisNoteText));
                    UpdateGraph();
                }
            }
        }

        /// <summary>
        /// 断面性状グラフに解析結果を重ねてよいか。
        /// 「現在の入力」基準では、曲線と結果の点で断面が食い違うので重ねない。
        /// </summary>
        public bool ShowAnalysisOverlay => !UseCurrentInputForCurves;

        /// <summary>「現在の入力」基準のときにグラフへ添える注記。</summary>
        public string GraphBasisNoteText => UseCurrentInputForCurves
            ? "現在の入力の断面（解析結果は非表示）"
            : string.Empty;

        private bool _isGraphBasisOptionVisible;

        /// <summary>基準の切替 UI を出すか（断面性状グラフ かつ 解析結果を保持しているときのみ）。</summary>
        public bool IsGraphBasisOptionVisible
        {
            get => _isGraphBasisOptionVisible;
            set => SetProperty(ref _isGraphBasisOptionVisible, value);
        }

        /// <summary>
        /// 断面性状グラフ（曲線が入力の断面から決まり、解析結果を重ねて描くもの）か。
        ///
        /// 杭頭 M-θ 系は対象外。曲線が「解析時に杭頭ばねへ設定された構成」そのもので、
        /// 現在の入力から作り直す経路が無いため（断面から引き直すには別途 M-θ の再計算が要る）。
        /// </summary>
        private static bool IsSectionPropertyGraph(string? option) =>
            option != null &&
            (option.StartsWith("NMINT") || option.StartsWith("QNINT") || option == "定着部NMINT"
             || option == "杭体M-φ" || option == "杭体EI-φ");

        public bool IsHorizontalAnalysisDone { get; set; }
        public bool IsVerticalAnalysisDone { get; set; }
        public bool IsGroupPileSettlementAnalysisDone { get; set; }
        public bool IsVerticalBeamAnalysisDone { get; set; }

        private string _graphErrorMessage;
        public string GraphErrorMessage
        {
            get => _graphErrorMessage;
            set => SetProperty(ref _graphErrorMessage, value);
        }

        // 情報メッセージ (エラーではないが、ユーザーに伝えたい説明文。
        // 例: 剛結杭で M-θ 描画なし、解析未実行ケース など)
        private string _graphInfoMessage;
        public string GraphInfoMessage
        {
            get => _graphInfoMessage;
            set => SetProperty(ref _graphInfoMessage, value);
        }

        // RequestCloseイベントの実装
        public event EventHandler RequestClose;

        // MainWindowViewModelからAnaModelを取得
        public AnaModel AnaModel => _mainWindowViewModel?.CurrentModel;

        public ObservableCollection<string> GraphOptions { get; } = [];

        private string _selectedGraphOption;
        public string SelectedGraphOption
        {
            get => _selectedGraphOption;
            set
            {
                if (SetProperty(ref _selectedGraphOption, value))
                {
                    // 基準の切替は断面性状グラフ かつ 解析結果を保持しているときだけ意味がある。
                    // 対象外のグラフへ移ったら「解析時の入力」へ戻す
                    // (結果グラフを現在の入力で描くと混在表示になるため)。
                    bool basisApplies = IsSectionPropertyGraph(value)
                                        && _mainWindowViewModel?.HasAnalysisResultSet == true;
                    IsGraphBasisOptionVisible = basisApplies;
                    if (!basisApplies && UseCurrentInputForCurves)
                    {
                        _useCurrentInputForCurves = false;
                        OnPropertyChanged(nameof(UseCurrentInputForCurves));
                        OnPropertyChanged(nameof(ShowAnalysisOverlay));
                        OnPropertyChanged(nameof(GraphBasisNoteText));
                    }

                    // パネル切替（単一⇔3分割）を先に通知
                    OnPropertyChanged(nameof(IsMultiGraphVisible));
                    OnPropertyChanged(nameof(IsSingleGraphVisible));
                    OnPropertyChanged(nameof(PileSegmentLabel));
                    OnPropertyChanged(nameof(IsDistributedModeOptionVisible));
                    OnPropertyChanged(nameof(IsPureTheoreticalOptionVisible));
                    UpdateGraph();
                    UpdatePileSegmentDetails();
                }
            }
        }

        public ObservableCollection<string> LoadCaseOptions { get; }

        private string _loadCaseOption;
        public string LoadCaseOption
        {
            get => _loadCaseOption;
            set
            {
                if (SetProperty(ref _loadCaseOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 荷重ケースオプション描画
        private bool _isLoadCaseOptionVisible;
        public bool IsLoadCaseOptionVisible
        {
            get => _isLoadCaseOptionVisible;
            set
            {
                if (SetProperty(ref _isLoadCaseOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedLoadCaseOption;
        public string SelectedLoadCaseOption
        {
            get => _selectedLoadCaseOption;
            set
            {
                if (SetProperty(ref _selectedLoadCaseOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 案 C (2026-04-24): 水平地盤反力 / ばね割線剛性 を「単位長さあたり」表示に切替。
        // false: 総量 (kN, kN/m) + 折線 (従来挙動)
        // true : 単位長さあたり (kN/m, kN/m²) + ステップグラフ (分布風)
        private bool _isDistributedMode;
        public bool IsDistributedMode
        {
            get => _isDistributedMode;
            set
            {
                if (SetProperty(ref _isDistributedMode, value))
                {
                    UpdateGraph();
                    OnPropertyChanged(nameof(IsPureTheoreticalOptionVisible));
                }
            }
        }

        // 分布モード内での表示切替: FEM 実測へ比例スケール か 純理論値か
        // false (既定): 上下寄与の合計が FEM 実測と一致するよう scale_k = F_actual/F_theory を乗ずる
        // true         : scale_k = 1 (純理論値のみ、土層パラメータ差がそのまま見える)
        private bool _isPureTheoreticalMode;
        public bool IsPureTheoreticalMode
        {
            get => _isPureTheoreticalMode;
            set
            {
                if (SetProperty(ref _isPureTheoreticalMode, value))
                {
                    UpdateGraph();
                }
            }
        }

        // XAML から Visibility 制御用
        public bool IsDistributedModeOptionVisible => SelectedGraphOption == "杭周地盤変位反力";
        public bool IsPureTheoreticalOptionVisible => IsDistributedModeOptionVisible && IsDistributedMode;

        private ObservableCollection<string> _loadCombinationOptions;
        public ObservableCollection<string> LoadCombinationOptions
        {
            get => _loadCombinationOptions;
            set
            {
                if (SetProperty(ref _loadCombinationOptions, value))
                {
                    UpdateGraph();
                }
            }
        }

        // 荷重組合せオプション描画
        private bool _isLoadCombinationOptionVisible;
        public bool IsLoadCombinationOptionVisible
        {
            get => _isLoadCombinationOptionVisible;
            set
            {
                if (SetProperty(ref _isLoadCombinationOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedLoadCombinationOption;
        public string SelectedLoadCombinationOption
        {
            get => _selectedLoadCombinationOption;
            set
            {
                if (SetProperty(ref _selectedLoadCombinationOption, value))
                {
                    UpdateGraph();
                }
            }
        }

        private ObservableCollection<string> _pileOptions;
        public ObservableCollection<string> PileOptions
        {
            get => _pileOptions;
            set
            {
                if (SetProperty(ref _pileOptions, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedPileOption;
        public string SelectedPileOption
        {
            get => _selectedPileOption;
            set
            {
                if (SetProperty(ref _selectedPileOption, value))
                {
                    UpdateGraph();
                    UpdatePileSegmentDetails();
                }
            }
        }

        private ObservableCollection<string> _pileBodyRefOptions;
        public ObservableCollection<string> PileBodyRefOptions
        {
            get => _pileBodyRefOptions;
            set
            {
                if (SetProperty(ref _pileBodyRefOptions, value))
                {
                    UpdateGraph();
                }
            }
        }

        private string _selectedPileBodyRef;
        public string SelectedPileBodyRef
        {
            get => _selectedPileBodyRef;
            set
            {
                if (SetProperty(ref _selectedPileBodyRef, value))
                {
                    UpdateGraph();
                    var pileBody = InputModel.GetPileBodyByPileBodyRef(_selectedPileBodyRef);
                    int segmentsCount = pileBody?.PileBodySegments?.Count ?? 0;
                    var options = new ObservableCollection<string> { UiText.All };
                    foreach (int i in Enumerable.Range(1, segmentsCount)) options.Add(i.ToString());
                    PileSegmentOptions = options;
                    SelectedPileSegmentOption = UiText.All;
                }
            }
        }

        // 杭体オプション描画
        private bool _isPileBodyOptionVisible;
        public bool IsPileBodyOptionVisible
        {
            get => _isPileBodyOptionVisible;
            set
            {
                if (SetProperty(ref _isPileBodyOptionVisible, value))
                {
                    UpdateGraph();
                }
            }
        }

        private ObservableCollection<string> _pileSegmentOptions;
        public ObservableCollection<string> PileSegmentOptions
        {
            get => _pileSegmentOptions;
            set
            {
                if (SetProperty(ref _pileSegmentOptions, value))
                {
                    UpdateGraph();
                }
            }
        }


        private string _selectedPileSegmentOption = UiText.All;
        public string SelectedPileSegmentOption
        {
            get => _selectedPileSegmentOption;
            set
            {
                if (SetProperty(ref _selectedPileSegmentOption, value))
                {
                    UpdateGraph();
                    UpdatePileSegmentDetails();
                }
            }
        }

        /// <summary>選択中の杭区間番号（0-based）。UiText.All の場合は -1 を返す。</summary>
        public int SelectedPileSegmentNo
        {
            get => int.TryParse(_selectedPileSegmentOption, out int n) ? n : 0;
            set => SelectedPileSegmentOption = value <= 0 ? UiText.All : value.ToString();
        }

        // p-y グラフ時は「杭要素番号」、他（杭体区間を指す場合）は「杭区間番号」
        public string PileSegmentLabel =>
            SelectedGraphOption == "水平地盤反力度p-y" ? "杭要素番号:" : "杭区間番号:";

        private string _selectedPileSegmentDetails = string.Empty;
        /// <summary>水平地盤反力度p-y グラフで選択中杭要素に対応する地盤/深さ等の詳細。
        /// 複数杭選択時は共通する値のみ表示、差異がある項目は「—」。</summary>
        public string SelectedPileSegmentDetails
        {
            get => _selectedPileSegmentDetails;
            private set => SetProperty(ref _selectedPileSegmentDetails, value);
        }

        // 各グラフ共通のホバー用: Scatter → 詳細文字列（p-y, M-φ/EI-φ, 杭頭M-θ 等で利用）
        private readonly Dictionary<ScottPlot.Plottables.Scatter, string> _graphHoverMap = [];

        /// <summary>
        /// 描画メソッドが Scatter を登録するためのマップ。
        /// </summary>
        internal Dictionary<ScottPlot.Plottables.Scatter, string> GraphHoverMap => _graphHoverMap;


        // コンストラクタ
        public GraphViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            IsHorizontalAnalysisDone = _mainWindowViewModel.IsHorizontalAnalysisDone;
            IsVerticalAnalysisDone = _mainWindowViewModel.IsVerticalAnalysisDone;
            IsGroupPileSettlementAnalysisDone = _mainWindowViewModel.IsGroupPileSettlementAnalysisDone;
            IsVerticalBeamAnalysisDone = _mainWindowViewModel.IsVerticalBeamAnalysisDone;

            // フィルタ: 全部 / レベル絞り込み / 個別ケース
            LoadCaseOptions = [UiText.All, LoadCaseFilterLevel1, LoadCaseFilterLevel2];
            foreach (LoadCase loadCase in InputModel.LoadCasesInput.AllLoadCases)
            {
                LoadCaseOptions.Add(loadCase.LoadName);
            }
            SelectedLoadCaseOption = LoadCaseOptions[0]; // 初期値

            LoadCombinationOptions = [UiText.All];
            foreach (LoadCombination loadCombination in InputModel.LoadCasesInput.LoadCombinations)
            {
                LoadCombinationOptions.Add(loadCombination.GetName());
            }
            SelectedLoadCombinationOption = LoadCombinationOptions[0]; // 初期値

            PileOptions = [UiText.All];
            foreach (PileLayoutDataItem pile in InputModel.PileLayoutItems)
            {
                PileOptions.Add($"{pile.No}" + "X:" + pile.X + "Y:" + pile.Y + "Z:" + pile.Z);
            }
            SelectedPileOption = PileOptions[0]; // 初期値

            PileBodyRefOptions = [];
            foreach (PileBodyInput pileBody in InputModel.PileBodies)
            {
                PileBodyRefOptions.Add(pileBody.PileBodyRef);
            }
            SelectedPileBodyRef = PileBodyRefOptions[0]; // 初期値
            SelectedPileSegmentNo = 1; // 初期値

            // 通り心
            foreach (var gridItem in InputModel.GridXItems)
            {
                gridItem.SetPiles(InputModel.PileLayoutItems, 90, 0.1);
            }
            foreach (var gridItem in InputModel.GridYItems)
            {
                gridItem.SetPiles(InputModel.PileLayoutItems, 0, 0.1);
            }

            GridOptions = [];
            foreach (var gridItem in InputModel.GridXItems)
            {
                GridOptions.Add(gridItem.Name + "(X=" + $"{gridItem.Coord:N1}" + ")");
            }
            foreach (var gridItem in InputModel.GridYItems)
            {
                GridOptions.Add(gridItem.Name + "(Y=" + $"{gridItem.Coord:N1}" + ")");
            }
        }

        // イニシャル
        public void Initialize()
        {
            // 状態に応じて初期化や表示内容を切り替える
            if (IsHorizontalAnalysisDone)
            {
                GraphOptions.Clear();
                GraphOptions.Add("慣性力作用点荷重変形関係");
                //GraphOptions.Add("杭頭応力変形関係F");
                //GraphOptions.Add("杭頭応力変形関係M");
                //GraphOptions.Add("杭応力F");
                //GraphOptions.Add("杭応力M");
                //GraphOptions.Add("杭変位UH");
                GraphOptions.Add("杭変位応力");
                GraphOptions.Add("NMINT");
                GraphOptions.Add("QNINT");
                GraphOptions.Add("杭頭M-θ");
                GraphOptions.Add("杭体M-φ");
                GraphOptions.Add("杭体EI-φ");
                GraphOptions.Add("杭周地盤変位反力");
                GraphOptions.Add("水平地盤反力度p-y");
                // 土圧合力ばねが存在する場合のみ追加
                if (AnaModel.HorizontalSoilSprings.Any(s => s.NodeI?.Name == "根入部節点"))
                    GraphOptions.Add("土圧合力ばね");

                // FT-Pile / キャプテンパイル M-θ グラフ（該当する杭頭タイプが存在する場合のみ）
                if (InputModel.PileBodies.Any(pb => pb.PileTopType?.Contains("FT-Pile構法") == true))
                    GraphOptions.Add("FTPileM-θ");
                if (InputModel.PileBodies.Any(pb => pb.PileTopType?.Contains("キャプテンパイル工法") == true))
                    GraphOptions.Add("キャプテンパイルM-θ");

                // 鉄筋定着工法・定着筋方式で、杭頭が設定済みの場合のみ定着部NMINT
                if (InputModel.PileBodies.Any(pb =>
                    (pb.PileTopType?.Contains("鉄筋定着工法") == true ||
                     pb.PileTopType?.Contains("定着筋方式") == true) &&
                    pb.PileTop?.ConcreteOutDia > 0 &&
                    pb.PileTop?.MainBarNum1 > 0))
                    GraphOptions.Add("定着部NMINT");

            }
            if (IsVerticalAnalysisDone)
            {
                GraphOptions.Add("荷重沈下曲線");
                GraphOptions.Add("沈下 単杭");
            }
            if (IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("沈下 群杭");
            }
            if (IsVerticalAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("沈下 単杭+群杭");
            }
            if (IsVerticalBeamAnalysisDone)
            {
                GraphOptions.Add("沈下 基礎梁考慮単杭");
            }
            if (IsVerticalBeamAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                GraphOptions.Add("沈下 基礎梁考慮単杭+群杭");
            }
            if (_mainWindowViewModel.HasGroupSettlementBeamAwareCases)
            {
                GraphOptions.Add("沈下 個別矩形(基礎梁考慮)");
            }

            if (GraphOptions.Count > 0 && string.IsNullOrEmpty(SelectedGraphOption))
            {
                SelectedGraphOption = GraphOptions[0];
            }

            // 解析結果に基づいて液状化オプションを更新
            UpdateLiquefactionOptions();
        }

        [RelayCommand]
        private void Undo() => _undoManager.Undo();

        [RelayCommand]
        private void Redo() => _undoManager.Redo();

        [RelayCommand]
        private void OnClose()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // M-φ曲線から指定曲率に対応するモーメントを線形補間で取得
        private static double InterpolateMomentFromCurve(List<double> phis, List<double> moments, double phi)
        {
            if (phis == null || moments == null || phis.Count < 2 || phis.Count != moments.Count)
                return 0.0;

            phi = Math.Abs(phi); // 曲率は正値で評価

            // 範囲外チェック
            if (phi <= phis[0]) return moments[0];
            if (phi >= phis[^1]) return moments[^1];

            // 線形補間
            for (int i = 0; i < phis.Count - 1; i++)
            {
                if (phi >= phis[i] && phi <= phis[i + 1])
                {
                    double t = (phi - phis[i]) / (phis[i + 1] - phis[i]);
                    return moments[i] + t * (moments[i + 1] - moments[i]);
                }
            }

            return moments[^1];
        }

        // プロット
        private void ConfigurePlot(
            WpfPlot wpfPlot,
            Crosshair crosshair,
            string CrosshairPositionText,
            string title,
            string xLabel,
            string yLabel,
            int decimalPlacesX = 1,
            int decimalPlacesY = 1)

        {
            if (SelectedGraphOption.StartsWith('杭'))
            {
                if (xLabel.StartsWith('F') || xLabel.StartsWith('Q'))
                { title = "せん断力"; }
                else if (xLabel.StartsWith('M'))
                { title = "曲げモーメント"; }
                else if (xLabel.StartsWith('U'))
                { title = "変位・地盤変位"; }
                else
                { title = xLabel; }
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
            {
                wpfPlot.Plot.Axes.Title.Label.Text = title;
                wpfPlot.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

                wpfPlot.Plot.Axes.Bottom.Label.Text = xLabel;
                wpfPlot.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

                wpfPlot.Plot.Axes.Left.Label.Text = yLabel;
                wpfPlot.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

                // 凡例フォント: 日本語テキスト（「最終」等）を含むため、日本語対応フォントを使用
                wpfPlot.Plot.Legend.FontName = Fonts.Detect("最終");

                Color grayColor = new(128, 128, 128, 255);
                wpfPlot.Plot.Add.VerticalLine(0, 1, grayColor);
                wpfPlot.Plot.Add.HorizontalLine(0, 1, grayColor);

                wpfPlot.Plot.Axes.AutoScale();
                wpfPlot.Plot.Axes.AutoScaleExpandX();
                wpfPlot.Plot.Axes.AutoScaleExpandY();
            }

            //int decimalPlacesX = 3;
            //int decimalPlacesY = 3;
            // クロスヘアの初期化
            crosshair = PlotHelper.InitCrosshair(wpfPlot, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (SelectedGraphOption.StartsWith('杭'))
            {
                if (xLabel.StartsWith('F'))
                { decimalPlacesX = 1; }
                else if (xLabel.StartsWith('M'))
                { decimalPlacesX = 1; }
                else if (xLabel.StartsWith('U'))
                { decimalPlacesX = 1; }
                else
                { decimalPlacesX = 1; }
            }
            else if (SelectedGraphOption.StartsWith("NMINT") || SelectedGraphOption.StartsWith("QNINT"))
            {
                decimalPlacesX = 1;
                decimalPlacesY = 1;
            }
            else if (SelectedGraphOption.StartsWith("慣性力作用点荷重変形関係"))
            {
                decimalPlacesX = 1;
                decimalPlacesY = 1;
            }
            else if (SelectedGraphOption.StartsWith("杭頭M-θ") ||
                     SelectedGraphOption == "FTPileM-θ" || SelectedGraphOption == "キャプテンパイルM-θ")
            {
                decimalPlacesX = 4;
                decimalPlacesY = 1;
            }

            wpfPlot.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, CrosshairPositionText, xLabel, yLabel, decimalPlacesX, decimalPlacesY);
            if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
            {
                wpfPlot.Refresh();
            }
        }

        // UpdateGraph 再入防止フラグ
        // Is*Visible などの property setter がここから再帰的に UpdateGraph を呼ぶと
        // Plot.Clear 後に scatter が累積してレジェンドが多重表示されるため、
        // 最外側の呼び出しのみ実処理を行う。
        private bool _isUpdatingGraph;

        // グラフ更新メソッド
        public void UpdateGraph()
        {
            if (WpfPlot == null) return;
            if (SelectedGraphOption == null) return;
            if (_isUpdatingGraph) return; // 再帰・多重呼び出しを抑制
            _isUpdatingGraph = true;
            try
            {
                UpdateGraphCore();
            }
            finally
            {
                _isUpdatingGraph = false;
            }
        }

        private void UpdateGraphCore()
        {
            WpfPlot.Plot.Clear();
            WpfPlot1.Plot.Clear();
            WpfPlot2.Plot.Clear();
            WpfPlot3.Plot.Clear();

            GraphErrorMessage = null;
            GraphInfoMessage = null;

            // 限界状態オプションをデフォルトで非表示
            IsLimitStateOptionVisible = false;
            IsMonQdSliderVisible = false;
            IsSeismicGradeOptionVisible = false;

            if (SelectedGraphOption.StartsWith("杭頭応力変形関係"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                SelectedPileOption = UiText.All;

                if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
                {
                    if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
                    {
                        WpfPlot.Plot.Clear();
                        WpfPlot.Refresh();
                    }
                    return;
                }

                foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                {
                    var beams = pileLayoutDataItem.Beams;

                    foreach (LoadCase loadCase in GetSelectedLoadCases())
                    {
                        foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                        {
                            foreach (var isLiquefaction in SelectedLiquefactionCases)
                            {
                                int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                // 解析結果がない場合はスキップ
                                if (lastStep < 0) continue;

                                // 杭頭
                                List<double> beamZs = [beams[0].NodeI.Coord.Z];
                                List<double> beamForces = [0];

                                for (int step = 0; step <= lastStep; step++)
                                {
                                    beamZs.Add(beams[0].NodeI.GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction, step).CumulativeDisp.Uh);

                                    var result = beams[0].GetBeamResult(AnaModel, loadCase, loadCombination, isLiquefaction, step);
                                    if (SelectedGraphOption.EndsWith("Fy"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Fyi);
                                    }
                                    else if (SelectedGraphOption.EndsWith("Fz"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Fzi);
                                    }
                                    else if (SelectedGraphOption.EndsWith('F'))
                                    {
                                        beamForces.Add(result.CumulativeForce.Fi);
                                    }
                                    else if (SelectedGraphOption.EndsWith("My"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Myi);
                                    }
                                    else if (SelectedGraphOption.EndsWith("Mz"))
                                    {
                                        beamForces.Add(result.CumulativeForce.Mzi);
                                    }
                                    else if (SelectedGraphOption.EndsWith('M'))
                                    {
                                        beamForces.Add(result.CumulativeForce.Mi);
                                    }
                                }

                                if (OperatingSystem.IsWindowsVersionAtLeast(7, 0))
                                {
                                    var scatter = WpfPlot.Plot.Add.Scatter(beamZs, beamForces);
                                    scatter.LegendText = GetPileLegendText(loadCase, loadCombination, isLiquefaction, pileLayoutDataItem);
                                }
                            }
                        }
                    }
                }

                string axisY = SelectedGraphOption switch
                {
                    var s when s.EndsWith("Fy") => "Fy (kN)",
                    var s when s.EndsWith("Fz") => "Fz (kN)",
                    var s when s.EndsWith('F') => "F (kN)",
                    var s when s.EndsWith("My") => "My (kNm)",
                    var s when s.EndsWith("Mz") => "Mz (kNm)",
                    var s when s.EndsWith('M') => "M (kNm)",
                    _ => string.Empty
                };

                ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", SelectedGraphOption, "水平変位(mm)", axisY);
                WpfPlot.Plot.ShowLegend();
                WpfPlot.Refresh();

            }
            else if (SelectedGraphOption.StartsWith("慣性力作用点荷重変形関係"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                SelectedPileOption = UiText.All;

                // 杭地盤ばねと土圧合力ばねを分類
                var pileSoilSprings = AnaModel.HorizontalSoilSprings
                    .Where(s => s.Name.StartsWith("杭地盤ばね")).ToList();
                var doatsuSoilSprings = AnaModel.HorizontalSoilSprings
                    .Where(s => s.NodeI?.Name == "根入部節点").ToList();

                // 重複描画防止: 同一 (LoadName, Angle, Combination.Name, IsLiq) を二重にプロットしない
                var plottedKeys = new HashSet<string>();

                // ホバーポップアップ用マップをクリア
                _graphHoverMap.Clear();

                foreach (LoadCase loadCase in GetSelectedLoadCases())
                {
                    foreach (LoadCombination loadCombination in GetSelectedLoadCombinations()
                    )
                    {
                        foreach (var isLiquefaction in SelectedLiquefactionCases)
                        {
                            int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                            // 解析結果がない場合はスキップ
                            if (lastStep < 0) continue;

                            // 重複スキップ
                            string dedupKey = $"{loadCase?.LoadName}@{loadCase?.LoadAngle:F2}|{loadCombination?.Name}|{isLiquefaction}";
                            if (!plottedKeys.Add(dedupKey)) continue;

                            List<double> disps = [0];
                            List<double> forces = [0];
                            List<double> pileSoilForces = [0];
                            List<double> doatsuSoilForces = [0];

                            for (int step = 0; step <= lastStep; step++)
                            {
                                NodeResult nodeResult = AnaModel.Nodes[0].GetNodeResult(AnaModel, loadCase, loadCombination, isLiquefaction, step);
                                double dispMm = nodeResult.CumulativeDisp.Uh * 1000.0;
                                disps.Add(dispMm);
                                forces.Add(nodeResult.CumulativedLoad.GetHorizontalAbsLoad());

                                // 杭地盤ばね反力の合計（作用荷重方向成分）
                                double pileSumFx = 0, pileSumFy = 0;
                                foreach (var spring in pileSoilSprings)
                                {
                                    var result = spring.HorizontalSpringResults?
                                        .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                          && r.LoadCombination?.No == loadCombination.No
                                                          && r.IsLiquefaction == isLiquefaction
                                                          && r.Step == step);
                                    if (result?.CumulativeForce != null)
                                    {
                                        pileSumFx += result.CumulativeForce.Fxi;
                                        pileSumFy += result.CumulativeForce.Fyi;
                                    }
                                }
                                // 載荷方向への射影
                                double radLC = loadCase.LoadAngle * Math.PI / 180.0;
                                double cosLC = Math.Cos(radLC);
                                double sinLC = Math.Sin(radLC);
                                pileSoilForces.Add(pileSumFx * cosLC + pileSumFy * sinLC);

                                // 土圧合力ばね反力の合計（作用荷重方向成分）
                                double doatsuSumFx = 0, doatsuSumFy = 0;
                                foreach (var spring in doatsuSoilSprings)
                                {
                                    var result = spring.HorizontalSpringResults?
                                        .FirstOrDefault(r => r.LoadCase?.LoadName == loadCase.LoadName
                                                          && r.LoadCombination?.No == loadCombination.No
                                                          && r.IsLiquefaction == isLiquefaction
                                                          && r.Step == step);
                                    if (result?.CumulativeForce != null)
                                    {
                                        doatsuSumFx += result.CumulativeForce.Fxi;
                                        doatsuSumFy += result.CumulativeForce.Fyi;
                                    }
                                }
                                doatsuSoilForces.Add(doatsuSumFx * cosLC + doatsuSumFy * sinLC);
                            }

                            string legend = GetGeneralLegendText(loadCase, loadCombination, isLiquefaction);

                            // ケースごとに共通の色を決定（3 系列を同色に揃え、線種で区別）
                            var caseColor = GetCaseColor(plottedKeys.Count - 1);

                            // ホバー共通情報
                            string hoverHeader = $"ケース: {loadCase.LoadName}@{loadCase.LoadAngle:F0}°\n"
                                + $"組合せ: cmb{loadCombination.No} "
                                + $"(αL={loadCombination.Alpha1:F2}/βU={loadCombination.Beta1:F2}/βL={loadCombination.Beta2:F2})\n"
                                + $"液状化: {(isLiquefaction ? "考慮" : "非考慮")}\n"
                                + $"ステップ数: {lastStep}";

                            var scatter = WpfPlot.Plot.Add.Scatter(disps, forces);
                            scatter.LegendText = $"慣性力 {legend}";
                            scatter.Color = caseColor;
                            scatter.MarkerStyle.FillColor = caseColor;
                            scatter.MarkerStyle.LineColor = caseColor;
                            _graphHoverMap[scatter] = hoverHeader
                                + $"\n系列: 作用点水平荷重"
                                + $"\n最終 変位: {disps[^1]:N2} mm"
                                + $"\n最終 荷重: {forces[^1]:N1} kN";

                            if (pileSoilSprings.Count > 0)
                            {
                                var scatterPile = WpfPlot.Plot.Add.Scatter(disps, pileSoilForces);
                                scatterPile.LegendText = $"杭地盤ばね反力 {legend}";
                                scatterPile.LineStyle.Pattern = ScottPlot.LinePattern.Dashed;
                                scatterPile.Color = caseColor;
                                scatterPile.MarkerStyle.FillColor = caseColor;
                                scatterPile.MarkerStyle.LineColor = caseColor;
                                _graphHoverMap[scatterPile] = hoverHeader
                                    + $"\n系列: 杭地盤ばね反力合計"
                                    + $"\n最終 変位: {disps[^1]:N2} mm"
                                    + $"\n最終 反力: {pileSoilForces[^1]:N1} kN"
                                    + $"\nばね本数: {pileSoilSprings.Count}";
                            }

                            if (doatsuSoilSprings.Count > 0)
                            {
                                var scatterDoatsu = WpfPlot.Plot.Add.Scatter(disps, doatsuSoilForces);
                                scatterDoatsu.LegendText = $"土圧合力ばね反力 {legend}";
                                scatterDoatsu.LineStyle.Pattern = ScottPlot.LinePattern.Dotted;
                                scatterDoatsu.Color = caseColor;
                                scatterDoatsu.MarkerStyle.FillColor = caseColor;
                                scatterDoatsu.MarkerStyle.LineColor = caseColor;
                                _graphHoverMap[scatterDoatsu] = hoverHeader
                                    + $"\n系列: 土圧合力ばね反力合計"
                                    + $"\n最終 変位: {disps[^1]:N2} mm"
                                    + $"\n最終 反力: {doatsuSoilForces[^1]:N1} kN"
                                    + $"\nばね本数: {doatsuSoilSprings.Count}";
                            }
                        }
                    }
                }
                ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", SelectedGraphOption, "作用点変形 (mm)", "作用点水平荷重(kN)");
                WpfPlot.Plot.ShowLegend();
                WpfPlot.Refresh();
            }
            else if (SelectedGraphOption.StartsWith("杭応力"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                (string forceType, string unit) = SelectedGraphOption switch
                {
                    var s when s.EndsWith("Fy") => ("Fy", "kN"),
                    var s when s.EndsWith("Fz") => ("Fz", "kN"),
                    var s when s.EndsWith('F') => ("F", "kN"),
                    var s when s.EndsWith("My") => ("My", "kNm"),
                    var s when s.EndsWith("Mz") => ("Mz", "kNm"),
                    var s when s.EndsWith('M') => ("M", "kNm"),
                    _ => (string.Empty, string.Empty)
                };

                DrawPileForce(WpfPlot, MyCrosshair, "CrosshairPositionText", forceType, unit);
            }

            else if (SelectedGraphOption.StartsWith("杭変位") && SelectedGraphOption != "杭変位応力")
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                (string dispType, string unit) = SelectedGraphOption switch
                {
                    var s when s.EndsWith("UX") => ("UX", "m"),
                    var s when s.EndsWith("UY") => ("UY", "m"),
                    var s when s.EndsWith("UH") => ("UH", "m"),  // 旧 "U" を "UH" にリネーム (節点変位ドロップダウンと統一)
                    _ => (string.Empty, string.Empty)
                };

                DrawPileDisp(WpfPlot, MyCrosshair, "CrosshairPositionText", dispType, unit);
            }

            else if (SelectedGraphOption.StartsWith("NMINT"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;
                IsSeismicGradeOptionVisible = true;

                WpfPlot.Plot.Clear();
                _graphHoverMap.Clear();
                EnsurePileSegmentOptionsForCurrentGraph();

                // 杭体と杭区間の取得（境界チェック付き）
                var pileBody = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
                if (pileBody?.PileBodySegments == null || SelectedPileSegmentNo < 1 || SelectedPileSegmentNo > pileBody.PileBodySegments.Count)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Refresh();
                    return;
                }

                var pileSection = pileBody.PileBodySegments[SelectedPileSegmentNo - 1].PileSection;
                if (pileSection == null)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Refresh();
                    return;
                }

                // 共通ホバー詳細（NMINT: 杭体/区間/PileSection）
                string nmSectionDetails =
                    $"杭体: {SelectedPileBodyRef} / 入力杭区間 No: {SelectedPileSegmentNo}\n" +
                    $"杭種: {pileSection.PileBodyType}\n" +
                    $"断面種別: {pileSection.PileSectionType}\n" +
                    $"杭径 D: {pileSection.PileDiameter:F0} mm\n" +
                    $"杭断面: {pileSection.PileDescription}";

                // 性能グレードに応じた限界曲線の選択:
                //   グレードA: レベル1 損傷限界 (NikkenGreen) + レベル2 安全限界 (NikkenPaleRed)
                //   グレードS: レベル2 損傷限界 (NikkenGreen)
                var damageColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0x23 << 16) | (0x89 << 8) | 0x66))); // NikkenGreen #238966
                var ultimateColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0xE9 << 16) | (0x55 << 8) | 0x41))); // NikkenPaleRed #E95541

                bool isGradeA = SelectedSeismicGrade == "A";
                int damageLevel = isGradeA ? 1 : 2;
                string damageLabel = ConcreteModelOptions.MapLimitStateText(isGradeA ? "レベル1 損傷限界" : "レベル2 損傷限界");

                var (factoredDmgN, factoredDmgM) = pileSection.GetFactoredDamageNM(damageLevel);
                if (factoredDmgN?.Count > 0 && factoredDmgM?.Count > 0)
                {
                    var scatterFaDamage = WpfPlot.Plot.Add.ScatterLine(factoredDmgN.ToArray(), factoredDmgM.ToArray());
                    scatterFaDamage.LegendText = $"低減後{damageLabel}";
                    scatterFaDamage.LineStyle.Color = damageColor;
                    _graphHoverMap[scatterFaDamage] = $"低減後{damageLabel}\n" + nmSectionDetails;
                }
                if (pileSection.UnfactoredDamageNM.N?.Count > 0 && pileSection.UnfactoredDamageNM.M?.Count > 0)
                {
                    var scatterUnDamage = WpfPlot.Plot.Add.ScatterLine(
                        pileSection.UnfactoredDamageNM.N.ToArray(), pileSection.UnfactoredDamageNM.M.ToArray());
                    scatterUnDamage.LegendText = $"低減前{damageLabel}";
                    scatterUnDamage.LineStyle.Pattern = LinePattern.Dashed;
                    scatterUnDamage.LineStyle.Color = damageColor;
                    _graphHoverMap[scatterUnDamage] = $"低減前{damageLabel}\n" + nmSectionDetails;
                }

                // グレードAのみ安全限界（レベル2用）を描画
                if (isGradeA)
                {
                    if (pileSection.FactoredUltimateNM.N?.Count > 0 && pileSection.FactoredUltimateNM.M?.Count > 0)
                    {
                        var scatterFaUltimate = WpfPlot.Plot.Add.ScatterLine(
                            pileSection.FactoredUltimateNM.N.ToArray(), pileSection.FactoredUltimateNM.M.ToArray());
                        scatterFaUltimate.LegendText = "低減後レベル2 安全限界";
                        scatterFaUltimate.LineStyle.Color = ultimateColor;
                        _graphHoverMap[scatterFaUltimate] = "低減後レベル2 安全限界\n" + nmSectionDetails;
                    }
                    if (pileSection.UnfactoredUltimateNM.N?.Count > 0 && pileSection.UnfactoredUltimateNM.M?.Count > 0)
                    {
                        var scatterUnUltimate = WpfPlot.Plot.Add.ScatterLine(
                            pileSection.UnfactoredUltimateNM.N.ToArray(), pileSection.UnfactoredUltimateNM.M.ToArray());
                        scatterUnUltimate.LegendText = "低減前レベル2 安全限界";
                        scatterUnUltimate.LineStyle.Pattern = LinePattern.Dashed;
                        scatterUnUltimate.LineStyle.Color = ultimateColor;
                        _graphHoverMap[scatterUnUltimate] = "低減前レベル2 安全限界\n" + nmSectionDetails;
                    }
                }

                List<double> axialForceResultsVL = [];
                List<double> momentResultsVL = [];

                List<double> axialForceResultsLevel1 = [];
                List<double> momentResultsLevel1 = [];

                List<double> axialForceResultsLevel2 = [];
                List<double> momentResultsLevel2 = [];

                if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
                {
                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                        {
                            continue;
                        }
                        LoadCase loadCase;
                        double axialForce;
                        if (SelectedLoadCaseOption == "VL0")
                        {
                            loadCase = InputModel.LoadCasesInput.LoadCaseVL0;
                            axialForce = pileLayoutDataItem.AxialForceVL0;
                        }
                        else if (SelectedLoadCaseOption == "VLadd")
                        {
                            loadCase = InputModel.LoadCasesInput.LoadCaseVLadd;
                            axialForce = pileLayoutDataItem.AxialForceVLAdditional;
                        }
                        else //if (SelectedLoadCaseOption == "VL")
                        {
                            loadCase = InputModel.LoadCasesInput.LoadCaseVL;
                            axialForce = pileLayoutDataItem.AxialForceVL;
                        }

                        axialForceResultsVL.Add(axialForce);
                        momentResultsVL.Add(0);

                        var scatterResult = WpfPlot.Plot.Add.Scatter(axialForceResultsVL.ToArray(), [.. momentResultsVL]);
                        scatterResult.LegendText = GetPileVLLegendText(loadCase, pileLayoutDataItem);
                        scatterResult.LineStyle.Width = 0;
                    }

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "MNINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }

                else
                {
                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                        {
                            continue;
                        }

                        foreach (LoadCase loadCase in GetSelectedLoadCases())
                        {
                            var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                            foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                            {
                                foreach (var isLiquefaction in SelectedLiquefactionCases)
                                {
                                    // 解析結果がない場合はスキップ
                                    int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                    if (lastStep < 0) continue;

                                    double moment = double.MinValue;
                                    double analysisFxi = 0; // 解析結果の軸力

                                    // PileBodySegmentループ
                                    bool isAllSegs = SelectedPileSegmentNo <= 0;
                                    var soilPile = InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                                    for (int i = 0; i < soilPile.PileBodySegments.Count; i++)
                                    {
                                        var pileBodySegment = soilPile.PileBodySegments[i];
                                        if (isAllSegs || pileBodySegment.No == SelectedPileSegmentNo)
                                        {
                                            // Beamsのnullチェックとインデックス範囲チェック
                                            if (pileLayoutDataItem.Beams != null && i < pileLayoutDataItem.Beams.Count)
                                            {
                                                var beamResult = pileLayoutDataItem.Beams[i]?.GetBeamResult(
                                                    AnaModel, loadCase, loadCombination, isLiquefaction);
                                                if (beamResult?.CumulativeForce != null)
                                                {
                                                    moment = Math.Max(moment, beamResult.CumulativeForce.MabsMax);
                                                    analysisFxi = beamResult.CumulativeForce.Fxi;
                                                }
                                            }
                                        }
                                    }

                                    // 入力値＋応力解析結果モード: Fxi（圧縮負）を符号反転して加算
                                    double plotAxialForce = InputModel.UseAnalysisAxialForce
                                        ? axialForce - analysisFxi
                                        : axialForce;

                                    // セグメントループの外で結果を追加
                                    if (loadCase.Level == 1)
                                    {
                                        axialForceResultsLevel1.Add(plotAxialForce);
                                        momentResultsLevel1.Add(moment);
                                    }
                                    else if (loadCase.Level == 2)
                                    {
                                        axialForceResultsLevel2.Add(plotAxialForce);
                                        momentResultsLevel2.Add(moment);
                                    }
                                }
                            }
                        }
                    }

                    // 全杭分のデータを 2 系列（Level1/Level2）にまとめて一度だけ描画
                    // レベル1: NikkenGreen, レベル2: NikkenPaleRed
                    // 「現在の入力」基準では曲線と結果の点で断面が食い違うため、点は描かない
                    if (axialForceResultsLevel1.Count > 0 && ShowAnalysisOverlay)
                    {
                        var scatterResultLevel1 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel1.ToArray(), [.. momentResultsLevel1]);
                        scatterResultLevel1.LegendText = "レベル1地震時";
                        scatterResultLevel1.LineStyle.Width = 0;
                        scatterResultLevel1.MarkerStyle.FillColor = damageColor;       // NikkenGreen
                        scatterResultLevel1.MarkerStyle.OutlineColor = damageColor;
                    }
                    if (axialForceResultsLevel2.Count > 0 && ShowAnalysisOverlay)
                    {
                        var scatterResultLevel2 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel2.ToArray(), [.. momentResultsLevel2]);
                        scatterResultLevel2.LegendText = "レベル2地震時";
                        scatterResultLevel2.LineStyle.Width = 0;
                        scatterResultLevel2.MarkerStyle.FillColor = ultimateColor;     // NikkenPaleRed
                        scatterResultLevel2.MarkerStyle.OutlineColor = ultimateColor;
                    }

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }
            }
            else if (SelectedGraphOption == "定着部NMINT")
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;

                WpfPlot.Plot.Clear();

                var pileBody = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
                if (pileBody?.PileTop == null ||
                    (pileBody.PileTopType?.Contains("鉄筋定着工法") != true &&
                     pileBody.PileTopType?.Contains("定着筋方式") != true))
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "定着部NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                    WpfPlot.Refresh();
                    return;
                }

                var pileTop = pileBody.PileTop;

                // 使用限界
                ScottPlot.Color serviceColor = default;
                if (pileTop.UnfactoredServiceNM.N?.Count > 0)
                {
                    var s = WpfPlot.Plot.Add.ScatterLine(
                        pileTop.UnfactoredServiceNM.N.ToArray(), pileTop.UnfactoredServiceNM.M.ToArray());
                    s.LegendText = ConcreteModelOptions.MapLimitStateText("使用限界");
                    serviceColor = s.LineStyle.Color;
                }

                // 損傷限界
                if (pileTop.UnfactoredDamageNM.N?.Count > 0)
                {
                    var s = WpfPlot.Plot.Add.ScatterLine(
                        pileTop.UnfactoredDamageNM.N.ToArray(), pileTop.UnfactoredDamageNM.M.ToArray());
                    s.LegendText = ConcreteModelOptions.MapLimitStateText("損傷限界");
                }

                // 安全限界
                if (pileTop.UnfactoredUltimateNM.N?.Count > 0)
                {
                    var s = WpfPlot.Plot.Add.ScatterLine(
                        pileTop.UnfactoredUltimateNM.N.ToArray(), pileTop.UnfactoredUltimateNM.M.ToArray());
                    s.LegendText = "安全限界";
                }

                // 解析結果プロット（杭頭N,M）
                List<double> axialForceResultsL1 = [];
                List<double> momentResultsL1 = [];
                List<double> axialForceResultsL2 = [];
                List<double> momentResultsL2 = [];

                foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                {
                    if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                        continue;

                    foreach (LoadCase loadCase in GetSelectedLoadCases())
                    {
                        var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                        foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                        {
                            foreach (var isLiquefaction in SelectedLiquefactionCases)
                            {
                                int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                if (lastStep < 0) continue;

                                double moment = double.MinValue;
                                double analysisFxi = 0;

                                // 杭頭（最上端要素の上端）の応力を取得
                                if (pileLayoutDataItem.Beams != null && pileLayoutDataItem.Beams.Count > 0)
                                {
                                    var beamResult = pileLayoutDataItem.Beams[0]?.GetBeamResult(
                                        AnaModel, loadCase, loadCombination, isLiquefaction);
                                    if (beamResult?.CumulativeForce != null)
                                    {
                                        moment = beamResult.CumulativeForce.MabsMax;
                                        analysisFxi = beamResult.CumulativeForce.Fxi;
                                    }
                                }

                                if (moment <= double.MinValue) continue;

                                double plotAxialForce = InputModel.UseAnalysisAxialForce
                                    ? axialForce - analysisFxi
                                    : axialForce;

                                if (loadCase.Level == 1)
                                {
                                    axialForceResultsL1.Add(plotAxialForce);
                                    momentResultsL1.Add(moment);
                                }
                                else if (loadCase.Level == 2)
                                {
                                    axialForceResultsL2.Add(plotAxialForce);
                                    momentResultsL2.Add(moment);
                                }
                            }
                        }
                    }
                }

                if (axialForceResultsL1.Count > 0 && ShowAnalysisOverlay)
                {
                    var sc = WpfPlot.Plot.Add.Scatter(axialForceResultsL1.ToArray(), momentResultsL1.ToArray());
                    sc.LegendText = "レベル1地震時";
                    sc.LineStyle.Width = 0;
                }
                if (axialForceResultsL2.Count > 0 && ShowAnalysisOverlay)
                {
                    var sc = WpfPlot.Plot.Add.Scatter(axialForceResultsL2.ToArray(), momentResultsL2.ToArray());
                    sc.LegendText = "レベル2地震時";
                    sc.LineStyle.Width = 0;
                }

                ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "定着部NMINT", "軸力(kN)", "曲げモーメント(kNm)");
                WpfPlot.Plot.ShowLegend();
                WpfPlot.Refresh();
            }
            else if (SelectedGraphOption.StartsWith("QNINT"))
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;
                IsMonQdSliderVisible = true;
                IsSeismicGradeOptionVisible = true;

                WpfPlot.Plot.Clear();
                _graphHoverMap.Clear();
                EnsurePileSegmentOptionsForCurrentGraph();

                var pileBody = InputModel.GetPileBodyByPileBodyRef(SelectedPileBodyRef);
                if (pileBody?.PileBodySegments == null || SelectedPileSegmentNo < 1 || SelectedPileSegmentNo > pileBody.PileBodySegments.Count)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Refresh();
                    return;
                }

                var pileSection = pileBody.PileBodySegments[SelectedPileSegmentNo - 1].PileSection;
                if (pileSection == null)
                {
                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Refresh();
                    return;
                }

                // 共通ホバー詳細（QNINT: 杭体/区間/PileSection/MonQd）
                string qnSectionDetails =
                    $"杭体: {SelectedPileBodyRef} / 入力杭区間 No: {SelectedPileSegmentNo}\n" +
                    $"杭種: {pileSection.PileBodyType}\n" +
                    $"断面種別: {pileSection.PileSectionType}\n" +
                    $"杭径 D: {pileSection.PileDiameter:F0} mm\n" +
                    $"杭断面: {pileSection.PileDescription}\n" +
                    $"M/(Q·d): {MonQd:N2}";

                // 性能グレードに応じた限界曲線の選択（NMINT と同じ規則）
                bool isGradeAQ = SelectedSeismicGrade == "A";
                int qDamageLevel = isGradeAQ ? 1 : 2;
                string qDamageLabel = ConcreteModelOptions.MapLimitStateText(isGradeAQ ? "レベル1 損傷限界" : "レベル2 損傷限界");

                // 損傷限界はレベル別に再計算
                var qnCurves = pileSection.ComputeQNForMonQd(MonQd, damageLevel: qDamageLevel);
                if (qnCurves.UnfactoredService.N == null)
                {
                    qnCurves = (
                        pileSection.UnfactoredServiceNQ, pileSection.FactoredServiceNQ,
                        pileSection.UnfactoredDamageNQ, pileSection.FactoredDamageNQ,
                        pileSection.UnfactoredUltimateNQ, pileSection.FactoredUltimateNQ
                    );
                }

                // 限界ごとの色: 損傷=NikkenGreen, 安全=NikkenPaleRed
                var qnDamageColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0x23 << 16) | (0x89 << 8) | 0x66))); // NikkenGreen #238966
                var qnUltimateColor = ScottPlot.Color.FromARGB(unchecked((uint)(0xFF << 24 | (0xE9 << 16) | (0x55 << 8) | 0x41))); // NikkenPaleRed #E95541

                void DrawQNCurvePair(
                    (List<double> N, List<double> Q) factored,
                    (List<double> N, List<double> Q) unfactored,
                    string label,
                    ScottPlot.Color color)
                {
                    if (factored.N?.Count > 0 && factored.Q?.Count > 0)
                    {
                        var sc = WpfPlot.Plot.Add.ScatterLine(factored.N.ToArray(), factored.Q.ToArray());
                        sc.LegendText = $"低減後{label}";
                        sc.LineStyle.Color = color;
                        _graphHoverMap[sc] = $"低減後{label}\n" + qnSectionDetails;
                    }
                    if (unfactored.N?.Count > 0 && unfactored.Q?.Count > 0)
                    {
                        var sc = WpfPlot.Plot.Add.ScatterLine(unfactored.N.ToArray(), unfactored.Q.ToArray());
                        sc.LegendText = $"低減前{label}";
                        sc.LineStyle.Pattern = LinePattern.Dashed;
                        sc.LineStyle.Color = color;
                        _graphHoverMap[sc] = $"低減前{label}\n" + qnSectionDetails;
                    }
                }

                // 損傷限界（指定レベル）を描画
                DrawQNCurvePair(qnCurves.FactoredDamage, qnCurves.UnfactoredDamage, qDamageLabel, qnDamageColor);
                // グレードAのみ安全限界を描画
                if (isGradeAQ)
                {
                    DrawQNCurvePair(qnCurves.FactoredUltimate, qnCurves.UnfactoredUltimate, "レベル2 安全限界", qnUltimateColor);
                }

                // 解析結果プロット
                List<double> axialForceResultsLevel1Q = [];
                List<double> shearResultsLevel1 = [];
                List<double> axialForceResultsLevel2Q = [];
                List<double> shearResultsLevel2 = [];

                if (SelectedLoadCaseOption == "VL0" || SelectedLoadCaseOption == "VLadd" || SelectedLoadCaseOption == "VL")
                {
                    // 常時荷重: せん断力=0
                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                            continue;

                        double axialForce = SelectedLoadCaseOption == "VL0" ? pileLayoutDataItem.AxialForceVL0
                            : SelectedLoadCaseOption == "VLadd" ? pileLayoutDataItem.AxialForceVLAdditional
                            : pileLayoutDataItem.AxialForceVL;

                        var scatter = WpfPlot.Plot.Add.Scatter([axialForce], new double[] { 0.0 });
                        scatter.LegendText = $"P{pileLayoutDataItem.PileNo}";
                        scatter.LineStyle.Width = 0;
                    }

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }
                else
                {
                    double globalMaxM = 0, globalMaxQ = 0;

                    foreach (PileLayoutDataItem pileLayoutDataItem in GetSelectedPileLayouts())
                    {
                        if (InputModel.PileBodies[pileLayoutDataItem.PileBodyNo - 1].PileBodyRef != SelectedPileBodyRef)
                            continue;

                        foreach (LoadCase loadCase in GetSelectedLoadCases())
                        {
                            var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                            foreach (LoadCombination loadCombination in GetSelectedLoadCombinations())
                            {
                                foreach (var isLiquefaction in SelectedLiquefactionCases)
                                {
                                    int lastStep = AnaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                    if (lastStep < 0) continue;

                                    double shear = double.MinValue;

                                    bool isAllSegsQ = SelectedPileSegmentNo <= 0;
                                    var soilPile = InputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1];
                                    for (int i = 0; i < soilPile.PileBodySegments.Count; i++)
                                    {
                                        var pileBodySegment = soilPile.PileBodySegments[i];
                                        if (isAllSegsQ || pileBodySegment.No == SelectedPileSegmentNo)
                                        {
                                            if (pileLayoutDataItem.Beams != null && i < pileLayoutDataItem.Beams.Count)
                                            {
                                                var beamResult = pileLayoutDataItem.Beams[i]?.GetBeamResult(
                                                    AnaModel, loadCase, loadCombination, isLiquefaction);
                                                if (beamResult?.CumulativeForce != null)
                                                {
                                                    shear = Math.Max(shear, beamResult.CumulativeForce.FabsMax);
                                                }
                                            }
                                        }
                                    }

                                    if (loadCase.Level == 1)
                                    {
                                        axialForceResultsLevel1Q.Add(axialForce);
                                        shearResultsLevel1.Add(shear);
                                    }
                                    else if (loadCase.Level == 2)
                                    {
                                        axialForceResultsLevel2Q.Add(axialForce);
                                        shearResultsLevel2.Add(shear);
                                    }
                                }
                            }
                        }

                        // 該当杭体全 beam の最大 M / Q を集計（MonQd 算定用）
                        if (pileLayoutDataItem.Beams != null)
                        {
                            foreach (var b in pileLayoutDataItem.Beams)
                            {
                                foreach (var br in b.BeamResults)
                                {
                                    if (br.CumulativeForce == null) continue;
                                    globalMaxM = Math.Max(globalMaxM, br.CumulativeForce.MabsMax);
                                    globalMaxQ = Math.Max(globalMaxQ, br.CumulativeForce.FabsMax);
                                }
                            }
                        }
                    }

                    // 全杭分のデータを 2 系列（Level1/Level2）にまとめて一度だけ描画
                    // レベル1: NikkenGreen, レベル2: NikkenPaleRed
                    if (axialForceResultsLevel1Q.Count > 0 && ShowAnalysisOverlay)
                    {
                        var scatterLevel1 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel1Q.ToArray(), [.. shearResultsLevel1]);
                        scatterLevel1.LegendText = "レベル1地震時";
                        scatterLevel1.LineStyle.Width = 0;
                        scatterLevel1.MarkerStyle.FillColor = qnDamageColor;
                        scatterLevel1.MarkerStyle.OutlineColor = qnDamageColor;
                    }
                    if (axialForceResultsLevel2Q.Count > 0 && ShowAnalysisOverlay)
                    {
                        var scatterLevel2 = WpfPlot.Plot.Add.Scatter(axialForceResultsLevel2Q.ToArray(), [.. shearResultsLevel2]);
                        scatterLevel2.LegendText = "レベル2地震時";
                        scatterLevel2.LineStyle.Width = 0;
                        scatterLevel2.MarkerStyle.FillColor = qnUltimateColor;
                        scatterLevel2.MarkerStyle.OutlineColor = qnUltimateColor;
                    }

                    // MonQd 参照値
                    double maxMonQd = 0;
                    double d = pileSection.EffectiveDepth; // [mm]
                    if (d > 0 && globalMaxQ > 0)
                        maxMonQd = (globalMaxM * 1e6) / (globalMaxQ * 1e3 * d);
                    MonQdReference = maxMonQd > 0 ? $"解析結果最大値: {maxMonQd:N2}" : "";

                    ConfigurePlot(WpfPlot, MyCrosshair, "CrosshairPositionText", "QNINT", "軸力(kN)", "せん断力(kN)");
                    WpfPlot.Plot.ShowLegend();
                    WpfPlot.Refresh();
                }
            }
            else if (SelectedGraphOption == "杭変位応力")
            {
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;
                IsLimitStateOptionVisible = true;

                // ホバーポップアップ用マップをクリア（3 パネル共通）
                _graphHoverMap.Clear();

                try { DrawPileDisp(WpfPlot1, MyCrosshair1, "CrosshairPositionText1", "UH", "mm"); }
                catch (Exception ex) { Serilog.Log.Debug($"[杭変位応力/Disp] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); GraphErrorMessage = $"変位グラフ描画エラー: {ex.Message}"; }
                try { DrawPileForce(WpfPlot2, MyCrosshair2, "CrosshairPositionText2", "F", "kN"); }
                catch (Exception ex) { Serilog.Log.Debug($"[杭変位応力/Force] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); GraphErrorMessage = $"せん断力グラフ描画エラー: {ex.Message}"; }
                try { DrawPileForce(WpfPlot3, MyCrosshair3, "CrosshairPositionText3", "M", "kNm"); }
                catch (Exception ex) { Serilog.Log.Debug($"[杭変位応力/Moment] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); GraphErrorMessage = $"曲げモーメントグラフ描画エラー: {ex.Message}"; }

            }
            else if (SelectedGraphOption == "荷重沈下曲線")
            {
                IsLoadCaseOptionVisible = false;
                IsLoadCombinationOptionVisible = false;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = false;

                DrawLoadSettlementCurve();
            }
            else if (SelectedGraphOption == "沈下 単杭" ||
                SelectedGraphOption == "沈下 群杭" ||
                SelectedGraphOption == "沈下 単杭+群杭" ||
                SelectedGraphOption == "沈下 基礎梁考慮単杭" ||
                SelectedGraphOption == "沈下 基礎梁考慮単杭+群杭" ||
                SelectedGraphOption == "沈下 個別矩形(基礎梁考慮)")
            {
                if (GridOptions.Count == 0)
                {
                    PileDesign.Services.MessageService.Show("沈下グラフを描くには、杭心を通る通り心を定義してください");
                }

                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = false;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = false;
                IsGridOptionVisible = true;


                foreach (var gridItem in InputModel.GridXItems) // Y軸平行通り心
                {
                    var gridID = (gridItem.Name + "(X=" + $"{gridItem.Coord:N1}" + ")");
                    if (SelectedGridOption == gridID)
                    {
                        DrawSettlement(gridItem, "X");
                    }
                }
                foreach (var gridItem in InputModel.GridYItems) // X軸平行通り心
                {
                    var gridID = (gridItem.Name + "(Y=" + $"{gridItem.Coord:N1}" + ")");
                    if (SelectedGridOption == gridID)
                    {
                        DrawSettlement(gridItem, "Y");
                    }
                }
            }

            else if (SelectedGraphOption.StartsWith("杭頭M-θ"))
            {
                // 杭頭M-θ: 任意の荷重ケース、任意の杭の杭頭M-θ曲線を描画（最終ステップマーカー付き）
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                DrawMThetaCurvesWithMarker(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "FTPileM-θ" || SelectedGraphOption == "キャプテンパイルM-θ")
            {
                // FT-Pile / キャプテンパイル M-θ: N値ごとの曲線群 + 最終ステップマーカー
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                DrawPileHeadTypeMTheta(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "杭体M-φ" || SelectedGraphOption == "杭体EI-φ")
            {
                // 杭体M-φ / EI-φ: 任意の荷重ケース、任意の杭、任意の要素のM-φ曲線を描画
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = true;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                EnsurePileSegmentOptionsForCurrentGraph();
                DrawMPhiCurves(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "杭周地盤変位反力")
            {
                // 水平地盤反力: 深さ-相対変位、深さ-水平地盤反力、深さ-ばね割線剛性の3パネル描画
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                // ホバーポップアップ用マップをクリア（3 パネル共通）
                _graphHoverMap.Clear();

                DrawHorizontalSoilReaction(WpfPlot1, MyCrosshair1, "CrosshairPositionText1", "RelativeDisp", "mm");
                // 案 C (v2): IsDistributedMode で単位表示を切替
                //   OFF: 総反力 [kN] / 総剛性 [kN/m]
                //   ON : 反力度 [kN/m²] (L, B で除算) / 反力係数 [kN/m³]
                string reactionUnit = IsDistributedMode ? "kN/m²" : "kN";
                string stiffnessUnit = IsDistributedMode ? "kN/m³" : "kN/m";
                DrawHorizontalSoilReaction(WpfPlot2, MyCrosshair2, "CrosshairPositionText2", "Reaction", reactionUnit);
                DrawHorizontalSoilReaction(WpfPlot3, MyCrosshair3, "CrosshairPositionText3", "SecantStiffness", stiffnessUnit);
            }
            else if (SelectedGraphOption == "水平地盤反力度p-y")
            {
                // 水平地盤反力度p-y: 各要素の理論P-y曲線 + 最終ステップマーカー
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = true;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = true;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                // 杭区間オプションを杭要素分割後のセグメント数に更新
                EnsurePileSegmentOptionsForCurrentGraph();

                DrawPyCurvesWithMarker(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            else if (SelectedGraphOption == "土圧合力ばね")
            {
                // 土圧合力ばね: 1パネルに最上点・最下点の2系列を描画
                IsLoadCaseOptionVisible = true;
                IsLoadCombinationOptionVisible = true;
                IsPileOptionVisible = false;
                IsPileBodyOptionVisible = false;
                IsPileSegmentOptionVisible = false;
                IsLiquefactionOptionVisible = true;
                IsGridOptionVisible = false;

                DrawDoatsuGoryokuBane(WpfPlot, MyCrosshair, "CrosshairPositionText");
            }
            // レジェンド描画
            UpdateLegendVisibility();

        }





        // 杭レジェンドテキスト取得メソッド
        private static string GetPileVLLegendText(LoadCase loadCase, PileLayoutDataItem pileLayoutDataItem)
        {
            return loadCase.LoadName + "|" + pileLayoutDataItem.No;
        }

        // 杭レジェンドテキスト取得メソッド
        private static string GetPileLegendText(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, PileLayoutDataItem pileLayoutDataItem)
        {
            return loadCase.LoadName + "|" + loadCombination.No + "|LIQ:" + isLiquefaction + "|" + pileLayoutDataItem.No;
        }

        // 一般レジェンド取得メソッド
        private static string GetGeneralLegendText(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction)
        {
            // 同名ケース（LoadAngle 違い）を区別するため角度を付記
            // LoadCombination は No 以外にも α₁/β₁/β₂ で同定されるため組合せ係数も付記して一意化
            return $"{loadCase.LoadName}@{loadCase.LoadAngle:F0}°|cmb{loadCombination.No}"
                 + $"(αL={loadCombination.Alpha1:F2}/βU={loadCombination.Beta1:F2}/βL={loadCombination.Beta2:F2})"
                 + $"|LIQ:{isLiquefaction}";
        }

        /// <summary>
        /// 慣性力作用点荷重変形関係グラフで、ケースごと（同一 LoadCase/Combination/Liq）に共通の色を返す。
        /// 3 系列（作用点荷重 / 杭地盤ばね反力 / 土圧合力ばね反力）を同色にして線種で区別するのに使用。
        /// </summary>
        private static ScottPlot.Color GetCaseColor(int caseIndex)
        {
            // 視認性の高い定番パレット。ケースが多い場合はインデックスで循環
            var palette = new ScottPlot.Color[]
            {
                ScottPlot.Color.FromHex("#1F77B4"), // 青
                ScottPlot.Color.FromHex("#D62728"), // 赤
                ScottPlot.Color.FromHex("#2CA02C"), // 緑
                ScottPlot.Color.FromHex("#FF7F0E"), // 橙
                ScottPlot.Color.FromHex("#9467BD"), // 紫
                ScottPlot.Color.FromHex("#8C564B"), // 茶
                ScottPlot.Color.FromHex("#E377C2"), // ピンク
                ScottPlot.Color.FromHex("#17BECF"), // シアン
            };
            int idx = ((caseIndex % palette.Length) + palette.Length) % palette.Length;
            return palette[idx];
        }


        // 沈下傾斜レジェンド取得メソッド
        // ys は mm、xs は m のため、Δy/Δx (mm/m) はそのまま「rad × 1/1000」の表記値に一致する。
        private static string GetSettlementAngleLegendText(double angle)
        {
            return $"傾斜角{angle:N1}/1000";
        }

        /// <summary>
        /// 基礎梁考慮鉛直解析結果から指定杭の沈下量を取得する
        /// </summary>
        private double GetVBSettlement(int pileNo, string loadCaseName)
        {
            var vbResults = _mainWindowViewModel.VerticalBeamCaseResults;
            if (vbResults == null || vbResults.Count == 0) return 0;

            // 荷重ケース名でマッチするケースを探す（VLの場合は最初のケース）
            FEM.VerticalBeamCaseResult caseResult = null;
            foreach (var cr in vbResults)
            {
                if (cr.LoadCaseName == loadCaseName ||
                    (loadCaseName == "VL" && cr.LoadCaseName.Contains("VL")))
                {
                    caseResult = cr;
                    break;
                }
            }
            // マッチしなければ最初のケースを使用
            caseResult ??= vbResults[0];

            if (caseResult.PileResults == null) return 0;
            foreach (var pr in caseResult.PileResults)
            {
                if (pr.PileNo == pileNo)
                    return pr.Settlement_mm;
            }
            return 0;
        }

        /// <summary>
        /// 個別矩形（基礎梁考慮）反復解析の CaseRecord から指定杭の沈下量 (mm) を取得。
        /// 荷重ケース名で CaseRecord を選び、なければアクティブケース、それも無ければ 0。
        /// </summary>
        private double GetBeamAwareCaseSettlement(PileLayoutDataItem pile, string loadCaseName)
        {
            var rec = ResolveBeamAwareCaseRecord(loadCaseName);
            if (rec == null) return 0;
            if (rec.PileSettlements_mm != null
                && rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double v))
                return v;
            return 0;
        }

        /// <summary>
        /// SelectedGraphOption に応じた地盤沈下コンタ用のグリッドデータを返す。
        /// 個別矩形（基礎梁考慮）が選ばれている場合は、対応する CaseRecord の SettlementGridData を返す。
        /// </summary>
        private IEnumerable<SettlementGridDataItem> GetSettlementGridForCurrentOption()
        {
            if (SelectedGraphOption == "沈下 個別矩形(基礎梁考慮)")
            {
                var rec = ResolveBeamAwareCaseRecord(SelectedLoadCaseOption);
                if (rec?.SettlementGridData != null && rec.SettlementGridData.Count > 0)
                    return rec.SettlementGridData;
            }
            return InputModel.PileGroupSettlement.SettlementGridData ?? [];
        }

        /// <summary>
        /// 荷重ケース名から基礎梁考慮 CaseRecord を解決する。
        /// 完全一致 → 末尾一致 (": <name>") → アクティブケース (あれば) → 最初の beam-aware ケース。
        /// </summary>
        private GroupSettlementCaseRecord ResolveBeamAwareCaseRecord(string loadCaseName)
        {
            var pgs = _mainWindowViewModel.CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return null;
            var beamAware = pgs.CaseRecords.Where(r => r.IsBeamAware).ToList();
            if (beamAware.Count == 0) return null;

            if (!string.IsNullOrEmpty(loadCaseName))
            {
                var exact = beamAware.FirstOrDefault(r => r.LoadCaseName == loadCaseName);
                if (exact != null) return exact;
                var suffix = beamAware.FirstOrDefault(r => r.LoadCaseName.EndsWith(": " + loadCaseName));
                if (suffix != null) return suffix;
            }

            int idx = pgs.ActiveCaseIndex;
            if (idx >= 0 && idx < pgs.CaseRecords.Count && pgs.CaseRecords[idx].IsBeamAware)
                return pgs.CaseRecords[idx];

            return beamAware[0];
        }

        /// <summary>
        /// NM/NQ曲線から指定軸力Nに対応する値（M or Q）を線形補間で取得
        /// </summary>
        private static double InterpolateLimitValue(List<double> nValues, List<double> mOrQValues, double targetN)
        {
            if (nValues == null || mOrQValues == null || nValues.Count == 0 || nValues.Count != mOrQValues.Count)
                return double.NaN;

            // N値の範囲外チェック
            double minN = nValues.Min();
            double maxN = nValues.Max();

            if (targetN <= minN) return mOrQValues[nValues.IndexOf(minN)];
            if (targetN >= maxN) return mOrQValues[nValues.IndexOf(maxN)];

            // N値でソートされたインデックスを作成
            var sortedIndices = nValues.Select((n, i) => new { N = n, Index = i })
                                        .OrderBy(x => x.N)
                                        .ToList();

            // 線形補間
            for (int i = 0; i < sortedIndices.Count - 1; i++)
            {
                double n1 = sortedIndices[i].N;
                double n2 = sortedIndices[i + 1].N;

                if (n1 <= targetN && targetN <= n2)
                {
                    double m1 = mOrQValues[sortedIndices[i].Index];
                    double m2 = mOrQValues[sortedIndices[i + 1].Index];

                    // 線形補間
                    double ratio = (n2 - n1) != 0 ? (targetN - n1) / (n2 - n1) : 0;
                    return m1 + ratio * (m2 - m1);
                }
            }

            return double.NaN;
        }

        /// <summary>
        /// 選択された限界状態に対応するNM曲線を取得
        /// </summary>
        private static (List<double> N, List<double> M) GetLimitStateNMCurve(PileSection pileSection, string limitState)
        {
            return limitState switch
            {
                "低減前使用限界状態" => (pileSection.UnfactoredServiceNM.N, pileSection.UnfactoredServiceNM.M),
                "低減後使用限界状態" => (pileSection.FactoredServiceNM.N, pileSection.FactoredServiceNM.M),
                "低減前損傷限界状態" => (pileSection.UnfactoredDamageNM.N, pileSection.UnfactoredDamageNM.M),
                "低減後損傷限界状態" => (pileSection.FactoredDamageNM.N, pileSection.FactoredDamageNM.M),
                "低減前安全限界状態" => (pileSection.UnfactoredUltimateNM.N, pileSection.UnfactoredUltimateNM.M),
                "低減後安全限界状態" => (pileSection.FactoredUltimateNM.N, pileSection.FactoredUltimateNM.M),
                _ => (null, null)
            };
        }

        /// <summary>
        /// 選択された限界状態に対応するNQ曲線を取得
        /// </summary>
        private static (List<double> N, List<double> Q) GetLimitStateNQCurve(PileSection pileSection, string limitState)
        {
            return limitState switch
            {
                "低減前使用限界状態" => (pileSection.UnfactoredServiceNQ.N, pileSection.UnfactoredServiceNQ.Q),
                "低減後使用限界状態" => (pileSection.FactoredServiceNQ.N, pileSection.FactoredServiceNQ.Q),
                "低減前損傷限界状態" => (pileSection.UnfactoredDamageNQ.N, pileSection.UnfactoredDamageNQ.Q),
                "低減後損傷限界状態" => (pileSection.FactoredDamageNQ.N, pileSection.FactoredDamageNQ.Q),
                "低減前安全限界状態" => (pileSection.UnfactoredUltimateNQ.N, pileSection.UnfactoredUltimateNQ.Q),
                "低減後安全限界状態" => (pileSection.FactoredUltimateNQ.N, pileSection.FactoredUltimateNQ.Q),
                _ => (null, null)
            };
        }

    }
}
