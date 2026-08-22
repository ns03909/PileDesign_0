using PileDesign.ViewModels;
using System;
using System.Windows;

namespace PileDesign.Views
{
    public partial class WelcomeDialog : Window
    {
        /// <summary>
        /// 選択結果。<see cref="WelcomeDialogResult.None"/> は閉じるだけで何も選ばなかった場合。
        /// </summary>
        public WelcomeDialogResult Result => ViewModel.Result;

        public WelcomeDialogViewModel ViewModel { get; }

        public WelcomeDialog()
        {
            InitializeComponent();

            // DataContext は XAML で設定していないので、ここで必ず入れる。
            // (未設定だと {Binding NewProjectCommand} 等が無言で効かず、ボタンが反応しない)
            ViewModel = new WelcomeDialogViewModel();
            DataContext = ViewModel;

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
