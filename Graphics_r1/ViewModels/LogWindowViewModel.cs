using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace PileDesign.ViewModels
{
    public partial class LogWindowViewModel : ObservableObject
    {
        private readonly Dictionary<string, List<string>> _logSources = new();

        [ObservableProperty]
        private string _logText = string.Empty;

        [ObservableProperty]
        private bool _autoScroll = true;

        [ObservableProperty]
        private string _statusText = "Ready";

        /// <summary>ログ種別の選択肢</summary>
        public ObservableCollection<string> LogCategories { get; } = [];

        [ObservableProperty]
        private string _selectedLogCategory;

        partial void OnSelectedLogCategoryChanged(string value) => UpdateLogText();

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
                var lines = initialLogs.ToList();
                _logSources["解析ログ"] = lines;
                LogCategories.Add("解析ログ");
                _selectedLogCategory = "解析ログ";
                UpdateLogText();
            }
        }

        /// <summary>複数ログソース</summary>
        public LogWindowViewModel(Dictionary<string, IEnumerable<string>> sources)
        {
            foreach (var kvp in sources)
            {
                _logSources[kvp.Key] = kvp.Value.ToList();
                LogCategories.Add(kvp.Key);
            }
            OnPropertyChanged(nameof(IsCategorySelectorVisible));

            if (LogCategories.Count > 0)
            {
                _selectedLogCategory = LogCategories[0];
                UpdateLogText();
            }
        }

        private void UpdateLogText()
        {
            if (_selectedLogCategory != null && _logSources.TryGetValue(_selectedLogCategory, out var lines))
            {
                LogText = string.Join(Environment.NewLine, lines);
                StatusText = $"{lines.Count} log entries";
            }
            else
            {
                LogText = string.Empty;
                StatusText = "Ready";
            }
        }

        [RelayCommand]
        private void CopyLog()
        {
            if (!string.IsNullOrEmpty(LogText))
            {
                Clipboard.SetText(LogText);
                StatusText = "ログをクリップボードにコピーしました";
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            var result = MessageBox.Show(
                "ログをクリアしますか？",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (_selectedLogCategory != null && _logSources.ContainsKey(_selectedLogCategory))
                {
                    _logSources[_selectedLogCategory].Clear();
                }
                UpdateLogText();
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
                    File.WriteAllText(dialog.FileName, LogText, Encoding.UTF8);
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
    }
}
