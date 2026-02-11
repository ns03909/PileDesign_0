using PileDesign.ViewModels;
using System;
using System.Windows;

namespace PileDesign.Views
{
    public partial class WelcomeDialog : Window
    {
        public WelcomeDialog()
        {
            InitializeComponent();
            Loaded += WelcomeDialog_Loaded;
        }

        private void WelcomeDialog_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is WelcomeDialogViewModel vm)
            {
                vm.RequestClose += ViewModel_RequestClose;
            }
        }

        private void ViewModel_RequestClose(object sender, EventArgs e)
        {
            if (IsLoaded && IsVisible)
            {
                Close();
            }
        }
    }
}
