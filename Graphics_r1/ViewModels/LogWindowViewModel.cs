using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace PileDesign.ViewModels
{
    public partial class LogWindowViewModel : ObservableObject
    {
        private readonly List<string> _logLines = new();

        [ObservableProperty]
        private string _logText = string.Empty;

        [ObservableProperty]
        private bool _autoScroll = true;

        [ObservableProperty]
        private string _statusText = "Ready";

        public event EventHandler? ScrollToEndRequested;

        public LogWindowViewModel()
        {
        }

        public LogWindowViewModel(IEnumerable<string> initialLogs)
        {
            if (initialLogs != null)
            {
                _logLines.AddRange(initialLogs);
                UpdateLogText();
            }
        }

        public void AddLog(string message)
        {
            _logLines.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            UpdateLogText();

            if (AutoScroll)
            {
                ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
            }

            StatusText = $"{_logLines.Count} log entries";
        }

        public void AddLogs(IEnumerable<string> messages)
        {
            foreach (var message in messages)
            {
                _logLines.Add(message);
            }
            UpdateLogText();

            if (AutoScroll)
            {
                ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
            }

            StatusText = $"{_logLines.Count} log entries";
        }

        private void UpdateLogText()
        {
            LogText = string.Join(Environment.NewLine, _logLines);
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
                "Are you sure you want to clear all log entries?",
                "Clear Log",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _logLines.Clear();
                UpdateLogText();
                StatusText = "Log cleared";
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
