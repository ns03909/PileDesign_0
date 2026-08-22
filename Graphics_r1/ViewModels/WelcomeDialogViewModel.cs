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

        [RelayCommand]
        private void ShowQuickStart()
        {
            Result = WelcomeDialogResult.ShowQuickStart;
            SavePreference();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void SavePreference()
        {
            if (DoNotShowAgain)
            {
                Properties.Settings.Default.ShowWelcomeDialog = false;
                Properties.Settings.Default.Save();
            }
        }
    }
}
