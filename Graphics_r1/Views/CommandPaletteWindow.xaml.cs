using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PileDesign.Services;

namespace PileDesign.Views
{
    /// <summary>
    /// コマンドパレット (C.9) — Ctrl+Shift+P で開き、機能名で検索→Enter で実行。
    /// 列挙はリフレクションではなく手動カタログ (CommandCatalog) で管理:
    ///   - 機械的な列挙だと表示名/順序が制御しづらい
    ///   - パラメータが必要なコマンドや Window 起動など、種類が多様
    ///   - 「ユーザに見せたい」コマンドのみキュレーションできる
    /// </summary>
    public partial class CommandPaletteWindow : Window
    {
        private readonly List<CommandItem> _allCommands;
        private readonly ObservableCollection<CommandItem> _filtered = new();

        public CommandPaletteWindow(MainWindowViewModel mainVm, MainWindow mainWindow)
        {
            InitializeComponent();
            _allCommands = CommandCatalog.Build(mainVm, mainWindow, this);
            ResultListBox.ItemsSource = _filtered;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyFilter("");
            SearchTextBox.Focus();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter(SearchTextBox.Text);

        private void ApplyFilter(string query)
        {
            _filtered.Clear();
            IEnumerable<CommandItem> matches = string.IsNullOrWhiteSpace(query)
                ? _allCommands
                : _allCommands.Where(c => MatchesQuery(c, query));
            foreach (var c in matches.Take(50))
                _filtered.Add(c);
            if (_filtered.Count > 0)
                ResultListBox.SelectedIndex = 0;
            HintText.Text = $"{_filtered.Count} 件 / {_allCommands.Count} 件中  —  ↑↓ で選択、Enter で実行";
        }

        /// <summary>
        /// 大文字小文字を無視した部分一致 + Title / Description / Tags のいずれかにヒット。
        /// 全角スペースで区切った AND 条件にも対応。
        /// </summary>
        private static bool MatchesQuery(CommandItem c, string query)
        {
            var tokens = query.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
            {
                bool hit = c.Title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                        || (c.Description?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                        || (c.Tags?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
                if (!hit) return false;
            }
            return true;
        }

        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 互換のため残す。実体は Window_PreviewKeyDown で集約処理。
        }

        private void ResultListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => ExecuteSelected();

        private void ResultListBox_KeyDown(object sender, KeyEventArgs e)
        {
            // 互換のため残す。実体は Window_PreviewKeyDown で集約処理。
        }

        /// <summary>
        /// Window レベルで ↑↓ Enter Esc を捕捉する。フォーカスが TextBox / ListBox /
        /// その他のどこにあってもキーが効くようにする。
        ///
        /// IME 変換中 (Key.ImeProcessed) の場合は ImeProcessedKey を見て本来のキーを取得する。
        /// IME 変換中の Enter は確定操作なので無視（誤発火防止）。
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // IME で変換候補ウィンドウが開いている場合、Enter は確定。Up/Down は候補移動。
            // ImeProcessed の Enter は SearchTextBox に流して候補確定させる。
            // ImeProcessed の Up/Down も同様（候補リスト操作）。
            if (e.Key == Key.ImeProcessed) return;

            if (e.Key == Key.Down)
            {
                if (_filtered.Count > 0)
                    ResultListBox.SelectedIndex = Math.Min(ResultListBox.SelectedIndex + 1, _filtered.Count - 1);
                ResultListBox.ScrollIntoView(ResultListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (_filtered.Count > 0)
                    ResultListBox.SelectedIndex = Math.Max(ResultListBox.SelectedIndex - 1, 0);
                ResultListBox.ScrollIntoView(ResultListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ExecuteSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void ExecuteSelected()
        {
            if (ResultListBox.SelectedItem is not CommandItem item) return;

            // 今は実行できないコマンドはパレットからも実行しない。
            // 以前は Action を直接呼んでいたため、リボンでは灰色のコマンドが
            // パレット上では有効に見え、押すと前提不足のダイアログで叱られていた。
            if (!item.CanRun)
            {
                MessageService.Show(
                    $"「{item.Title}」は今は実行できません。\n" +
                    "リボンの同じボタンにカーソルを合わせると、必要な条件が表示されます。",
                    "コマンドパレット", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 先に閉じてから実行 (実行先が Window を開くなら主役を譲る)
            Close();
            try
            {
                item.Action?.Invoke();
            }
            catch (Exception ex)
            {
                MessageService.Show($"コマンド実行に失敗しました:\n{ex.Message}", "コマンドパレット",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>パレットに表示する 1 コマンド。</summary>
    public class CommandItem
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? Shortcut { get; set; }
        public string? Tags { get; set; }
        public Action? Action { get; set; }

        /// <summary>
        /// 元のコマンド。持っている場合は CanExecute で実行可否を判定する。
        /// ウィンドウを直接開くだけの項目など、コマンドが無いものは常に実行可。
        /// </summary>
        public ICommand? Command { get; set; }

        /// <summary>今このコマンドを実行できるか。一覧の淡色表示にも使う。</summary>
        public bool CanRun => Command?.CanExecute(null) ?? true;
    }

    /// <summary>
    /// コマンドカタログ。MainWindowViewModel と MainWindow の参照を受け取り、
    /// 主要操作の Action を構築する。新コマンドはここに追記する。
    /// </summary>
    public static class CommandCatalog
    {
        public static List<CommandItem> Build(MainWindowViewModel vm, MainWindow mainWindow, Window paletteOwner)
        {
            var list = new List<CommandItem>();

            // ── ファイル ──────────────────────────────────────────────
            Add(list, "新規ファイル", "新しい入力モデルを作成", "Ctrl+N", "new file 新規",
                vm.NewInputModelFileCommand);
            Add(list, "ファイルを開く", "既存の入力モデルファイルを開く", null, "open file 開く",
                vm.OpenInputModelFileCommand);
            Add(list, "ファイル保存", "現在のモデルを上書き保存", "Ctrl+S", "save file 保存",
                vm.SaveInputModelFileCommand);
            Add(list, "ファイル名を付けて保存", "別名で保存", "Ctrl+Shift+S", "save as 別名",
                vm.SaveInputModelFileAsCommand);
            Add(list, "Word 出力ウィンドウを開く", "Word ドキュメント出力", null, "docx word 出力 report",
                vm.OpenDocxOutputWindowCommand);

            // ── 解析 ──────────────────────────────────────────────────
            Add(list, "水平解析を開く", "水平解析ウィンドウ", "F5", "horizontal lateral analysis 水平 解析",
                vm.OpenLateralLoadAnalysisWindowCommand);
            Add(list, "単杭沈下解析を開く", "単杭沈下解析ウィンドウ", "F6", "settlement 沈下 解析 単杭",
                vm.OpenSettlementWindowCommand);
            Add(list, "単杭沈下解析（基礎梁考慮）を開く", "基礎梁の剛性を考慮した単杭沈下解析", "F7", "vertical beam 鉛直 解析 基礎梁 単杭沈下",
                vm.OpenVerticalBeamCalculationCommand);
            Add(list, "群杭沈下解析", "群杭沈下解析（一般）", "F8", "group pile settlement 群杭",
                vm.PileGroupSettlementAnalysisCommand);
            Add(list, "杭要素分割ウィンドウ", "杭の要素分割設定", "F4", "element division 要素 分割",
                vm.OpenElementDivisionWindowCommand);

            // ── 入力ウィンドウ ────────────────────────────────────────
            Add(list, "基本条件 ウィンドウ", "Z=0 標高など基本設定", null, "fundamental basic 基本",
                vm.OpenFundamentalWindowCommand);
            Add(list, "地盤 ウィンドウ", "地盤入力ウィンドウ", null, "ground soil 地盤",
                vm.OpenGroundWindowCommand);
            Add(list, "杭体 ウィンドウ", "杭体（断面）の入力", null, "pile body 杭体 断面",
                vm.OpenPileBodyWindowCommand);
            Add(list, "荷重ケース ウィンドウ", "荷重ケース・組合せ設定", null, "load case 荷重",
                vm.OpenLoadCaseWindowCommand);

            // ── 表示 ──────────────────────────────────────────────────
            Add(list, "表示: ズームフィット", "全体が見える倍率に調整", null, "zoom fit ズーム",
                vm.ZoomFitCommand);
            Add(list, "表示: 平面図", "上から見下ろす視点", null, "top view plan 平面",
                vm.ViewXYPlaneCommand);
            Add(list, "表示: アイソメ", "等角投影視点", null, "isometric iso アイソ",
                vm.ViewIsometricCommand);

            // ── ダッシュボード / ヘルプ ──────────────────────────────
            Add(list, "解析結果 ダッシュボード", "解析モデル / 統計をカード表示", null, "dashboard summary 結果 ダッシュボード",
                () =>
                {
                    var w = new ResultDashboardWindow(vm) { Owner = mainWindow };
                    w.ShowDialog();
                });
            Add(list, "ショートカット一覧", "キーボードショートカット表", null, "shortcut keyboard ショートカット",
                vm.OpenShortcutKeysWindowCommand);
            Add(list, "ヘルプを開く", "アプリのヘルプ HTML", null, "help ヘルプ",
                vm.OpenHelpWindowCommand);
            Add(list, "クイックスタートを開く", "入力から解析までの流れ", null, "quickstart 入門 使い方 はじめて",
                vm.OpenQuickStartHelpCommand);
            Add(list, "バージョン情報", "バージョンと更新履歴を確認", null, "version about バージョン 更新履歴 リリースノート",
                vm.OpenAboutWindowCommand);

            // ── Undo / Redo ───────────────────────────────────────────
            Add(list, "元に戻す", "直前の編集を取り消し", "Ctrl+Z", "undo 元 戻す",
                vm.UndoCommand);
            Add(list, "やり直し", "取り消した編集をやり直す", "Ctrl+Y", "redo やり直し",
                vm.RedoCommand);
            Add(list, "編集履歴 パネル", "Undo/Redo 履歴の一覧表示・任意ジャンプ", null, "history undo redo 履歴",
                () =>
                {
                    var w = new HistoryPanelWindow(vm, vm.UndoManager) { Owner = mainWindow };
                    w.Show();
                });

            return list;
        }

        private static void Add(List<CommandItem> list, string title, string desc, string? shortcut, string tags, Action action)
            => list.Add(new CommandItem
            {
                Title = title,
                Description = desc,
                Shortcut = shortcut,
                Tags = tags,
                Action = action,
            });

        /// <summary>
        /// コマンドをそのまま渡す形。実行可否 (CanExecute) がパレットにも効く。
        /// ボタン・キーボード・パレットで条件が食い違わないよう、こちらを使うこと。
        /// </summary>
        private static void Add(List<CommandItem> list, string title, string desc, string? shortcut, string tags, ICommand? command)
            => list.Add(new CommandItem
            {
                Title = title,
                Description = desc,
                Shortcut = shortcut,
                Tags = tags,
                Command = command,
                Action = () => command?.Execute(null),
            });
    }
}
