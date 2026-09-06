using PileDesign.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// 群杭沈下の入力ウィンドウ。
    ///
    /// もとはメイン画面の左ペイン (データタブ) の 1 タブだった。
    /// 他の解析条件 (基本設定・荷重・地盤・杭体) はすべて独立したウィンドウなのに
    /// ここだけタブで、リボンの群杭沈下解析ボタンも「群杭沈下タブで設定してください」と
    /// 別の場所を案内していた。揃えるためにウィンドウへ移した (2026-09-06)。
    ///
    /// DataContext は <see cref="MainWindowViewModel"/> (メイン画面と同じインスタンス)。
    /// 束縛先はタブだったときと同じで、入力の保存形式は変えていない。
    ///
    /// メイン画面の図を見ながら編集できるよう<b>モードレス</b>で開く
    /// (グリッドや荷重面は 3D ビューに形が出る)。
    /// </summary>
    public partial class GroupSettlementWindow : Window
    {
        private readonly MainWindow? _owner;
        private object _prevLoadingType;

        /// <summary>
        /// 表示の準備中は true。
        ///
        /// 荷重タイプのコンボは<b>開くたびに束縛の解決で SelectionChanged が出る</b>。
        /// これを利用者の操作として扱うと、ウィンドウを開いただけで
        /// 沈下用土層の表示が入り、開きたいタブも土層に切り替わってしまう
        /// (タブだったときは起動時に一度だけだったので目立たなかった)。
        /// </summary>
        private bool _initializing = true;

        public GroupSettlementWindow(MainWindow owner, MainWindowViewModel viewModel)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Owner = owner;
            _prevLoadingType = ComboBoxLoadingType.SelectedItem;
            Loaded += (_, __) =>
            {
                // 束縛が解決したあとの値を「前回値」にする
                _prevLoadingType = ComboBoxLoadingType.SelectedItem;
                _initializing = false;
                if (_pendingTab is { } t) { _pendingTab = null; ApplyTab(t); }
            };
        }

        /// <summary>
        /// XAML が開けることだけを確かめるテスト用。
        /// メイン画面も ViewModel も持たないので<b>編集のハンドラは何もしない</b>
        /// (実機では必ず上の生成口を使う)。
        ///
        /// StaticResource のキー誤りはビルドを通り、開いた瞬間に例外になるので、
        /// テストから開けるようにしてある。
        /// ViewModel を渡さないのは、テストで共有している STA スレッドに
        /// ViewModel のタイマーなどを残さないため (残すと後続のウィンドウのテストが落ちる)。
        /// </summary>
        internal GroupSettlementWindow()
        {
            InitializeComponent();
            _prevLoadingType = ComboBoxLoadingType.SelectedItem;
        }

        /// <summary>
        /// 指定のタブを選ぶ。実行できない理由を出す前に呼び、直す場所を見せる。
        ///
        /// 開いた直後は<b>読み込みが済んでから</b>選び直す。
        /// 表示前に選んでも、TabControl が最初の項目を選び直して上書きしてしまう。
        /// </summary>
        public void SelectTab(MainWindowViewModel.GroupSettlementInputTab tab)
        {
            if (!IsLoaded)
            {
                _pendingTab = tab;
                return;
            }
            ApplyTab(tab);
        }

        private MainWindowViewModel.GroupSettlementInputTab? _pendingTab;

        private void ApplyTab(MainWindowViewModel.GroupSettlementInputTab tab)
        {
            if (GroupPileTabControl == null) return;
            TabItem? target = tab switch
            {
                MainWindowViewModel.GroupSettlementInputTab.SoilLayers => TabItemSettlementSoilLayers,
                MainWindowViewModel.GroupSettlementInputTab.Grid => TabItemSettlementGrid,
                _ => TabItemLoadNonBeam,
            };
            if (target != null) GroupPileTabControl.SelectedItem = target;
        }

        // ── メイン画面から移したハンドラ (中身はそのまま) ──

        private void ComboBoxLoadingType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 開いたときの束縛解決では何もしない (利用者が選び直したときだけ効かせる)
            if (_initializing) return;

            _prevLoadingType ??= ComboBoxLoadingType.SelectedItem;

            // ComboBoxの選択変更後の内容を取得
            var comboBox = sender as ComboBox;
            var selectedItem = comboBox?.SelectedItem as string;

            var vm = this.DataContext as PileDesign.ViewModels.MainWindowViewModel;

            // 個別十字系・個別矩形系に切り替わった場合は RectLoads を自動生成で置換
            if (vm != null && (selectedItem == "個別十字" || selectedItem == "個別十字（基礎梁反力）"
                            || selectedItem == "個別矩形" || selectedItem == "個別矩形（基礎梁考慮）"))
            {
                // 既存の RectLoads (任意矩形等で入力済) があれば、上書き確認ダイアログを表示
                var existingRectLoads = vm.CurrentInputModel?.PileGroupSettlement?.RectLoads;
                int existingCount = existingRectLoads?.Count ?? 0;
                if (existingCount > 0)
                {
                    var prevName = _prevLoadingType as string ?? "(以前の荷重タイプ)";
                    string shapeDesc = (selectedItem == "個別矩形" || selectedItem == "個別矩形（基礎梁考慮）")
                        ? "杭頭ごとの正方形荷重"
                        : "杭頭ごとの十字形矩形荷重";
                    var msg = $"現在「{prevName}」で {existingCount} 件の矩形荷重が登録されています。\n\n" +
                              $"「{selectedItem}」へ切替えると、これらは破棄され、{shapeDesc}で上書きされます。\n\n" +
                              "切替えを続行しますか? (キャンセルで元の荷重タイプに戻ります)";
                    var result = PileDesign.Services.MessageService.Show(
                        msg, "荷重タイプ切替確認",
                        System.Windows.MessageBoxButton.OKCancel,
                        System.Windows.MessageBoxImage.Warning);
                    if (result != System.Windows.MessageBoxResult.OK)
                    {
                        // キャンセル: 前回値に戻す (SelectionChanged 再発火は短絡される)
                        if (_prevLoadingType != null)
                            comboBox.SelectedItem = _prevLoadingType;
                        return;
                    }
                }

                // UpdateSourceTrigger=LostFocus のためモデル側 LoadingType が
                // まだ古い値の可能性 → 先にソース更新してから再生成
                comboBox?.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
                vm.RebuildAutoCrossRectLoadsIfNeeded();
            }

            // 群杭表示
            if (vm != null) vm.IsSettlementGroundVisible = true;

            // 変更を確定し、前回値を更新
            _prevLoadingType = ComboBoxLoadingType.SelectedItem;

            _owner.UpdateWindow();
        }

        private void GroupPileTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tabControl = sender as TabControl;
            if (tabControl?.SelectedItem is TabItem selectedTab)
            {
                if (selectedTab.Header?.ToString() == "グリッド")
                {
                    // ViewModel取得
                    if (this.DataContext is PileDesign.ViewModels.MainWindowViewModel vm)
                    {
                        vm.IsGroupPileGridVisible = true;
                    }
                }

                if (selectedTab.Header?.ToString() == "荷重")
                {
                    // ViewModel取得
                    if (this.DataContext is PileDesign.ViewModels.MainWindowViewModel vm)
                    {
                        vm.IsSettlementLoadVisible = true;
                    }
                }
            }
        }

        // ── メイン画面と共通のハンドラ (実装はメイン画面のものをそのまま使う) ──
        // 同じ動き (行番号の採番・数値入力の作法・編集の確定) を二重に持たないための委譲。

        private void DataGrid_LoadingRow_Numbering(object sender, DataGridRowEventArgs e)
            => _owner?.DataGrid_LoadingRow_Numbering(sender, e);

        private void DataGridGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
            => _owner?.DataGridGrid_BeginningEdit(sender, e);

        private void DataGridRectLoads_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
            => _owner?.DataGridRectLoads_CellEditEnding(sender, e);

        private void DataGridSettlementSoilLayers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
            => _owner?.DataGridSettlementSoilLayers_CellEditEnding(sender, e);

        private void DataGridSoilPile_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
            => _owner?.DataGridSoilPile_CellEditEnding(sender, e);

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
            => _owner?.ContextMenu_Opened(sender, e);

        private void ExportCsvFromContextMenu_Click(object sender, RoutedEventArgs e)
            => _owner?.ExportCsvFromContextMenu_Click(sender, e);

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => _owner?.TextBox_PreviewMouseLeftButtonDown(sender, e);

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
            => _owner?.TextBox_GotFocus(sender, e);

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
            => _owner?.TextBox_KeyDown(sender, e);
    }
}
