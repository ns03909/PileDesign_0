using PileDesign.ViewModels;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// FundamentalWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class FundamentalWindow : Window
    {
        private bool _isClosingHandled = false;

        // コンストラクタ
        public FundamentalWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                if (DataContext is FundamentalViewModel vm)
                {
                    // 多重登録防止のため一度解除してから登録
                    vm.RequestClose -= FundamentalViewModel_RequestClose;
                    vm.RequestClose += FundamentalViewModel_RequestClose;
                }
            };
        }

        private void FundamentalViewModel_RequestClose(object sender, System.EventArgs e)
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

            if (DataContext is FundamentalViewModel vm)
            {
                vm.CancelCommand?.Execute(null);
            }
        }


        // モデル化オプション各項目の「ヘルプ」リンクは MaterialOptionRadioPair コントロール側で処理する
        // （HelpAnchor プロパティ → MainWindowViewModel.OpenHelpWindowAt）。

        private static void RestorePreviousPropertyValues(FundamentalViewModel viewModel)
        {
            // 全てのプロパティを前回の値に戻す
            // 未実装: 以前のプロパティ値の復元
        }

        private void TextBoxLoadCombinationFactor_TextInput(object sender, TextCompositionEventArgs e)
        {
            TextBox textBox = (TextBox)sender;

            // 現在のテキストボックスの内容と新しい入力を結合して、数値に変換できるか確認
            string newText = textBox.Text + e.Text;
            if (double.TryParse(newText, out double result))
            {
                // 数値が 0.0 以上 1.0 以下の範囲内でない場合、処理済みにする
                if (result < 0.5 || result > 1.0)
                {
                    e.Handled = false;
                }
            }
            else
            {
                // 数値に変換できない場合も処理済みにする
                e.Handled = true;
            }
        }

        private void DataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // DataGridの高さを行数に合わせて調整する
            if (sender is DataGrid grid && grid.Items.Count > 0)
            {
                double rowHeight = grid.RowHeight;
                int rowCount = grid.Items.Count;
                double totalHeight = rowHeight * rowCount;
                grid.Height = totalHeight;
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // テキストボックスがフォーカスを持っていない場合、フォーカスを設定し、全テキストを選択
                textBox.Focus();
                e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox)
                {
                    var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                    binding?.UpdateSource();
                }
            }
        }

        private void ComboBoxSeismicGrade_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}

