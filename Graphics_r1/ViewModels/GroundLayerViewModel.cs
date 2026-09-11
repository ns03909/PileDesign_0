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
    /// GroundLayerViewModelクラス
    /// </summary>
    public partial class GroundLayerViewModel : ObservableObject, ICloseable
    {
        public readonly UndoManager _undoManager = new();

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

        // Update() 再入防止・デバウンス
        private bool _isUpdating;
        private bool _updatePending;
        private System.Windows.Threading.DispatcherTimer? _updateDebounceTimer;

        // GroundInput プロパティ: 購読の付け替えを内包
        private GroundInput _groundInput;
        public GroundInput? GroundInput
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

            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            UpdateGroundsCountPlusOneList();

            // ここで GroundInput セッターを通す（購読される）
            GroundInput = GroundsInput[Math.Clamp(GroundNo - 1, 0, GroundsInput.Count - 1)];

            // Update(); は Initialize() で呼ばれる
        }
        // 変更監視の購読・解除
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

                // 再計算・再描画（デバウンス）
                ScheduleUpdate();
            }
        }

        // GroundNo 変更時も GroundInput セッターで購読が付け替えられる
        public void ComboBoxGroundNo_SelectionChanged(int selectedIndex/*, int previousSelectedIndex*/)
        {
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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

        [RelayCommand]
        public void Undo()
        {
            // Redo時に現在のライブ状態を復元できるよう、Undo前に履歴へ追加
            // (PushState は「編集前」状態を積むパターンのためライブ状態が履歴に無い)
            if (_undoManager.CurrentIndex == _undoManager.History.Count - 1)
            {
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());
            }
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is IEnumerable<GroundInput> state)
            {
                GroundsInput = new ObservableCollection<GroundInput>(state.Select(x => x.DeepCopy()));
                if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
                    GroundInput = GroundsInput[GroundNo - 1];
                else if (GroundsInput.Count > 0)
                    GroundInput = GroundsInput[0];
                else
                    GroundInput = null;

                Update();
            }
        }

        [RelayCommand]
        public void Redo()
        {
            _undoManager.RedoSnapshot();
            if (_undoManager.CurrentState is IEnumerable<GroundInput> state)
            {
                GroundsInput = new ObservableCollection<GroundInput>(state.Select(x => x.DeepCopy()));
                if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
                    GroundInput = GroundsInput[GroundNo - 1];
                else if (GroundsInput.Count > 0)
                    GroundInput = GroundsInput[0];
                else
                    GroundInput = null;

                Update();
            }
        }

        private ObservableCollection<string> _groundCountPlusOneList;
        public ObservableCollection<string> GroundCountPlusOneList
        {
            get => _groundCountPlusOneList;
            set => SetProperty(ref _groundCountPlusOneList, value);
        }

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

        // セル選択で最後にフォーカスされた土質点行（SelectedItemバインディングに影響されない）
        internal GroundMassDataInput LastFocusedGroundMass { get; set; }


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

        // N値分布グラフ — 表示系列の切替 (どちらも既定 ON)
        private bool _isLayerNValueGraphVisible = true;
        public bool IsLayerNValueGraphVisible
        {
            get => _isLayerNValueGraphVisible;
            set { if (SetProperty(ref _isLayerNValueGraphVisible, value)) DrawNValueGraph(); }
        }

        private bool _isMassPointNValueGraphVisible = true;
        public bool IsMassPointNValueGraphVisible
        {
            get => _isMassPointNValueGraphVisible;
            set { if (SetProperty(ref _isMassPointNValueGraphVisible, value)) DrawNValueGraph(); }
        }

        public string[] AgeCategoryOption { get; } = ["沖積層", "洪積層"];

        public string[] ShallowSoilTypeOption { get; } =
        [
            "粘性土",
            "砂質土"
        ];

        //// 算定法
        public string[] CalculationMethodOption { get; } =
        [
            "a1(b1)",
            "a2(b2)",
            "応答スペクトル法"
        ];

        public string[] ChartDispContentOption { get; } =
        [
            "DmaxU*(レベル1)",
            "DmaxU*(レベル2)",
            "DmaxU*(レベル1, 2)",
            "DmaxU*, DmaxU*+∑γcyH(レベル1)",
            "DmaxU*, DmaxU*+∑γcyH(レベル2)",
            "DmaxU*, DmaxU*+∑γcyH(レベル1, 2)",
        ];

        public ObservableCollection<string> ChartDispContents { get; } = [];
        private string _ChartDispContent = "DmaxU*(レベル1, 2)";
        public string ChartDispContent
        {
            get => _ChartDispContent;
            set => SetProperty(ref _ChartDispContent, value);
        }

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

        public ObservableCollection<ExampleItem> ExampleItems { get; } = [];

        private ExampleItem? _selectedExampleItem;
        public ExampleItem? SelectedExampleItem
        {
            get => _selectedExampleItem;
            set
            {
                // null をセットする場合はそのまま反映（UIクリア用）
                if (value == null)
                {
                    SetProperty(ref _selectedExampleItem, null);
                    return;
                }

                // 選択されたら、まず現在状態を undo スタックへ保存
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                // 実行（ExampleItem が ICommand を保持している前提）
                value.Command?.Execute(null);

                // 実行後に選択をクリアして、同じ項目を再選択できるようにする
                _selectedExampleItem = null;
                OnPropertyChanged(nameof(SelectedExampleItem));
            }
        }

        [RelayCommand]
        private void OnSliderEngineeringBedrockValueChanged(double value)
        {
            if (GroundInput?.GroundLayers == null) return;
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
            if (GroundInput?.GroundLayers == null) return;
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

        // 土層削除メソッド
        [RelayCommand]
        public void DeleteGroundLayer(object sender)
        {
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());
            if (sender is not GroundLayerInput itemToDelete) return;
            if (GroundInput?.GroundLayers == null) return;
            GroundInput.GroundLayers.Remove(itemToDelete);

            SafeRefreshDataGrid(GroundWindowInstance?.DataGridGroundLayer);

            UpdateGroundLayerNo();
            Update();
        }

        // 土質点削除メソッド
        [RelayCommand]
        public void DeleteGroundMass(object sender)
        {
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());
            if (sender is not GroundMassDataInput itemToDelete) return;
            if (GroundInput?.GroundMassesData == null) return;
            GroundInput.GroundMassesData.Remove(itemToDelete);

            SafeRefreshDataGrid(GroundWindowInstance?.DataGridGroundMass);
            UpdateGroundMassDataLayer();
            Update();
        }


        // すべての行の番号を更新
        private static void UpdateAllRowNumbers(DataGrid dataGrid)
        {
            for (int i = 0; i < dataGrid.Items.Count; i++)
            {
                if (dataGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                {
                    row.Header = (i + 1).ToString();
                }
            }
        }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;
        private readonly ObservableCollection<GroundInput> PrevGroundsInput;
        private readonly Dictionary<string, object> previousPropertyValues = [];


        public void ShowGroundInputErrorAlert()
        {
            var gi = GroundInput;
            if (gi == null) return;
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
                MessageService.Show(string.Join("\n", errors), "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void GroundDelete()
        {
            // 地盤が1つしかない場合は削除不可
            if (GroundsInput.Count <= 1)
            {
                MessageService.Show("地盤が1つしか存在しないため、削除できません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 選択中の地盤番号
            int index = GroundNo - 1;
            if (index < 0 || index >= GroundsInput.Count)
            {
                MessageService.Show("削除対象が選択されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 杭配置からの参照チェック (使用中なら削除を拒否)
            var referencingPiles = _mainWindowViewModel?.CurrentInputModel?.PileLayoutItems?
                .Where(p => p != null && p.GroundNo == GroundNo)
                .Select(p => p.PileNo)
                .OrderBy(no => no)
                .ToList();
            if (referencingPiles != null && referencingPiles.Count > 0)
            {
                string list = string.Join(", ", referencingPiles.Take(20).Select(n => $"#{n}"));
                if (referencingPiles.Count > 20) list += $" ほか {referencingPiles.Count - 20} 件";
                MessageService.Show(
                    $"地盤番号 {GroundNo} は杭配置 {list} が参照中のため削除できません。\n" +
                    $"先に杭配置側で地盤番号を別の値に変更してから削除してください。",
                    "削除不可",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // より大きい地盤番号を参照している PileLayoutItem は、削除後にインデックスが
            // 1 つずれて意味が変わる (旧 #3 → 新 #2)。アラートで通知し、自動リナンバリングする
            // か削除中止かをユーザーに選択させる。
            var shiftingPiles = _mainWindowViewModel?.CurrentInputModel?.PileLayoutItems?
                .Where(p => p != null && p.GroundNo > GroundNo)
                .Select(p => p.PileNo)
                .OrderBy(no => no)
                .ToList();
            if (shiftingPiles != null && shiftingPiles.Count > 0)
            {
                string list = string.Join(", ", shiftingPiles.Take(20).Select(n => $"#{n}"));
                if (shiftingPiles.Count > 20) list += $" ほか {shiftingPiles.Count - 20} 件";
                var shiftResult = MessageService.Show(
                    $"地盤番号 {GroundNo} を削除すると、より大きい地盤番号を参照している杭配置 {list} の番号が 1 つずれます。\n\n" +
                    $"・OK: 該当する杭配置の地盤番号を自動的に 1 つ下げて削除します\n" +
                    $"・キャンセル: 削除を中止します",
                    "番号シフトの確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (shiftResult != MessageBoxResult.OK) return;
            }

            // 確認メッセージ
            var result = MessageService.Show(
                $"地盤番号 {GroundNo} を削除しますか？\n元に戻せません。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 変更前の状態を保存
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                // 後続の GroundNo を持つ PileLayoutItem を 1 つ下げる (リナンバリング)
                var liveItems = _mainWindowViewModel?.CurrentInputModel?.PileLayoutItems;
                if (liveItems != null)
                {
                    foreach (var p in liveItems)
                    {
                        if (p != null && p.GroundNo > GroundNo)
                            p.GroundNo -= 1;
                    }
                }

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
            // コンテキスト（Window, DataContext）が準備された段階で呼ばれる想定なのでここで初期項目を用意
            InitializeExampleItems();

            Update();
        }

        // 階段状データの作成メソッド
        internal static (List<double>, List<double>) GetSteppedData(List<double> originalX, List<double> originalY)
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
        internal static List<CoordinateRect> GetRectangleGeometry(List<double> originalX, List<double> originalY)
        {
            List<CoordinateRect> coordinateRects = [];
            // GetSteppedData と同じガードを置く。同じ 2 本のリストを続けて渡す
            // 使い方なので、片方だけ空や null を許すと折れ線は描けて矩形で落ちる。
            if (originalX != null && originalY != null && originalX.Count > 0 && originalY.Count > 0)
            {
                for (int i = 0; i < originalX.Count && i < originalY.Count; i++)
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

        // 太田・後藤式
        [RelayCommand]
        private void OnCalculateOtaVs()
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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

        // 全土層削除メソッド
        [RelayCommand]
        private void OnDeleteAllGroundLayers()
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());
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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            var target = LastFocusedGroundMass ?? SelectedGroundMassOnDataGrid;
            int selectedIndex = target != null ? masses.IndexOf(target) : -1;
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

        // 全土質点削除メソッド
        [RelayCommand]
        private void OnDeleteAllGroundMasses()
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            GroundInput.GroundMassesData.Clear();
            UpdateGroundMassDataLayer();
            Update();
        }

        // 選択行より下の行の土質点の間隔を1mに揃えるメソッド
        [RelayCommand]
        private void OnMake1mSpacing()
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            var masses = GroundInput?.GroundMassesData;
            if (masses == null || masses.Count == 0) return;

            // DataGridから直接現在行を取得（複数の方法でフォールバック）
            GroundMassDataInput? target = null;
            var grid = GroundWindowInstance?.DataGridGroundMass;
            if (grid != null)
            {
                // CurrentCell.Itemが最も確実（ボタンクリック後もクリアされない）
                try
                {
                    if (grid.CurrentCell.Item is GroundMassDataInput cellItem)
                        target = cellItem;
                }
                catch { /* CurrentCellが無効な場合 */ }

                // CurrentItemで試行
                target ??= grid.CurrentItem as GroundMassDataInput;
            }
            target ??= LastFocusedGroundMass ?? SelectedGroundMassOnDataGrid;

            int selectedIndex = target != null ? masses.IndexOf(target) : -1;
            int startIndex = (selectedIndex >= 0) ? selectedIndex + 1 : 1;

            if (startIndex >= masses.Count) return;

            // 編集中のセルを先にコミット
            if (grid != null)
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }

            // 1m ピッチで GLDepth を再配置し、Spacing と H も1mに設定
            for (int i = startIndex; i < masses.Count; i++)
            {
                // 工学的基盤に到達したら以降は触らない
                if (masses[i].IsEngineeringBedrock) break;

                masses[i].GLDepth = masses[i - 1].GLDepth - 1.0;
                masses[i].Spacing = 1.0;
                masses[i].H = 1.0;
            }

            // startIndexより前のSpacingも再計算（GLDepth起点の整合性のため）
            for (int i = 0; i < startIndex && i < masses.Count; i++)
            {
                if (i == 0)
                    masses[i].Spacing = -masses[i].GLDepth;
                else
                    masses[i].Spacing = -masses[i].GLDepth + masses[i - 1].GLDepth;
            }

            // 以降の派生値（Altitude など）を再計算・描画
            UpdateGroundMassDataLayer();
            Update();

            // エラーチェック（上行より小さいか）を実行してフラグを更新
            bool hasError = ValidateGroundMassMonotone(out string errorMessage);

            // DataGrid を強制更新
            SafeRefreshDataGrid(GroundWindowInstance?.DataGridGroundMass);

            // 必要に応じてメッセージ表示
            if (hasError)
            {
                MessageService.Show(errorMessage, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

        /// <summary>
        /// DataGridの編集トランザクションを確定してからRefreshする安全なヘルパー
        /// </summary>
        private static void SafeRefreshDataGrid(DataGrid? grid)
        {
            if (grid == null) return;
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            grid.Items.Refresh();
        }

        // エラーチェック再実行＋グリッド強制更新
        private void RevalidateAndRefreshGroundMassGrid()
        {
            // 必要なら GroundInput 側の整合検証を併用（エラーフラグ更新が内包されている前提）
            _ = GroundInput?.ValidateForAnalysis(out _);

            var grid = GroundWindowInstance?.DataGridGroundMass;
            if (grid != null)
            {
                // 編集中のトランザクションを先に確定してからRefresh
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }

            var view = CollectionViewSource.GetDefaultView(GroundInput?.GroundMassesData);
            view?.Refresh();

            if (grid == null) return;
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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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

        // 土質点データの N 値に、その点が属する土層の N 値を代入する (層 → 点)
        [RelayCommand]
        private void OnApplyGroundLayerNValue()
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
                {
                    double topAltitude = groundLayerDataItem.BottomAltitude + groundLayerDataItem.LayerThickness;
                    if (topAltitude > groundMassData.AltitudeDepth &&
                        groundMassData.AltitudeDepth >= groundLayerDataItem.BottomAltitude)
                    {
                        groundMassData.NValue = groundLayerDataItem.NValue;
                        break; // 最初に該当した土層を採用
                    }
                }
            }
            Update(); // グラフを更新
        }

        // 各土質点の「下端深度GL」を、当該行と次行の NPT 深度 (GLDepth、負方向=深さ) の平均深度に設定する。
        // 最下行は土層入力の最下層の BottomGLDepth (正の深さ) を採用する。
        // 内部的には新しい下端深度差から H を計算し、Update() で連動を更新する。
        [RelayCommand]
        private void OnApplyNptDepthToLayerBottomDepth()
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            var masses = GroundInput?.GroundMassesData;
            var layers = GroundInput?.GroundLayers;
            if (masses == null || masses.Count == 0) return;
            if (layers == null || layers.Count == 0) return;

            int n = masses.Count;
            var newDepths = new double[n];

            // i 行と i+1 行の NPT 深度の平均 (標高ベース=負方向のまま平均)
            for (int i = 0; i < n - 1; i++)
            {
                newDepths[i] = (masses[i].GLDepth + masses[i + 1].GLDepth) / 2.0;
            }
            // 最下行 = 最下層 BottomGLDepth を負方向に直して採用
            newDepths[n - 1] = -layers[layers.Count - 1].BottomGLDepth;

            // 連続する下端深度差から H を導出して反映 (標高ベース: H = prev − current)
            // H が 0 / 負になっても下端深度GL は上書きし、整合性は順序エラー (赤セル) で示す。
            // (打ち切ると以降の行の変換が前行の影響で連鎖的にキャンセルされてしまうため)
            double prev = 0.0;
            for (int i = 0; i < n; i++)
            {
                masses[i].H = prev - newDepths[i];
                masses[i].LayerBottomDepth = newDepths[i];
                prev = newDepths[i];
            }

            Update(); // RecalculateH で LayerBottomZ / LayerBottomDepth を確定し、グラフ更新
            ValidateGroundMassDepthOrders(); // 各セルと上下行の順序関係をチェックして赤セル表示を更新
        }

        // 土質点の参考NPT深度GL (GLDepth) と 下端深度GL (LayerBottomDepth) について、
        // 各行が上下隣接行と比べて単調減少（標高ベースで下方=より負）になっているかをチェックし、
        // 列別エラーフラグ (IsErrorGLDepth / IsErrorLayerBottomDepth) を設定する。
        public void ValidateGroundMassDepthOrders()
        {
            var masses = GroundInput?.GroundMassesData;
            if (masses == null) return;
            int count = masses.Count;
            for (int i = 0; i < count; i++)
            {
                var m = masses[i];
                m.IsErrorGLDepth = HasOrderError(masses, i, x => x.GLDepth);
                m.IsErrorLayerBottomDepth = HasOrderError(masses, i, x => x.LayerBottomDepth);
                m.IsError = m.IsErrorGLDepth || m.IsErrorLayerBottomDepth;
            }
        }

        private static bool HasOrderError(
            System.Collections.Generic.IList<GroundMassDataInput> masses, int i,
            System.Func<GroundMassDataInput, double?> getValue)
        {
            double? v = getValue(masses[i]);
            if (!v.HasValue || double.IsNaN(v.Value)) return false;
            double value = v.Value;
            int count = masses.Count;

            if (i == 0 && value >= 0) return true;
            if (i > 0)
            {
                double? above = getValue(masses[i - 1]);
                if (above.HasValue && !double.IsNaN(above.Value) && above.Value <= value) return true;
            }
            if (i < count - 1)
            {
                double? below = getValue(masses[i + 1]);
                if (below.HasValue && !double.IsNaN(below.Value) && value <= below.Value) return true;
            }
            return false;
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
                    MessageBoxResult result = MessageService.Show(warningMessage, "警告", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Cancel) return;
                }

                // 任意地盤変位のバリデーション
                for (int i = 0; i < GroundsInput.Count; i++)
                {
                    var gi = GroundsInput[i];
                    if (gi.CustomDisplacementProfile?.IsEnabled == true)
                    {
                        // GroundInputを一時的にセットしてバリデーション
                        var prevGI = GroundInput;
                        GroundInput = gi;
                        ValidateCustomDisplacementProfiles();
                        GroundInput = prevGI;

                        if (HasCustomDispWarnings)
                        {
                            MessageBoxResult dispResult = MessageService.Show(
                                $"地盤番号{i + 1}の任意地盤変位に警告があります:\n\n{CustomDispWarnings}\n\n状態を保存してウィンドウを閉じますか？",
                                "任意地盤変位 警告", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                            if (dispResult == MessageBoxResult.Cancel) return;
                        }
                    }
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
            // ViewModel は自前の GroundsInput (DeepCopy) を編集対象としており、
            // 編集中に InputModel.GroundsInput は変更されない。
            // よってキャンセル時に InputModel.GroundsInput を Clear+Add する必要はない。
            // (旧実装は CollectionChanged を発火させて SoilPiles 再生成 → IsElementSplit=false の
            //  リセットを引き起こしていた)
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 全再計算＋グラフ再描画を行う。
        /// 再入防止ガード付き：実行中に呼ばれた場合は完了後に1回だけ再実行する。
        /// </summary>
        public void Update()
        {
            if (_isUpdating)
            {
                // 再入：現在の実行完了後にもう1回実行する
                _updatePending = true;
                return;
            }

            _isUpdating = true;
            try
            {
                UpdateCore();
            }
            finally
            {
                _isUpdating = false;
            }

            // 再入リクエストがあった場合は1回だけ再実行
            if (_updatePending)
            {
                _updatePending = false;
                UpdateCore();
            }

            // 任意地盤変位バリデーション更新（孔口標高・土質点変更時にも反映）
            ValidateCustomDisplacementProfiles();
        }

        /// <summary>
        /// デバウンス付きUpdate。短時間の連続呼び出しをバッチ化する。
        /// DataGrid編集やPropertyChanged連鎖からはこちらを呼ぶ。
        /// </summary>
        public void ScheduleUpdate()
        {
            _updateDebounceTimer?.Stop();
            _updateDebounceTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _updateDebounceTimer.Tick += (s, e) =>
            {
                _updateDebounceTimer?.Stop();
                _updateDebounceTimer = null;
                Update();
            };
            _updateDebounceTimer.Start();
        }

        /// <summary>
        /// 再計算の実体。グラフ描画はDispatcherで遅延実行する。
        /// </summary>
        private void UpdateCore()
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
                RecalculatePL();
                RecalculateBetaL();
                RecalculateGammaCy();
                RecalculateSigmaGammaCyH();
                RecalculateMass();
                RecalculateVSE();
            }

            // グラフ描画はUIスレッドで遅延実行（計算とバッチ化）
            GroundWindowInstance?.Dispatcher?.InvokeAsync(() =>
            {
                DrawNValueGraph();
                DrawCuGraph();
                DrawVsGraph();
                DrawEsGraph();
                DrawGroundDisplacementGraph();
                DrawFLGraph();
                DrawResponseSpectrumGraph();
            }, System.Windows.Threading.DispatcherPriority.Background);
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

            // H の設定：ユーザー入力 (JSON 含む) を最優先で保持。
            // 未入力 (null) の場合のみ間隔 (Spacing) をデフォルト値として設定。
            // 工学的基盤質点も応答スペクトル法の固有値解析等から除外されるが、表示用に
            // H は保持する (旧実装は強制 null にしていたが、ユーザー入力を尊重する方向に変更)。
            for (int i = 0; i < count; i++)
            {
                var current = groundMassesData[i];
                if (!current.H.HasValue)
                    current.H = current.Spacing; // 未入力時のデフォルト：間隔
            }

            // 層下面Z = GroundTopAltitude から H を上から累積して算出
            // 下端深度GL (LayerBottomDepth) = LayerBottomZ − GroundTopAltitude (標高ベース、下方=負)
            double bottomZ = GroundInput.GroundTopAltitude;
            for (int i = 0; i < count; i++)
            {
                var current = groundMassesData[i];
                if (current.H.HasValue)
                {
                    bottomZ -= current.H.Value;
                    current.LayerBottomZ = bottomZ;
                    current.LayerBottomDepth = bottomZ - GroundInput.GroundTopAltitude;
                }
                else
                {
                    current.LayerBottomZ = null;
                    current.LayerBottomDepth = null;
                }
            }
        }

        // 密度、工学的基盤の再計算メソッド
        // 土質点の担当範囲 (上隣の質点 GL ～ 自身の GL) と各土層の範囲との重複長を比較し、
        // 最も多く含まれる土層のパラメータを採用する。範囲が縮退/異常データの場合や
        // どの層とも重ならない場合は GLDepth 単点判定にフォールバック。
        internal void RecalculateDensityIsEngineeringBedrock()
        {
            var masses = GroundInput.GroundMassesData;
            var layers = GroundInput.GroundLayers;
            if (layers.Count == 0) return;

            for (int i = 0; i < masses.Count; i++)
            {
                var m = masses[i];
                // 土質点 i の担当範囲 (GL): 上端 = i-1 質点の GLDepth (i=0 のとき GL=0), 下端 = 自身の GLDepth
                double massTop = (i == 0) ? 0.0 : masses[i - 1].GLDepth;
                double massBot = m.GLDepth;

                if (massTop <= massBot)
                {
                    AssignLayerByPoint(m, layers);
                    continue;
                }

                int bestIdx = -1;
                double bestOverlap = 0.0;
                for (int j = 0; j < layers.Count; j++)
                {
                    double layerTop = (j == 0) ? 0.0 : layers[j - 1].BottomGLDepth;
                    double layerBot = layers[j].BottomGLDepth;
                    double overlap = Math.Min(massTop, layerTop) - Math.Max(massBot, layerBot);
                    if (overlap > bestOverlap)
                    {
                        bestOverlap = overlap;
                        bestIdx = j;
                    }
                }

                if (bestIdx >= 0)
                {
                    var l = layers[bestIdx];
                    m.Density = l.Density;
                    m.GranularityClass = l.GranularityClass;
                    m.AgeCategory = l.AgeCategory;
                    m.IsEngineeringBedrock = l.IsEngineeringBedrock;
                }
                else
                {
                    AssignLayerByPoint(m, layers);
                }
            }
        }

        private static void AssignLayerByPoint(GroundMassDataInput m, ObservableCollection<GroundLayerInput> layers)
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


        // τL/σz'
        internal void RecalculateTauLonSigmaZPrime()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                if (groundMassData.IsLiquefactionLayer)
                {
                    double _NL = groundMassData.NL.GetValueOrDefault();
                    groundMassData.TauLonSigmaZPrime = LiquefactionResistance(_NL);
                }
                else
                {
                    groundMassData.TauLonSigmaZPrime = null;
                }
            }
        }

        /// <summary>
        /// 液状化抵抗比 τL/σz′ (補正 N 値 <paramref name="na"/>)。基礎指針'19 は図3.2.1 のせん断ひずみ振幅
        /// 5% の線から読み取るとしており、式は示していない。その線を閉じた形
        /// a·Cr·{16√Na/100 + (16√Na/Cs)^n} (a = 0.45, Cr = 0.57, Cs = 80, n = 14) で表す。
        /// 16√Na/Cs = 1 となる Na = 25 で立ち上がり、5% の線に当たる。
        /// 1.0.32-beta までは近似式 0.0410·{√Na + 0.00903·(Na/10)^7} (Cs ≈ 80.7 相当。Na = 28 で 8.6% 小さい)
        /// を使っていた。2026-09-12 に利用者と図を見比べて閉じた形に切り替えた (LiquefactionResistanceTests)。
        /// </summary>
        internal static double LiquefactionResistance(double na)
        {
            const double a = 0.45, cr = 0.57, cs = 80.0, n = 14.0;
            double s = 16.0 * Math.Sqrt(na);
            return a * cr * (s / 100.0 + Math.Pow(s / cs, n));
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

        // 液状化指標 PL (岩崎ら 1982): PL = Σ (1-FL)·W(z)·H, F=max(0,1-FL), W=max(0,10-0.5z), 深さ z∈[0, 20m]
        internal void RecalculatePL()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                GroundInput.PL[levelIndex] = ComputeIwasakiPL(GroundInput.GroundMassesData, levelIndex);
            }
        }

        /// <summary>
        /// 岩崎ら (1982) の液状化指標 PL を 1 レベル分計算する純粋関数。
        /// テスト容易性とロジック分離のため static で公開。
        /// </summary>
        internal static double ComputeIwasakiPL(System.Collections.Generic.IEnumerable<GroundMassDataInput> masses, int levelIndex)
        {
            double pl = 0.0;
            foreach (GroundMassDataInput groundMassData in masses)
            {
                if (!groundMassData.IsLiquefactionLayer) continue;
                if (groundMassData.FL == null || groundMassData.FL.Count <= levelIndex) continue;
                if (groundMassData.FL[levelIndex] == null) continue;

                double z = -groundMassData.GLDepth; // 深さ (m, 正値)
                if (z < 0 || z > 20.0) continue;

                double fl = groundMassData.FL[levelIndex].GetValueOrDefault();
                double f = Math.Max(0.0, 1.0 - fl);
                if (f <= 0) continue;

                double w = Math.Max(0.0, 10.0 - 0.5 * z);
                double h = groundMassData.H.GetValueOrDefault();
                pl += f * w * h;
            }
            return pl;
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
                        // 基礎指針 2019: FL ≥ 1.0 (液状化に至らない) のときは γcy = 0
                        // テーブル (Na, τd/σz') からの読取りは「液状化発生時 (FL<1)」の規定。
                        double? fl = groundMassData.FL != null && groundMassData.FL.Count > levelIndex
                            ? groundMassData.FL[levelIndex]
                            : null;
                        if (fl.HasValue && fl.Value >= 1.0)
                        {
                            groundMassData.GammaCy[levelIndex] = 0.0;
                        }
                        else
                        {
                            groundMassData.GammaCy[levelIndex]
                                = Liquefaction.CalculateGammaCy(
                                    groundMassData.NL.GetValueOrDefault(), groundMassData.TauDonSigmaZPrime[levelIndex].GetValueOrDefault());
                        }
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
                        // 基礎指針 2019: FL ≥ 1.0 (液状化に至らない) のときは βL = 1.0 (低減なし)
                        // テーブル (Na, z) からの読取りは「液状化発生時 (FL<1)」の規定。
                        double? fl = groundMassData.FL != null && groundMassData.FL.Count > levelIndex
                            ? groundMassData.FL[levelIndex]
                            : null;
                        if (fl.HasValue && fl.Value >= 1.0)
                        {
                            groundMassData.BetaL[levelIndex] = 1.0;
                        }
                        else
                        {
                            groundMassData.BetaL[levelIndex] = Liquefaction.CalculateBetaL(groundMassData.GLDepth, groundMassData.NL.GetValueOrDefault());
                        }
                    }
                    else
                    {
                        groundMassData.BetaL[levelIndex] = null;
                    }
                }
            }
        }

        /// <summary>
        /// 土質点の代表土層 (LayerNo / Name) を再計算する。
        /// 土質点は深さ区間 [GLDepth - H, GLDepth] を占め、各土層 i は
        /// 深さ区間 [layers[i-1].BottomGLDepth, layers[i].BottomGLDepth] (i=0 は 0 起点)。
        /// 区間の重なり長 (overlap) が最大の土層を代表とし、同値で複数候補があるときは
        /// 最も深い (リスト後方の) 土層を選ぶ。
        /// H が null / 0 のときや、点が全層と重ならないときは、
        /// 単一深さ GLDepth を含む土層を代表とし、境界上で複数候補があれば深い方を選ぶ。
        /// </summary>
        internal void RecalculateName()
        {
            var masses = GroundInput?.GroundMassesData;
            var layers = GroundInput?.GroundLayers;
            if (masses == null) return;
            if (layers == null || layers.Count == 0)
            {
                foreach (var m in masses) { m.LayerNo = null; m.Name = ""; }
                return;
            }

            // GLDepth / BottomGLDepth は標高ベース (地表 = 0、下方ほど負) の値。
            // 土質点の区間: [GLDepth, GLDepth + H] = [下端 (より負), 上端 (より 0 寄り)]
            // 土層 i の区間: [layers[i].BottomGLDepth, (i==0 ? 0 : layers[i-1].BottomGLDepth)]
            //                = [下端 (より負), 上端 (より 0 寄り)]
            foreach (var m in masses)
            {
                double h = m.H ?? 0.0;
                double pointLow = m.GLDepth;        // 深い側 (より負)
                double pointHigh = m.GLDepth + h;   // 浅い側 (より 0 寄り)

                // 1) 重なり長が最大の土層を選ぶ (同値なら後方=深い側を優先)
                int? bestIdx = null;
                double bestOverlap = 0.0;
                for (int i = 0; i < layers.Count; i++)
                {
                    double layerHigh = (i == 0) ? 0.0 : layers[i - 1].BottomGLDepth; // 浅い側
                    double layerLow = layers[i].BottomGLDepth;                       // 深い側
                    double overlap = Math.Max(0.0,
                        Math.Min(pointHigh, layerHigh) - Math.Max(pointLow, layerLow));
                    if (overlap > bestOverlap || (bestIdx.HasValue && overlap == bestOverlap))
                    {
                        bestOverlap = overlap;
                        bestIdx = i; // リスト後方ほど深いので、同値時はここで上書きされ深い側が残る
                    }
                }

                if (bestIdx.HasValue && bestOverlap > 0)
                {
                    m.LayerNo = bestIdx.Value + 1;
                    m.Name = layers[bestIdx.Value].Name;
                    continue;
                }

                // 2) 重なり 0 のフォールバック: 単一深さ GLDepth を含む土層 (境界上は深い方を採用)
                int? containingIdx = null;
                for (int i = 0; i < layers.Count; i++)
                {
                    double layerHigh = (i == 0) ? 0.0 : layers[i - 1].BottomGLDepth;
                    double layerLow = layers[i].BottomGLDepth;
                    if (m.GLDepth >= layerLow && m.GLDepth <= layerHigh)
                    {
                        containingIdx = i; // 後勝ち→深い側を採用
                    }
                }

                if (containingIdx.HasValue)
                {
                    m.LayerNo = containingIdx.Value + 1;
                    m.Name = layers[containingIdx.Value].Name;
                }
                else
                {
                    m.LayerNo = null;
                    m.Name = "";
                }
            }
        }


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

        // M
        /// <summary>
        /// 質点 <paramref name="i"/> が受け持つ深さの範囲 (GL 基準、下向き負)。質点 i は層の上端
        /// (上の層厚 H の和) にあり、上下の層の半分ずつを受け持つ (GroundMassRegionTests)。
        /// </summary>
        internal static (double Top, double Bottom) MassRegion(IReadOnlyList<double> h, int i)
        {
            if (i == 0) return (0.0, -0.5 * h[0]);

            // 上端 = 質点 i の深さ −(H0 + … + H_{i-1}) から H_{i-1} の半分だけ上。
            // 以前はこの和を j < i-2 で打ち切っていて H_{i-2} が抜け、3 番目以降の質点の
            // 範囲が 1 小層ぶん上にずれていた (2026-09-11 に修正)
            double top = -0.5 * h[i - 1];
            for (int j = 0; j < i - 1; j++)
            {
                top -= h[j];
            }
            return (top, top - 0.5 * h[i - 1] - 0.5 * h[i]);
        }

        internal void RecalculateMass()
        {
            double zi1;
            double zi2;
            double zj1;
            double zj2;
            var h = GroundInput.GroundMassesData.Select(m => m.H.GetValueOrDefault()).ToList();

            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                GroundInput.GroundMassesData[i].Mass = 0.0;

                (zi1, zi2) = MassRegion(h, i);
                    //zi1 = (GroundInput.GroundMassesData[i - 1].GLDepth + GroundInput.GroundMassesData[i].GLDepth) / 2.0;


                //if (i != GroundInput.GroundMassesData.Count - 1)
                //    zi2 = (GroundInput.GroundMassesData[i].GLDepth + GroundInput.GroundMassesData[i + 1].GLDepth) / 2.0;
                //else
                //    zi2 = GroundInput.GroundMassesData[i].GLDepth;

                for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
                {
                    zj1 = GroundInput.GroundLayers[j].BottomGLDepth + GroundInput.GroundLayers[j].LayerThickness;
                    zj2 = GroundInput.GroundLayers[j].BottomGLDepth;

                    GroundInput.GroundMassesData[i].Mass += Math.Max(Math.Min(zi1, zj1) - Math.Max(zi2, zj2), 0)
                        * GroundInput.GroundLayers[j].Density / 9.806665;
                }

            }
        }


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

                // Gs1/Gs2/Impedance/T2/XiE/Beta は応答スペクトル法のみで使用 — 他算定法では 0 にクリア
                GroundInput.Gs1Levels[levelIndex] = 0.0;
                GroundInput.Gs2Levels[levelIndex] = 0.0;
                GroundInput.ImpedanceLevels[levelIndex] = 0.0;
                GroundInput.T2Levels[levelIndex] = 0.0;
                GroundInput.XiELevels[levelIndex] = 0.0;
                GroundInput.BetaLevels[levelIndex] = 0.0;

                // 応答スペクトル法 (MDOF + 等価線形化反復)
                if (calculationMethod == "応答スペクトル法")
                {
                    var rs = PileDesign.Services.GroundResponseSpectrumCalc.Compute(
                        groundMassesData, bedrockDensity, bedrockShearWaveVelocity,
                        shallowSoilType, L, Z);
                    if (!double.IsNaN(rs.T1))
                    {
                        // T0 (初期周期) は UI 互換のため別途計算
                        double T0Init = 0.0;
                        double SigmaHInit = 0.0;
                        foreach (var gmd in groundMassesData)
                        {
                            if (gmd.IsEngineeringBedrock) break;
                            T0Init += 4.0 * gmd.H.GetValueOrDefault() / gmd.VS0;
                            SigmaHInit += gmd.H.GetValueOrDefault();
                        }
                        GroundInput.NaturalPeriod = T0Init;
                        GroundInput.NaturalPeriods[levelIndex] = rs.T1;
                        GroundInput.Gs1Levels[levelIndex] = rs.Gs1;
                        GroundInput.Gs2Levels[levelIndex] = rs.Gs2;
                        GroundInput.ImpedanceLevels[levelIndex] = rs.Impedance;
                        GroundInput.T2Levels[levelIndex] = rs.T2;
                        GroundInput.XiELevels[levelIndex] = rs.XiE;
                        GroundInput.BetaLevels[levelIndex] = rs.Beta;

                        // フィールド書き戻し
                        for (int i = 0; i < groundMassesData.Count; i++)
                        {
                            if (i < rs.G.Length)
                            {
                                var gmd = groundMassesData[i];
                                double rho = gmd.Density;
                                double h = gmd.H.GetValueOrDefault();
                                double vse = Math.Sqrt(rs.G[i] * 9.80665 / rho);
                                gmd.VSE[levelIndex] = vse;
                                gmd.K[levelIndex] = (h > 0) ? rho / 9.80665 * vse * vse / h : 0.0;
                                gmd.U[levelIndex] = rs.PhiU0_1[i];
                                gmd.UStar[levelIndex] = rs.PhiU0_1[i]; // U[0]=1, U_bedrock=0 → UStar=U
                                gmd.DmaxUStar[levelIndex] = rs.DispMm[i];
                                gmd.DmaxUStarSigmaGammaCyH[levelIndex] = gmd.DmaxUStar[levelIndex] + gmd.SigmaGammaCyH[levelIndex];
                            }
                            else
                            {
                                groundMassesData[i].U[levelIndex] = 0.0;
                                groundMassesData[i].UStar[levelIndex] = 0.0;
                                groundMassesData[i].DmaxUStar[levelIndex] = 0.0;
                                groundMassesData[i].DmaxUStarSigmaGammaCyH[levelIndex] = groundMassesData[i].SigmaGammaCyH[levelIndex];
                            }
                        }
                        continue; // 次レベルへ
                    }
                    Serilog.Log.Debug($"[ResponseSpectrum] Level {levelIndex + 1} 計算失敗。a2(b2) にフォールバック。");
                }

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
                    groundMassData.K[levelIndex] = density / 9.80665 * Math.Pow(groundMassData.VSE[levelIndex], 2.0) / groundMassData.H.GetValueOrDefault();

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
                // C2: 表層の土質の減衰特性から決まる定数 (文献値: 粘性土 0.53、砂質土 0.66)。
                // 砂質土は 0.666 になっていたのを 2026-09-11 に文献値へ直した (GroundDisplacementConstantTests)
                double C2 = (shallowSoilType == "粘性土") ? 0.53 : 0.66;
                double Dmax = 0;

                if (calculationMethod == "a1(b1)")
                {
                    Dmax = C1 * (Math.Pow(alpha, 2.0) - 1.0) * fA * SigmaH * (C2 * (1 - 1 / Math.Pow(alpha, 2.0)) + 2.0 * Rz0 / alpha);
                }
                else if (calculationMethod == "a2(b2)" || calculationMethod == "応答スペクトル法")
                {
                    // 応答スペクトル法の計算が失敗した場合は a2(b2) と同じ式でフォールバック
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
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            Update();
        }

        private void DataGridGroundMass_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.EditingElement is not TextBox editedTextBox) return;
            if (!double.TryParse(editedTextBox.Text, out double doubleValue)) return;
            if (e.Column is not DataGridBoundColumn boundColumn || boundColumn.Binding is not Binding binding) return;

            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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

        private bool _isUpdatingValues = true;

        public void TextBoxGroundWaterTableAltitude_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // 変更前の状態を保存
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                _isUpdatingValues = false;
                GroundInput.GroundWaterTableAltitude = GroundInput.GroundWaterGLDepth + GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;
                Update();
            }
        }

        public void DataGridGroundLayer_RowEditEnding(/*string newText*/)
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            Update();
        }

        public void GroundTopAltitudeTextBox_LostFocus()
        {
            // 変更前の状態を保存
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

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
            ScheduleUpdate(); // デバウンス付きで再計算・グラフ更新
        }

        // ======== 任意地盤変位プロファイル ========

        // 計算値モード（IsEnabled の反転）
        public bool IsCustomDisplacementDisabled
        {
            get => !(GroundInput?.CustomDisplacementProfile?.IsEnabled ?? false);
            set
            {
                if (GroundInput?.CustomDisplacementProfile != null)
                {
                    GroundInput.CustomDisplacementProfile.IsEnabled = !value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedCustomDispProfile));
                    ScheduleUpdate(); // グラフ更新
                    ValidateCustomDisplacementProfiles();

                    // 任意入力モードON時に「任意地盤変位」タブを前面に表示
                    if (!value)
                        ActivateCustomDispTab?.Invoke();
                    // 任意入力モード時のみタブ表示
                    SetCustomDispTabVisibility?.Invoke(!value);
                }
            }
        }

        /// <summary>
        /// 「基礎指針'19 4.5自動計算」ラジオボタン用プロパティ。
        /// 3 つの地盤変位モード (自動計算 / 任意入力 / 考慮しない) を排他制御するため、
        /// 「任意入力 OFF かつ 考慮しない OFF」の場合に自動計算モードとなる。
        /// </summary>
        public bool IsAutoDisplacementMode
        {
            get => !(GroundInput?.CustomDisplacementProfile?.IsEnabled ?? false)
                && !(GroundInput?.IsGroundDisplacementIgnored ?? false);
            set
            {
                if (GroundInput == null) return;
                if (value)
                {
                    // 自動計算を選択 → 他の 2 モードを OFF
                    if (GroundInput.CustomDisplacementProfile != null)
                        GroundInput.CustomDisplacementProfile.IsEnabled = false;
                    GroundInput.IsGroundDisplacementIgnored = false;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomDisplacementDisabled));
                OnPropertyChanged(nameof(IsGroundDisplacementIgnoredProxy));
                ScheduleUpdate();
                // 自動計算モードでは任意地盤変位タブを非表示
                SetCustomDispTabVisibility?.Invoke(false);
            }
        }

        /// <summary>
        /// 「考慮しない」ラジオボタンの ON/OFF。
        /// ON 時は他のモードを OFF にし、グラフ描画と水平解析で地盤変位を 0 として扱う。
        /// (XAML からは直接 GroundInput.IsGroundDisplacementIgnored を Two-way 連動するが、
        ///  排他制御の副作用を起こすための proxy として用意。)
        /// </summary>
        public bool IsGroundDisplacementIgnoredProxy
        {
            get => GroundInput?.IsGroundDisplacementIgnored ?? false;
            set
            {
                if (GroundInput == null) return;
                GroundInput.IsGroundDisplacementIgnored = value;
                if (value)
                {
                    if (GroundInput.CustomDisplacementProfile != null)
                        GroundInput.CustomDisplacementProfile.IsEnabled = false;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomDisplacementDisabled));
                OnPropertyChanged(nameof(IsAutoDisplacementMode));
                ScheduleUpdate();
                // 考慮しないモードでは任意地盤変位タブを非表示
                SetCustomDispTabVisibility?.Invoke(false);
            }
        }

        /// <summary>任意地盤変位タブの表示/非表示を切り替えるコールバック (Window から設定)。
        /// 任意入力モードのときのみタブを表示する。</summary>
        public Action<bool> SetCustomDispTabVisibility { get; set; }

        /// <summary>任意地盤変位タブを前面に表示するコールバック（Windowから設定）</summary>
        public Action ActivateCustomDispTab { get; set; }

        // ケース選択肢
        public ObservableCollection<string> CustomDispCaseOptions { get; } =
        [
            "レベル1 液状化なし",
            "レベル1 液状化あり",
            "レベル2 液状化なし",
            "レベル2 液状化あり"
        ];

        private int _selectedCustomDispCaseIndex = 0;
        public int SelectedCustomDispCaseIndex
        {
            get => _selectedCustomDispCaseIndex;
            set
            {
                if (SetProperty(ref _selectedCustomDispCaseIndex, value))
                    OnPropertyChanged(nameof(SelectedCustomDispProfile));
            }
        }

        // 選択中のプロファイル
        public ObservableCollection<DisplacementPoint>? SelectedCustomDispProfile =>
            GroundInput?.CustomDisplacementProfile?.GetProfile(_selectedCustomDispCaseIndex);

        // ======== 任意地盤変位 バリデーション ========

        private string _customDispWarnings = "";
        public string CustomDispWarnings
        {
            get => _customDispWarnings;
            set => SetProperty(ref _customDispWarnings, value);
        }

        public bool HasCustomDispWarnings => !string.IsNullOrEmpty(CustomDispWarnings);

        /// <summary>
        /// 4ケースすべてについてバリデーションを実行し、警告メッセージを更新する
        /// </summary>
        public void ValidateCustomDisplacementProfiles()
        {
            if (GroundInput?.CustomDisplacementProfile == null || !GroundInput.CustomDisplacementProfile.IsEnabled)
            {
                CustomDispWarnings = "";
                OnPropertyChanged(nameof(HasCustomDispWarnings));
                return;
            }

            var warnings = new List<string>();
            double boreholeAlt = GroundInput.GroundTopAltitude;

            // 土質点の最下面標高
            double bottomAlt = double.MaxValue;
            if (GroundInput.GroundMassesData != null && GroundInput.GroundMassesData.Count > 0)
                bottomAlt = GroundInput.GroundMassesData.Min(m => m.AltitudeDepth);

            var caseNames = new[] { "L1 非液状化", "L1 液状化", "L2 非液状化", "L2 液状化" };
            var custom = GroundInput.CustomDisplacementProfile;

            for (int i = 0; i < 4; i++)
            {
                var profile = custom.GetProfile(i);
                if (profile == null || profile.Count == 0)
                {
                    warnings.Add($"[{caseNames[i]}] データが入力されていません。");
                    continue;
                }

                double maxZ = profile.Max(p => p.Z);
                double minZ = profile.Min(p => p.Z);

                if (Math.Abs(maxZ - boreholeAlt) > 0.001)
                    warnings.Add($"[{caseNames[i]}] 最高標高({maxZ:F3}m)が孔口標高({boreholeAlt:F3}m)と異なります。");

                if (bottomAlt < double.MaxValue && minZ > bottomAlt + 0.001)
                    warnings.Add($"[{caseNames[i]}] 最低標高({minZ:F3}m)が土質点最下面標高({bottomAlt:F3}m)より高いです。");
            }

            CustomDispWarnings = string.Join("\n", warnings);
            OnPropertyChanged(nameof(HasCustomDispWarnings));
        }
    }
}
