using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    public partial class LogWindowViewModel : ObservableObject, IDisposable
    {
        // 元となるログソース（参照を保持。ObservableCollection ならライブ更新を購読）
        private readonly Dictionary<string, IEnumerable<string>> _logSources = new();
        // 現在の購読先（カテゴリ切替時に解除するため保持）
        private INotifyCollectionChanged? _currentSourceSubscription;

        /// <summary>
        /// 表示中のログ行（仮想化 ListBox にバインド）。ソースが ObservableCollection の場合は
        /// CollectionChanged を購読して増分のみを追加するので、数万行でも軽快。
        /// </summary>
        public ObservableCollection<string> LogLines { get; } = [];

        [ObservableProperty]
        private string _statusText = "Ready";

        /// <summary>ログ種別の選択肢</summary>
        public ObservableCollection<string> LogCategories { get; } = [];

        [ObservableProperty]
        private string _selectedLogCategory;

        partial void OnSelectedLogCategoryChanged(string value) => ReloadFromCurrentSource();

        /// <summary>カテゴリが複数ある場合のみセレクタを表示</summary>
        public bool IsCategorySelectorVisible => LogCategories.Count > 1;

        public event EventHandler? ScrollToEndRequested;

        // ScrollIntoView を Add 毎に呼ぶと数千行で UI thread が飽和するため、
        // Dispatcher.Background プライオリティで coalesce (UI idle 時に 1 回だけ実行)。
        private bool _scrollPending;

        public LogWindowViewModel()
        {
        }

        /// <summary>単一ログソース（後方互換）</summary>
        public LogWindowViewModel(IEnumerable<string> initialLogs)
        {
            if (initialLogs != null)
            {
                _logSources["解析ログ"] = initialLogs;
                LogCategories.Add("解析ログ");
                _selectedLogCategory = "解析ログ";
                ReloadFromCurrentSource();
            }
        }

        /// <summary>複数ログソース。ObservableCollection を渡すとライブ更新される。</summary>
        public LogWindowViewModel(Dictionary<string, IEnumerable<string>> sources)
        {
            foreach (var kvp in sources)
            {
                _logSources[kvp.Key] = kvp.Value;
                LogCategories.Add(kvp.Key);
            }
            OnPropertyChanged(nameof(IsCategorySelectorVisible));

            if (LogCategories.Count > 0)
            {
                _selectedLogCategory = LogCategories[0];
                ReloadFromCurrentSource();
            }
        }

        private void ReloadFromCurrentSource()
        {
            // 旧 subscription があれば解除
            if (_currentSourceSubscription != null)
            {
                _currentSourceSubscription.CollectionChanged -= OnSourceCollectionChanged;
                _currentSourceSubscription = null;
            }

            LogLines.Clear();
            if (SelectedLogCategory != null && _logSources.TryGetValue(SelectedLogCategory, out var source))
            {
                foreach (var line in source) LogLines.Add(line);
                StatusText = $"{LogLines.Count} log entries";

                // ソースが ObservableCollection なら新規エントリをライブ追従
                if (source is INotifyCollectionChanged notify)
                {
                    _currentSourceSubscription = notify;
                    _currentSourceSubscription.CollectionChanged += OnSourceCollectionChanged;
                }

                RequestScrollToEnd();
            }
            else
            {
                StatusText = "Ready";
            }
        }

        private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 既に UI スレッド上ならディスパッチ不要 (バッチ追加時の二重ホップを回避)。
            // FlushLogsToUi 経由の Add は UI スレッドで発火するため、Dispatcher.BeginInvoke をスキップ。
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            if (dispatcher.CheckAccess())
            {
                ApplyCollectionChange(sender, e);
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => ApplyCollectionChange(sender, e)));
            }
        }

        private void ApplyCollectionChange(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        if (e.NewItems != null)
                            foreach (var x in e.NewItems)
                                if (x is string s) LogLines.Add(s);
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        LogLines.Clear();
                        if (sender is IEnumerable<string> src)
                            foreach (var s in src) LogLines.Add(s);
                        break;
                    default:
                        // Remove / Replace 系は解析ログでは想定外。フル再構築でフォールバック。
                        LogLines.Clear();
                        if (sender is IEnumerable<string> src2)
                            foreach (var s in src2) LogLines.Add(s);
                        break;
                }
                StatusText = $"{LogLines.Count} log entries";
                RequestScrollToEnd();
            }
            catch
            {
                // UI 更新失敗は非致命
            }
        }

        private void RequestScrollToEnd()
        {
            // 既に保留中ならスキップ。Background プライオリティで UI idle 時に 1 回だけ実行。
            // (旧: AutoScroll トグル制御。「カテゴリ切替時 / オープン時 / バルク追加時に末尾へ」を内部固定し UI を簡素化)
            if (_scrollPending) return;
            _scrollPending = true;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _scrollPending = false;
                ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        [RelayCommand]
        private void CopyLog(object? parameter)
        {
            // 選択行があれば選択のみ、なければ全件をコピー。
            // 全選択 (Ctrl+A 後) は SelectedItems の Cast 列挙が遅いので LogLines を直接使う。
            IEnumerable<string> source;
            int selectedCount = 0;
            int totalCount = LogLines.Count;
            if (parameter is IList list && list.Count > 0)
            {
                if (list.Count >= totalCount)
                {
                    source = LogLines;
                    selectedCount = totalCount;
                }
                else
                {
                    source = list.Cast<string>();
                    selectedCount = list.Count;
                }
            }
            else
            {
                source = LogLines;
            }

            if (totalCount == 0) return;

            var sb = new StringBuilder();
            bool first = true;
            foreach (var s in source)
            {
                if (!first) sb.Append(Environment.NewLine);
                sb.Append(s);
                first = false;
            }
            if (sb.Length == 0) return;
            Clipboard.SetText(sb.ToString());
            StatusText = selectedCount > 0
                ? $"{selectedCount} 行をクリップボードにコピーしました"
                : "ログをクリップボードにコピーしました";
        }

        /// <summary>
        /// 全件コピー (選択不要)。Ctrl+Shift+C にバインド。Ctrl+A→Ctrl+C より高速。
        /// </summary>
        [RelayCommand]
        private void CopyAllLog()
        {
            if (LogLines.Count == 0) return;
            var sb = new StringBuilder();
            bool first = true;
            foreach (var s in LogLines)
            {
                if (!first) sb.Append(Environment.NewLine);
                sb.Append(s);
                first = false;
            }
            Clipboard.SetText(sb.ToString());
            StatusText = $"全 {LogLines.Count} 行をクリップボードにコピーしました";
        }

        [RelayCommand]
        private void ExportLog()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|All Files (*.*)|*.*",
                    DefaultExt = ".txt",
                    FileName = $"AnalysisLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllLines(dialog.FileName, LogLines, Encoding.UTF8);
                    StatusText = $"Log exported to: {Path.GetFileName(dialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageService.Show(
                    $"Error exporting log:\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusText = "Export failed";
            }
        }

        public void Dispose()
        {
            if (_currentSourceSubscription != null)
            {
                _currentSourceSubscription.CollectionChanged -= OnSourceCollectionChanged;
                _currentSourceSubscription = null;
            }
        }
    }
}
