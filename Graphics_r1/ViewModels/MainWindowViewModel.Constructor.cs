using AvalonDock.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace PileDesign.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        // ステータスメッセージ
        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    StatusMessageColor = value == "要素追加モード(解除: [Esc], [Alt]+[1])" ? Brushes.Red : Brushes.Black;
                }
            }
        }

        public AnaModel CurrentModel { get; set; }

        private Brush _statusMessageColor = Brushes.Black;
        public Brush StatusMessageColor
        {
            get => _statusMessageColor;
            set => SetProperty(ref _statusMessageColor, value);
        }

        private int _selectedGroundInputModelNo;
        public int SelectedGroundInputModelNo
        {
            get => _selectedGroundInputModelNo;
            set => SetProperty(ref _selectedGroundInputModelNo, value);
        }

        public ObservableCollection<RectLoad> RectLoads
        {
            get => CurrentInputModel.PileGroupSettlement.RectLoads;
            set
            {
                if (!ReferenceEquals(CurrentInputModel.PileGroupSettlement.RectLoads, value))
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads = value ?? [];
                    //OnPropertyChanged();            // nameof(RectLoads)
                    OnPropertyChanged(nameof(RectLoads));
                    UpdateWindowAction?.Invoke();   // 画面更新が必要なら
                }
            }
        }

        [ObservableProperty] // レベル1地震時軸力
        public bool _isElastic;

        private ObservableCollection<CTreeViewData> _cTreeViewDatas = [];
        public ObservableCollection<CTreeViewData> CTreeViewDatas
        {
            get => _cTreeViewDatas;
            set => SetProperty(ref _cTreeViewDatas, value);
        }

        private void OnSelectedPileLayoutItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
        }

        public CanvasThreeDView CanvasThreeDView { get; set; }

        private ObservableCollection<int> _labelSizeOption = new(Enumerable.Range(7, 14)); // 7 to 20
        public ObservableCollection<int> LabelSizeOption
        {
            get => _labelSizeOption;
            set => SetProperty(ref _labelSizeOption, value);
        }

        // リボン最小化
        private bool _isRibbonMinimized;
        public bool IsRibbonMinimized
        {
            get => _isRibbonMinimized;
            set => SetProperty(ref _isRibbonMinimized, value);
        }

        // リボン表示/非表示
        private bool _isRibbonVisible = true;
        public bool IsRibbonVisible
        {
            get => _isRibbonVisible;
            set => SetProperty(ref _isRibbonVisible, value);
        }

        // XYZ軸トグル用プロパティ
        private bool _isCenterCoordEditorVisible;
        public bool IsCenterCoordEditorVisible
        {
            get => _isCenterCoordEditorVisible;
            set
            {
                if (_isCenterCoordEditorVisible == value) return;
                _isCenterCoordEditorVisible = value;
                OnPropertyChanged(nameof(IsCenterCoordEditorVisible));
            }
        }

        // 展開トグル用プロパティ（バブル設定）
        private bool _isBubbleSettingExpanded = false;
        public bool IsBubbleSettingExpanded
        {
            get => _isBubbleSettingExpanded;
            set => SetProperty(ref _isBubbleSettingExpanded, value);
        }

        // 展開トグル用プロパティ（矢印設定）
        private bool _isArrowSettingExpanded = false;
        public bool IsArrowSettingExpanded
        {
            get => _isArrowSettingExpanded;
            set => SetProperty(ref _isArrowSettingExpanded, value);
        }

        // プロパティ
        public LayoutAnchorable InputDataAnchorable { get; set; }

        // 慣性力描画
        private bool _isMassLoadingVisible;
        public bool IsMassLoadingVisible
        {
            get => _isMassLoadingVisible;
            set
            {
                if (SetProperty(ref _isMassLoadingVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバスを更新
                }
            }
        }


        // 軸力描画
        private bool _isAxialLoadingVisible;
        public bool IsAxialLoadingVisible
        {
            get => _isAxialLoadingVisible;
            set
            {
                if (SetProperty(ref _isAxialLoadingVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 荷重面描画
        private bool _isLoadingPlaneVisible;
        public bool IsLoadingPlaneVisible
        {
            get => _isLoadingPlaneVisible;
            set
            {
                if (SetProperty(ref _isLoadingPlaneVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 地盤変位描画
        private bool _isForcedDisplacementVisible;
        public bool IsForcedDisplacementVisible
        {
            get => _isForcedDisplacementVisible;
            set
            {
                //if (value && !IsElementSplit)
                //{
                //    MessageBox.Show("要素分割が完了していないため、地盤変位描画を有効にできません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                //    return;
                //}

                if (SetProperty(ref _isForcedDisplacementVisible, value))
                {
                    // 変位表示ONで倍率が未設定(0.0)なら、見やすい初期値にブートストラップ
                    if (value && DispDiagramMultiplier == 0.0)
                    {
                        IsDispDiagramMultiplierApplicable = true;
                        DispDiagramMultiplier = 10.0;
                    }

                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // LabelSize プロパティ
        private int _labelSize = 10;
        public int LabelSize
        {
            get => _labelSize;
            set
            {
                if (SetProperty(ref _labelSize, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // エリア塗りつぶし描画
        private bool _isAreaPainted = true;
        public bool IsAreaPainted
        {
            get => _isAreaPainted;
            set
            {
                if (SetProperty(ref _isAreaPainted, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }
        

        // バブル描画
        private bool _isBubbleVisible;
        public bool IsBubbleVisible
        {
            get => _isBubbleVisible;
            set
            {
                if (SetProperty(ref _isBubbleVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢描画
        private bool _isArrowVisible;
        public bool IsArrowVisible
        {
            get => _isArrowVisible;
            set
            {
                if (SetProperty(ref _isArrowVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // バブルサイズ
        private double _bubbleDia = 50;
        public double BubbleDia
        {
            get => _bubbleDia;
            set
            {
                if (SetProperty(ref _bubbleDia, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢印サイズ
        private double _arrowLength = 50;
        public double ArrowLength
        {
            get => _arrowLength;
            set
            {
                if (SetProperty(ref _arrowLength, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢印頭サイズ
        private double _arrowHeadLength = 15;
        public double ArrowHeadLength
        {
            get => _arrowHeadLength;
            set
            {
                if (SetProperty(ref _arrowHeadLength, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢印サイズ
        private double _arrowHeadDia = 5;
        public double ArrowHeadDia
        {
            get => _arrowHeadDia;
            set
            {
                if (SetProperty(ref _arrowHeadDia, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // LoadOption
        private ObservableCollection<string> _loadCaseNameOption = [];

        public ObservableCollection<string> LoadCaseNameOption
        {
            get => _loadCaseNameOption;
            set => SetProperty(ref _loadCaseNameOption, value);
        }

        // LoadCombination プロパティ
        private string _selectedLoadCaseName = "VL";
        public string SelectedLoadCaseName
        {
            get => _selectedLoadCaseName;
            set
            {
                if (SetProperty(ref _selectedLoadCaseName, value))
                {
                    UpdateDirectionOption();

                }
            }
        }

        private void UpdateDirectionOption()
        {
            var selectedLoadCase = CurrentInputModel.LoadCasesInput.AllLoadCases
                .FirstOrDefault(lc => lc.LoadName == SelectedLoadCaseName);

            if (selectedLoadCase == null)
            {
                DirectionOption = [];
                return;
            }

            if (selectedLoadCase.Level == 1)
            {
                DirectionOption = new ObservableCollection<string>(
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1
                        .Select(lc => lc.LoadAngle.ToString("N1"))
                );
            }
            else if (selectedLoadCase.Level == 2)
            {
                DirectionOption = new ObservableCollection<string>(
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2
                        .Select(lc => lc.LoadAngle.ToString("N1"))
                );
            }

            // 選択中の荷重ケースの角度を SelectedDirection に反映
            SelectedDirection = selectedLoadCase.LoadAngle;
        }
        //{
        //    var selectedLoadCase = CurrentInputModel.LoadCasesInput.AllLoadCases.FirstOrDefault(lc => lc.LoadName == SelectedLoadCaseName);
        //    if (selectedLoadCase == null) return;
        //    if (selectedLoadCase.Level == 1)
        //    {
        //        DirectionOption = new ObservableCollection<string>(
        //            CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Select(loadCase => loadCase.LoadAngle.ToString("N1"))
        //        );
        //    }
        //    else if (selectedLoadCase.Level == 2)
        //    {
        //        DirectionOption = new ObservableCollection<string>(
        //            CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Select(loadCase => loadCase.LoadAngle.ToString("N1"))
        //        );
        //    }
        //}

        private ObservableCollection<string> _directionOption = [];

        public ObservableCollection<string> DirectionOption
        {
            get => _directionOption;
            set => SetProperty(ref _directionOption, value);
        }

        // Direction プロパティ
        private double _selectedDirection;
        public double SelectedDirection
        {
            get => _selectedDirection;
            set => SetProperty(ref _selectedDirection, value);
        }

        // LoadCombinationOption プロパティ
        private ObservableCollection<string> _loadCombinationNameOption;
        public ObservableCollection<string> LoadCombinationNameOption
        {
            get => _loadCombinationNameOption;
            set => SetProperty(ref _loadCombinationNameOption, value);
        }

        // LoadCombination プロパティ
        private string _selectedLoadCombinationName;
        public string SelectedLoadCombinationName
        {
            get => _selectedLoadCombinationName;
            set => SetProperty(ref _selectedLoadCombinationName, value);
        }

        private ObservableCollection<string> _analysisResultContentOption = []; /*= [*/
        public ObservableCollection<string> AnalysisResultContentOption
        {
            get => _analysisResultContentOption;
            set => SetProperty(ref _analysisResultContentOption, value);
        }

        private string _analysisResultContent /*= "梁応力"*/;
        public string AnalysisResultContent
        {
            get => _analysisResultContent;
            set
            {
                if (SetProperty(ref _analysisResultContent, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        private ObservableCollection<string> _analysisResultSettlementOption = []; //= [
        public ObservableCollection<string> AnalysisResultSettlementOption
        {
            get => _analysisResultSettlementOption;
            set => SetProperty(ref _analysisResultSettlementOption, value);
        }
        private string _analysisResultSettlementType /*= "群杭"*/;
        public string AnalysisResultSettlementType
        {
            get => _analysisResultSettlementType;
            set
            {
                if (SetProperty(ref _analysisResultSettlementType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        private ObservableCollection<string> _analysisResultBeamForceOption = [
            "Fh",
            "Mh",
            "Fx",
            "Fy",
            "Fz",
            "Mx",
            "My",
            "Mz",
            ];

        public ObservableCollection<string> AnalysisResultBeamForceOption
        {
            get => _analysisResultBeamForceOption;
            set => SetProperty(ref _analysisResultBeamForceOption, value);
        }
        private string _analysisResultBeamForceType = "Mh";
        public string AnalysisResultBeamForceType
        {
            get => _analysisResultBeamForceType;
            set
            {
                if (SetProperty(ref _analysisResultBeamForceType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        private ObservableCollection<string> _analysisResultNodeDisplacementOption = [
            "UH",
            "UX",
            "UY",
            "UZ",
            "θX",
            "θY",
            "θZ",
            "θH",
            ];

        public ObservableCollection<string> AnalysisResultNodeDisplacementOption
        {
            get => _analysisResultNodeDisplacementOption;
            set => SetProperty(ref _analysisResultNodeDisplacementOption, value);
        }
        private string _analysisResultNodeDisplacementType = "UH";
        public string AnalysisResultNodeDisplacementType
        {
            get => _analysisResultNodeDisplacementType;
            set
            {
                if (SetProperty(ref _analysisResultNodeDisplacementType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        private ObservableCollection<string> _analysisSoilSpringOption = [
            "RH",
            "RX",
            "RY",
            "RZ",
            "MX",
            "MY",
            "MZ",
            "MH",
            ];

        public ObservableCollection<string> AnalysisResultSoilSpringOption
        {
            get => _analysisSoilSpringOption;
            set => SetProperty(ref _analysisSoilSpringOption, value);
        }
        private string _analysisResultSoilSpringType = "RH";
        public string AnalysisResultSoilSpringType
        {
            get => _analysisResultSoilSpringType;
            set
            {
                if (SetProperty(ref _analysisResultSoilSpringType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        // 値表示
        private bool _isSoilValueVisible = false;
        public bool IsSoilValueVisible
        {
            get => _isSoilValueVisible;
            set
            {
                if (SetProperty(ref _isSoilValueVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 解析結果値表示
        private bool _isResultValueVisible = false;
        public bool IsResultValueVisible
        {
            get => _isResultValueVisible;
            set
            {
                if (SetProperty(ref _isResultValueVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 相互排他の内側更新・再描画抑止用
        private bool _suppressMutualToggle;

        // 梁中央値表示
        private bool _isMidSpanResultValueVisibleOnly = false;
        public bool IsMidSpanResultValueVisibleOnly
        {
            get => _isMidSpanResultValueVisibleOnly;
            set
            {
                if (!SetProperty(ref _isMidSpanResultValueVisibleOnly, value)) return;

                // 自分がtrueになったら、もう片方をfalseに
                if (value && !_suppressMutualToggle)
                {
                    _suppressMutualToggle = true;
                    IsPileTopResultValueVisibleOnly = false;
                    _suppressMutualToggle = false;
                }

                if (!_suppressMutualToggle)
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新（内側更新では抑止）
            }
        }

        // 梁中央値表示
        private bool _isPileTopResultValueVisibleOnly = false;
        public bool IsPileTopResultValueVisibleOnly
        {
            get => _isPileTopResultValueVisibleOnly;
            set
            {
                if (!SetProperty(ref _isPileTopResultValueVisibleOnly, value)) return;

                // 自分がtrueになったら、もう片方をfalseに
                if (value && !_suppressMutualToggle)
                {
                    _suppressMutualToggle = true;
                    IsMidSpanResultValueVisibleOnly = false;
                    _suppressMutualToggle = false;
                }

                if (!_suppressMutualToggle)
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新（内側更新では抑止）
            }
        }

        // 値小数点位置
        private int _dicimalPlaces = 1;
        public int DecimalPlaces
        {
            get => _dicimalPlaces;
            set
            {
                if (SetProperty(ref _dicimalPlaces, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }


        // 力結果表示倍率
        private double _forceDiagramMultiplier = 1.0;
        public double ForceDiagramMultiplier
        {
            get => _forceDiagramMultiplier;
            set
            {
                if (SetProperty(ref _forceDiagramMultiplier, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 変位結果倍率適用
        private bool _isDispDiagramMultiplierApplicable = true; // trueにしっぱなし
        public bool IsDispDiagramMultiplierApplicable
        {
            get => _isDispDiagramMultiplierApplicable;
            set
            {
                if (SetProperty(ref _isDispDiagramMultiplierApplicable, value))
                {
                    if (!value) DispDiagramMultiplier = 0.0;
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 変位結果表示倍率
        private double _dispDiagramMultiplier = 0.0;
        public double DispDiagramMultiplier
        {
            get => _dispDiagramMultiplier;
            set
            {
                if (SetProperty(ref _dispDiagramMultiplier, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }


        // テキスト位置調整　
        private double _textPositionAdjuster = 0.0;
        public double TextPosiitonAdjuster
        {
            get => _textPositionAdjuster;
            set
            {
                if (SetProperty(ref _textPositionAdjuster, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // VL0合計
        public double SumVL0 => GetSumVL0();

        // VLadd合計
        public double SumVLadd => GetSumVLadd();

        // VL+VLadd合計
        public double SumVL => GetSumVL0() + GetSumVLadd();

        // VL重心
        public Point3D GravityCenterVL0 => CurrentInputModel.GetVLGravityCenter();

        // VLadd重心
        public Point3D GravityCenterVLadd => CurrentInputModel.GetVLaddGravityCenter();

        // VL+VLadd重心
        public Point3D GravityCenterVLplusVLadd => CurrentInputModel.GetVLplusVLaddGravityCenter();

        private double GetSumVL0()
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceVL0;
            return sum;
        }


        private double GetSumVLadd()
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceVLAdditional;
            return sum;
        }

        // sum（get専用の計算プロパティに変更）
        public double Sum1_1 => GetSumLevel1(1);
        public double Sum1_2 => GetSumLevel1(2);
        public double Sum1_3 => GetSumLevel1(3);
        public double Sum1_4 => GetSumLevel1(4);

        public double Sum2_1 => GetSumLevel2(1);
        public double Sum2_2 => GetSumLevel2(2);
        public double Sum2_3 => GetSumLevel2(3);
        public double Sum2_4 => GetSumLevel2(4);

        // 合計計算（null/空に強い）
        private double GetSumLevel1(int no)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceLevel1s[no - 1];
            return sum;
        }

        private double GetSumLevel2(int no)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceLevel2s[no - 1];
            return sum;
        }

        // OTM（get専用の計算プロパティに変更）
        public double OverturningMoment1_1X => GetOverturningMoment(level: 1, dir: 1, axis: 'X');
        public double OverturningMoment1_2X => GetOverturningMoment(level: 1, dir: 2, axis: 'X');
        public double OverturningMoment1_3X => GetOverturningMoment(level: 1, dir: 3, axis: 'X');
        public double OverturningMoment1_4X => GetOverturningMoment(level: 1, dir: 4, axis: 'X');

        public double OverturningMoment1_1Y => GetOverturningMoment(level: 1, dir: 1, axis: 'Y');
        public double OverturningMoment1_2Y => GetOverturningMoment(level: 1, dir: 2, axis: 'Y');
        public double OverturningMoment1_3Y => GetOverturningMoment(level: 1, dir: 3, axis: 'Y');
        public double OverturningMoment1_4Y => GetOverturningMoment(level: 1, dir: 4, axis: 'Y');

        public double OverturningMoment2_1X => GetOverturningMoment(level: 2, dir: 1, axis: 'X');
        public double OverturningMoment2_2X => GetOverturningMoment(level: 2, dir: 2, axis: 'X');
        public double OverturningMoment2_3X => GetOverturningMoment(level: 2, dir: 3, axis: 'X');
        public double OverturningMoment2_4X => GetOverturningMoment(level: 2, dir: 4, axis: 'X');

        public double OverturningMoment2_1Y => GetOverturningMoment(level: 2, dir: 1, axis: 'Y');
        public double OverturningMoment2_2Y => GetOverturningMoment(level: 2, dir: 2, axis: 'Y');
        public double OverturningMoment2_3Y => GetOverturningMoment(level: 2, dir: 3, axis: 'Y');
        public double OverturningMoment2_4Y => GetOverturningMoment(level: 2, dir: 4, axis: 'Y');

        // OTM計算ヘルパー（回転中心はVL+VLadd重心を採用）
        private double GetOverturningMoment(int level, int dir, char axis)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            // 回転中心（必要に応じて 0,0 に変更可）
            var pivot = GravityCenterVLplusVLadd;

            double sum = 0.0;
            foreach (var item in items)
            {
                // レベル/方向別の鉛直力成分
                double f = level == 1
                    ? item.AxialForceLevel1s[dir - 1]
                    : item.AxialForceLevel2s[dir - 1];

                // X回りはY距離、Y回りはX距離
                if (axis == 'X')
                    sum += f * (item.Y - pivot.Y);
                else
                    sum += f * (item.X - pivot.X);
            }
            return sum;
        }

        ////OTM
        //private double _overturningMoment1_1X;
        //public double OverturningMoment1_1X
        //{
        //    get => _overturningMoment1_1X;
        //    set
        //    {
        //        _overturningMoment1_1X = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment1_2X;
        //public double OverturningMoment1_2X
        //{
        //    get => _overturningMoment1_2X;
        //    set
        //    {
        //        _overturningMoment1_2X = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment1_3X;
        //public double OverturningMoment1_3X
        //{
        //    get => _overturningMoment1_3X;
        //    set
        //    {
        //        _overturningMoment1_3X = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment1_4X;
        //public double OverturningMoment1_4X
        //{
        //    get => _overturningMoment1_4X;
        //    set
        //    {
        //        _overturningMoment1_4X = value;
        //        OnPropertyChanged();
        //    }
        //}


        //private double _overturningMoment1_1Y;
        //public double OverturningMoment1_1Y
        //{
        //    get => _overturningMoment1_1Y;
        //    set
        //    {
        //        _overturningMoment1_1Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment1_2Y;
        //public double OverturningMoment1_2Y
        //{
        //    get => _overturningMoment1_2Y;
        //    set
        //    {
        //        _overturningMoment1_2Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment1_3Y;
        //public double OverturningMoment1_3Y
        //{
        //    get => _overturningMoment1_3Y;
        //    set
        //    {
        //        _overturningMoment1_3Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment1_4Y;
        //public double OverturningMoment1_4Y
        //{
        //    get => _overturningMoment1_4Y;
        //    set
        //    {
        //        _overturningMoment1_4Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment2_1X;
        //public double OverturningMoment2_1X
        //{
        //    get => _overturningMoment2_1X;
        //    set
        //    {
        //        _overturningMoment2_1X = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment2_2X;
        //public double OverturningMoment2_2X
        //{
        //    get => _overturningMoment2_2X;
        //    set
        //    {
        //        _overturningMoment2_2X = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment2_3X;
        //public double OverturningMoment2_3X
        //{
        //    get => _overturningMoment2_3X;
        //    set
        //    {
        //        _overturningMoment2_3X = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment2_4X;
        //public double OverturningMoment2_4X
        //{
        //    get => _overturningMoment2_4X;
        //    set
        //    {
        //        _overturningMoment2_4X = value;
        //        OnPropertyChanged();
        //    }
        //}


        //private double _overturningMoment2_1Y;
        //public double OverturningMoment2_1Y
        //{
        //    get => _overturningMoment2_1Y;
        //    set
        //    {
        //        _overturningMoment2_1Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment2_2Y;
        //public double OverturningMoment2_2Y
        //{
        //    get => _overturningMoment2_2Y;
        //    set
        //    {
        //        _overturningMoment2_2Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment2_3Y;
        //public double OverturningMoment2_3Y
        //{
        //    get => _overturningMoment2_3Y;
        //    set
        //    {
        //        _overturningMoment2_3Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        //private double _overturningMoment2_4Y;
        //public double OverturningMoment2_4Y
        //{
        //    get => _overturningMoment2_4Y;
        //    set
        //    {
        //        _overturningMoment2_4Y = value;
        //        OnPropertyChanged();
        //    }
        //}

        // 作用点描画
        private bool _isActionPointVisible = true;
        public bool IsActionPointVisible
        {
            get => _isActionPointVisible;
            set
            {
                if (SetProperty(ref _isActionPointVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 沈下検討用荷重面描画
        private bool _isSettlementLoadVisible = true;
        public bool IsSettlementLoadVisible
        {
            get => _isSettlementLoadVisible;
            set
            {
                if (SetProperty(ref _isSettlementLoadVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 液状化
        private bool _isLiquefaction = false;
        public bool IsLiquefaction
        {
            get => _isLiquefaction;
            set
            {
                if (SetProperty(ref _isLiquefaction, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 剛床描画
        private bool _isRigidFloorVisible = true;
        public bool IsRigidFloorVisible
        {
            get => _isRigidFloorVisible;
            set
            {
                if (SetProperty(ref _isRigidFloorVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // ラベル描画
        private bool _isLabelVisible = true;
        public bool IsLabelVisible
        {
            get => _isLabelVisible;
            set
            {
                if (SetProperty(ref _isLabelVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭符号ラベル描画
        private bool _isPileRefVisible = false;
        public bool IsPileRefVisible
        {
            get => _isPileRefVisible;
            set
            {
                if (SetProperty(ref _isPileRefVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 地盤符号ラベル描画
        private bool _isSoilRefVisible = false;
        public bool IsSoilRefVisible
        {
            get => _isSoilRefVisible;
            set
            {
                if (SetProperty(ref _isSoilRefVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭頭レベル(m)ラベル描画
        private bool _isPileTopLevelVisible = false;
        public bool IsPileTopLevelVisible
        {
            get => _isPileTopLevelVisible;
            set
            {
                if (SetProperty(ref _isPileTopLevelVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 郡杭係数ラベル描画
        private bool _isGroupPileFactorLabelVisible = false;
        public bool IsGroupPileFactorLabelVisible
        {
            get => _isGroupPileFactorLabelVisible;
            set
            {
                if (SetProperty(ref _isGroupPileFactorLabelVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }
        // 杭間隔比ラベル描画
        private bool _isPileDiaSpacingRatioLabelVisible = false;
        public bool IsPileDiaSpacingRatioLabelVisible
        {
            get => _isPileDiaSpacingRatioLabelVisible;
            set
            {
                if (SetProperty(ref _isPileDiaSpacingRatioLabelVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 前後ラベル描画
        private bool _isFrontPileLabelVisible = false;
        public bool IsFrontPileLabelVisible
        {
            get => _isFrontPileLabelVisible;
            set
            {
                if (SetProperty(ref _isFrontPileLabelVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 結果描画
        private bool _isAnalysisResultVisible = false;
        public bool IsAnalysisResultVisible
        {
            get => _isAnalysisResultVisible;
            set
            {
                if (value && !IsVerticalAnalysisDone && !IsHorizontalAnalysisDone && !IsGroupPileSettlementAnalysisDone)
                {
                    MessageBox.Show("水平解析、単杭解析、群杭解析実行後でないと解析結果表示はできません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SetProperty(ref _isAnalysisResultVisible, false); // 明示的に戻す
                    return;
                }
                if (SetProperty(ref _isAnalysisResultVisible, value))
                {
                    UpdateWindowAction?.Invoke();
                }
            }
        }

        // 杭形状描画
        private bool _isPileSectionVisible = true;
        public bool IsPileSectionVisible
        {
            get => _isPileSectionVisible;
            set
            {
                if (SetProperty(ref _isPileSectionVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 根入れ形状描画
        private bool _isEmbedmentBoxVisible = true;
        public bool IsEmbedmentBoxVisible
        {
            get => _isEmbedmentBoxVisible;
            set
            {
                if (SetProperty(ref _isEmbedmentBoxVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // XYZ軸描画
        private bool _isXYZAxesVisible = false;
        public bool IsXYZAxesVisible
        {
            get => _isXYZAxesVisible;
            set
            {
                if (SetProperty(ref _isXYZAxesVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        //要素座標描画
        private bool _isBeamLocalAxesVisible = false;
        public bool IsBeamLocalAxesVisible
        {
            get => _isBeamLocalAxesVisible;
            set
            {
                if (SetProperty(ref _isBeamLocalAxesVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // ティックマーク描画
        private bool _isTickMarkVisible = true;
        public bool IsTickMarkVisible
        {
            get => _isTickMarkVisible;
            set
            {
                if (SetProperty(ref _isTickMarkVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 通り心描画
        private bool _isGridLineVisible = true;
        public bool IsGridLineVisible
        {
            get => _isGridLineVisible;
            set
            {
                if (SetProperty(ref _isGridLineVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭周地盤描画
        private bool _isGroundVisible = true;
        public bool IsGroundVisible
        {
            get => _isGroundVisible;
            set
            {
                if (SetProperty(ref _isGroundVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // N値描画
        private bool _isNValueVisible = false;
        public bool IsNValueVisible
        {
            get => _isNValueVisible;
            set
            {
                if (SetProperty(ref _isNValueVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // VS0描画
        private bool _isVS0Visible = false;
        public bool IsVS0Visible
        {
            get => _isVS0Visible;
            set
            {
                if (SetProperty(ref _isVS0Visible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // Fc描画
        private bool _isFcVisible = false;
        public bool IsFcVisible
        {
            get => _isFcVisible;
            set
            {
                if (SetProperty(ref _isFcVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private bool _isSoilMassParamDisplayEnabled;
        public bool IsSoilMassParamDisplayEnabled
        {
            get => _isSoilMassParamDisplayEnabled;
            set
            {
                if (SetProperty(ref _isSoilMassParamDisplayEnabled, value))
                    ApplySoilMassParamDisplay();
            }
        }

        public ObservableCollection<string> SoilMassParamDisplayOptions { get; } =
        [
            "N値",
            "Vs0",
            "Fc",
        ];

        private string _selectedSoilMassParamDisplay = "N値";
        public string SelectedSoilMassParamDisplay
        {
            get => _selectedSoilMassParamDisplay;
            set
            {
                if (SetProperty(ref _selectedSoilMassParamDisplay, value))
                    ApplySoilMassParamDisplay();
            }
        }

        private void ApplySoilMassParamDisplay()
        {
            // 既存の個別表示フラグを一旦クリア
            IsNValueVisible = false;
            IsVS0Visible = false;
            IsFcVisible = false;

            if (!IsSoilMassParamDisplayEnabled) return;

            switch (SelectedSoilMassParamDisplay)
            {
                case "N値":
                    IsNValueVisible = true;
                    break;
                case "Vs0":
                    IsVS0Visible = true;
                    break;
                case "Fc":
                    IsFcVisible = true;
                    break;
            }

            // 画面更新が必要な場合
            UpdateViewCommand?.Execute(null);
        }

        // 既存フラグから初期値を推定（任意）
        public void InitializeSoilMassParamDisplayFromLegacyFlags()
        {
            if (IsNValueVisible || IsVS0Visible || IsFcVisible)
            {
                IsSoilMassParamDisplayEnabled = true;
                if (IsNValueVisible) SelectedSoilParamDisplay = "N値";
                else if (IsVS0Visible) SelectedSoilParamDisplay = "Vs0";
                else if (IsFcVisible) SelectedSoilParamDisplay = "Fc";
            }
            else
            {
                IsSoilMassParamDisplayEnabled = false;
                SelectedSoilMassParamDisplay = SoilMassParamDisplayOptions.First();
            }

            ApplySoilMassParamDisplay();
        }


        private bool _isSoilLayerParamDisplayEnabled;
        public bool IsSoilLayerParamDisplayEnabled
        {
            get => _isSoilLayerParamDisplayEnabled;
            set
            {
                if (SetProperty(ref _isSoilLayerParamDisplayEnabled, value))
                    ApplySoilLayerParamDisplay();
            }
        }

        public ObservableCollection<string> SoilParamDisplayOptions { get; } =
        [
            "密度",
            "粘着力",
            "Vs",
            "Es",
        ];

        private string _selectedSoilParamDisplay = "粘着力";
        public string SelectedSoilParamDisplay
        {
            get => _selectedSoilParamDisplay;
            set
            {
                if (SetProperty(ref _selectedSoilParamDisplay, value))
                    ApplySoilLayerParamDisplay();
            }
        }

        private void ApplySoilLayerParamDisplay()
        {
            // 既存の個別表示フラグを一旦クリア
            IsDensityVisible = false;
            IsCohesiveVisible = false;
            IsVsVisible = false;
            IsEsVisible = false;

            if (!IsSoilLayerParamDisplayEnabled) return;

            switch (SelectedSoilParamDisplay)
            {
                case "密度":
                    IsDensityVisible = true;
                    break;
                case "粘着力":
                    IsCohesiveVisible = true;
                    break;
                case "Vs":
                    IsVsVisible = true;
                    break;
                case "Es":
                    IsEsVisible = true;
                    break;
            }

            // 画面更新が必要な場合
            UpdateViewCommand?.Execute(null);
        }

        // 既存フラグから初期値を推定（任意）
        public void InitializeSoilParamDisplayFromLegacyFlags()
        {
            if (IsDensityVisible || IsCohesiveVisible || IsVsVisible || IsEsVisible)
            {
                IsSoilLayerParamDisplayEnabled = true;
                if (IsDensityVisible) SelectedSoilParamDisplay = "密度";
                else if (IsCohesiveVisible) SelectedSoilParamDisplay = "粘着力";
                else if (IsVsVisible) SelectedSoilParamDisplay = "Vs";
                else if (IsEsVisible) SelectedSoilParamDisplay = "Es";
            }
            else
            {
                IsSoilLayerParamDisplayEnabled = false;
                SelectedSoilParamDisplay = SoilParamDisplayOptions.First();
            }

            ApplySoilLayerParamDisplay();
        }

        // 密度描画
        private bool _isDensityVisible = false;
        public bool IsDensityVisible
        {
            get => _isDensityVisible;
            set
            {
                if (SetProperty(ref _isDensityVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 粘着力描画
        private bool _isCohesiveVisible = false;
        public bool IsCohesiveVisible
        {
            get => _isCohesiveVisible;
            set
            {
                if (SetProperty(ref _isCohesiveVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // Vs描画
        private bool _isVsVisible = false;
        public bool IsVsVisible
        {
            get => _isVsVisible;
            set
            {
                if (SetProperty(ref _isVsVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // Es描画
        private bool _isEsVisible = false;
        public bool IsEsVisible
        {
            get => _isEsVisible;
            set
            {
                if (SetProperty(ref _isEsVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }


        // 沈下検討用土層描画
        private bool _isSettlementGroundVisible = true;
        public bool IsSettlementGroundVisible
        {
            get => _isSettlementGroundVisible;
            set
            {
                if (SetProperty(ref _isSettlementGroundVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 節点描画
        private bool _isNodeVisible = true;
        public bool IsNodeVisible
        {
            get => _isNodeVisible;
            set
            {
                if (SetProperty(ref _isNodeVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 要素描画
        private bool _isElementVisible = true;
        public bool IsElementVisible
        {
            get => _isElementVisible;
            set
            {
                if (SetProperty(ref _isElementVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 節点番号描画
        private bool _isNodeNoVisible = true;
        public bool IsNodeNoVisible
        {
            get => _isNodeNoVisible;
            set
            {
                if (SetProperty(ref _isNodeNoVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 要素番号描画
        private bool _isElementNoVisible = true;
        public bool IsElementNoVisible
        {
            get => _isElementNoVisible;
            set
            {
                if (SetProperty(ref _isElementNoVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }


        // 変形後の要素描画
        private bool _isDeformedElementVisible = false;
        public bool IsDeformedElementVisible
        {
            get => _isDeformedElementVisible;
            set
            {
                if (SetProperty(ref _isDeformedElementVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 要素追加モード
        private bool _isElementAddMode = false;
        public bool IsElementAddMode
        {
            get => _isElementAddMode;
            set
            {
                if (SetProperty(ref _isElementAddMode, value))
                {
                    StatusMessage = value ? "要素追加モード(解除: [Esc], [Alt]+[1])" : "通常モード";
                }
            }
        }

        // 要素レベル
        private bool _isElementShownAtSettlementPlane = false;
        public bool IsElementShownAtSettlementPlane
        {
            get => _isElementShownAtSettlementPlane;
            set/* => SetProperty(ref _isElementShownAtSettlementPlane, value);*/
            {
                if (SetProperty(ref _isElementShownAtSettlementPlane, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭心地下外壁間距離
        private double _embedmentPileDistance = 1.5;
        public double EmbedmentPileDistance
        {
            get => _embedmentPileDistance;
            set => SetProperty(ref _embedmentPileDistance, value);
        }

        // 群杭沈下荷重面距離
        private double _rectLoadPileDistance = 1.5;
        public double RectLoadPileDistance
        {
            get => _rectLoadPileDistance;
            set => SetProperty(ref _rectLoadPileDistance, value);
        }

        private string _comboBox3DLabelContent_LabelContent;
        public string ComboBox3DLabelContent_LabelContent
        {
            get => _comboBox3DLabelContent_LabelContent;
            set => SetProperty(ref _comboBox3DLabelContent_LabelContent, value);
        }

        // 群杭沈下量カラーバブル表示
        private bool _isGroupPileSettlementColorBubbleVisible = false;
        public bool IsGroupPileSettlementColorBubbleVisible
        {
            get => _isGroupPileSettlementColorBubbleVisible;
            set => SetProperty(ref _isGroupPileSettlementColorBubbleVisible, value);
        }

        // 群杭沈下量カラー矢印表示
        private bool _isGroupPileSettlementColorArrowVisible = false;
        public bool IsGroupPileSettlementColorArrowVisible
        {
            get => _isGroupPileSettlementColorArrowVisible;
            set => SetProperty(ref _isGroupPileSettlementColorArrowVisible, value);
        }

        // 要素分割済か否か
        private bool _isElementSplit;
        public bool IsElementSplit
        {
            get => _isElementSplit;
            set
            {
                if (SetProperty(ref _isElementSplit, value))
                {
                    if (!value)
                    {
                        IsForcedDisplacementVisible = false;
                    }
                }
            }
        }

        // 鉛直解析済か否か
        private bool _isVerticalAnalysisDone;
        public bool IsVerticalAnalysisDone
        {
            get => _isVerticalAnalysisDone;
            set
            {
                if (!SetProperty(ref _isVerticalAnalysisDone, value))
                    return;

                const string settlementLabel = "沈下";
                const string singlePileLabel = "単杭";

                if (value)
                {
                    if (!AnalysisResultContentOption.Contains(settlementLabel))
                        AnalysisResultContentOption.Add(settlementLabel);
                    if (!AnalysisResultSettlementOption.Contains(singlePileLabel))
                        AnalysisResultSettlementOption.Add(singlePileLabel);
                }
                else
                {
                    AnalysisResultContentOption.Remove(settlementLabel);
                    AnalysisResultSettlementOption.Remove(singlePileLabel);
                }

                Both(); // 単杭+群杭 の表示制御
            }
        }

        private void Both()
        {
            // "単杭+群杭"の表示制御
            const string bothLabel = "単杭+群杭";
            if (IsVerticalAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                // 両方trueなら追加
                if (!AnalysisResultSettlementOption.Contains(bothLabel))
                {
                    AnalysisResultSettlementOption.Add(bothLabel);
                }
            }
            else
            {
                // それ以外は削除
                AnalysisResultSettlementOption.Remove(bothLabel);
            }
        }

        // 鉛直解析済か否か
        private bool _isGroupPileSettlementAnalysisDone;
        public bool IsGroupPileSettlementAnalysisDone
        {
            get => _isGroupPileSettlementAnalysisDone;
            set
            {
                if (SetProperty(ref _isGroupPileSettlementAnalysisDone, value))
                {
                    // "梁応力"の表示制御
                    const string settlementLabel = "沈下";
                    if (value)
                    {
                        // true: "梁応力"がなければ追加
                        if (!AnalysisResultContentOption.Contains(settlementLabel))
                        {
                            AnalysisResultContentOption.Add(settlementLabel);
                        }
                    }
                    else
                    {
                        // false: "梁応力"があれば削除
                        AnalysisResultContentOption.Remove(settlementLabel);
                    }

                    const string groupPileLabel = "群杭";
                    if (value)
                    {
                        // true: "群杭"がなければ追加
                        if (!AnalysisResultSettlementOption.Contains(groupPileLabel))
                        {
                            AnalysisResultSettlementOption.Add(groupPileLabel);
                        }
                    }
                    else
                    {
                        // false: "群杭"があれば削除
                        AnalysisResultSettlementOption.Remove(groupPileLabel);
                    }
                    Both();
                }
            }
        }


        // 水平解析済か否か
        private bool _isHorizontalAnalysisDone;
        public bool IsHorizontalAnalysisDone
        {
            get => _isHorizontalAnalysisDone;
            set
            {
                if (SetProperty(ref _isHorizontalAnalysisDone, value))
                {
                    // "梁応力"の表示制御
                    const string beamForceLabel = "梁応力";
                    const string nodeDisplacementLabel = "節点変位";
                    const string nodeSoilSpringLabel = "地盤ばね";
                    if (value)
                    {
                        // true: "梁応力"がなければ追加
                        if (!AnalysisResultContentOption.Contains(beamForceLabel))
                            AnalysisResultContentOption.Add(beamForceLabel);
                        if (!AnalysisResultContentOption.Contains(nodeDisplacementLabel))
                            AnalysisResultContentOption.Add(nodeDisplacementLabel);
                        if (!AnalysisResultContentOption.Contains(nodeSoilSpringLabel))
                            AnalysisResultContentOption.Add(nodeSoilSpringLabel);
                    }
                    else
                    {
                        // false: "梁応力"があれば削除
                        AnalysisResultContentOption.Remove(beamForceLabel);
                        AnalysisResultContentOption.Remove(nodeDisplacementLabel);
                        AnalysisResultContentOption.Remove(nodeSoilSpringLabel);
                    }
                }
            }
        }

        // 解析後処理モード
        private bool _isPostAnalysisMode = false;
        public bool IsPostAnalysisMode
        {
            get => _isPostAnalysisMode;
            set => SetProperty(ref _isPostAnalysisMode, value);
        }

        // 要素タイプオプション
        private List<string> _elementTypeOption = ["ダミー"];
        public List<string> ElementTypeOption
        {
            get => _elementTypeOption;
            set => SetProperty(ref _elementTypeOption, value);
        }

        // 要素タイプ
        private string _elementType = "ダミー";
        public string ElementType
        {
            get => _elementType;
            set => SetProperty(ref _elementType, value);
        }

        // マージ対象限界距離
        private double _editDistanceThreashold = 0.005;
        public double EditDistanceThreashold
        {
            get => _editDistanceThreashold;
            set => SetProperty(ref _editDistanceThreashold, value);
        }


        public TextBox TextBoxElementNodeInput { get; set; }

        // 要素縮小表示
        private bool _isShrinkElementMode = false;
        public bool IsShrinkElementMode
        {
            get => _isShrinkElementMode;
            set
            {
                if (SetProperty(ref _isShrinkElementMode, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 交差選択窓モード
        private bool _isCrossSelectionMode = false;
        public bool IsCrossSelectionMode
        {
            get => _isCrossSelectionMode;
            set => SetProperty(ref _isCrossSelectionMode, value);
        }

        // 目盛り帯の幅
        private double _tickZoneWidth = 35;
        public double TickZoneWidth
        {
            get => _tickZoneWidth;
            set => SetProperty(ref _tickZoneWidth, value);
        }

        // 目盛り文字位置
        public double TickTextPos => TickZoneWidth - 5;


        // 通り心シンボル径
        private double _gridSymbolCircleDia = 20;
        public double GridSymbolCircleDia
        {
            get => _gridSymbolCircleDia;
            set => SetProperty(ref _gridSymbolCircleDia, value);
        }

        // 通り心帯の幅
        public double GridSymbolZoneWidth => GridSymbolCircleDia * 1.5;

        // 土層ライン幅
        private double _soilStrokeThickness = 0.75;
        public double SoilStrokeThickness
        {
            get => _soilStrokeThickness;
            set
            {
                if (SetProperty(ref _soilStrokeThickness, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭ライン幅
        private double _pileStrokeThickness = 1;
        public double PileStrokeThickness
        {
            get => _pileStrokeThickness;
            set
            {
                if (SetProperty(ref _pileStrokeThickness, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 群杭沈下グリッド表示 //
        private bool _isGroupPileGridVisible;
        public bool IsGroupPileGridVisible
        {
            get => _isGroupPileGridVisible;
            set
            {
                if (SetProperty(ref _isGroupPileGridVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 群杭沈下グリッド変位表示 //
        private bool _isGroupPileGridDeformationVisible;
        public bool IsGroupPileGridDeformationVisible
        {
            get => _isGroupPileGridDeformationVisible;
            set
            {
                if (SetProperty(ref _isGroupPileGridDeformationVisible, value))
                {
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }


        // 群杭沈下 //
        private double _groupPileSettlementXOffset;
        public double GroupPileSettlementXOffset
        {
            get => _groupPileSettlementXOffset;
            set
            {
                if (SetProperty(ref _groupPileSettlementXOffset, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private double _groupPileSettlementYOffset;
        public double GroupPileSettlementYOffset
        {
            get => _groupPileSettlementYOffset;
            set
            {
                if (SetProperty(ref _groupPileSettlementYOffset, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 群杭沈下 //
        public double GroupPileSettlementXmin
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Min(pile => pile.X);
            }
        }

        public double GroupPileSettlementXmax
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Max(pile => pile.X);
            }
        }

        public double GroupPileSettlementYmin
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Min(pile => pile.Y);
            }
        }

        public double GroupPileSettlementYmax
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Max(pile => pile.Y);
            }
        }



        private double _groupPileSettlementXSpacing = 1.8;
        public double GroupPileSettlementXSpacing
        {
            get => _groupPileSettlementXSpacing;
            //set => SetProperty(ref _groupPileSettlementXSpacing, value);
            set
            {
                if (SetProperty(ref _groupPileSettlementXSpacing, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private double _groupPileSettlementYSpacing = 1.8;
        public double GroupPileSettlementYSpacing
        {
            get => _groupPileSettlementYSpacing;
            set
            {
                if (SetProperty(ref _groupPileSettlementYSpacing, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
                    UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // QuickHintの表示制御 //
        private bool _isQuickHintVisible;
        public bool IsQuickHintVisible
        {
            get => _isQuickHintVisible;
            set
            {
                if (SetProperty(ref _isQuickHintVisible, value))
                {
                    Console.WriteLine($"IsQuickHintVisible changed to: {value}");
                }
            }
        }

        // 変位コンター図キャッシュ
        private PathGeometry _cachedSettlementGridGeometry;
        public PathGeometry CachedSettlementGridGeometry
        {
            get => _cachedSettlementGridGeometry;
            set => SetProperty(ref _cachedSettlementGridGeometry, value);
        }

        private bool _isSettlementGridCacheValid = false;
        public bool IsSettlementGridCacheValid
        {
            get => _isSettlementGridCacheValid;
            set => SetProperty(ref _isSettlementGridCacheValid, value);
        }

        public MainCanvasGeometry CanvasGeometry { get; }

        // MainWindowViewModel の partial 部分に追加
        [ObservableProperty] private bool includeGroundInformation = true;
        [ObservableProperty] private bool includeLiquefaction = false;
        [ObservableProperty] private bool includeHorizontal = true;
        [ObservableProperty] private bool includeVertical = true;
        [ObservableProperty] private bool includeHorizontal_Bending = true;
        [ObservableProperty] private bool includeHorizontal_Shear = true;
        [ObservableProperty] private bool includeHorizontal_NMINT = true;
        [ObservableProperty] private bool includePileHeadMomentMap = false;
        [ObservableProperty] private bool includePileHeadShearMap = false;
        [ObservableProperty] private bool includeSettlement = true;
        [ObservableProperty] private bool includeLoadSettlementCurve = false;

        // コンストラクタ //
        public MainWindowViewModel()
        {
            CurrentInputModel = new InputModel();
            CurrentInputModel.SetMainWindowViewModel(this);

            // ここで各アイテムのPropertyChangedを購読
            foreach (var item in CurrentInputModel.PileLayoutItems)
                item.PropertyChanged += PileLayoutItem_PropertyChanged;
            CurrentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;

            // LoadCase.IsApplicable の変更監視を追加
            SubscribeLoadCaseApplicabilityChanged();

            CanvasGeometry = new MainCanvasGeometry(this);

            UpdateLoadCaseOption();
            //SelectedLoadCaseName = CurrentInputModel.LoadCasesInput.LoadCasesLevel1[0].LoadName;
            if (CurrentInputModel.LoadCasesInput.LoadCasesLevel1?.Count > 0)
                SelectedLoadCaseName = CurrentInputModel.LoadCasesInput.LoadCasesLevel1[0].LoadName;

            // LoadCombinationOptionの初期化
            UpdateLoadCombinationOption();
            //SelectedLoadCombinationName = LoadCombinationNameOption[0];
            if (LoadCombinationNameOption != null && LoadCombinationNameOption.Count > 0)
                SelectedLoadCombinationName = LoadCombinationNameOption[0];

            // 初期データのロードや、必要に応じて初期化処理を行う
            LoadInitialData();

            CanvasThreeDView = new CanvasThreeDView();

            DataGridSettlementSoilLayersCellEditEnding += HandleDataGridSettlementSoilLayersCellEditEnding;

            // 初期化処理
            StatusMessage = "準備完了";

            // 沈下コンター図のキャッシュを無効化
            CurrentInputModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(CurrentInputModel.PileGroupSettlement))
                {
                    IsSettlementGridCacheValid = false;
                }
            };

            // 沈下コンター図のキャッシュを無効化
            CurrentInputModel.PileGroupSettlement.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PileGroupSettlement.SettlementGridX) ||
                    e.PropertyName == nameof(PileGroupSettlement.SettlementGridY) ||
                    e.PropertyName == nameof(PileGroupSettlement.SettlementGridData))
                {
                    IsSettlementGridCacheValid = false;
                }
            };

            // コンストラクタ内の適当な位置
            OpenTableWindowCommand = new ToolkitRelayCommand(
                OpenTableWindow,
                () => LatestResultTables != null && LatestResultTables.Count > 0);
        }

        private void PileLayoutItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PileLayoutDataItem.AxialForceLevel1s) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceLevel2s) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceVL0) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceVLAdditional) ||
                e.PropertyName == nameof(PileLayoutDataItem.X) ||
                e.PropertyName == nameof(PileLayoutDataItem.Y))
            {
                UpdateSumAndOTM();
            }
        }

        private void LoadCasesInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCasesInput.LoadCombinations))
            {
                UpdateLoadCombinationOption();
            }
        }

        private void UpdateSumAndOTM()
        {
            OnPropertyChanged(nameof(Sum1_1));
            OnPropertyChanged(nameof(Sum1_2));
            OnPropertyChanged(nameof(Sum1_3));
            OnPropertyChanged(nameof(Sum1_4));
            OnPropertyChanged(nameof(Sum2_1));
            OnPropertyChanged(nameof(Sum2_2));
            OnPropertyChanged(nameof(Sum2_3));
            OnPropertyChanged(nameof(Sum2_4));

            OnPropertyChanged(nameof(SumVL0));
            OnPropertyChanged(nameof(SumVLadd));
            OnPropertyChanged(nameof(SumVL));

            OnPropertyChanged(nameof(OverturningMoment1_1X));
            OnPropertyChanged(nameof(OverturningMoment1_1Y));
            OnPropertyChanged(nameof(OverturningMoment1_2X));
            OnPropertyChanged(nameof(OverturningMoment1_2Y));
            OnPropertyChanged(nameof(OverturningMoment1_3X));
            OnPropertyChanged(nameof(OverturningMoment1_3Y));
            OnPropertyChanged(nameof(OverturningMoment1_4X));
            OnPropertyChanged(nameof(OverturningMoment1_4Y));
            OnPropertyChanged(nameof(OverturningMoment2_1X));
            OnPropertyChanged(nameof(OverturningMoment2_1Y));
            OnPropertyChanged(nameof(OverturningMoment2_2X));
            OnPropertyChanged(nameof(OverturningMoment2_2Y));
            OnPropertyChanged(nameof(OverturningMoment2_3X));
            OnPropertyChanged(nameof(OverturningMoment2_3Y));
            OnPropertyChanged(nameof(OverturningMoment2_4X));
            OnPropertyChanged(nameof(OverturningMoment2_4Y));

            // 追加: 重心・外接範囲の通知もここで一括
            OnPropertyChanged(nameof(GravityCenterVL0));
            OnPropertyChanged(nameof(GravityCenterVLadd));
            OnPropertyChanged(nameof(GravityCenterVLplusVLadd));

            OnPropertyChanged(nameof(GroupPileSettlementXmin));
            OnPropertyChanged(nameof(GroupPileSettlementXmax));
            OnPropertyChanged(nameof(GroupPileSettlementYmin));
            OnPropertyChanged(nameof(GroupPileSettlementYmax));
        }

        //// LoadCaseOptionの更新メソッド
        //private void UpdateLoadCaseOption()
        //{
        //    var loadCaseNames = new ObservableCollection<string>();
        //    ObservableCollection<LoadCase> allLoadCases = CurrentInputModel.LoadCasesInput.AllLoadCases;
        //    foreach (var loadCase in allLoadCases)
        //    {
        //        loadCaseNames.Add(loadCase.GetLoadName());
        //    }
        //    LoadCaseNameOption = loadCaseNames;
        //}


        // LoadCombinationOptionの更新メソッド
        private void UpdateLoadCombinationOption()
        {
            var loadCombinationNames = new ObservableCollection<string>();

            foreach (var loadCombination in CurrentInputModel.LoadCasesInput.LoadCombinations)
            {
                loadCombinationNames.Add(loadCombination.GetName());
            }
            LoadCombinationNameOption = loadCombinationNames;
        }

        // DataGridSelecitonコピーメソッド
        [RelayCommand]
        private static void CopyDataGridSelection(DataGrid dataGrid)
        {
            if (dataGrid == null || dataGrid.SelectedCells.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();

            var selectedCells = dataGrid.SelectedCells.GroupBy(cell => cell.Item).ToList();

            foreach (var row in selectedCells)
            {
                var rowValues = new List<string>();

                foreach (var cell in row)
                {
                    if (cell.Column.GetCellContent(cell.Item) is TextBlock textBlock)
                    {
                        rowValues.Add(textBlock.Text);
                    }
                }

                sb.AppendLine(string.Join("\t", rowValues));
            }

            Clipboard.SetText(sb.ToString());
        }

        private static void LoadInitialData()
        {

        }

        // 
        private void PileLayoutItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (PileLayoutDataItem oldItem in e.OldItems)
                    RemoveElementsContainingPileLayoutItem(oldItem);
            }

            if (e.NewItems != null)
            {
                foreach (PileLayoutDataItem newItem in e.NewItems)
                    newItem.PropertyChanged += PileLayoutItem_PropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (PileLayoutDataItem oldItem in e.OldItems)
                    oldItem.PropertyChanged -= PileLayoutItem_PropertyChanged;
            }

            // 一括通知
            UpdateSumAndOTM();
            // 削除されたアイテムを処理
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (PileLayoutDataItem oldItem in e.OldItems)
                {
                    RemoveElementsContainingPileLayoutItem(oldItem);
                }
            }

            if (e.NewItems != null)
            {
                foreach (PileLayoutDataItem newItem in e.NewItems)
                    newItem.PropertyChanged += PileLayoutItem_PropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (PileLayoutDataItem oldItem in e.OldItems)
                    oldItem.PropertyChanged -= PileLayoutItem_PropertyChanged;
            }

            // 集計やOTMは共通メソッドで一括更新
            UpdateSumAndOTM();

            // 重心・外接範囲なども再通知
            OnPropertyChanged(nameof(GravityCenterVL0));
            OnPropertyChanged(nameof(GravityCenterVLadd));
            OnPropertyChanged(nameof(GravityCenterVLplusVLadd));
            OnPropertyChanged(nameof(GroupPileSettlementXmin));
            OnPropertyChanged(nameof(GroupPileSettlementXmax));
            OnPropertyChanged(nameof(GroupPileSettlementYmin));
            OnPropertyChanged(nameof(GroupPileSettlementYmax));
        }


        // 追加: IsApplicable 変更監視の購読セットアップ
        private void SubscribeLoadCaseApplicabilityChanged()
        {
            var lci = CurrentInputModel.LoadCasesInput;
            if (lci == null) return;

            void attach(IEnumerable<LoadCase> cases)
            {
                if (cases == null) return;
                foreach (var lc in cases)
                    lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
            }

            attach(lci.LoadCasesLevel1);
            attach(lci.LoadCasesLevel2);
            // attach(lci.AllLoadCombinations); // ← これが型不一致。不要なので削除

            // コレクションへの追加にも追随
            lci.LoadCasesLevel1.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (LoadCase lc in e.NewItems)
                        lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
                if (e.OldItems != null)
                    foreach (LoadCase lc in e.OldItems)
                        lc.PropertyChanged -= LoadCase_PropertyChanged_ForOption;
                UpdateLoadCaseOption();
            };
            lci.LoadCasesLevel2.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (LoadCase lc in e.NewItems)
                        lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
                if (e.OldItems != null)
                    foreach (LoadCase lc in e.OldItems)
                        lc.PropertyChanged -= LoadCase_PropertyChanged_ForOption;
                UpdateLoadCaseOption();
            };
            lci.LoadCombinations.CollectionChanged += (s, e) =>
            {
                // 組合せが UI に影響する場合に再構築
                UpdateLoadCombinationOption();
            };
        }

        // 追加: IsApplicable 変更時にオプション更新
        private void LoadCase_PropertyChanged_ForOption(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCase.IsApplicable))
            {
                UpdateLoadCaseOption();
                // 現在選択が非適用になったときのフォールバック
                if (!LoadCaseNameOption.Contains(SelectedLoadCaseName))
                {
                    SelectedLoadCaseName = LoadCaseNameOption.FirstOrDefault() ?? "VL";
                }
            }
        }

        // 既存: LoadCaseOptionの更新
        private void UpdateLoadCaseOption()
        {
            var loadCaseNames = new ObservableCollection<string>();
            var allLoadCases = CurrentInputModel.LoadCasesInput.AllLoadCases;

            // IsApplicable=true のみ表示したい場合は以下のフィルタを有効化
            foreach (var loadCase in allLoadCases.Where(lc => lc.IsApplicable))
                loadCaseNames.Add(loadCase.GetLoadName());

            // IsApplicable 無視して全件表示したいなら上の Where を外す

            LoadCaseNameOption = loadCaseNames;
        }
    }
}
