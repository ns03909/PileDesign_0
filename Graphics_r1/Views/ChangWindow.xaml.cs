using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;


namespace PileDesign.Views
{
    public partial class ChangWindow : Window
    {
        // 編集前の値を一時保存する辞書
        private readonly Dictionary<(object item, string prop), object?> _cellEditOldValues = new();


        private bool _mouseMoveMRegistered = false;
        private bool _mouseMoveQRegistered = false;
        private bool _mouseMoveDRegistered = false;

        // コンストラクタ
        //public ChangWindow()
        //{
        //    InitializeComponent();

        //    Loaded += ChangWindow_Loaded;
        //    DataContextChanged += ChangWindow_DataContextChanged;
        //    //MainTab.SelectionChanged += MainTab_SelectionChanged;

        //}
        public ChangWindow()
        {
            InitializeComponent();
            Loaded += ChangWindow_Loaded;
            DataContextChanged += ChangWindow_DataContextChanged;
        }


        //private void ChangWindow_Loaded(object? sender, RoutedEventArgs e)
        //{
        //    // Loaded 時にも DataContext が既にあるなら登録を行う
        //    if (DataContext is ChangViewModel vm)
        //        InitializePlotsForViewModel(vm);

        //}


        private async void ChangWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            this.Loaded -= ChangWindow_Loaded;

            // UI メッセージ処理を一回はさむ（重い初期化のブロックを避ける）
            await System.Threading.Tasks.Task.Yield();

            try
            {
                InitChartsSafe(); // 既存の初期化処理をここへ移してください（最初は空にして確認）
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chang 初期化中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ここに既存の ScottPlot/Skia 初期化を移動
        private void InitChartsSafe()
        {
            // 例: コメントアウトしてから段階的に戻してください
            //InitializeScottPlot();
            // InitializeOtherControls();
        }


        private void ChangWindow_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ChangViewModel oldVm)
            {
                oldVm.Changs.CollectionChanged -= Changs_CollectionChanged;
            }

            if (e.NewValue is ChangViewModel newVm)
            {
                newVm.ChangWindowInstance = this;
                newVm.Changs.CollectionChanged += Changs_CollectionChanged;
                InitializePlotsForViewModel(newVm);
            }
        }

