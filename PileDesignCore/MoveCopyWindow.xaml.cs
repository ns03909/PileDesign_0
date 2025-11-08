using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PileDesignCore
{
    /// <summary>
    /// MoveCopyWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MoveCopyWindow : Window
    {
        private readonly MoveCopyViewModel viewModel;

        // コンストラクタ
        public MoveCopyWindow()
        {
            InitializeComponent();
            viewModel = new MoveCopyViewModel();
            DataContext = viewModel;
        }

        public class MoveCopyEventArgs : EventArgs
        {
            public bool IsMove { get; set; }
            public bool IsCopy { get; set; }
            public double DX { get; set; }
            public double DY { get; set; }
            public int RepetitionNumber { get; set; }
        }

        public event EventHandler<MoveCopyEventArgs> MoveCopyCompleted;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            MoveCopyEventArgs args = new MoveCopyEventArgs();
            args.IsMove = viewModel.IsMoveSelected;
            args.IsCopy = viewModel.IsCopySelected;
            args.DX = viewModel.DX;
            args.DY = viewModel.DY;
            args.RepetitionNumber = viewModel.RepetitionNumber;

            MoveCopyCompleted?.Invoke(this, args);
            viewModel.ResetStatus();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // キャンセルボタンのクリック時の処理を実装する
            viewModel.ResetStatus();
            Close();
        }

        private void TextBoxRepetitionNumber_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (int.TryParse(TextBoxRepetitionNumber.Text, out int result))
            {
                if (result <= 0)
                {
                    MessageBox.Show("自然数を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // ビューモデルのプロパティに値を設定
                    viewModel.RepetitionNumber = result;
                }
            }
            else
            {
                MessageBox.Show("自然数を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    //public class BooleanToVisibilityConverter : IValueConverter
    //{
    //    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        if (value is bool boolValue && boolValue)
    //        {
    //            return Visibility.Visible;
    //        }
    //        else
    //        {
    //            return Visibility.Collapsed;
    //        }
    //    }

    //    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
}
