using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace PileDesign.Views
{
    /// <summary>
    /// AutoIsFrontPilesWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class AutoIsFrontPilesWindow : Window
    {
        private readonly AutoIsFrontPileViewModel viewModel;

        // コンストラクタ
        public AutoIsFrontPilesWindow()
        {
            InitializeComponent();
            viewModel = new AutoIsFrontPileViewModel();
            DataContext = viewModel;
        }

        public class AutoIsFrontEventArgs : EventArgs
        {
            public double Angle { get; set; }
            public ObservableCollection<bool> IsChecked { get; set; }
        }

        public event EventHandler<AutoIsFrontEventArgs> AutoIsFrontPileCompleted;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            AutoIsFrontEventArgs args = new()
            {
                Angle = viewModel.Angle,
                IsChecked = viewModel.IsChecked,
            };

            AutoIsFrontPileCompleted?.Invoke(this, args);
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // キャンセルボタンのクリック時の処理を実装する
            Close();
        }

        private void TextBoxAngle_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (int.TryParse(TextBoxAngle.Text, out int result))
            {
                if (result <= 0)
                {
                    MessageBox.Show("0より大きな数値を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else if (result >= 90)
                {
                    MessageBox.Show("90より小さな数値を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // ビューモデルのプロパティに値を設定
                    viewModel.Angle = result;
                }
            }
            else
            {
                MessageBox.Show("数値を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