        private void InitializePlotsForViewModel(ChangViewModel vm)
        {
            // VM を保持させる
            vm.ChangWindowInstance = this;

            // Proactively set WpfPlot.DataContext to the ViewModel (PlotHelper uses DataContext reflection)
            if (wpfPlotM != null) wpfPlotM.DataContext = vm;
            if (wpfPlotQ != null) wpfPlotQ.DataContext = vm;
            if (wpfPlotD != null) wpfPlotD.DataContext = vm;

            // Ensure Crosshair exists and MouseMove registered once per plot
            if (wpfPlotM != null)
            {
                PlotHelper.InitCrosshair(wpfPlotM, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
                if (!_mouseMoveMRegistered)
                {
                    wpfPlotM.MouseMove += (s, ev) => PlotHelper.WpfPlot_MouseMove(s, ev, nameof(ChangViewModel.CrosshairPositionText_M), "M (kNm)", "GL基準深さ(m)", 1, 3);
                    _mouseMoveMRegistered = true;

                    // CSVエクスポートメニューを追加
                    PlotHelper.AddCsvExportMenu(wpfPlotM, "曲げモーメント");
                }
            }

            if (wpfPlotQ != null)
            {
                PlotHelper.InitCrosshair(wpfPlotQ, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
                if (!_mouseMoveQRegistered)
                {
                    wpfPlotQ.MouseMove += (s, ev) => PlotHelper.WpfPlot_MouseMove(s, ev, nameof(ChangViewModel.CrosshairPositionText_Q), "せん断力 (kN)", "GL基準深さ(m)", 1, 3);
                    _mouseMoveQRegistered = true;

                    // CSVエクスポートメニューを追加
                    PlotHelper.AddCsvExportMenu(wpfPlotQ, "せん断力");
                }
            }

            if (wpfPlotD != null)
            {
                PlotHelper.InitCrosshair(wpfPlotD, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
                if (!_mouseMoveDRegistered)
                {
                    wpfPlotD.MouseMove += (s, ev) => PlotHelper.WpfPlot_MouseMove(s, ev, nameof(ChangViewModel.CrosshairPositionText_D), "変位 (m)", "GL基準深さ(m)", 1, 3);
                    _mouseMoveDRegistered = true;

                    // CSVエクスポートメニューを追加
                    PlotHelper.AddCsvExportMenu(wpfPlotD, "変位");
                }
            }
            // 追加：初回のプロット描画を要求（DataContext 設定後に必須）
            // UI スレッドで安全に呼び出す
            vm.RefreshPlots();
        }

        private void MainTab_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ChangViewModel vm)
                Dispatcher.Invoke(() => vm.RefreshPlots());
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var help = new HelpWindow { Owner = this };
            help.ShowDialog();
        }

        private void Changs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e?.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (DataGridChang.Items.Count > 0)
                    {
                        int idx = DataGridChang.Items.Count - 1;
                        DataGridChang.SelectedIndex = idx;
                        DataGridChang.ScrollIntoView(DataGridChang.Items[idx]);
                        DataGridChang.CurrentCell = new DataGridCellInfo(DataGridChang.Items[idx], DataGridChang.Columns[0]);
                        DataGridChang.BeginEdit();
                    }
                });
            }
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }


        // 以下をクラス内（ChangWindow クラスの他のメソッドと並ぶ位置）に追加してください
        private void CopyDataGridSelection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;

            DataGrid? dataGrid = null;
            if (menuItem.CommandParameter is DataGrid dg) dataGrid = dg;
            else if (menuItem.Parent is ContextMenu cm && cm.PlacementTarget is DataGrid dg2) dataGrid = dg2;

            if (dataGrid == null || dataGrid.SelectedCells.Count == 0) return;

            var sb = new StringBuilder();
            var selectedRows = dataGrid.SelectedCells.GroupBy(c => c.Item);

            foreach (var row in selectedRows)
            {
                var rowValues = new List<string>();
                foreach (var cell in row)
                {
                    if (cell.Column.GetCellContent(cell.Item) is TextBlock tb)
                        rowValues.Add(tb.Text);
                    else
                        rowValues.Add(string.Empty);
                }
                sb.AppendLine(string.Join("\t", rowValues));
            }

            Clipboard.SetText(sb.ToString());
        }

        private void ExportCsvFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;

            DataGrid? dataGrid = null;
            if (menuItem.CommandParameter is DataGrid dg) dataGrid = dg;
            else if (menuItem.Parent is ContextMenu cm && cm.PlacementTarget is DataGrid dg2) dataGrid = dg2;

            if (dataGrid == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "CSV ファイル (*.csv)|*.csv",
                FileName = "export.csv",
                Title = "CSV 出力"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                var cols = dataGrid.Columns;
                var sb = new StringBuilder();

                // ヘッダ
                var headers = cols.Select(c => (c.Header?.ToString() ?? "").Replace("\"", "\"\""));
                sb.AppendLine(string.Join(",", headers.Select(h => h.Contains(',') || h.Contains('"') ? $"\"{h}\"" : h)));

                // 行（Items を使う：ItemsSource が null の場合も安全）
                foreach (var item in dataGrid.Items)
                {
                    // 新規行プレースホルダ等をスキップ
                    if (Equals(item, System.Windows.Data.CollectionView.NewItemPlaceholder)) continue;

                    var row = new List<string>();
                    foreach (var col in cols)
                    {
                        string text = "";

                        // まず表示要素から取得
                        var content = col.GetCellContent(item);
                        if (content is TextBlock tb) text = tb.Text;
                        else if (content is TextBox tbox) text = tbox.Text;
                        else
                        {
                            // バインディングからプロパティを取得できるなら取得（安全策）
                            if (col is DataGridBoundColumn boundCol && boundCol.Binding is Binding b && b.Path != null)
                            {
                                var propName = b.Path.Path;
                                var prop = item?.GetType().GetProperty(propName);
                                if (prop != null)
                                {
                                    var val = prop.GetValue(item);
                                    text = val?.ToString() ?? "";
                                }
                            }
                        }

                        // エスケープ
                        if (text.Contains('"')) text = text.Replace("\"", "\"\"");
                        if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
                            text = $"\"{text}\"";

                        row.Add(text);
                    }
                    sb.AppendLine(string.Join(",", row));
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"CSV 出力に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // Save / Load ハンドラ（JSON）
        private async void SaveChangData_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ChangViewModel vm)
            {
                MessageBox.Show(this, "ViewModel が設定されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "JSON ファイル (*.json)|*.json",
                FileName = "chang_data.json",
                Title = "Chang 入力を保存"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                // 簡易 DTO 作成（必要なプロパティのみ）
                var soilPilesDto = vm.ChangSoilPiles?.Select(sp => new
                {
                    sp.OuterDiameter,
                    sp.IsHollow,
                    sp.Thickness,
                    sp.SteelThickness,
                    sp.Es,
                    sp.Fc,
                    sp.Gamma,
                    sp.Ec,
                    sp.EI,
                    sp.Alpha,
                    sp.Xi,
                    sp.E0,
                    sp.Kh0
                }).ToList();

                var changsDto = vm.Changs?.Select(ch => new
                {
                    ch.SelectedSoilPileIndex,
                    ch.Number,
                    ch.Ar,
                    ch.HorizontalLoad,
                    ch.H,
                    ch.EI,
                    ch.Kh0,
                    ch.Beta0,
                    ch.PileHeadMoment,
                    ch.PileHeadDisplacement,
                    ch.Kh,
                    ch.Beta,
                    ch.MaxBendingMoment,
                    ch.DepthOfMaxBendingMoment
                }).ToList();

                var payload = new
                {
                    Created = DateTime.UtcNow,
                    TotalHorizontalLoad = vm.TotalHorizontalLoad,
                    ChangSoilPiles = soilPilesDto,
                    Changs = changsDto
                };

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                string json = JsonSerializer.Serialize(payload, opts);
                await File.WriteAllTextAsync(sfd.FileName, json, System.Text.Encoding.UTF8);

                MessageBox.Show(this, "保存しました。", "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadChangData_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ChangViewModel vm)
            {
                MessageBox.Show(this, "ViewModel が設定されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var ofd = new OpenFileDialog
            {
                Filter = "JSON ファイル (*.json)|*.json",
                Title = "Chang 入力を読み込み"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                string json = await File.ReadAllTextAsync(ofd.FileName, System.Text.Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 全体水平力があれば設定
                if (root.TryGetProperty("TotalHorizontalLoad", out var thlProp) && thlProp.TryGetDouble(out var thl))
                {
                    vm.TotalHorizontalLoad = thl;
                }

                // SoilPiles 読み込み
                if (root.TryGetProperty("ChangSoilPiles", out var spArray) && spArray.ValueKind == JsonValueKind.Array)
                {
                    vm.ChangSoilPiles.Clear();

                    // 要素型を取得
                    var spCollectionType = vm.ChangSoilPiles.GetType();
                    var spItemType = spCollectionType.IsGenericType ? spCollectionType.GetGenericArguments()[0] : null;
                    foreach (var elem in spArray.EnumerateArray())
                    {
                        object? newItem = null;
                        if (spItemType != null)
                            newItem = Activator.CreateInstance(spItemType);

                        if (newItem != null)
                        {
                            // プロパティを設定（存在する場合のみ）
                            void SetIfExists(string propName)
                            {
                                if (!elem.TryGetProperty(propName, out var p)) return;
                                var pi = spItemType?.GetProperty(propName);
                                if (pi == null) return;
                                try
                                {
                                    object? val = null;
                                    if (p.ValueKind == JsonValueKind.Number)
                                    {
                                        if (pi.PropertyType == typeof(int)) val = p.GetInt32();
                                        else if (pi.PropertyType == typeof(double)) val = p.GetDouble();
                                        else if (pi.PropertyType == typeof(long)) val = p.GetInt64();
                                        else val = p.GetDouble();
                                    }
                                    else if (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)
                                    {
                                        if (pi.PropertyType == typeof(bool)) val = p.GetBoolean();
                                    }
                                    else if (p.ValueKind == JsonValueKind.String)
                                    {
                                        if (pi.PropertyType == typeof(string)) val = p.GetString();
                                    }
                                    if (val != null) pi.SetValue(newItem, Convert.ChangeType(val, pi.PropertyType));
                                }
                                catch { /* 無視して次へ */ }
                            }

                            foreach (var name in new[] { "OuterDiameter", "IsHollow", "Thickness", "SteelThickness", "Es", "Fc", "Gamma", "Ec", "EI", "Alpha", "Xi", "E0", "Kh0" })
                                SetIfExists(name);

                            // コレクションへ追加（型安全な Add を使うため反射）
                            var addMethod = spCollectionType.GetMethod("Add");
                            addMethod?.Invoke(vm.ChangSoilPiles, new[] { newItem });
                        }
                    }
                }

                // Changs 読み込み
                if (root.TryGetProperty("Changs", out var chArray) && chArray.ValueKind == JsonValueKind.Array)
                {
                    vm.Changs.Clear();
                    var chCollectionType = vm.Changs.GetType();
                    var chItemType = chCollectionType.IsGenericType ? chCollectionType.GetGenericArguments()[0] : null;

                    foreach (var elem in chArray.EnumerateArray())
                    {
                        object? newItem = null;
                        if (chItemType != null)
                            newItem = Activator.CreateInstance(chItemType);

                        if (newItem != null)
                        {
                            void SetIfExists(string propName)
                            {
                                if (!elem.TryGetProperty(propName, out var p)) return;
                                var pi = chItemType?.GetProperty(propName);
                                if (pi == null) return;
                                try
                                {
                                    object? val = null;
                                    if (p.ValueKind == JsonValueKind.Number)
                                    {
                                        if (pi.PropertyType == typeof(int)) val = p.GetInt32();
                                        else if (pi.PropertyType == typeof(double)) val = p.GetDouble();
                                        else val = p.GetDouble();
                                    }
                                    else if (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)
                                    {
                                        if (pi.PropertyType == typeof(bool)) val = p.GetBoolean();
                                    }
                                    else if (p.ValueKind == JsonValueKind.String)
                                    {
                                        if (pi.PropertyType == typeof(string)) val = p.GetString();
                                    }
                                    if (val != null) pi.SetValue(newItem, Convert.ChangeType(val, pi.PropertyType));
                                }
                                catch { }
                            }

                            foreach (var name in new[] { "SelectedSoilPileIndex", "Number", "Ar", "HorizontalLoad", "H", "EI", "Kh0", "Beta0", "PileHeadMoment", "PileHeadDisplacement", "Kh", "Beta", "MaxBendingMoment", "DepthOfMaxBendingMoment" })
                                SetIfExists(name);

                            var addMethod = chCollectionType.GetMethod("Add");
                            addMethod?.Invoke(vm.Changs, new[] { newItem });
                        }
                    }
                }

                // 読み込み後に再描画
                vm.RefreshPlots();

                MessageBox.Show(this, "読み込みが完了しました。", "読み込み完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // PreparingCellForEdit で編集前値をキャプチャ
        private void DataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            var item = e.Row.Item;
            if (item == System.Windows.Data.CollectionView.NewItemPlaceholder) return;

            if (e.Column is DataGridBoundColumn bound && bound.Binding is Binding b && b.Path != null)
            {
                var propName = b.Path.Path;
                var pi = item.GetType().GetProperty(propName);
                if (pi != null)
                {
                    var old = pi.GetValue(item);
                    _cellEditOldValues[(item, propName)] = old;
                }
            }
        }

        // CellEditEnding で差分があれば UndoService に積む
        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var item = e.Row.Item;
            if (item == System.Windows.Data.CollectionView.NewItemPlaceholder) return;

            if (e.Column is DataGridBoundColumn bound && bound.Binding is Binding b && b.Path != null)
            {
                var propName = b.Path.Path;
                var pi = item.GetType().GetProperty(propName);
                if (pi == null) return;

                // 編集確定後に値が反映されるので Dispatcher で後続処理
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    object? oldVal = null;
                    _cellEditOldValues.TryGetValue((item, propName), out oldVal);
                    var currentVal = pi.GetValue(item);
                    if (!Equals(oldVal, currentVal))
                    {
                        var act = new PropertyChangeAction(item, propName, oldVal, currentVal);
                        UndoService.Instance.Push(act);
                    }
                    _cellEditOldValues.Remove((item, propName));
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // キーボードで Ctrl+Z / Ctrl+Y を受けて Undo/Redo（どちらかを呼ぶ）
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (e.Key == System.Windows.Input.Key.Z)
                {
                    UndoService.Instance.Undo();
                    if (DataContext is PileDesign.ViewModels.ChangViewModel vm) vm.RefreshPlots();
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Y)
                {
                    UndoService.Instance.Redo();
                    if (DataContext is PileDesign.ViewModels.ChangViewModel vm) vm.RefreshPlots();
                    e.Handled = true;
                }
            }
        }

        // --- 追加: XAML で参照されるイベントハンドラのスタブ実装 ---
        // ComboBox.DropDownOpened は EventHandler を受け取る
        private void ComboBox_DropDownOpened(object? sender, EventArgs e)
        {
            // 現状は特別な処理不要。将来、選択肢の再ロード等が必要ならここに実装。
        }

        // PCRing 選択変更（DataGrid 内の ComboBox で呼ばれる）
        private void ComboBoxPCRingChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm)
                {
                    // 選択変更に応じて必要なら再描画
                    vm.RefreshPlots();
                }
            }
            catch { /* 安全に無視 */ }
        }

        // 絞り率コンボ選択変更
        private void ComboBoxContractionRatioSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm)
                {
                    vm.RefreshPlots();
                }
            }
            catch { }
        }

        // 引張定着筋チェック ON
        private void CheckBoxHasTensionAnchorsChecked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm)
                {
                    vm.RefreshPlots();
                }
            }
            catch { }
        }

        // 引張定着筋チェック OFF
        private void CheckBoxHasTensionAnchorsUnchecked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm)
                {
                    vm.RefreshPlots();
                }
            }
            catch { }
        }

        // FT 関連のイベントハンドラ（ChangWindow.xaml が参照しているスタブ）
        private void ComboBoxFTCapChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm) vm.RefreshPlots();
            }
            catch { }
        }

        private void CheckBoxHasFTTensionAnchorsChecked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm) vm.RefreshPlots();
            }
            catch { }
        }

        private void CheckBoxHasFTTensionAnchorsUnchecked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm) vm.RefreshPlots();
            }
            catch { }
        }

        private void TextBoxFTTensionNumChanged(object? sender, TextChangedEventArgs e)
        {
            try
            {
                if (DataContext is ChangViewModel vm) vm.RefreshPlots();
            }
            catch { }
        }
        // --- 追加終了 ---

    }
}