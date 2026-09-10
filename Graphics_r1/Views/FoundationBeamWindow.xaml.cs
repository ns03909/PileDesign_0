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

        // × で閉じたときも「キャンセル」と同じ扱いにする。
        //
        // このウィンドウは入力の実体をそのまま編集し、戻すのはキャンセルだけなので、
        // キャンセルを通らずに閉じると編集が残ってしまう。
        // 地盤・荷重ケース・杭体・杭断面・杭頭・単杭沈下は同じ形で塞いである。
        //
        // OK で閉じるときは RequestClose 側が先に _isClosingHandled を立てるので、
        // ここは素通りする (OK をキャンセルで打ち消してしまわない)。
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;

            if (DataContext is FoundationBeamViewModel vm)
            {
                vm.CancelCommand?.Execute(null);
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
