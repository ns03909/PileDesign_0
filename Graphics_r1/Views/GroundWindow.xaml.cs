using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

using Serilog;
using PileDesign.Services;
namespace PileDesign.Views
{
    /// <summary>
    /// GroundWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GroundWindow : Window

    {
        private bool _isClosingHandled = false;

        // 再入防止フラグ
        private bool _isCellEditEndingInProgress = false;

        // コンストラクタ
        public GroundWindow()
        {
            InitializeComponent();
            InitializePaneMaximizer();
            Loaded += GroundWindow_Loaded;
            // ★ ContentRendered で Initialize() を呼ぶように変更
            ContentRendered += GroundWindow_ContentRendered;
        }

        private void GroundWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                var _viewModel = viewModel;
                _viewModel.GroundWindowInstance = this;
                _viewModel.ActivateCustomDispTab = () =>
                {
                    // 任意地盤変位タブを前面に表示
                    if (CustomDispTab != null)
                    {
                        if (CustomDispTab.IsHidden) CustomDispTab.Show();
                        CustomDispTab.IsSelected = true;
                    }
                };

                _viewModel.SetCustomDispTabVisibility = (visible) =>
                {
                    if (CustomDispTab == null) return;
                    if (visible)
                    {
                        if (CustomDispTab.IsHidden) CustomDispTab.Show();
                    }
                    else
                    {
                        if (!CustomDispTab.IsHidden) CustomDispTab.Hide();
                    }
                };

                // 初期状態を任意入力モードか否かで設定
                if (CustomDispTab != null)
                {
                    bool isCustomMode = !_viewModel.IsCustomDisplacementDisabled;
                    if (isCustomMode)
                    {
                        if (CustomDispTab.IsHidden) CustomDispTab.Show();
                    }
                    else
                    {
                        if (!CustomDispTab.IsHidden) CustomDispTab.Hide();
                    }
                }

                //_viewModel.RequestClose += (s, e)
                _viewModel.RequestClose += (s, e2) =>
                {
                    {
                        // すでにクローズ処理中なら何もしない
                        if (_isClosingHandled) return;
                        _isClosingHandled = true;

                        if (this.IsLoaded && this.IsVisible)
                        {
                            this.Close();
                        }
                    }
                };
                _viewModel.NValueTab = NValueTab;
                _viewModel.CuValueTab = CuValueTab;
                _viewModel.VsValueTab = VsValueTab;
                _viewModel.EsValueTab = EsValueTab;
                _viewModel.DefTab = DefTab;
                _viewModel.FsTab = FsTab;
                // 行コミット後の再計算を確実にする
                // 行コミット後の再計算を確実にする（両DataGridとも同一ハンドラ）
                //DataGridGroundLayer.RowEditEnding += DataGrid_Common_RowEditEnding;
                //DataGridGroundMass.RowEditEnding += DataGrid_Common_RowEditEnding;

                // ★ Initialize() は ContentRendered で呼び出すため、ここでは呼ばない
                // _viewModel.Initialize();

                // セル選択モードでSelectedItemが追跡されない問題の対策
                DataGridGroundMass.CurrentCellChanged += DataGridGroundMass_CurrentCellChanged;
            }
        }

        private void DataGridGroundMass_CurrentCellChanged(object? sender, EventArgs e)
        {
            if (DataGridGroundMass.CurrentItem is GroundMassDataInput item
                && DataContext is GroundLayerViewModel vm)
            {
                vm.LastFocusedGroundMass = item;
            }
        }

        /// <summary>
        /// ウィンドウが完全にレンダリングされた後に呼ばれる
        /// ScottPlot などのコントロールが完全に初期化された状態で Initialize() を呼ぶ
        /// </summary>
        private async void GroundWindow_ContentRendered(object? sender, EventArgs e)
        {
            // イベントを解除（1回だけ実行）
            ContentRendered -= GroundWindow_ContentRendered;

            try
            {
                // さらに少し待って、すべてのコントロールが完全に初期化されるのを待つ
                await System.Threading.Tasks.Task.Delay(50);

                if (DataContext is GroundLayerViewModel viewModel)
                {
                    try
                    {
                        viewModel.Initialize();
                    }
                    catch (System.Runtime.InteropServices.COMException comEx) when (comEx.HResult == unchecked((int)0x80040206))
                    {
                        // 初期化時の COMException は無視して再試行
                        await System.Threading.Tasks.Task.Delay(100);
                        try
                        {
                            viewModel.Initialize();
                        }
                        catch (Exception innerEx)
                        {
                            // 最終的に失敗しても続行
                            Log.Warning(innerEx, "[GroundWindow_ContentRendered] Initialize retry failed");
                        }
                    }

                    // CSVエクスポートメニュー（兼・別ウィンドウで開くメニュー）を追加
                    PlotHelper.AddCsvExportMenu(wpfPlotNValue, "N値");
                    PlotHelper.AddCsvExportMenu(wpfPlotCu, "Cu");
                    PlotHelper.AddCsvExportMenu(wpfPlotVs, "Vs");
                    PlotHelper.AddCsvExportMenu(wpfPlotEs, "Es");
                    PlotHelper.AddCsvExportMenu(wpfPlotDisplacement, "地盤変位");
                    PlotHelper.AddCsvExportMenu(wpfPlotFL, "FL値");
                    PlotHelper.AddCsvExportMenu(wpfPlotResponseSpectrum, "加速度応答スペクトル");

                    // マウスホバー詳細ポップアップを各プロットに取り付け
                    AttachGroundHoverPopup(wpfPlotNValue, "N値", "GL基準深さ(m)", 1, 2);
                    AttachGroundHoverPopup(wpfPlotCu, "粘着力(kN/m²)", "GL基準深さ(m)", 1, 2);
                    AttachGroundHoverPopup(wpfPlotVs, "S波速度(m/s)", "GL基準深さ(m)", 1, 2);
                    AttachGroundHoverPopup(wpfPlotEs, "変形係数 Es(kN/m²)", "GL基準深さ(m)", 0, 2);
                    AttachGroundHoverPopup(wpfPlotDisplacement, "地盤変位(mm)", "GL基準深さ(m)", 2, 2);
                    AttachGroundHoverPopup(wpfPlotFL, "FL値", "GL基準深さ(m)", 2, 2);
                    AttachGroundHoverPopup(wpfPlotResponseSpectrum, "周期 T(s)", "Sa(m/s²)", 3, 2);

                    // 応答スペクトルタブの初期表示判定 (自動計算 + 応答スペクトル法 のときのみ表示)
                    UpdateSaTabVisibility();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[GroundWindow_ContentRendered]");
                MessageService.Show($"地盤ウィンドウの初期化でエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // 共通: CellEditEnding（Layer/Mass 両方から使用）
        private void DataGrid_Common_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // 編集中 TextBox のバインディングを明示的にソース更新
            if (e.EditingElement is TextBox tb)
            {
                var be = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
                be?.UpdateSource();
            }

            if (sender is DataGrid grid)
            {
                // セル/行コミット完了後にデバウンス付きUpdate
                grid.Dispatcher.InvokeAsync(() =>
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);

                    if (DataContext is GroundLayerViewModel vm)
                        vm.ScheduleUpdate();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        // 行編集終了でも保険として再計算（行コミット後）
        private void DataGridGroundLayer_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var grid = (DataGrid)sender;
            // ★ 修正: 行編集終了時に保留中の更新を処理
            // 行編集が終わった時点で IME/TextStore は解放されているはず
            _pendingUpdate = true;
            SchedulePendingUpdate();
        }

        private void DataGridGroundMass_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            var grid = (DataGrid)sender;
            // ★ 修正: 行編集終了時に保留中の更新を処理
            _pendingUpdate = true;
            SchedulePendingUpdate();
        }

        /// <summary>
        /// 保留中の更新をスケジュールする
        /// タイマーを使って IME/TextStore が完全に解放された後に実行
        /// </summary>
        private DispatcherTimer? _pendingUpdateTimer;

        private void SchedulePendingUpdate()
        {
            if (!_pendingUpdate) return;

            // 既存のタイマーがあれば停止（連続編集時の重複防止）
            _pendingUpdateTimer?.Stop();

            // ★ 重要: 現在フォーカスがある TextBox からフォーカスを外す
            // これにより IME/TextStore が解放される
            if (Keyboard.FocusedElement is TextBox focusedTextBox)
            {
                // DataGrid 自体にフォーカスを移動
                var parent = focusedTextBox.Parent;
                while (parent != null && parent is not DataGrid)
                {
                    parent = (parent as FrameworkElement)?.Parent;
                }
                if (parent is DataGrid dg)
                {
                    dg.Focus();
                }
            }

            // 十分な遅延を設けて IME/TextStore の解放を待つ（200ms に増加）
            _pendingUpdateTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _pendingUpdateTimer.Tick += (s, e) =>
            {
                _pendingUpdateTimer?.Stop();
                _pendingUpdateTimer = null;
                if (_pendingUpdate)
                {
                    _pendingUpdate = false;
                    SafeUpdate();
                }
            };
            _pendingUpdateTimer.Start();
        }

        /// <summary>
        /// COMException をキャッチしながら Update() を安全に実行
        /// </summary>
        private void SafeUpdate()
        {
            try
            {
                if (DataContext is GroundLayerViewModel vm)
                {
                    vm.ScheduleUpdate();
                }
            }
            catch (System.Runtime.InteropServices.COMException comEx) when (comEx.HResult == unchecked((int)0x80040206))
            {
                // IME/TextStore の競合による COMException を無視
                // ScheduleUpdate のデバウンスで自然に再試行される
            }
        }

        // 「基礎指針'19 4.5自動計算」「考慮しない」ラジオ共通 Checked ハンドラ
        // (任意入力ラジオは別途専用ハンドラあり)
        private void DispMode_Generic_Checked(object sender, RoutedEventArgs e)
        {
            UpdateSaTabVisibility();
            // 「考慮しない」ラジオは GroundInput.IsGroundDisplacementIgnored へ直接バインドしており、
            // ViewModel の ScheduleUpdate が走らないので明示的に呼ぶ。
            // 「自動計算」ラジオは IsAutoDisplacementMode setter 経由で ScheduleUpdate が走るが、
            // 「考慮しない → 自動計算」の経路でも呼んで安全側にしておく。
            if (DataContext is GroundLayerViewModel vm)
            {
                vm.ScheduleUpdate();
            }
        }

        // 応答スペクトルタブの表示/非表示を更新する
        // 「基礎指針'19 4.5自動計算」モード かつ 算定法が「応答スペクトル法」の時のみ表示
        private void UpdateSaTabVisibility()
        {
            if (SaTab == null) return;
            if (DataContext is not GroundLayerViewModel vm) return;

            bool isAutoMode = vm.IsAutoDisplacementMode;
            string method = vm.GroundInput?.CalculationMethod ?? "";
            bool shouldShow = isAutoMode && method == "応答スペクトル法";

            if (shouldShow)
            {
                if (SaTab.IsHidden) SaTab.Show();
            }
            else
            {
                if (!SaTab.IsHidden) SaTab.Hide();
            }
        }

        private void GroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 地盤変位算定法 ComboBox の変更で SaTab 表示判定を更新
            UpdateSaTabVisibility();
            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (sender is ComboBox)
                {
                    viewModel.ScheduleUpdate();
                }
            }
        }

        private void CustomDispCase_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ケース切り替え時にDataGridのItemsSourceが更新される（VMのPropertyChanged経由）
        }

        // 「任意入力」ラジオボタンが選択されたら、右ペインの「任意地盤変位」タブを前面に表示する。
        // (タブが他のタブ (地盤変位 DmaxU*, 液状化安全率, 応答スペクトル) と同一グループにあるため、
        //  ラジオを選んだだけでは見えない、というユーザーから報告された問題への対策)
        private void RadioCustomDisplacement_Checked(object sender, RoutedEventArgs e)
        {
            if (CustomDispTab != null)
            {
                if (CustomDispTab.IsHidden) CustomDispTab.Show();
                CustomDispTab.IsSelected = true;
                CustomDispTab.IsActive = true;
            }
            UpdateSaTabVisibility();
            // 任意入力ラジオは GroundInput.CustomDisplacementProfile.IsEnabled へ直接バインドしており、
            // ViewModel の ScheduleUpdate が走らないので明示的に呼ぶ (グラフ再描画)
            if (DataContext is GroundLayerViewModel vm) vm.ScheduleUpdate();
        }

        // 「任意入力」ラジオから他モード (基礎指針'19 自動計算 / 考慮しない) に切替えた時、
        // 「任意地盤変位」タブを非表示に戻す。
        private void RadioCustomDisplacement_Unchecked(object sender, RoutedEventArgs e)
        {
            if (CustomDispTab != null && !CustomDispTab.IsHidden)
            {
                CustomDispTab.Hide();
            }
            UpdateSaTabVisibility();
            if (DataContext is GroundLayerViewModel vm) vm.ScheduleUpdate();
        }

        // ============== グラフホバー詳細ポップアップ ==============
        // マウスがマーカー近傍にあるとき、最も近いマーカーの (Legend, X, Y) を
        // 小さな Popup で表示する。プロット領域外に出たらポップアップを閉じる。

        private void AttachGroundHoverPopup(ScottPlot.WPF.WpfPlot plot,
            string xLabel, string yLabel, int decimalPlacesX, int decimalPlacesY)
        {
            if (plot == null) return;
            plot.MouseMove += (s, args) => HandleGroundHoverMove(plot, args, xLabel, yLabel, decimalPlacesX, decimalPlacesY);
            plot.MouseLeave += (s, args) => GroundHoverPopup.IsOpen = false;
        }

        private void HandleGroundHoverMove(ScottPlot.WPF.WpfPlot plot, MouseEventArgs e,
            string xLabel, string yLabel, int dpX, int dpY)
        {
            var p = e.GetPosition(plot);
            var mousePixel = new ScottPlot.Pixel(p.X * plot.DisplayScale, p.Y * plot.DisplayScale);
            var mouseLocation = plot.Plot.GetCoordinates(mousePixel);

            double minDist = double.MaxValue;
            ScottPlot.DataPoint? nearest = null;
            ScottPlot.Plottables.Scatter nearestScatter = null;

            foreach (var scatter in plot.Plot.GetPlottables().OfType<ScottPlot.Plottables.Scatter>())
            {
                var pt = scatter.Data.GetNearest(mouseLocation, plot.Plot.LastRender);
                if (!pt.IsReal) continue;
                double dx = pt.Coordinates.X - mouseLocation.X;
                double dy = pt.Coordinates.Y - mouseLocation.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = pt;
                    nearestScatter = scatter;
                }
            }

            if (nearest is { IsReal: true })
            {
                var c = nearest.Value.Coordinates;
                string fX = "N" + dpX;
                string fY = "N" + dpY;
                var sb = new StringBuilder();
                string legend = nearestScatter?.LegendText;
                if (!string.IsNullOrWhiteSpace(legend)) sb.AppendLine(legend);
                sb.AppendLine($"{xLabel} = {c.X.ToString(fX)}");
                sb.Append($"{yLabel} = {c.Y.ToString(fY)}");

                var pWin = e.GetPosition(this);
                GroundHoverText.Text = sb.ToString();
                GroundHoverPopup.HorizontalOffset = pWin.X + 16;
                GroundHoverPopup.VerticalOffset = pWin.Y + 16;
                if (!GroundHoverPopup.IsOpen) GroundHoverPopup.IsOpen = true;
            }
            else
            {
                GroundHoverPopup.IsOpen = false;
            }
        }

        private void CustomDispAddRow_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                vm.SelectedCustomDispProfile.Add(new Models.InputData.DisplacementPoint(0, 0));
            }
        }

        private void CustomDispInsertBelowRow_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                var profile = vm.SelectedCustomDispProfile;

                // CurrentCellからアイテムを取得してインデックスを特定
                int idx = -1;
                if (DataGridCustomDisplacement.CurrentCell.Item is Models.InputData.DisplacementPoint currentItem)
                {
                    idx = profile.IndexOf(currentItem);
                }
                // フォールバック: SelectedIndex
                if (idx < 0)
                    idx = DataGridCustomDisplacement.SelectedIndex;

                if (idx >= 0 && idx < profile.Count)
                {
                    profile.Insert(idx + 1, new Models.InputData.DisplacementPoint(0, 0));
                }
                else
                {
                    profile.Add(new Models.InputData.DisplacementPoint(0, 0));
                }
            }
        }

        private void CustomDispDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GroundLayerViewModel vm || vm.SelectedCustomDispProfile == null) return;

            // CurrentCellまたはSelectedItemからアイテムを取得
            var point = DataGridCustomDisplacement.CurrentCell.Item as Models.InputData.DisplacementPoint
                        ?? DataGridCustomDisplacement.SelectedItem as Models.InputData.DisplacementPoint;
            if (point != null)
            {
                vm.SelectedCustomDispProfile.Remove(point);
                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        private void CustomDispClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                vm.SelectedCustomDispProfile.Clear();
                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        private void CustomDispSortAndDedupe_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm && vm.SelectedCustomDispProfile != null)
            {
                var profile = vm.SelectedCustomDispProfile;
                // 標高Zで降順ソートし、重複するZは最初の1つだけ残す
                var sorted = profile
                    .OrderByDescending(p => p.Z)
                    .GroupBy(p => Math.Round(p.Z, 6))
                    .Select(g => g.First())
                    .ToList();

                profile.Clear();
                foreach (var p in sorted)
                    profile.Add(p);

                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        private void CustomDispDataGrid_PasteCompleted(object sender, EventArgs e)
        {
            if (DataContext is GroundLayerViewModel vm)
            {
                vm.ScheduleUpdate();
                vm.ValidateCustomDisplacementProfiles();
            }
        }

        // 変位係数スケーラー: 現状の Displacement 値にスライダー係数を一括乗算
        // 適用後はスライダーを 1.0 にリセット (重ね掛けを防ぐ)
        // Undo (GroundLayerViewModel の Undo スタック) で元に戻せる
        private void CustomDispApplyScale_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GroundLayerViewModel vm) return;
            var profile = vm.SelectedCustomDispProfile;
            if (profile == null || profile.Count == 0) return;

            double factor = SliderCustomDispScale.Value;
            // 等倍は何もしない (UI 操作の余計な undo step を作らない)
            if (Math.Abs(factor - 1.0) < 1e-9) return;

            // Undo 履歴を一意な操作として残す (各 DataItem の値を直接書き換えるため、
            // 履歴上では「変位係数 N.NN 倍を適用」の 1 ステップとして記録)
            vm._undoManager?.PushState(vm.GroundsInput?.Select(g => g.DeepCopy()).ToList());

            foreach (var p in profile)
            {
                p.Displacement *= factor;
            }

            // スライダーを 1.0 にリセット (重ね掛け防止 + 視覚的フィードバック)
            SliderCustomDispScale.Value = 1.0;

            vm.ScheduleUpdate();
            vm.ValidateCustomDisplacementProfiles();
        }

        // 基礎指針'19 4.5 自動計算結果 (DmaxU* / DmaxU*+ΣγcyH) を、選択中ケースのプロファイルへコピー
        // ケース対応: 0=L1非液状化(DmaxUStar[0]), 1=L1液状化(DmaxUStarSigmaGammaCyH[0]),
        //             2=L2非液状化(DmaxUStar[1]), 3=L2液状化(DmaxUStarSigmaGammaCyH[1])
        private void CustomDispCopyAuto_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GroundLayerViewModel vm) return;
            var ground = vm.GroundInput;
            if (ground == null) return;
            var masses = ground.GroundMassesData;
            if (masses == null || masses.Count == 0)
            {
                MessageService.Show("土質点データが空です。先に「土質点入力」で土質点を入力してください。",
                    "コピー不可", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var profile = vm.SelectedCustomDispProfile;
            if (profile == null) return;

            int caseIndex = vm.SelectedCustomDispCaseIndex;
            int level = caseIndex / 2;            // 0=L1, 1=L2
            bool withLiq = (caseIndex % 2) == 1;  // 1,3 が液状化あり (ΣγcyH 加算)

            // 既存データがあれば確認
            if (profile.Count > 0)
            {
                var ans = MessageService.Show(
                    $"現在「{vm.CustomDispCaseOptions[caseIndex]}」に {profile.Count} 件のデータがあります。\nこれらを破棄して自動計算値で置き換えますか?",
                    "上書き確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans != MessageBoxResult.Yes) return;
            }

            // 各土質点について Z (標高) と変位を算出
            double topAlt = ground.GroundTopAltitude;
            var newPoints = new List<DisplacementPoint>(masses.Count);
            int skipped = 0;
            for (int i = 0; i < masses.Count; i++)
            {
                var data = masses[i];
                // GroundLayerViewModel.DrawGroundDisplacementGraph と同じ深さ算出ロジック
                double factor = (i == 0) ? 1.0
                              : (i == masses.Count - 1) ? 0.0
                              : 0.5;
                double gLDepth = data.GLDepth + data.Spacing * factor;
                double Z = topAlt + gLDepth; // 標高 = 孔口標高 + GL基準深さ(負値)

                double disp;
                var src = withLiq ? data.DmaxUStarSigmaGammaCyH : data.DmaxUStar;
                if (src == null || src.Count <= level)
                {
                    skipped++;
                    continue;
                }
                disp = src[level];
                if (double.IsNaN(disp))
                {
                    skipped++;
                    continue;
                }
                newPoints.Add(new DisplacementPoint(Z, disp));
            }

            if (newPoints.Count == 0)
            {
                MessageService.Show("自動計算値がすべて空または NaN でした。「液状化」タブで先に計算を実行してください。",
                    "コピー不可", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            profile.Clear();
            foreach (var p in newPoints) profile.Add(p);

            vm.ScheduleUpdate();
            vm.ValidateCustomDisplacementProfiles();

            string msg = $"{newPoints.Count} 件のデータをコピーしました。";
            if (skipped > 0) msg += $" ({skipped} 件は計算値なしでスキップ)";
            MessageService.Show(msg, "コピー完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 基礎指針'19 4.5 自動計算結果を 4 ケース全てに一括コピー
        // 各ケースの (level, withLiq) 対応:
        //   0=L1非液状化, 1=L1液状化, 2=L2非液状化, 3=L2液状化
        private void CustomDispCopyAutoAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GroundLayerViewModel vm) return;
            var ground = vm.GroundInput;
            if (ground == null) return;
            var masses = ground.GroundMassesData;
            if (masses == null || masses.Count == 0)
            {
                MessageService.Show("土質点データが空です。先に「土質点入力」で土質点を入力してください。",
                    "コピー不可", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var custom = ground.CustomDisplacementProfile;
            if (custom == null) return;

            // 既存データのケース毎件数を集計し、上書き確認
            int existingTotal = 0;
            for (int ci = 0; ci < 4; ci++)
            {
                var p = custom.GetProfile(ci);
                if (p != null) existingTotal += p.Count;
            }
            if (existingTotal > 0)
            {
                var ans = MessageService.Show(
                    $"4 ケース合計 {existingTotal} 件のデータが既に入力されています。\n" +
                    $"全て破棄して自動計算値で置き換えますか?",
                    "上書き確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans != MessageBoxResult.Yes) return;
            }

            double topAlt = ground.GroundTopAltitude;
            int totalCopied = 0;
            int totalSkipped = 0;

            for (int caseIndex = 0; caseIndex < 4; caseIndex++)
            {
                int level = caseIndex / 2;            // 0=L1, 1=L2
                bool withLiq = (caseIndex % 2) == 1;  // 1,3 = 液状化あり

                var newPoints = new List<DisplacementPoint>(masses.Count);
                for (int i = 0; i < masses.Count; i++)
                {
                    var data = masses[i];
                    double factor = (i == 0) ? 1.0
                                  : (i == masses.Count - 1) ? 0.0
                                  : 0.5;
                    double gLDepth = data.GLDepth + data.Spacing * factor;
                    double Z = topAlt + gLDepth;

                    var src = withLiq ? data.DmaxUStarSigmaGammaCyH : data.DmaxUStar;
                    if (src == null || src.Count <= level)
                    {
                        totalSkipped++;
                        continue;
                    }
                    double disp = src[level];
                    if (double.IsNaN(disp))
                    {
                        totalSkipped++;
                        continue;
                    }
                    newPoints.Add(new DisplacementPoint(Z, disp));
                }

                var profile = custom.GetProfile(caseIndex);
                if (profile != null)
                {
                    profile.Clear();
                    foreach (var p in newPoints) profile.Add(p);
                    totalCopied += newPoints.Count;
                }
            }

            if (totalCopied == 0)
            {
                MessageService.Show(
                    "自動計算値が全て空または NaN でした。「液状化」タブで先に計算を実行してください。",
                    "コピー不可", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            vm.ScheduleUpdate();
            vm.ValidateCustomDisplacementProfiles();

            string msg = $"4 ケース合計 {totalCopied} 件のデータをコピーしました。";
            if (totalSkipped > 0) msg += $" ({totalSkipped} 件は計算値なしでスキップ)";
            MessageService.Show(msg, "コピー完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CustomDispDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DataContext is GroundLayerViewModel vm)
                {
                    vm.ScheduleUpdate();
                    vm.ValidateCustomDisplacementProfiles();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void HandleTextBoxValueConfirmed(TextBox textBox)
        {
            var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            binding?.UpdateSource();
            if (DataContext is GroundLayerViewModel viewModel)
            {
                switch (textBox.Name)
                {
                    case "TextBoxGroundTopAltitude":
                        viewModel.GroundTopAltitudeTextBox_LostFocus();
                        break;
                    case "TextBoxGroundWaterTableAltitude":
                        viewModel.TextBoxGroundWaterTableAltitude_LostFocus();
                        break;
                    case "TextBoxGroundStressAltitude":
                        viewModel.TextBoxGroundStressAltitude_LostFocus();
                        break;
                    case "TextBoxGroundWaterGLDepth":
                        viewModel.TextBoxGroundWaterGLDepth_LostFocus();
                        break;
                    case "TextBoxStressGLDepth":
                        viewModel.TextBoxStressGLDepth_LostFocus();
                        break;
                    default:
                        viewModel.GroundTextBox_LostFocus();
                        break;
                }
            }
        }

        private void TextBox_Common_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                HandleTextBoxValueConfirmed(textBox);
            }
        }

        private void TextBox_Common_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                HandleTextBoxValueConfirmed(textBox);
            }
        }

        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            viewModel?.DataGridPileLayout_OnLoadingRowCommand.Execute(e); // ビューモデルのコマンドを実行

            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                // 変更前の状態を保存
                //viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());

                viewModel.SliderEngineeringBedrockValueChangedCommand.Execute(e.NewValue);
            }
        }

        private object _originalValue;
        private string _originalPropertyName;

        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column is DataGridBoundColumn editedColumn && editedColumn.Binding is Binding binding)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());
                }

                var item = e.Row.Item;
                var propertyInfo = item.GetType().GetProperty(binding.Path.Path);
                if (propertyInfo != null)
                {
                    _originalValue = propertyInfo.GetValue(item);
                    _originalPropertyName = binding.Path.Path;
                }
            }
        }

        private void DataGrid_CellEditEnding<T>(object sender, DataGridCellEditEndingEventArgs e, string propertyName)
    where T : class
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // 再入防止
            if (_isCellEditEndingInProgress) return;
            _isCellEditEndingInProgress = true;

            try
            {
                // 編集中 TextBox のバインディングをソースへ確定
                if (e.EditingElement is TextBox tb)
                {
                    var be = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
                    be?.UpdateSource();
                }

                // 対象列（propertyName）編集時のみ、全行でエラーチェック
                bool targetColumn =
                    e.Column is DataGridBoundColumn editedColumn &&
                    editedColumn.Binding is Binding binding &&
                    binding.Path?.Path == propertyName;

                if (sender is not DataGrid dg) return;

                // エラーチェックは同期的に行う（Update() は呼ばない）
                if (targetColumn)
                {
                    int count = dg.Items.Count;

                    for (int i = 0; i < count; i++)
                    {
                        if (dg.Items[i] is not T rowItem) continue;

                        double value = GetDepthValue(rowItem, propertyName);
                        bool isError = false;

                        if (double.IsNaN(value))
                        {
                            isError = true;
                        }
                        else
                        {
                            if (i == 0) isError |= value >= 0;
                            if (i > 0)
                            {
                                double above = GetDepthValue(dg.Items[i - 1] as T, propertyName);
                                if (!double.IsNaN(above)) isError |= above <= value;
                            }
                            if (i < count - 1)
                            {
                                double below = GetDepthValue(dg.Items[i + 1] as T, propertyName);
                                if (!double.IsNaN(below)) isError |= value <= below;
                            }
                        }

                        SetIsError(rowItem, isError);
                    }
                }

                // ★ 修正: CellEditEnding 内では Update() を呼ばない
                // 代わりに _pendingUpdate フラグを立てて、タイマーで遅延更新する
                _pendingUpdate = true;
                SchedulePendingUpdate();
            }
            finally
            {
                // 遅延して再入フラグを解除
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    _isCellEditEndingInProgress = false;
                }), DispatcherPriority.Background);
            }
        }

        // 保留中の更新があるかどうか
        private bool _pendingUpdate = false;


        // GroundLayer 用: BottomGLDepth 列
        private void DataGridGroundLayer_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
            => DataGrid_CellEditEnding<GroundLayerInput>(sender, e, "BottomGLDepth");

        // GroundMass 用: GLDepth 列
        private void DataGridGroundMass_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
            => DataGrid_CellEditEnding<GroundMassDataInput>(sender, e, "GLDepth");


        // 型ごとに値取得
        private static double GetDepthValue(object row, string propertyName)
        {
            if (row == null) return double.NaN;
            var prop = row.GetType().GetProperty(propertyName);
            if (prop != null)
            {
                var value = prop.GetValue(row);
                if (value is double d) return d;
            }
            return double.NaN;
        }

        // 型ごとにIsErrorセット
        private static void SetIsError(object row, bool isError)
        {
            if (row == null) return;                  // 追加: nullガード
            var prop = row.GetType().GetProperty("IsError");
            if (prop == null || !prop.CanWrite) return;
            prop.SetValue(row, isError);
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                textBox.SelectAll();
                e.Handled = true;
            }
        }

        private void SliderEngineeringBedrock_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                int value = (int)SliderEngineeringBedrock.Value;
                int n = viewModel.GroundInput.GroundLayers.Count;

                // i行のチェックボックスの状態が変更されたとき、1～i-1行のチェックボックスを有効化、i+1行目以降のチェックボックスを無効化
                for (int i = 0; i < n; i++)
                {
                    if (n - 1 - i < value)
                    {
                        viewModel.GroundInput.GroundLayers[i].IsEngineeringBedrock = true;
                    }
                    else
                    {
                        viewModel.GroundInput.GroundLayers[i].IsEngineeringBedrock = false;
                    }
                }
                viewModel.Update();
            }
        }

        private void GroundLayerCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // 行が削除された場合、すべての行番号を再計算する
                for (int i = 0; i < DataGridGroundLayer.Items.Count; i++)
                {
                    if (DataGridGroundLayer.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                    {
                        row.Header = (i + 1).ToString();
                    }
                }
            }
        }


        private void ComboBoxGroundNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (ComboBoxGroundNo.SelectedItem is string /*selectedItem*/)
                {
                    // 例: "3 (New)" → 3, "2" → 2
                    int selectedIndex = ComboBoxGroundNo.SelectedIndex;
                    //int previousSelectedIndex = -1;
                    //if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is string removedItem)
                    //{
                    //    previousSelectedIndex = ComboBoxGroundNo.Items.IndexOf(removedItem);
                    //}

                    viewModel.ComboBoxGroundNo_SelectionChanged(selectedIndex/*, previousSelectedIndex*/);
                }
            }
        }



        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;

            if (DataContext is GroundLayerViewModel viewModel)
            {
                viewModel.GetType().GetMethod("OnCancel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.Invoke(viewModel, null);
            }
        }

        // LevelのComboBoxの選択が変更されたときの処理
        private void ComboBoxLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = (GroundLayerViewModel)DataContext;
            viewModel?.Update();
        }

        private void ButtonDeleteGroundMass_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (GroundLayerViewModel)DataContext;
            InputModel InputModel = viewModel.InputModel;
            if (DataGridGroundMass.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridGroundMass.SelectedItem is GroundMassDataInput selectedItem)
                {
                    InputModel.GroundsInput[viewModel.GroundNo - 1].GroundMassesData.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    MessageService.Show("選択されたアイテムの型が正しくありません。");
                }
                DataGridGroundMass.Items.Refresh();
            }
        }

        private void ButtonDeleteGroundLayer_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (GroundLayerViewModel)DataContext;
            InputModel InputModel = viewModel.InputModel;
            if (DataGridGroundLayer.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridGroundLayer.SelectedItem is GroundLayerInput selectedItem)
                {
                    InputModel.GroundsInput[viewModel.GroundNo - 1].GroundLayers.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    MessageService.Show("選択されたアイテムの型が正しくありません。");
                }
                DataGridGroundLayer.Items.Refresh();
                viewModel.Update();
            }
        }

        private void ExportCsvFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is DataGrid dataGrid)
            {
                var data = dataGrid.ItemsSource.Cast<object>();
                {
                    DataGridCsv.Export(data, dataGrid);
                }
            }
        }

        // ContextMenuが開かれたときにDataGridをCommandParameterに設定するイベントハンドラ
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is DataGrid dataGrid)
                {
                    foreach (MenuItem menuItem in contextMenu.Items)
                    {
                        menuItem.CommandParameter = dataGrid;
                    }
                }
            }
        }

        // PreviewKeyDownイベントを使用して編集をキャンセルする
        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (sender is DataGrid dataGrid && dataGrid.SelectedItem is GroundLayerInput /*selectedItem*/)
                {
                    // 編集をキャンセルする
                    dataGrid.CancelEdit();
                    e.Handled = true;
                }
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                comboBox.Style = null; // 一度スタイルを解除
                comboBox.Style = (Style)FindResource("GranularityClassComboBoxStyle"); // 再適用


                // 重要: バインディングのソースへ反映を先に行う
                var be = BindingOperations.GetBindingExpression(comboBox, ComboBox.SelectedItemProperty);
                be?.UpdateSource();
            }

            if (DataContext is GroundLayerViewModel viewModel)
            {
                viewModel.ComboBox_SelectionChangedCommand();
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                binding?.UpdateSource();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.RedoCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void GroundWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is GroundLayerViewModel vm && vm.OkCommand?.CanExecute(null) == true)
                {
                    vm.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Y)
            {
                if (DataContext is GroundLayerViewModel viewModel)
                {
                    viewModel.RedoCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Undoスタック（状態保存）
            if (DataContext is GroundLayerViewModel viewModel)
            {
                // 編集前の状態を保存（EnterキーやBackspace/文字入力時など）
                // 必要に応じてキー判定を追加
                if (e.Key == Key.Enter)
                //if (e.Key == Key.Enter || e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Space)
                {
                    viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());
                }
            }
        }

        private void TextBox_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Undoスタック（状態保存）
            if (DataContext is GroundLayerViewModel viewModel)
            {
                // 編集前の状態を保存（EnterキーやBackspace/文字入力時など）
                {
                    viewModel._undoManager.PushState(viewModel.GroundsInput.Select(x => x.DeepCopy()).ToList());
                }
            }
        }

        // Button を押したときに ContextMenu を開く
        private void ButtonExamples_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var cm = btn.ContextMenu;
            if (cm == null) return;

            // ContextMenu をボタン右下に表示
            cm.PlacementTarget = btn;
            cm.Placement = PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        // ContextMenu 内の MenuItem が選択されたとき（DataContext は ExampleItem）
        private void ExampleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi) return;
            if (mi.DataContext is not PileDesign.ViewModels.ExampleItem exampleItem) return;

            if (DataContext is not GroundLayerViewModel vm)
            {
                // 念のため閉じる
                if (mi.Parent is ContextMenu parentCm) parentCm.IsOpen = false;
                return;
            }

            // メニューを先に閉じてからダイアログ
            if (mi.Parent is ContextMenu cm) cm.IsOpen = false;

            var result = MessageBox.Show(
                this,
                $"計算例「{exampleItem.Display}」を読み込むと、現在の地盤入力は上書きされます。\n\n続行しますか？\n\n（Undo (Ctrl+Z) で復元可能です）",
                "計算例の読み込み",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (result != MessageBoxResult.OK) return;

            try
            {
                // 実行前に undo スタックへ現在状態を保存（既存パターンに合わせる）
                vm._undoManager.PushState(vm.GroundsInput.Select(x => x.DeepCopy()).ToList());

                // 実行
                exampleItem.Command?.Execute(null);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "計算例の読み込み中にエラーが発生しました。\n" + ex.Message,
                    "計算例の読み込み", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 左ペイン (土層+土質点) の最大化トグル:
        // 右ペインの DockWidth トグル (左 2 タブはアンドック・最大化方式に切替済み)
        private bool _rightMaximized;
        private WindowState _savedWindowState = WindowState.Normal;

        private static readonly System.Windows.GridLength _maximizedWidth = new(0.999, System.Windows.GridUnitType.Star);
        private static readonly System.Windows.GridLength _minimizedWidth = new(0.001, System.Windows.GridUnitType.Star);
        private static readonly System.Windows.GridLength _restoredHalfWidth = new(0.5, System.Windows.GridUnitType.Star);

        private void ButtonToggleRightMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (_rightMaximized)
            {
                if (LeftPaneGroup != null) LeftPaneGroup.DockWidth = _restoredHalfWidth;
                if (RightChartsPaneGroup != null) RightChartsPaneGroup.DockWidth = _restoredHalfWidth;
                this.WindowState = _savedWindowState;
                _rightMaximized = false;
            }
            else
            {
                _savedWindowState = this.WindowState;
                if (LeftPaneGroup != null) LeftPaneGroup.DockWidth = _minimizedWidth;
                if (RightChartsPaneGroup != null) RightChartsPaneGroup.DockWidth = _maximizedWidth;
                this.WindowState = WindowState.Maximized;
                _rightMaximized = true;
            }
            ButtonToggleRightMaximize.Content = _rightMaximized ? "元に戻す" : "右ペイン";
        }

        private PileDesign.Common.PaneFloatMaximizer? _paneMaximizer;

        private void InitializePaneMaximizer()
        {
            _paneMaximizer = new PileDesign.Common.PaneFloatMaximizer(dockingManager);
            _paneMaximizer.Register("GroundLayer", GroundLayerTab, ButtonMaximizeGroundLayer, "土層入力");
            _paneMaximizer.Register("GroundMass",  GroundMassTab,  ButtonMaximizeGroundMass,  "土質点入力");
        }
    }
}
