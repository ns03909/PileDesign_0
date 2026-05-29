using PileDesign.ViewModels;
using System;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// FoundationBeamWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class FoundationBeamWindow : Window
    {
        private bool _isClosingHandled = false;

        public FoundationBeamWindow()
        {
            InitializeComponent();
            Loaded += FoundationBeamWindow_Loaded;
            Loaded += (_, _) => OkButton?.Focus();
        }

        private void FoundationBeamWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is FoundationBeamViewModel viewModel)
            {
                viewModel.RequestClose += (s, e2) =>
                {
                    // すでにクローズ処理中なら何もしない
                    if (_isClosingHandled) return;
                    _isClosingHandled = true;

                    if (this.IsLoaded && this.IsVisible)
                    {
                        this.Close();
                    }
                };

                // 初期化
                viewModel.Initialize();
            }
        }

        private void DeleteNodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is FoundationBeamViewModel viewModel)
            {
                viewModel.DeleteSelectedNode();
            }
        }

        private void DeleteBeamButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is FoundationBeamViewModel viewModel)
            {
                viewModel.DeleteSelectedBeam();
            }
        }
    }
}
