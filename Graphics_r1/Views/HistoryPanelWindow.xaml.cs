using PileDesign.Common.Undo;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PileDesign.Views
{
    /// <summary>
    /// 編集履歴パネル (D.16) — UndoManager のスナップショット履歴を一覧表示し、
    /// クリックで任意の地点へジャンプ可能。
    ///
    /// MainWindowViewModel の _undoManager と直接やりとりする。
    /// HistoryChanged イベントを購読してリストを自動更新。
    /// </summary>
    public partial class HistoryPanelWindow : Window
    {
        private readonly MainWindowViewModel _vm;
        private readonly UndoManager _undoManager;
        private readonly ObservableCollection<HistoryRow> _rows = new();
        private bool _suppressSelectionChanged;

        public HistoryPanelWindow(MainWindowViewModel vm, UndoManager undoManager)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _undoManager = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
            InitializeComponent();
            HistoryListBox.ItemsSource = _rows;

            _undoManager.HistoryChanged += OnHistoryChanged;
            Closed += (_, __) => _undoManager.HistoryChanged -= OnHistoryChanged;
            Loaded += (_, __) => Refresh();
        }

        private void OnHistoryChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

        private void Refresh()
        {
            _suppressSelectionChanged = true;
            try
            {
                _rows.Clear();
                int total = _undoManager.History.Count;
                int curr = _undoManager.CurrentIndex;
                for (int i = 0; i < total; i++)
                {
                    var entry = _undoManager.History[i];
                    bool isCurrent = i == curr;
                    bool isFuture = i > curr; // Redo 可能領域 (薄く表示)
                    _rows.Add(new HistoryRow
                    {
                        Index = i,
                        IndexLabel = $"{i + 1}.",
                        Title = entry.Description ?? "編集",
                        Description = null, // 副題は不要 (Title に操作名が入るため)
                        TimeText = entry.Timestamp.ToString("HH:mm:ss"),
                        IsCurrentVisibility = isCurrent ? Visibility.Visible : Visibility.Collapsed,
                        FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                        TitleBrush = isFuture ? Brushes.Gray : Brushes.Black,
                    });
                }

                if (curr >= 0 && curr < _rows.Count)
                    HistoryListBox.SelectedIndex = curr;

                SubText.Text = total == 0
                    ? "履歴なし"
                    : $"{total} 件の履歴 / 現在 {curr + 1} 番目";
            }
            finally
            {
                _suppressSelectionChanged = false;
            }
        }

        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged) return;
            if (HistoryListBox.SelectedItem is not HistoryRow row) return;
            if (row.Index == _undoManager.CurrentIndex) return;

            _undoManager.JumpToIndex(row.Index);
            _vm.ApplyCurrentUndoSnapshot();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }

    /// <summary>HistoryListBox の DataTemplate バインディング用。</summary>
    public class HistoryRow
    {
        public int Index { get; set; }
        public string IndexLabel { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string TimeText { get; set; } = "";
        public Visibility IsCurrentVisibility { get; set; }
        public FontWeight FontWeight { get; set; }
        public Brush TitleBrush { get; set; } = Brushes.Black;
    }
}
