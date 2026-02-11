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
        OpenSample
    }

    public partial class WelcomeDialogViewModel : ObservableObject
    {
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
