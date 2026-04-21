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
        private bool _autoScroll = true;

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
            if (_selectedLogCategory != null && _logSources.TryGetValue(_selectedLogCategory, out var source))
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
            // UI スレッドにマーシャリング
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
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
                    if (AutoScroll) RequestScrollToEnd();
                }
                catch
                {
                    // UI 更新失敗は非致命
                }
            }));
        }

        private void RequestScrollToEnd()
        {
            if (AutoScroll)
            {
                ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        [RelayCommand]
        private void CopyLog(object? parameter)
        {
            // 選択行があれば選択のみ、なければ全件をコピー
            IEnumerable<string> source;
            int selectedCount = 0;
            if (parameter is IList list && list.Count > 0)
            {
                source = list.Cast<string>();
                selectedCount = list.Count;
            }
            else
            {
                source = LogLines;
            }

            if (!source.Any()) return;

            var text = string.Join(Environment.NewLine, source);
            Clipboard.SetText(text);
            StatusText = selectedCount > 0
                ? $"{selectedCount} 行をクリップボードにコピーしました"
                : "ログをクリップボードにコピーしました";
        }

        [RelayCommand]
        private void ClearLog()
        {
            var result = MessageBox.Show(
                "ログ表示をクリアしますか？（元データは残ります）",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                LogLines.Clear();
                StatusText = "クリアしました";
            }
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
                MessageBox.Show(
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
