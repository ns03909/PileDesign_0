using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace PileDesign.ViewModels
{
    public enum WelcomeDialogResult
    {
        None,
        NewProject,
        OpenExisting,
        OpenSample,
        ShowQuickStart
    }

    public partial class WelcomeDialogViewModel : ObservableObject
    {
        /// <summary>XAMLからバージョンを参照するためのシングルトン</summary>
        public static WelcomeDialogViewModel Instance { get; } = new();

        public string AppVersion => $"v{MainWindowViewModel.AppVersion}";

        [ObservableProperty]
        private bool _doNotShowAgain;

        public WelcomeDialogResult Result { get; private set; } = WelcomeDialogResult.None;

        public event EventHandler RequestClose;

        [RelayCommand]
        private void NewProject()
        {
            Result = WelcomeDialogResult.NewProject;
            SavePreference();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OpenExisting()
        {
            Result = WelcomeDialogResult.OpenExisting;
            SavePreference();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OpenSample()
        {
            Result = WelcomeDialogResult.OpenSample;
            SavePreference();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 右上の × (と Esc)。何も選ばずに閉じる (起動直後の空プロジェクトのまま。「新規プロジェクト」と同じ動き)。
        /// </summary>
        [RelayCommand]
        private void Close()
        {
            Result = WelcomeDialogResult.None;
            SavePreference();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void ShowQuickStart()
        {
            Result = WelcomeDialogResult.ShowQuickStart;
            SavePreference();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private bool _preferenceSaved;

        /// <summary>
        /// 「次回から表示しない」を設定に書く (チェックしたときだけ)。どの閉じ方でも 1 回だけ。
        /// 以前はボタンを押したときだけ書いていて、Esc で閉じるとチェックが効かなかった。
        /// 画面は閉じるとき (Closing) にも呼ぶ。
        /// </summary>
        internal void SavePreference()
        {
            if (_preferenceSaved) return;
            _preferenceSaved = true;
            if (DoNotShowAgain)
            {
                Properties.Settings.Default.ShowWelcomeDialog = false;
                Properties.Settings.Default.Save();
            }
        }
    }
}
