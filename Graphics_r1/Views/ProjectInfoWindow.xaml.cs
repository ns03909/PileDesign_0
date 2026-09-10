using PileDesign.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// ProjectInfoWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class ProjectInfoWindow : Window
    {
        private bool _isClosingHandled = false;

        public ProjectInfoWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                if (DataContext is ProjectInfoViewModel vm)
                {
                    // 多重登録防止のため一度解除してから登録
                    vm.RequestClose -= ProjectInfoViewModel_RequestClose;
                    vm.RequestClose += ProjectInfoViewModel_RequestClose;
                }
            };
        }

        private void ProjectInfoViewModel_RequestClose(object sender, System.EventArgs e)
        {
            // すでに閉じ処理中なら何もしない
            if (_isClosingHandled) return;
            if (!this.IsLoaded || !this.IsVisible) return;
            _isClosingHandled = true;
            this.Close();
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

            if (DataContext is ProjectInfoViewModel vm)
            {
                vm.CancelCommand?.Execute(null);
            }
        }


        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox)?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // テキストボックスがフォーカスを持っていない場合、フォーカスを設定し、全テキストを選択
                textBox.Focus();
                e.Handled = true;
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
            }
        }
    }
}
