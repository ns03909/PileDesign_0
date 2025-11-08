using AvalonDock.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore.Defaults;
using PileDesign.Common;
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
//using System.Windows.Media;
using static PileDesign.ViewModels.MainWindowViewModel;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// GroundLayerViewModelクラス
    /// </summary>
    public partial class GroundLayerViewModel : ObservableObject, ICloseable
    {
        public readonly GroundUndoManager _undoManager = new();

        public GroundWindow GroundWindowInstance { get; set; } // GroundWindow のインスタンスを保持するプロパティを追加
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // Ground
        private ObservableCollection<GroundInput> _groundsInput;
        public ObservableCollection<GroundInput> GroundsInput
        {
            get => _groundsInput;
            set => SetProperty(ref _groundsInput, value);
        }

        // 再入防止フラグ
        private bool _isSyncingGroundInput;

        // GroundInput プロパティ: 購読の付け替えを内包
        private GroundInput _groundInput;
        public GroundInput GroundInput
        {
            get => _groundInput;
            set
            {
                if (_groundInput == value) return;

                UnsubscribeFromGroundInput(_groundInput);
                SetProperty(ref _groundInput, value);
                SubscribeToGroundInput(_groundInput);
            }
        }

        // コンストラクタ内: 末尾の Update() 呼び出し前に購読済みになるように GroundInput の代入経路を通っていればOK
        //public GroundLayerViewModel(MainWindowViewModel mainWindowViewModel)
        //{
        //    _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        //    PrevGroundsInput = new ObservableCollection<GroundInput>(
        //        InputModel.GroundsInput.Select(groundInput => groundInput.DeepCopy())
        //    );

        //    GroundsInput = new ObservableCollection<GroundInput>(
        //        InputModel.GroundsInput.Select(groundInput => groundInput.DeepCopy())
        //    );

        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //    UpdateGroundsCountPlusOneList();

        //    // ここで GroundInput セッターを通す（購読される）
        //    GroundInput = GroundsInput[GroundNo - 1];

        //    Update();
        //}
        public GroundLayerViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            PrevGroundsInput = new ObservableCollection<GroundInput>(
                InputModel.GroundsInput.Select(groundInput => groundInput.DeepCopy())
            );

            GroundsInput = new ObservableCollection<GroundInput>(
                InputModel.GroundsInput.Select(groundInput => groundInput.DeepCopy())
            );

            if (GroundsInput.Count == 0)
                GroundsInput.Add(new GroundInput());

            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            UpdateGroundsCountPlusOneList();

            // ここで GroundInput セッターを通す（購読される）
            GroundInput = GroundsInput[Math.Clamp(GroundNo - 1, 0, GroundsInput.Count - 1)];

            Update();
        }

        // GroundInput 変更監視の購読・解除
        private void SubscribeToGroundInput(GroundInput gi)
        {
            if (gi == null) return;
            gi.PropertyChanged += OnGroundInputPropertyChanged;
        }

        private void UnsubscribeFromGroundInput(GroundInput gi)
        {
            if (gi == null) return;
            gi.PropertyChanged -= OnGroundInputPropertyChanged;
        }

        // 監視対象プロパティ名
        private static readonly HashSet<string> GroundInputTriggerProps =
        [
            nameof(GroundInput.GroundTopAltitude),
            nameof(GroundInput.GroundWaterTableAltitude),
            nameof(GroundInput.StressAltitude),
            nameof(GroundInput.GroundWaterGLDepth),
            nameof(GroundInput.StressGLDepth),
            // 必要なら加速度や方法変更も足せる:
            // nameof(GroundInput.GroundAcceleration1),
            // nameof(GroundInput.GroundAcceleration2),
            // nameof(GroundInput.ShallowSoilType),
            // nameof(GroundInput.CalculationMethod),
        ];

        // 相互換算（標高ZとGL深さ）の同期
        private void SyncDepthAltitude(GroundInput gi, string propertyName)
        {
            if (gi == null) return;

            // 再入防止
            if (_isSyncingGroundInput) return;
            _isSyncingGroundInput = true;
            try
            {
                switch (propertyName)
                {
                    case nameof(GroundInput.GroundTopAltitude):
                        // 孔口Zが変わったら、水位/応力の標高Zを深さから再作成
                        gi.GroundWaterTableAltitude = gi.GroundWaterGLDepth + gi.GroundTopAltitude;
                        gi.StressAltitude = gi.StressGLDepth + gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.GroundWaterTableAltitude):
                        gi.GroundWaterGLDepth = gi.GroundWaterTableAltitude - gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.GroundWaterGLDepth):
                        gi.GroundWaterTableAltitude = gi.GroundWaterGLDepth + gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.StressAltitude):
                        gi.StressGLDepth = gi.StressAltitude - gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.StressGLDepth):
                        gi.StressAltitude = gi.StressGLDepth + gi.GroundTopAltitude;
                        break;
                }
            }
            finally
            {
                _isSyncingGroundInput = false;
            }
        }

        // GroundInput の PropertyChanged ハンドラ
        private void OnGroundInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not GroundInput gi) return;

            if (string.IsNullOrEmpty(e.PropertyName)) return;

            if (GroundInputTriggerProps.Contains(e.PropertyName))
            {
                // 相互換算の同期
                SyncDepthAltitude(gi, e.PropertyName);

                // 再計算・再描画
                Update();
            }
        }

        // GroundNo 変更時も GroundInput セッターで購読が付け替えられる
        public void ComboBoxGroundNo_SelectionChanged(int selectedIndex/*, int previousSelectedIndex*/)
        {
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            if (selectedIndex == GroundCountPlusOneList.Count - 1)
            {
                int newNo = GroundsInput.Count + 1;
                GroundsInput.Add(new GroundInput() { GroundRef = "(GR" + newNo.ToString() + ")" });
                UpdateGroundsCountPlusOneList();
                GroundNo = newNo;
                GroundInput = GroundsInput.Last(); // セッター経由で購読
            }
            else
            {
                if (selectedIndex >= 0 && selectedIndex < GroundsInput.Count)
                {
                    GroundNo = selectedIndex + 1;
                    GroundInput = GroundsInput[selectedIndex]; // セッター経由で購読
                }
            }
            Update();
        }


        // Undo
        [RelayCommand]
        public void Undo()
        {
            _undoManager.Undo();
            if (_undoManager.CurrentState != null)
            {
                GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
                if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
                    GroundInput = GroundsInput[GroundNo - 1];   // セッター経由で購読
                else if (GroundsInput.Count > 0)
                    GroundInput = GroundsInput[0];
                else
                    GroundInput = null;

                Update();
            }
        }

        // Redo
        [RelayCommand]
        public void Redo()
        {
            _undoManager.Redo();
            if (_undoManager.CurrentState != null)
            {
                GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
                if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
                    GroundInput = GroundsInput[GroundNo - 1];   // セッター経由で購読
                else if (GroundsInput.Count > 0)
                    GroundInput = GroundsInput[0];
                else
                    GroundInput = null;

                Update();
            }
        }
        //// GroundInput
        //private GroundInput _groundInput;
        //public GroundInput GroundInput
        //{
        //    get => _groundInput;
        //    set => SetProperty(ref _groundInput, value);
        //}

        // 地盤数+1リスト
        //private ObservableCollection<int> _groundCountPlusOneList;
        //public ObservableCollection<int> GroundCountPlusOneList
        //{
        //    get => _groundCountPlusOneList;
        //    set => SetProperty(ref _groundCountPlusOneList, value);
        //}
        private ObservableCollection<string> _groundCountPlusOneList;
        public ObservableCollection<string> GroundCountPlusOneList
        {
            get => _groundCountPlusOneList;
            set => SetProperty(ref _groundCountPlusOneList, value);
        }

        //private void UpdateGroundsCountPlusOneList()
        //{
        //    GroundCountPlusOneList = new ObservableCollection<int>(Enumerable.Range(1, GroundsInput.Count + 1));
        //}
        private void UpdateGroundsCountPlusOneList()
        {
            var list = new ObservableCollection<string>();
            int count = GroundsInput.Count;
            for (int i = 1; i <= count; i++)
            {
                list.Add(i.ToString());
            }
            list.Add($"{count + 1} (New)");
            GroundCountPlusOneList = list;
        }

        // 選択地盤番号
        private int _groundNo = 1;
        public int GroundNo
        {
            get => _groundNo;
            set => SetProperty(ref _groundNo, value);
        }

        // DataGrid上の選択中のGroundInputデータ
        private GroundMassDataInput _selectedGroundMassOnDataGrid;
        public GroundMassDataInput SelectedGroundMassOnDataGrid
        {
            get => _selectedGroundMassOnDataGrid;
            set => SetProperty(ref _selectedGroundMassOnDataGrid, value);
        }

        // DataGrid上の選択中のGroundLayerデータ
        private GroundLayerInput _selectedGroundLayerOnDataGrid;
        public GroundLayerInput SelectedGroundLayerOnDataGrid
        {
            get => _selectedGroundLayerOnDataGrid;
            set => SetProperty(ref _selectedGroundLayerOnDataGrid, value);
        }

        public LayoutAnchorable NValueTab { get; set; }
        public LayoutAnchorable CuValueTab { get; set; }
        public LayoutAnchorable VsValueTab { get; set; }
        public LayoutAnchorable EsValueTab { get; set; }
        public LayoutAnchorable DefTab { get; set; }
        public LayoutAnchorable FsTab { get; set; }

        public string[] AgeCategoryOption { get; } = ["沖積層", "洪積層"];
        //public enum AgeCategoryOption { 沖積層, 洪積層 }

        public string[] ShallowSoilTypeOption { get; } =
        [
            "粘性土",
            "砂質土"
        ];
        //public enum ShallowSoilTypeOption { 粘性土, 砂質土 }

        //// 算定法
        public string[] CalculationMethodOption { get; } =
        [
            "a1(b1)",
            "a2(b2)"
        ];

        public string[] ChartDispContentOption { get; } =
        [
            "DmaxU*(レベル1)",
            "DmaxU*(レベル2)",
            "DmaxU*(レベル1,2)",
            "DmaxU*+∑γcyH(レベル1)",
            "DmaxU*+∑γcyH(レベル2)",
            "DmaxU*+∑γcyH(レベル1,2)",
        ];

        public ObservableCollection<string> ChartDispContents { get; } = [];
        private string _ChartDispContent = "DmaxU*(レベル1,2)";
        public string ChartDispContent
        {
            get => _ChartDispContent;
            set => SetProperty(ref _ChartDispContent, value);
        }

        // グラフ2内容
        public string[] ChartFLContentOption { get; } =
        [
            "FL(レベル1)",
            "FL(レベル2)",
            "FL(レベル1,2)",
        ];

        public ObservableCollection<string> ChartFLContents { get; } = [];
        private string _ChartFLContent = "FL(レベル1,2)";
        public string ChartFLContent
        {
            get => _ChartFLContent;
            set => SetProperty(ref _ChartFLContent, value);
        }

        private object _dataContextFundamental;
        public object DataContextFundamental
        {
            get => _dataContextFundamental;
            set => SetProperty(ref _dataContextFundamental, value);
        }

        [RelayCommand]
        private void OnSliderEngineeringBedrockValueChanged(double value)
        {
            int intValue = (int)value;
            int n = GroundInput.GroundLayers.Count;

            // i行のチェックボックスの状態が変更されたとき、1～i-1行のチェックボックスを有効化、i+1行目以降のチェックボックスを無効化
            for (int i = 0; i < n; i++)
            {
                //if (n - 1 - i < intValue)
                //{
                //    GroundInput.GroundLayers[i].IsEngineeringBedrock = true;
                //}
                //else
                //{
                //    GroundInput.GroundLayers[i].IsEngineeringBedrock = false;
                //}
                GroundInput.GroundLayers[i].IsEngineeringBedrock = n - 1 - i < intValue;
            }
            Update();
        }

        // はじめて工学的基盤となる層以下の層をすべて工学的基盤に変えるメソッド
        public void UpdateBedrockChecks()
        {
            bool isEngineeringBedrock = false;
            foreach (var groundLayer in GroundInput.GroundLayers)
            {
                if (groundLayer.IsEngineeringBedrock)
                {
                    isEngineeringBedrock = true;
                }

                if (isEngineeringBedrock)
                {
                    groundLayer.IsEngineeringBedrock = true;
                }
            }
        }

        //[RelayCommand]
        //public void Undo()
        //{

        //    _undoManager.Undo();
        //    if (_undoManager.CurrentState != null)
        //    {
        //        GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
        //        if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
        //            GroundInput = GroundsInput[GroundNo - 1];
        //        else if (GroundsInput.Count > 0)
        //            GroundInput = GroundsInput[0];
        //        else
        //            GroundInput = null;

        //        Update(); // UI再描画
        //    }
        //}


        //[RelayCommand]
        //public void Redo()
        //{
        //    _undoManager.Redo();
        //    if (_undoManager.CurrentState != null)
        //    {
        //        GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
        //        if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
        //            GroundInput = GroundsInput[GroundNo - 1];
        //        else if (GroundsInput.Count > 0)
        //            GroundInput = GroundsInput[0];
        //        else
        //            GroundInput = null;
        //        Update();
        //    }
        //}

        // 土層削除メソッド
        [RelayCommand]
        public void DeleteGroundLayer(object sender)
        {
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);
            if (sender is not GroundLayerInput itemToDelete) return;
            GroundInput.GroundLayers.Remove(itemToDelete);

            // 行番号は LoadingRow で設定済み。必要なら Items.Refresh のみ
            GroundWindowInstance?.DataGridGroundLayer?.Items.Refresh();

            UpdateGroundLayerNo();
            Update();
        }
        //public void DeleteGroundLayer(object sender)
        //{
        //    // 変更前の状態を保存
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //    // sender が GridDataItem であることを確認
        //    if (sender is not GroundLayerInput itemToDelete) return;

        //    // コレクションから削除
        //    GroundInput.GroundLayers.Remove(itemToDelete);

        //    // 番号更新
        //    UpdateAllRowNumbers(GroundWindowInstance.DataGridGroundLayer);

        //    UpdateGroundLayerNo();
        //    Update(); ///
        //}

        // 土質点削除メソッド
        [RelayCommand]
        public void DeleteGroundMass(object sender)
        {
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);
            if (sender is not GroundMassDataInput itemToDelete) return;
            GroundInput.GroundMassesData.Remove(itemToDelete);

            GroundWindowInstance?.DataGridGroundMass?.Items.Refresh();
            UpdateGroundMassDataLayer();
            Update();
        }
        //public void DeleteGroundMass(object sender)
        //{
        //    // 変更前の状態を保存
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //    // sender が GridDataItem であることを確認
        //    if (sender is not GroundMassDataInput itemToDelete) return;

        //    // コレクションから削除
        //    GroundInput.GroundMassesData.Remove(itemToDelete);

        //    // 番号更新
        //    UpdateAllRowNumbers(GroundWindowInstance.DataGridGroundMass);

        //    UpdateGroundMassDataLayer();

        //    Update(); ///
        //}

        // すべての行の番号を更新
        private static void UpdateAllRowNumbers(DataGrid dataGrid)
        {
            for (int i = 0; i < dataGrid.Items.Count; i++)
            {
                if (dataGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                {
                    row.Header = (i + 1).ToString(); // 行番号を設定
                }
            }
        }

        public static Crosshair MyCrosshair_NValue { get; private set; }

        private string _crosshairPositionText_NValue;
        public string CrosshairPositionText_NValue
        {
            get => _crosshairPositionText_NValue;
            set => SetProperty(ref _crosshairPositionText_NValue, value);
        }

        public static Crosshair MyCrosshair_Cu { get; private set; }

        private string _crosshairPositionText_Cu;
        public string CrosshairPositionText_Cu
        {
            get => _crosshairPositionText_Cu;
            set => SetProperty(ref _crosshairPositionText_Cu, value);
        }

        public static Crosshair MyCrosshair_Vs { get; private set; }

        private string _crosshairPositionText_Vs;
        public string CrosshairPositionText_Vs
        {
            get => _crosshairPositionText_Vs;
            set => SetProperty(ref _crosshairPositionText_Vs, value);
        }

        public static Crosshair MyCrosshair_Es { get; private set; }

        private string _crosshairPositionText_Es;
        public string CrosshairPositionText_Es
        {
            get => _crosshairPositionText_Es;
            set => SetProperty(ref _crosshairPositionText_Es, value);
        }

        public static Crosshair MyCrosshair_Disp { get; private set; }

        private string _crosshairPositionText_Disp;
        public string CrosshairPositionText_Disp
        {
            get => _crosshairPositionText_Disp;
            set => SetProperty(ref _crosshairPositionText_Disp, value);
        }

        public static Crosshair MyCrosshair_FL { get; private set; }

        private string _crosshairPositionText_FL;
        public string CrosshairPositionText_FL
        {
            get => _crosshairPositionText_FL;
            set => SetProperty(ref _crosshairPositionText_FL, value);
        }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;
        private readonly ObservableCollection<GroundInput> PrevGroundsInput;
        private readonly Dictionary<string, object> previousPropertyValues = [];


        public void ShowGroundInputErrorAlert()
        {
            var gi = GroundInput;
            var errors = new List<string>();
            if (gi.IsErrorGroundWaterTableAltitude)
                errors.Add("地下水位Zは孔口標高Z以下にしてください。");
            if (gi.IsErrorStressAltitude)
                errors.Add("地中応力計算用Zは孔口標高Z以下にしてください。");
            if (gi.IsErrorGroundWaterGLDepth)
                errors.Add("地下水位深度は0以下にしてください。");
            if (gi.IsErrorStressGLDepth)
                errors.Add("地中応力計算用深度は0以下にしてください。");

            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void GroundDelete()
        {
            // 地盤が1つしかない場合は削除不可
            if (GroundsInput.Count <= 1)
            {
                MessageBox.Show("地盤が1つしか存在しないため、削除できません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 選択中の地盤番号
            int index = GroundNo - 1;
            if (index < 0 || index >= GroundsInput.Count)
            {
                MessageBox.Show("削除対象が選択されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 確認メッセージ
            var result = MessageBox.Show(
                $"地盤番号 {GroundNo} を削除しますか？\n元に戻せません。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 変更前の状態を保存
                _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

                GroundsInput.RemoveAt(index);
                UpdateGroundsCountPlusOneList();

                // 削除後の選択状態を調整
                if (GroundsInput.Count > 0)
                {
                    GroundNo = Math.Min(GroundNo, GroundsInput.Count);
                    GroundInput = GroundsInput[GroundNo - 1];
                }
                else
                {
                    GroundNo = 1;
                    GroundInput = null;
                }

                Update();
            }
        }

        // GroundWindowInstance が設定された後に初期化処理を行う
        public void Initialize()
        {
            Update();
        }

        // 階段状データの作成メソッド
        private static (List<double>, List<double>) GetSteppedData(List<double> originalX, List<double> originalY)
        {
            // ガード節
            if (originalX == null || originalY == null || originalX.Count == 0 || originalY.Count == 0)
                return ([], []);

            // ステップ状のデータを生成
            List<double> steppedX = [];
            List<double> steppedY = [];

            for (int i = 0; i < originalX.Count; i++)
            {
                if (i == 0)
                {
                    steppedX.Add(0);
                    steppedY.Add(0);

                    steppedX.Add(originalX[i]);
                    steppedY.Add(0);
                }
                else
                {
                    steppedX.Add(originalX[i]);
                    steppedY.Add(originalY[i - 1]);
                }
                steppedX.Add(originalX[i]);
                steppedY.Add(originalY[i]);

                if (i == originalX.Count - 1)
                {
                    steppedX.Add(0);
                    steppedY.Add(originalY[i]);
                }
            }

            // 最後のデータポイントを追加
            steppedX.Add(originalX[^1]);
            steppedY.Add(originalY[^1]);

            return (steppedX, steppedY);
        }

        // rectangle
        private static List<CoordinateRect> GetRectangleGeometry(List<double> originalX, List<double> originalY)
        {
            List<CoordinateRect> coordinateRects = [];
            if (originalX.Count > 0)
            {
                for (int i = 0; i < originalX.Count; i++)
                {
                    if (i == 0)
                    {
                        coordinateRects.Add(new()
                        {
                            Bottom = originalY[i],
                            Top = 0,
                            Left = 0,
                            Right = originalX[i]
                        });
                    }
                    else
                    {
                        coordinateRects.Add(new()
                        {
                            Bottom = originalY[i],
                            Top = originalY[i - 1],
                            Left = 0,
                            Right = originalX[i]
                        });
                    }
                }
            }
            return coordinateRects;
        }

        private bool _hookedDispMouseMove, _hookedFLMouseMove, _hookedNMouseMove, _hookedVsMouseMove, _hookedEsMouseMove, _hookedCuMouseMove;

        // 地盤変位描画メソッド
        private void DrawGroundDisplacementGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> gLDepths = [];

            foreach (var data in GroundInput.GroundMassesData)
            {
                double _factor = data == GroundInput.GroundMassesData.First() ? 1.0 :
                                 data == GroundInput.GroundMassesData.Last() ? 0.0 : 0.5;
                double gLDepth = data.GLDepth + data.Spacing * _factor;
                gLDepths.Add(gLDepth);
            }

            var wpf = GroundWindowInstance.wpfPlotDisplacement;

            wpf.Plot.Clear();
            DrawSoilLayer(wpf);

            if (GroundInput.GroundLayers.Count != 0)
            {
                if (ChartDispContent.Contains("DmaxU*(レベル1)") || ChartDispContent.Contains("DmaxU*(レベル1,2)"))
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

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU1s[i]:N1}", new(dMaxU1s[i], gLDepths[i]));
                        }
                    }
                }
                if (ChartDispContent.Contains("DmaxU*(レベル2)") || ChartDispContent.Contains("DmaxU*(レベル1,2)"))
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

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU2s[i]:N1}", new(dMaxU2s[i], gLDepths[i]));
                        }
                    }
                }
                if (ChartDispContent.Contains("DmaxU*+∑γcyH(レベル1)") || ChartDispContent.Contains("DmaxU*+∑γcyH(レベル1,2)"))
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

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU1Pluss[i]:N1}", new(dMaxU1Pluss[i], gLDepths[i]));
                        }
                    }
                }
                if (ChartDispContent.Contains("DmaxU*+∑γcyH(レベル2)") || ChartDispContent.Contains("DmaxU*+∑γcyH(レベル1,2)"))
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

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU2Pluss[i]:N1}", new(dMaxU2Pluss[i], gLDepths[i]));
                        }
                    }
                }
            }

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
            //MyCrosshair_Disp = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            MyCrosshair_Disp ??= PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedDispMouseMove)
            {
                wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Disp", "変位(mm)", "GL基準深さ(m)", 1, 3);
                _hookedDispMouseMove = true;
            }
            //wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Disp", "変位(mm)", "GL基準深さ(m)", 1, 3);

            wpf.Refresh();
        }

        private void DrawFLScatter(List<double> gLDepths, int index, WpfPlot wpf, SKColor skColor)
        {
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
                for (int j = 0; j < gLDepth1ss[i].Count; j++)
                {
                    wpf.Plot.Add.Text($"{fL1ss[i][j]:N2}", new(fL1ss[i][j], gLDepth1ss[i][j]));
                }
            }
        }

        private void DrawFLGraph()
        {
            if (GroundWindowInstance == null) return;

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
        //private void DrawFLGraph()
        //{
        //    if (GroundWindowInstance == null)
        //    { return; }

        //    List<double> gLDepths = [];

        //    foreach (var data in GroundInput.GroundMassesData)
        //    {
        //        double _factor = data == GroundInput.GroundMassesData.First() ? 1.0 :
        //                         data == GroundInput.GroundMassesData.Last() ? 0.0 : 0.5;
        //        double gLDepth = data.GLDepth + data.Spacing * _factor;
        //        gLDepths.Add(gLDepth);
        //    }

        //    var wpf = GroundWindowInstance.wpfPlotFL;

        //    wpf.Plot.Clear();
        //    DrawSoilLayer(wpf);

        //    if (ChartFLContent.Contains("FL"))
        //    {
        //        if (ChartFLContent.Contains("FL(レベル1)") || ChartFLContent.Contains("FL(レベル1,2)"))
        //        {
        //            DrawFLScatter(gLDepths, 0, wpf, NikkenSKColor.SkyBlue);
        //        }

        //        if (ChartFLContent.Contains("FL(レベル2)") || ChartFLContent.Contains("FL(レベル1,2)"))
        //        {
        //            DrawFLScatter(gLDepths, 1, wpf, NikkenSKColor.DeepBlue);
        //        }
        //    }

        //    //var verticalLine = wpf.Plot.Add.VerticalLine(1, 1, Color.FromSKColor(NikkenSKColor.Red));

        //    string title = "液状化安全率 FL値分布";
        //    wpf.Plot.Axes.Title.Label.Text = title;
        //    wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

        //    string xLabel = "FL値";
        //    wpf.Plot.Axes.Bottom.Label.Text = xLabel;
        //    wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

        //    string yLabel = "GL基準深さ(m)";
        //    wpf.Plot.Axes.Left.Label.Text = yLabel;
        //    wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

        //    wpf.Plot.Axes.AutoScale();
        //    wpf.Plot.Axes.AutoScaleExpandY();

        //    wpf.Plot.Axes.Bottom.Min = 0.0;
        //    wpf.Plot.Axes.Bottom.Max = 1.0;

        //    // クロスヘアの初期化
        //    MyCrosshair_FL = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

        //    // 例: グラフ初期化時
        //    wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_FL", "FL", "GL基準深さ(m)", 1, 3);

        //    wpf.Refresh();
        //}

        // N値グラフ描画メソッド
        private void DrawNValueGraph()
        {
            if (GroundWindowInstance == null) return;

            List<double> ns = [];
            List<double> _bottomGLDepths = [];
            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                ns.Add(GroundInput.GroundMassesData[i].NValue);
                _bottomGLDepths.Add(GroundInput.GroundMassesData[i].GLDepth);
            }

            var wpfNValue = GroundWindowInstance.wpfPlotNValue;
            wpfNValue.Plot.Clear();
            DrawSoilLayer(wpfNValue);

            var scatter = wpfNValue.Plot.Add.Scatter(ns, _bottomGLDepths);
            scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;

            for (int i = 0; i < _bottomGLDepths.Count; i++)
                wpfNValue.Plot.Add.Text($"{ns[i]:N0}", new(ns[i], _bottomGLDepths[i]));

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
        //private void DrawNValueGraph()
        //{
        //    if (GroundWindowInstance == null)
        //    { return; }

        //    List<double> ns = [];
        //    List<double> _bottomGLDepths = [];

        //    for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
        //    {
        //        ns.Add(GroundInput.GroundMassesData[i].NValue);
        //        _bottomGLDepths.Add(GroundInput.GroundMassesData[i].GLDepth);
        //    }

        //    var wpfNValue = GroundWindowInstance.wpfPlotNValue;

        //    wpfNValue.Plot.Clear();
        //    DrawSoilLayer(wpfNValue);

        //    var scatter = wpfNValue.Plot.Add.Scatter(ns, _bottomGLDepths);

        //    scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
        //    scatter.LineWidth = 2;

        //    for (int i = 0; i < _bottomGLDepths.Count; i++)
        //    {
        //        wpfNValue.Plot.Add.Text($"{ns[i]:N0}", new(ns[i], _bottomGLDepths[i]));
        //    }

        //    string title = "N値分布";
        //    wpfNValue.Plot.Axes.Title.Label.Text = title;
        //    wpfNValue.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

        //    string xLabel = "N値";
        //    wpfNValue.Plot.Axes.Bottom.Label.Text = xLabel;
        //    wpfNValue.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

        //    string yLabel = "GL基準深さ(m)";
        //    wpfNValue.Plot.Axes.Left.Label.Text = yLabel;
        //    wpfNValue.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

        //    wpfNValue.Plot.Axes.AutoScale();

        //    wpfNValue.Plot.Axes.AutoScaleExpandY();

        //    wpfNValue.Plot.Axes.Bottom.Min = 0.0;
        //    wpfNValue.Plot.Axes.Bottom.Max = 60.0;

        //    // クロスヘアの初期化
        //    MyCrosshair_NValue = PlotHelper.InitCrosshair(wpfNValue, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

        //    // 例: グラフ初期化時
        //    wpfNValue.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_NValue", "N値", "GL基準深さ(m)", 1, 3);

        //    wpfNValue.Refresh();
        //}

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

                Color fillColor = new(0, 0, 0, 255);
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

            for (int i = 0; i < dataX1.Length; i++)
            {
                wpf.Plot.Add.Text($"{dataX1[i]:N0}", new(dataX1[i], dataY1[i]));
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


        // 太田・後藤式
        [RelayCommand]
        private void OnCalculateOtaVs()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                double yg;
                if (groundMassData.AgeCategory == "沖積層")
                { yg = 1.0; }
                else if (groundMassData.AgeCategory == "洪積層")
                { yg = 1.3; }
                else
                { yg = 1.0; }

                double si;
                if (groundMassData.GranularityClass == "粘性土")
                { si = 1.0; }
                else if (groundMassData.GranularityClass == "砂質土" || groundMassData.GranularityClass == "砂礫土")
                { si = 1.1; }
                else if (groundMassData.GranularityClass == "礫質土")
                { si = 1.4; }
                else
                { si = 1.0; }

                groundMassData.VS0 = 69 * Math.Pow(groundMassData.NValue, 0.17) * Math.Pow(Math.Abs(groundMassData.GLDepth) / 1.0, 0.2) * yg * si;
            }
        }

        // 今井・殿内式
        [RelayCommand]
        private void OnCalculateImaiVs()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            double a;
            double b;
            double c;

            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                if (groundMassData.AgeCategory == "沖積層" && groundMassData.GranularityClass == "粘性土")
                {
                    a = 50;
                    b = 0.42;
                    c = 80.0;
                }
                else if (groundMassData.AgeCategory == "沖積層" && groundMassData.GranularityClass == "砂質土")
                {
                    a = 90;
                    b = 0.30;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "沖積層" && groundMassData.GranularityClass == "礫質土")
                {
                    a = 80;
                    b = 0.38;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "洪積層" && groundMassData.GranularityClass == "粘性土")
                {
                    a = 130;
                    b = 0.29;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "洪積層" && groundMassData.GranularityClass == "砂質土")
                {
                    a = 110;
                    b = 0.30;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "洪積層" && groundMassData.GranularityClass == "礫質土")
                {
                    a = 140;
                    b = 0.26;
                    c = 0.0;
                }
                else
                {
                    a = 50;
                    b = 0.42;
                    c = 80.0;
                }
                groundMassData.VS0 = a * Math.Pow(groundMassData.NValue, b) + c;
            }
        }

        // 土層追加メソッド
        [RelayCommand]
        private void OnAddGroundLayer()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            var layers = GroundInput?.GroundLayers;
            if (layers == null) return;

            // 土層が0件のときは初期値を追加して終了
            if (layers.Count == 0)
            {
                var firstLayer = new GroundLayerInput
                {
                    BottomGLDepth = -3.0, // GL基準で下向きが負の想定
                };
                layers.Add(firstLayer);
                SelectedGroundLayerOnDataGrid = firstLayer;

                UpdateBedrockChecks();
                UpdateGroundLayerNo();
                Update();
                return;
            }

            // 選択行の直下 or 末尾へ追加
            int selectedIndex = layers.IndexOf(SelectedGroundLayerOnDataGrid);
            int insertIndex;
            GroundLayerInput newGroundLayer;

            if (selectedIndex >= 0 && selectedIndex < layers.Count - 1)
            {
                // 選択行とその下行の中間に追加
                double d1 = layers[selectedIndex].BottomGLDepth;
                double d2 = layers[selectedIndex + 1].BottomGLDepth;
                newGroundLayer = new GroundLayerInput
                {
                    BottomGLDepth = 0.5 * (d1 + d2),
                };
                insertIndex = selectedIndex + 1;
                layers.Insert(insertIndex, newGroundLayer);
            }
            else
            {
                // 末尾に追加（最後の下端から一定深さ下げる）
                double last = layers[layers.Count - 1].BottomGLDepth;
                newGroundLayer = new GroundLayerInput
                {
                    BottomGLDepth = last - 3.0,
                };
                layers.Add(newGroundLayer);
                insertIndex = layers.Count - 1;
            }

            // 追加行を選択
            SelectedGroundLayerOnDataGrid = layers[insertIndex];

            UpdateBedrockChecks();
            UpdateGroundLayerNo();
            Update();
        }
        //{
        //    // 変更前の状態を保存
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);


        //    // 選択されている行のインデックスを取得
        //    int selectedIndex = GroundInput.GroundLayers.IndexOf(SelectedGroundLayerOnDataGrid);

        //    // 選択されている行がある場合、その下に追加
        //    if (0 <= selectedIndex && selectedIndex < GroundInput.GroundLayers.Count - 1)
        //    {
        //        // 新しい GroundLayerDataItem を作成
        //        var newGroundLayer = new GroundLayerInput
        //        {
        //            BottomGLDepth = (
        //            GroundInput.GroundLayers[selectedIndex].BottomGLDepth +
        //            GroundInput.GroundLayers[selectedIndex + 1].BottomGLDepth) * 0.5,
        //        };
        //        GroundInput.GroundLayers.Insert(selectedIndex + 1, newGroundLayer);
        //    }
        //    else
        //    {
        //        // 新しい GroundLayerDataItem を作成
        //        var newGroundLayer = new GroundLayerInput
        //        {
        //            BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - 3.0,
        //        };

        //        // 選択行indexを最終行にあわせる
        //        selectedIndex = GroundInput.GroundLayers.Count - 1;

        //        // 選択されている行がない場合、末尾に追加
        //        GroundInput.GroundLayers.Add(newGroundLayer);
        //    }

        //    // 選択行を追加行にずらす
        //    SelectedGroundLayerOnDataGrid = GroundInput.GroundLayers[selectedIndex + 1];

        //    UpdateBedrockChecks();
        //    UpdateGroundLayerNo();
        //    Update();
        //}

        // 全土層削除メソッド
        [RelayCommand]
        private void OnDeleteAllGroundLayers()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);
            GroundInput.GroundLayers.Clear();
            UpdateBedrockChecks();
            UpdateGroundLayerNo();
            Update();
        }

        // GroundLayer番号の更新
        private void UpdateGroundLayerNo()
        {
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                GroundInput.GroundLayers[i].No = i + 1;
            }
        }

        // 土質点追加メソッド
        [RelayCommand]
        private void OnAddGroundMass()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            var masses = GroundInput?.GroundMassesData;
            if (masses == null) return;

            // 0件時は初期値を追加
            if (masses.Count == 0)
            {
                var first = new GroundMassDataInput
                {
                    GLDepth = -1.0, // GL基準で下向きが負の想定
                };
                masses.Add(first);
                SelectedGroundMassOnDataGrid = first;

                UpdateGroundMassDataLayer();
                Update();
                return;
            }

            // 選択行の直下 or 末尾へ追加
            int selectedIndex = masses.IndexOf(SelectedGroundMassOnDataGrid);
            int insertIndex;
            GroundMassDataInput newMass;

            if (selectedIndex >= 0 && selectedIndex < masses.Count - 1)
            {
                // 選択行とその下行の中間に追加
                double d1 = masses[selectedIndex].GLDepth;
                double d2 = masses[selectedIndex + 1].GLDepth;
                newMass = new GroundMassDataInput { GLDepth = 0.5 * (d1 + d2) };
                insertIndex = selectedIndex + 1;
                masses.Insert(insertIndex, newMass);
            }
            else
            {
                // 末尾に追加（最後のGLDepthから一定深さ下げる）
                double last = masses[masses.Count - 1].GLDepth;
                newMass = new GroundMassDataInput { GLDepth = last - 1.0 };
                masses.Add(newMass);
                insertIndex = masses.Count - 1;
            }

            // 追加行を選択
            SelectedGroundMassOnDataGrid = masses[insertIndex];

            UpdateGroundMassDataLayer();
            Update();
        }
        //{
        //    // 変更前の状態を保存
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //    // 選択されている行のインデックスを取得
        //    int selectedIndex = GroundInput.GroundMassesData.IndexOf(SelectedGroundMassOnDataGrid);

        //    // 選択されている行がある場合、その下に追加
        //    if (0 <= selectedIndex && selectedIndex < GroundInput.GroundMassesData.Count - 1)
        //    {
        //        // 新しい GroundLayerDataItem を作成
        //        var newGroundMass = new GroundMassDataInput
        //        {
        //            GLDepth = (
        //            GroundInput.GroundMassesData[selectedIndex].GLDepth +
        //            GroundInput.GroundMassesData[selectedIndex + 1].GLDepth) * 0.5,
        //        };

        //        GroundInput.GroundMassesData.Insert(selectedIndex + 1, newGroundMass);
        //    }
        //    else
        //    {
        //        // 新しい GroundLayerDataItem を作成
        //        var newGroundMass = new GroundMassDataInput
        //        {
        //            GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
        //        };

        //        // 選択行indexを最終行にあわせる
        //        selectedIndex = GroundInput.GroundMassesData.Count - 1;

        //        // 選択されている行がない場合、末尾に追加
        //        GroundInput.GroundMassesData.Add(newGroundMass);

        //    }

        //    // 選択行を追加行にずらす
        //    SelectedGroundMassOnDataGrid = GroundInput.GroundMassesData[selectedIndex + 1];

        //    UpdateGroundMassDataLayer();
        //    Update();
        //}

        // 全土質点削除メソッド
        [RelayCommand]
        private void OnDeleteAllGroundMasses()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            GroundInput.GroundMassesData.Clear();
            UpdateGroundMassDataLayer();
            Update();
        }

        // 選択行より下の行の土質点の間隔を1mに揃えるメソッド
        [RelayCommand]
        private void OnMake1mSpacing()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            var masses = GroundInput?.GroundMassesData;
            if (masses == null || masses.Count == 0) return;

            // 対象開始位置: 選択行の「次の行」から。未選択なら2行目(=index=1)から。
            int selectedIndex = masses.IndexOf(SelectedGroundMassOnDataGrid);
            int startIndex = (selectedIndex >= 0) ? selectedIndex + 1 : 1;

            if (startIndex >= masses.Count) return;

            // 1m ピッチで GLDepth を再配置（GLDepthは下向きが負）
            for (int i = startIndex; i < masses.Count; i++)
            {
                // 工学的基盤に到達したら以降は触らない
                if (masses[i].IsEngineeringBedrock) break;

                masses[i].GLDepth = masses[i - 1].GLDepth - 1.0;
            }

            // 以降の派生値（Spacing, Altitude など）を再計算・描画
            UpdateGroundMassDataLayer();
            Update();

            // エラーチェック（上行より小さいか）を実行してフラグを更新
            bool hasError = ValidateGroundMassMonotone(out string errorMessage);

            // DataGrid を強制更新（バインディング/スタイルの再評価で赤表示を反映）
            RevalidateAndRefreshGroundMassGrid();

            // 必要に応じてメッセージ表示
            if (hasError)
            {
                MessageBox.Show(errorMessage, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 「一つ上の行より小さい（より深い）GLDepthになっているか」を検証し、違反行の IsError を立てる
        private bool ValidateGroundMassMonotone(out string message)
        {
            message = string.Empty;
            var masses = GroundInput?.GroundMassesData;
            if (masses == null || masses.Count == 0) return false;

            // いったん全行のエラーフラグをクリア
            foreach (var m in masses) m.IsError = false;

            bool hasError = false;
            var lines = new List<string>();

            for (int i = 1; i < masses.Count; i++)
            {
                // 工学的基盤以降は任意でスキップ
                if (masses[i].IsEngineeringBedrock) break;

                // 下端Zの検証と同等: 現行は必ず一つ上の行より「小さい」必要がある
                if (masses[i].GLDepth >= masses[i - 1].GLDepth)
                {
                    masses[i].IsError = true;
                    hasError = true;
                    lines.Add($"行 {i + 1}: GLDepth は一つ上の行より小さい値（より深い値）にしてください。");
                }
            }

            if (hasError)
                message = string.Join("\n", lines);

            return hasError;
        }

        // エラーチェック再実行＋グリッド強制更新
        private void RevalidateAndRefreshGroundMassGrid()
        {
            // 必要なら GroundInput 側の整合検証を併用（エラーフラグ更新が内包されている前提）
            _ = GroundInput?.ValidateForAnalysis(out _);

            var view = CollectionViewSource.GetDefaultView(GroundInput?.GroundMassesData);
            view?.Refresh();

            var grid = GroundWindowInstance?.DataGridGroundMass;
            if (grid == null) return;

            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            grid.Items.Refresh();

            foreach (var item in grid.Items)
            {
                if (item == CollectionView.NewItemPlaceholder) continue;
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row) continue;

                foreach (var col in grid.Columns)
                {
                    if (col is DataGridBoundColumn bc)
                    {
                        if (bc.GetCellContent(item) is FrameworkElement fe)
                        {
                            BindingOperations.GetBindingExpression(fe, TextBox.TextProperty)?.UpdateSource();
                            BindingOperations.GetBindingExpression(fe, TextBox.TextProperty)?.UpdateTarget();
                            BindingOperations.GetBindingExpression(fe, TextBlock.TextProperty)?.UpdateSource();
                            BindingOperations.GetBindingExpression(fe, TextBlock.TextProperty)?.UpdateTarget();
                        }
                    }
                }
            }
        }

        // GroundMassDataLayer番号の更新
        private void UpdateGroundMassDataLayer()
        {
            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                GroundInput.GroundMassesData[i].No = i + 1;
            }
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }


        private void DataGridGroundLayer_Loaded(object sender, RoutedEventArgs e)
        {
            //if (DataGridGroundLayer.ItemsSource is ObservableCollection<GroundLayerDataItem> observableCollection)
            //{
            //    observableCollection.CollectionChanged += GroundLayerCollection_CollectionChanged;
            //}
        }

        private readonly bool initialSelection = true;


        // GroundNoコンボボックス
        //public void ComboBoxGroundNo_SelectionChanged(int selectedGroundNo, int previousSelectedGroundNo)
        //{
        //    if (selectedGroundNo != 1 && selectedGroundNo == GroundCountPlusOneList[^1])
        //    {
        //        GroundsInput.Add(new GroundInput() { GroundRef = "(GR" + selectedGroundNo.ToString() + ")" });
        //        UpdateGroundsCountPlusOneList();
        //    }

        //    if (previousSelectedGroundNo != -1)
        //    {
        //        GroundInput = GroundsInput[GroundNo - 1];
        //    }
        //    Update();
        //}
        //public void ComboBoxGroundNo_SelectionChanged(int selectedIndex, int previousSelectedIndex)
        //{
        //    // 変更前の状態を保存
        //    _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

        //    // selectedIndex: 0-based
        //    if (selectedIndex == GroundCountPlusOneList.Count - 1)
        //    {
        //        // (New)が選択された場合
        //        int newNo = GroundsInput.Count + 1;
        //        GroundsInput.Add(new GroundInput() { GroundRef = "(GR" + newNo.ToString() + ")" });
        //        UpdateGroundsCountPlusOneList();
        //        GroundNo = newNo; // 新しい地盤番号に切り替え
        //        GroundInput = GroundsInput.Last();
        //    }
        //    else
        //    {
        //        if (selectedIndex >= 0 && selectedIndex < GroundsInput.Count)
        //        {
        //            GroundNo = selectedIndex + 1;
        //            GroundInput = GroundsInput[selectedIndex];
        //        }
        //    }
        //    Update();
        //}

        public void GroundTextBox_LostFocus()
        {
            Update();
        }

        [RelayCommand]
        private void OnComboBoxLevelSelectionChanged(int selectedLevel)
        {
            Update();
        }

        //土質データ　土質点データの平均N値を代入する
        [RelayCommand]
        private void OnInputAverageNValue()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                List<double> nValues = [];
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundLayerDataItem.BottomAltitude + groundLayerDataItem.LayerThickness > groundMassData.AltitudeDepth &&
                        groundMassData.AltitudeDepth >= groundLayerDataItem.BottomAltitude)
                    {
                        nValues.Add(groundMassData.NValue);
                    }
                }
                if (nValues.Count > 0)
                {
                    groundLayerDataItem.NValue = nValues.Average();
                }
            }
            Update();
        }

        // 土層データ　土質点データの平均Vsを代入する
        [RelayCommand]
        private void InputModelAverageVs()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                List<double> vS0 = [];
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundLayerDataItem.BottomAltitude + groundLayerDataItem.LayerThickness > groundMassData.AltitudeDepth &&
                        groundMassData.AltitudeDepth > groundLayerDataItem.BottomAltitude)
                    {
                        vS0.Add(groundMassData.VS0);
                    }
                }
                if (vS0.Count > 0)
                {
                    groundLayerDataItem.Vs = vS0.Average();
                }
            }
            Update(); // グラフを更新
        }

        // 土層データ　変形係数にN値×700を代入する
        [RelayCommand]
        private void OnInput700N()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                if (groundLayerDataItem.GranularityClass == "砂質土" || groundLayerDataItem.GranularityClass == "砂礫土")
                {
                    groundLayerDataItem.Es = groundLayerDataItem.NValue * 700;
                }
            }
            Update(); // グラフを更新
        }

        // 土層データ　Cu=12.5N, 25Nを代入する
        [RelayCommand]
        private void OnInputC()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                if (groundLayerDataItem.GranularityClass == "粘性土" && groundLayerDataItem.AgeCategory == "沖積層")
                {
                    groundLayerDataItem.Cohesive = 20 - groundLayerDataItem.BottomGLDepth * 2.0 - groundLayerDataItem.LayerThickness / 2.0;
                }
                else if (groundLayerDataItem.GranularityClass == "粘性土" && groundLayerDataItem.AgeCategory == "洪積層")
                {
                    groundLayerDataItem.Cohesive = groundLayerDataItem.NValue * 12.5;
                }
            }
            Update(); // グラフを更新
        }

        [RelayCommand]
        private void OnApplyTypicalFc()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            foreach (var groundMassDataItem in GroundInput.GroundMassesData)
            {
                if (groundMassDataItem.GranularityClass == "砂質土" || groundMassDataItem.GranularityClass == "砂礫土")
                {
                    groundMassDataItem.Fc = 10;
                }
                else if (groundMassDataItem.GranularityClass == "粘性土")
                {
                    groundMassDataItem.Fc = 70;
                }

            }
            Update(); // グラフを更新
        }

        [RelayCommand]
        private void OnApplyGroundLayerNValue()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        }


        // Viewを閉じるためのメソッド
        [RelayCommand]
        private void CloseWindow()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }


        public bool ValidateForAnalysis(out string warningMessage)
        {
            bool hasWarning = false;
            warningMessage = "以下の項目に問題があります:\n";

            if (GroundsInput != null)
            {
                for (int i = 0; i < GroundsInput.Count; i++)
                {
                    if (!GroundsInput[i].ValidateForAnalysis(out string groundWarning))
                    {
                        hasWarning = true;
                        warningMessage += $"- 地盤番号{i + 1}:\n{groundWarning}";
                    }
                }
            }
            return !hasWarning;
        }

        [RelayCommand]
        private void OnOk()
        {
            if (GroundsInput != null)
            {
                if (!_mainWindowViewModel.CheckAndResetElementSplit("地盤"))
                    return; // キャンセル時は処理中断

                bool hasWarning = false;
                string warningMessage = "以下の項目に問題があります:\n";

                for (int i = 0; i < GroundsInput.Count; i++)
                {
                    if (!GroundsInput[i].ValidateForAnalysis(out string groundWarning))
                    {
                        hasWarning = true;
                        warningMessage += $"- 地盤番号{i + 1}:\n{groundWarning}";
                    }
                }

                if (hasWarning)
                {
                    warningMessage += "\n状態を保存してウィンドウを閉じますか？";
                    MessageBoxResult result = MessageBox.Show(warningMessage, "警告", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Cancel) return;
                }

                // 深いコピーを作成して代入
                InputModel.GroundsInput.Clear();

                foreach (var groundInput in GroundsInput)
                {
                    InputModel.GroundsInput.Add(groundInput.DeepCopy());
                }
            }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            // InputModel.GroundsInputをクリア
            InputModel.GroundsInput.Clear();

            // PrevGroundsInputの内容をInputModel.GroundsInputに追加
            foreach (var groundInput in PrevGroundsInput)
            {
                InputModel.GroundsInput.Add(groundInput.DeepCopy());
            }

            // ダイアログを閉じる
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public void Update()
        {
            if (GroundInput.GroundLayers.Count != 0)
            {
                RecalculateGroundLayerNo();
                RecalculateLayerThickness();
                RecalculateBottomAltitude();
            }

            if (GroundInput.GroundMassesData.Count != 0)
            {
                RecalculateGroundMassDataNo();
                RecalculateMassSpacing();
                RecalculateAltitude();
                RecalculateName();
                RecalculateDensityIsEngineeringBedrock();
                RecalculateH();
                RecalculateSigmaZ();
                RecalculateSigmaZPrime();
                RecalculateIsLiquefaction();
                RecalculateNL();
                RecalculateTauLonSigmaZPrime();
                RecalculateTauDonSigmaZprime();
                RecalculateFL();
                RecalculateBetaL();
                RecalculateGammaCy();
                RecalculateSigmaGammaCyH();
                RecalculateMass();
                RecalculateVSE();
            }

            DrawNValueGraph();
            DrawCuGraph();
            DrawVsGraph();
            DrawEsGraph();

            DrawGroundDisplacementGraph();
            DrawFLGraph();
        }

        // 土層番号の再計算
        internal void RecalculateGroundLayerNo()
        {
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                GroundInput.GroundLayers[i].No = i + 1;
            }
        }

        // 土質点番号の再計算
        internal void RecalculateGroundMassDataNo()
        {
            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                GroundInput.GroundMassesData[i].No = i + 1;
            }
        }

        // 下端Zの再計算
        internal void RecalculateBottomAltitude()
        {
            foreach (GroundLayerInput groundLayer in GroundInput.GroundLayers)
            {
                groundLayer.BottomAltitude = groundLayer.BottomGLDepth + GroundInput.GroundTopAltitude;
            }
        }

        // 深さの再計算
        internal void RecalculateGLDepth()
        {
            double totalThickness = 0;
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                totalThickness += groundMassData.Spacing;
                groundMassData.GLDepth = -totalThickness;
            }
        }

        // 厚さの再計算
        internal void RecalculateLayerThickness()
        {
            ObservableCollection<GroundLayerInput> groundLayerInput = GroundInput.GroundLayers;
            for (int i = 0; i < groundLayerInput.Count; i++)
            {
                if (i == 0)
                    groundLayerInput[i].LayerThickness = -groundLayerInput[i].BottomGLDepth;
                else
                    groundLayerInput[i].LayerThickness = -groundLayerInput[i].BottomGLDepth + groundLayerInput[i - 1].BottomGLDepth;
            }
        }


        // 深さの再計算
        internal void RecalculateBottomGLDepth()
        {
            double totalThickness = 0;
            foreach (GroundLayerInput groundLayer in GroundInput.GroundLayers)
            {
                totalThickness += groundLayer.LayerThickness;
                groundLayer.BottomGLDepth = -totalThickness;
            }
        }

        // 厚さの再計算
        internal void RecalculateMassSpacing()
        {
            ObservableCollection<GroundMassDataInput> groundMassesData = GroundInput.GroundMassesData;
            for (int i = 0; i < groundMassesData.Count; i++)
            {
                if (i == 0)
                    groundMassesData[i].Spacing = -groundMassesData[i].GLDepth;
                else
                    groundMassesData[i].Spacing = -groundMassesData[i].GLDepth + groundMassesData[i - 1].GLDepth;
            }
        }

        // Zの再計算
        internal void RecalculateAltitude()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                groundMassData.AltitudeDepth = groundMassData.GLDepth + GroundInput.GroundTopAltitude;
            }
        }

        // 高さの再計算
        internal void RecalculateH()
        {
            var groundMassesData = GroundInput.GroundMassesData;
            int count = groundMassesData.Count;

            for (int i = 0; i < count; i++)
            {
                var current = groundMassesData[i];
                if (current.IsEngineeringBedrock)
                    current.H = null;
                else if (i == 0)
                    current.H = (count == 1 || groundMassesData[1].IsEngineeringBedrock) ? current.Spacing : current.Spacing + groundMassesData[1].Spacing * 0.5;
                else if (i == count - 1 || groundMassesData[i + 1].IsEngineeringBedrock)
                    current.H = current.Spacing * 0.5;
                else
                    current.H = groundMassesData[i - 1].Spacing * 0.5 + current.Spacing * 0.5;
            }
        }
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    int count = groundMassesData.Count;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        for (int i = 0; i < count; i++)
        //        {
        //            var current = groundMassesData[i];
        //            if (current.IsEngineeringBedrock)
        //            {
        //                current.H = null;
        //            }
        //            else if (i == 0)
        //            {
        //                if (count == 1 || groundMassesData[1].IsEngineeringBedrock)
        //                {
        //                    current.H = current.Spacing;
        //                }
        //                else
        //                {
        //                    current.H = current.Spacing + groundMassesData[1].Spacing * 0.5;
        //                }
        //            }
        //            else if (i == count - 1 || groundMassesData[i + 1].IsEngineeringBedrock)
        //            {
        //                current.H = current.Spacing * 0.5;
        //            }
        //            else
        //            {
        //                current.H = groundMassesData[i - 1].Spacing * 0.5 + current.Spacing * 0.5;
        //            }
        //        }
        //    }
        //}

        // 密度、工学的基盤の再計算メソッド
        internal void RecalculateDensityIsEngineeringBedrock()
        {
            var masses = GroundInput.GroundMassesData;
            var layers = GroundInput.GroundLayers;

            foreach (var m in masses)
            {
                foreach (var l in layers)
                {
                    if (m.GLDepth >= l.BottomGLDepth)
                    {
                        m.Density = l.Density;
                        m.GranularityClass = l.GranularityClass;
                        m.AgeCategory = l.AgeCategory;
                        m.IsEngineeringBedrock = l.IsEngineeringBedrock;
                        break;
                    }
                }
            }
        }
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    var groundLayers = GroundInput.GroundLayers;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var massData in groundMassesData)
        //        {
        //            foreach (var layer in groundLayers)
        //            {
        //                if (massData.GLDepth >= layer.BottomGLDepth)
        //                {
        //                    massData.Density = layer.Density;
        //                    massData.GranularityClass = layer.GranularityClass;
        //                    massData.AgeCategory = layer.AgeCategory;
        //                    massData.IsEngineeringBedrock = layer.IsEngineeringBedrock;
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //}

        // 液状化の再計算メソッド
        //internal void RecalculateIsLiquefaction()
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    double groundWaterGLDepth = GroundInput.GroundWaterGLDepth;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var groundMassData in groundMassesData)
        //        {
        //            if (groundMassData.IsEngineeringBedrock)
        //            {
        //                groundMassData.IsLiquefactionLayer = false;
        //            }
        //            else
        //            {
        //                double Fc = groundMassData.Fc;
        //                double z = groundMassData.GLDepth;
        //                groundMassData.IsLiquefactionLayer = Liquefaction.IsLiquefactionLayer(groundWaterGLDepth, z, Fc);
        //            }
        //        }
        //    }
        //}
        internal void RecalculateIsLiquefaction()
        {
            var groundMassesData = GroundInput.GroundMassesData;
            double groundWaterGLDepth = GroundInput.GroundWaterGLDepth;

            foreach (var groundMassData in groundMassesData)
            {
                if (groundMassData.IsEngineeringBedrock)
                {
                    groundMassData.IsLiquefactionLayer = false;
                }
                else
                {
                    double Fc = groundMassData.Fc;
                    double z = groundMassData.GLDepth;
                    groundMassData.IsLiquefactionLayer = Liquefaction.IsLiquefactionLayer(groundWaterGLDepth, z, Fc);
                }
            }
        }

        internal void RecalculateNL()
        {
            var groundMassesData = GroundInput.GroundMassesData;

            foreach (var groundMassData in groundMassesData)
            {
                if (groundMassData.IsLiquefactionLayer)
                {
                    double CN = Math.Sqrt(100.0 / groundMassData.SigmaZPrime);
                    groundMassData.N1 = CN * groundMassData.NValue;
                    groundMassData.DeltaNf = 0.0;

                    double Fc = groundMassData.Fc;
                    if (Fc >= 5.0 && Fc < 10.0)
                        groundMassData.DeltaNf = 6.0 / 5.0 * (Fc - 5.0);
                    else if (Fc >= 10.0 && Fc < 20.0)
                        groundMassData.DeltaNf = 0.2 * (Fc - 10.0) + 6.0;
                    else if (Fc >= 20.0 && Fc <= 50.0)
                        groundMassData.DeltaNf = 0.1 * (Fc - 20.0) + 8.0;

                    groundMassData.NL = groundMassData.N1 + groundMassData.DeltaNf;
                }
                else
                {
                    groundMassData.N1 = null;
                    groundMassData.DeltaNf = null;
                    groundMassData.NL = null;
                }
            }
        }
        //internal void RecalculateNL()
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var groundMassData in groundMassesData)
        //        {
        //            if (groundMassData.IsLiquefactionLayer)
        //            {
        //                double CN = Math.Sqrt(100.0 / groundMassData.SigmaZPrime);
        //                groundMassData.N1 = CN * groundMassData.NValue;
        //                groundMassData.DeltaNf = 0.0;

        //                double Fc = groundMassData.Fc;
        //                if (Fc >= 5.0 && Fc < 10.0)
        //                {
        //                    groundMassData.DeltaNf = 6.0 / 5.0 * (Fc - 5.0);
        //                }
        //                else if (Fc >= 10.0 && Fc < 20.0)
        //                {
        //                    groundMassData.DeltaNf = 0.2 * (Fc - 10.0) + 6.0;
        //                }
        //                else if (Fc >= 20.0 && Fc <= 50.0)
        //                {
        //                    groundMassData.DeltaNf = 0.1 * (Fc - 20.0) + 8.0;
        //                }
        //                groundMassData.NL = groundMassData.N1 + groundMassData.DeltaNf;
        //            }
        //            else
        //            {
        //                groundMassData.N1 = null;
        //                groundMassData.DeltaNf = null;
        //                groundMassData.NL = null;
        //            }
        //        }
        //    }
        //}

        // τL/σz'
        internal void RecalculateTauLonSigmaZPrime()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                if (groundMassData.IsLiquefactionLayer)
                {
                    double _NL = groundMassData.NL.GetValueOrDefault();
                    groundMassData.TauLonSigmaZPrime = 0.0410 * (Math.Sqrt(_NL) + 0.00903 * Math.Pow(_NL / 10, 7));
                }
                else
                {
                    groundMassData.TauLonSigmaZPrime = null;
                }
            }
        }
        //internal void RecalculateTauLonSigmaZPrime()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            if (groundMassData.IsLiquefactionLayer)
        //            {
        //                double _NL = groundMassData.NL.GetValueOrDefault();
        //                groundMassData.TauLonSigmaZPrime = 0.0410 * (Math.Sqrt(_NL) + 0.00903 * Math.Pow(_NL / 10, 7));
        //            }
        //            else
        //            {
        //                groundMassData.TauLonSigmaZPrime = null;
        //            }
        //        }
        //    }
        //}

        // Kohji Tokimatsu and Yoshiaki Yoshimi (1983) Empirical correlation of
        // soil Liquefaction based on SPT N-value and fines content,
        // "Soils and Foundations, vol 23, No. 4, pp. 56-74
        //internal void RecalculateTauLonSigmaZPrime2()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            if (groundMassData.IsLiquefactionLayer)
        //            {
        //                double _NL = groundMassData.NL.GetValueOrDefault();
        //                double Cs = 80;
        //                double a = 0.45;
        //                double Cr = 0.57;
        //                double n = 14;
        //                groundMassData.TauLonSigmaZPrime = a * Cr * (16 * Math.Sqrt(_NL) / 100.0 + Math.Pow(16 * Math.Sqrt(_NL) / Cs, n));

        //            }
        //            else
        //            {
        //                groundMassData.TauLonSigmaZPrime = null;
        //            }
        //        }
        //    }
        //}
        internal void RecalculateTauLonSigmaZPrime2()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                if (groundMassData.IsLiquefactionLayer)
                {
                    double _NL = groundMassData.NL.GetValueOrDefault();
                    double Cs = 80;
                    double a = 0.45;
                    double Cr = 0.57;
                    double n = 14;
                    groundMassData.TauLonSigmaZPrime = a * Cr * (16 * Math.Sqrt(_NL) / 100.0 + Math.Pow(16 * Math.Sqrt(_NL) / Cs, n));
                }
                else
                {
                    groundMassData.TauLonSigmaZPrime = null;
                }
            }
        }

        // τd/σz'
        internal void RecalculateTauDonSigmaZprime()
        {
            double magnitude = 7.5;
            double rn = 0.1 * (magnitude - 1.0);
            double alphaMax = 3.5;
            double gravity = 9.8;

            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {

                if (levelIndex == 0)
                { alphaMax = GroundInput.GroundAcceleration1; }
                else if (levelIndex == 1)
                { alphaMax = GroundInput.GroundAcceleration2; }

                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.RD = 1.0 - 0.015 * Math.Abs(groundMassData.GLDepth);
                        double sigmaZ = groundMassData.SigmaZ;
                        double sigmaZPrime = groundMassData.SigmaZPrime;
                        groundMassData.TauDonSigmaZPrime[levelIndex] = rn * alphaMax / gravity * sigmaZ / sigmaZPrime * groundMassData.RD;
                    }
                    else
                    {
                        groundMassData.RD = null;
                        groundMassData.TauDonSigmaZPrime[levelIndex] = null;
                    }
                }
            }
        }

        // FL
        internal void RecalculateFL()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.FL[levelIndex] = groundMassData.TauLonSigmaZPrime / groundMassData.TauDonSigmaZPrime[levelIndex];
                    }
                    else
                    {
                        groundMassData.FL[levelIndex] = null;
                    }
                }
            }
        }

        // γcy
        internal void RecalculateGammaCy()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.GammaCy[levelIndex]
                            = Liquefaction.CalculateGammaCy(
                                groundMassData.NL.GetValueOrDefault(), groundMassData.TauDonSigmaZPrime[levelIndex].GetValueOrDefault());
                    }
                    else
                    {
                        groundMassData.GammaCy[levelIndex] = null;
                    }
                }
            }
        }

        // ∑σcyH
        internal void RecalculateSigmaGammaCyH()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                double _sigmaGammaCyH = 0;
                for (int i = GroundInput.GroundMassesData.Count - 1; i >= 0; i--)
                {
                    if (GroundInput.GroundMassesData[i].IsEngineeringBedrock == true)
                    {
                        GroundInput.GroundMassesData[i].SigmaGammaCyH[levelIndex] = 0.0;
                    }
                    else if (GroundInput.GroundMassesData[i].IsEngineeringBedrock == false)
                    {
                        _sigmaGammaCyH += GroundInput.GroundMassesData[i].GammaCy[levelIndex].GetValueOrDefault() / 100.0
                            * GroundInput.GroundMassesData[i].H.GetValueOrDefault() * 1000.0;
                        GroundInput.GroundMassesData[i].SigmaGammaCyH[levelIndex] = _sigmaGammaCyH;
                    }
                }
            }
        }

        // βL
        internal void RecalculateBetaL()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.BetaL[levelIndex] = Liquefaction.CalculateBetaL(groundMassData.GLDepth, groundMassData.NL.GetValueOrDefault());
                    }
                    else
                    {
                        groundMassData.BetaL[levelIndex] = null;
                    }
                }
            }
        }

        internal void RecalculateName()
        {
            var masses = GroundInput.GroundMassesData;
            var layers = GroundInput.GroundLayers;

            foreach (var m in masses)
            {
                bool found = false;
                foreach (var l in layers)
                {
                    if (m.GLDepth >= l.BottomGLDepth)
                    {
                        m.LayerNo = layers.IndexOf(l) + 1;
                        m.Name = l.Name;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    m.LayerNo = null;
                    m.Name = "";
                }
            }
        }
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    var groundLayers = GroundInput.GroundLayers;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var groundMassData in groundMassesData)
        //        {
        //            bool found = false;
        //            foreach (var layer in groundLayers)
        //            {
        //                if (groundMassData.GLDepth >= layer.BottomGLDepth)
        //                {
        //                    groundMassData.LayerNo = groundLayers.IndexOf(layer) + 1;
        //                    groundMassData.Name = layer.Name;
        //                    found = true;
        //                    break;
        //                }
        //            }
        //            if (!found)
        //            {
        //                groundMassData.LayerNo = null;
        //                groundMassData.Name = "";
        //            }
        //        }
        //    }
        //}

        // σz
        internal void RecalculateSigmaZ()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                groundMassData.SigmaZ = 0.0;

                for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
                {
                    if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
                    {
                        groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
                    }
                    else
                    {
                        if (j == 0)
                        {
                            groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density * (0 - groundMassData.GLDepth);
                        }
                        else
                        {
                            groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density
                                * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
                        }
                        break;
                    }
                }
            }
        }
        //internal void RecalculateSigmaZ()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            groundMassData.SigmaZ = 0.0;

        //            for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
        //            {
        //                if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
        //                {
        //                    groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
        //                }
        //                else
        //                {
        //                    if (j == 0)
        //                    {
        //                        groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density
        //                            * (0 - groundMassData.GLDepth);
        //                    }
        //                    else
        //                    {
        //                        groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density
        //                            * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
        //                    }
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //}

        // σz'
        internal void RecalculateSigmaZPrime()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                groundMassData.SigmaZPrime = 0.0;

                for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
                {
                    if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
                    {
                        groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
                    }
                    else
                    {
                        if (j == 0)
                        {
                            groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density * (0 - groundMassData.GLDepth);
                        }
                        else
                        {
                            groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density
                                * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
                        }
                    }
                }
                groundMassData.SigmaZPrime -= 10.0 * Math.Max(0.0, GroundInput.GroundWaterGLDepth - groundMassData.GLDepth);
            }
        }
        //internal void RecalculateSigmaZPrime()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            groundMassData.SigmaZPrime = 0.0;

        //            for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
        //            {
        //                if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
        //                {
        //                    groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
        //                }
        //                else
        //                {
        //                    if (j == 0)
        //                    {
        //                        groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density
        //                            * (0 - groundMassData.GLDepth);
        //                    }
        //                    else
        //                    {
        //                        groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density
        //                            * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
        //                    }
        //                }
        //            }
        //            groundMassData.SigmaZPrime -= 10.0 * Math.Max(0.0, GroundInput.GroundWaterGLDepth - groundMassData.GLDepth);
        //        }
        //    }
        //}

        // M
        internal void RecalculateMass()
        {
            double zi1;
            double zi2;
            double zj1;
            double zj2;

            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                GroundInput.GroundMassesData[i].Mass = 0.0;

                if (i == 0)
                    zi1 = 0;
                else
                    zi1 = (GroundInput.GroundMassesData[i - 1].GLDepth + GroundInput.GroundMassesData[i].GLDepth) / 2.0;

                if (i != GroundInput.GroundMassesData.Count - 1)
                    zi2 = (GroundInput.GroundMassesData[i].GLDepth + GroundInput.GroundMassesData[i + 1].GLDepth) / 2.0;
                else
                    zi2 = GroundInput.GroundMassesData[i].GLDepth;

                for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
                {
                    zj1 = GroundInput.GroundLayers[j].BottomGLDepth + GroundInput.GroundLayers[j].LayerThickness;
                    zj2 = GroundInput.GroundLayers[j].BottomGLDepth;

                    GroundInput.GroundMassesData[i].Mass += Math.Max(Math.Min(zi1, zj1) - Math.Max(zi2, zj2), 0)
                        * GroundInput.GroundLayers[j].Density / 9.806665;
                }
            }
        }
        //internal void RecalculateMass()
        //{
        //    double zi1;
        //    double zi2;
        //    double zj1;
        //    double zj2;
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
        //        {
        //            GroundInput.GroundMassesData[i].Mass = 0.0;
        //            if (i == 0)
        //            {
        //                zi1 = 0;
        //            }
        //            else
        //            {
        //                zi1 = (GroundInput.GroundMassesData[i - 1].GLDepth + GroundInput.GroundMassesData[i].GLDepth) / 2.0;
        //            }

        //            if (i != GroundInput.GroundMassesData.Count - 1)
        //            {
        //                zi2 = (GroundInput.GroundMassesData[i].GLDepth + GroundInput.GroundMassesData[i + 1].GLDepth) / 2.0;
        //            }
        //            else
        //            {
        //                zi2 = GroundInput.GroundMassesData[i].GLDepth;
        //            }

        //            for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
        //            {
        //                zj1 = GroundInput.GroundLayers[j].BottomGLDepth + GroundInput.GroundLayers[j].LayerThickness;
        //                zj2 = GroundInput.GroundLayers[j].BottomGLDepth;

        //                GroundInput.GroundMassesData[i].Mass += Math.Max(Math.Min(zi1, zj1) - Math.Max(zi2, zj2), 0) * GroundInput.GroundLayers[j].Density / 9.806665;
        //            }
        //        }
        //    }
        //}

        //Vse
        internal void RecalculateVSE()
        {
            var groundMassesData = GroundInput.GroundMassesData;
            //var groundLayers = GroundInput.GroundLayers;
            var bedrockDensity = GroundInput.BedrockDensity;
            var bedrockShearWaveVelocity = GroundInput.BedrockShearWaveVelocity;
            var shallowSoilType = GroundInput.ShallowSoilType;
            var calculationMethod = GroundInput.CalculationMethod;

            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                // 地震荷重により決まる係数
                double L = (levelIndex == 0) ? 0.2 : 1.0;

                // 地域係数
                double Z = 1.0;

                // 表層の土質の動的変形特性から決まる定数
                double CAlpha = (shallowSoilType == "粘性土") ? 25.0 : 40.0;

                double T0 = 0.0; // 初期値
                double SigmaH = 0.0; // 初期値
                double SigmaGammaVS0H = 0.0; // 初期値
                foreach (var groundMassData in groundMassesData)
                {
                    if (groundMassData.IsEngineeringBedrock) break;

                    var h = groundMassData.H.GetValueOrDefault();
                    var vs0 = groundMassData.VS0;
                    var density = groundMassData.Density;

                    T0 += 4.0 * h / vs0;
                    SigmaH += h;
                    SigmaGammaVS0H += density * vs0 * h;
                }

                // 地盤の地震時の固有周期ののび
                double alpha = Math.Min(1 + L * Z * CAlpha * T0 / SigmaH, 4.0);

                GroundInput.NaturalPeriod = T0;
                GroundInput.NaturalPeriods[levelIndex] = alpha * T0;

                // 地盤の表層と工学的基盤の初期インピーダンス比
                double Rz0 = SigmaGammaVS0H / (bedrockDensity * bedrockShearWaveVelocity * SigmaH);
                double beta = 3.0 / 4.0 * (1.0 - 1.0 / Math.Pow(2.0, alpha - 1.0)) / (1 - Rz0);

                double mu = 0.0;
                double uNPlusOne = 0.0;

                for (int i = 0; i < groundMassesData.Count; i++)
                {
                    var groundMassData = groundMassesData[i];
                    var density = groundMassData.Density;
                    var vs0 = groundMassData.VS0;
                    //var h = groundMassData.H.GetValueOrDefault();

                    // 等価S波速度
                    groundMassData.VSE[levelIndex] = Math.Pow(density * vs0 / bedrockDensity / bedrockShearWaveVelocity, beta) * vs0;

                    // 等価せん断ばね剛性
                    groundMassData.K[levelIndex] = density / 9.80665 * Math.Pow(groundMassData.VSE[levelIndex], 2.0) / groundMassData.Spacing;

                    if (i == 0)
                    {
                        groundMassData.U[levelIndex] = 1.0; // 地表における変位
                    }
                    else
                    {
                        mu += groundMassesData[i - 1].Mass * groundMassesData[i - 1].U[levelIndex];
                        groundMassData.U[levelIndex] = groundMassesData[i - 1].U[levelIndex] - 40.0 / groundMassesData[i - 1].K[levelIndex] / Math.Pow(alpha * T0, 2.0) * mu;
                    }

                    if (groundMassData.IsEngineeringBedrock && i < groundMassesData.Count - 1)
                    {
                        uNPlusOne = groundMassData.U[levelIndex];
                        for (int j = i + 1; j < groundMassesData.Count; j++)
                        {
                            groundMassesData[j].U[levelIndex] = 0.0;
                        }
                        break;
                    }
                    else if (i == groundMassesData.Count - 1)
                    {
                        uNPlusOne = groundMassData.U[levelIndex];
                    }
                }

                foreach (var groundMassData in groundMassesData)
                {
                    groundMassData.UStar[levelIndex] = (groundMassData.U[levelIndex] - uNPlusOne) / (1 - uNPlusOne);
                    if (groundMassData.IsEngineeringBedrock)
                    {
                        for (int j = groundMassesData.IndexOf(groundMassData) + 1; j < groundMassesData.Count; j++)
                        {
                            groundMassesData[j].UStar[levelIndex] = 0.0;
                        }
                        break;
                    }
                }

                double fA = Math.Min(1.6 * alpha * T0, 1);
                double C1 = (shallowSoilType == "粘性土") ? 0.0028 : 0.0015;
                double C2 = (shallowSoilType == "粘性土") ? 0.53 : 0.666;
                double Dmax = 0;

                if (calculationMethod == "a1(b1)")
                {
                    Dmax = C1 * (Math.Pow(alpha, 2.0) - 1.0) * fA * SigmaH * (C2 * (1 - 1 / Math.Pow(alpha, 2.0)) + 2.0 * Rz0 / alpha);
                }
                else if (calculationMethod == "a2(b2)")
                {
                    Dmax = C1 * (Math.Pow(alpha, 2.0) - 1.0) * fA * SigmaH;
                }

                foreach (var groundMassData in groundMassesData)
                {
                    groundMassData.DmaxUStar[levelIndex] = Dmax * groundMassData.UStar[levelIndex] * 1000.0;
                    groundMassData.DmaxUStarSigmaGammaCyH[levelIndex] = groundMassData.DmaxUStar[levelIndex] + groundMassData.SigmaGammaCyH[levelIndex];
                }
            }
        }

        public void DataGridGroundLayer_CellEditEnding()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            Update();
        }


        private void DataGridGroundMass_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.EditingElement is not TextBox editedTextBox) return;
            if (!double.TryParse(editedTextBox.Text, out double doubleValue)) return;
            if (e.Column is not DataGridBoundColumn boundColumn || boundColumn.Binding is not Binding binding) return;

            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            var targetData = e.Row?.Item as GroundMassDataInput;
            if (targetData == null) return;

            switch (binding.Path.Path)
            {
                case nameof(GroundMassDataInput.Spacing): targetData.Spacing = doubleValue; break;
                case nameof(GroundMassDataInput.Fc): targetData.Fc = doubleValue; break;
                case nameof(GroundMassDataInput.NValue): targetData.NValue = doubleValue; break;
                case nameof(GroundMassDataInput.VS0): targetData.VS0 = doubleValue; break;
            }

            Update();
            RevalidateAndRefreshGroundMassGrid();
        }
        //{
        //    if (e.EditAction == DataGridEditAction.Commit && e.EditingElement is TextBox editedTextBox)
        //    {
        //        if (double.TryParse(editedTextBox.Text, out double doubleValue))
        //        {
        //            if (e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is System.Windows.Data.Binding binding)
        //            {
        //                // 変更前の状態を保存
        //                _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //                string bindingPath = binding.Path.Path;
        //                Console.WriteLine($"Binding Name: {bindingPath}");

        //                var targetData = GroundInput.GroundMassesData[GroundNo - 1];
        //                switch (bindingPath)
        //                {
        //                    case "Spacing":
        //                        targetData.Spacing = doubleValue;
        //                        break;
        //                    case "Fc":
        //                        targetData.Fc = doubleValue;
        //                        break;
        //                    case "NValue":
        //                        targetData.NValue = doubleValue;
        //                        break;
        //                    case "VS0":
        //                        targetData.VS0 = doubleValue;
        //                        break;
        //                }
        //            }
        //        }
        //    }
        //}

        private bool _isUpdatingValues = true;

        public void TextBoxGroundWaterTableAltitude_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // 変更前の状態を保存
                _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

                _isUpdatingValues = false;
                GroundInput.GroundWaterGLDepth = GroundInput.GroundWaterTableAltitude - GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;

                // UI・グラフ等の再描画
                Update();
            }
        }

        public void TextBoxGroundStressAltitude_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // 変更前の状態を保存
                _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

                _isUpdatingValues = false;
                GroundInput.StressGLDepth = GroundInput.StressAltitude - GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;

                // UI・グラフ等の再描画
                Update();
            }
        }

        public void TextBoxStressGLDepth_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // 変更前の状態を保存
                _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

                _isUpdatingValues = false;
                GroundInput.StressAltitude = GroundInput.StressGLDepth + GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;

                //UI更新
                Update();
            }
        }

        public void TextBoxGroundWaterGLDepth_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // 変更前の状態を保存
                _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

                _isUpdatingValues = false;
                GroundInput.GroundWaterTableAltitude = GroundInput.GroundWaterGLDepth + GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;
                Update();
            }
        }


        public void DataGridGroundLayer_RowEditEnding(/*string newText*/)
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            Update();
        }

        public void GroundTopAltitudeTextBox_LostFocus()
        {
            // 変更前の状態を保存
            _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

            GroundInput.GroundWaterTableAltitude = GroundInput.GroundWaterGLDepth + GroundInput.GroundTopAltitude;
            GroundInput.StressAltitude = GroundInput.StressGLDepth + GroundInput.GroundTopAltitude;

            // UI・グラフ等の再描画
            Update();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // テキストボックスがフォーカスを持っていない場合、フォーカスを設定し、全テキストを選択
                textBox.Focus();
                e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        // 土層入力内コンボボックス変化時のメソッド
        public void ComboBox_SelectionChangedCommand()
        {
            Update(); // Update() を呼び出してグラフを更新
        }
    }

    [Serializable]
    public class CustomScatterPoint(double x, double y, string text) : ObservablePoint(x, y)
    {
        // テキストラベルのプロパティ
        public string Text { get; set; } = text;
    }
}