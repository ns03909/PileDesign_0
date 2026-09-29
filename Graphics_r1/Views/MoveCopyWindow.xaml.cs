using PileDesign.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PileDesign.Services;


namespace PileDesign.Views
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
            public bool Cancel { get; set; }
            public bool IsMove { get; set; }
            public bool IsCopy { get; set; }
            public double DX { get; set; }
            public double DY { get; set; }
            public double DZ { get; set; }
            public int RepetitionNumber { get; set; }
            public bool IsInputNodesIncluded { get; set; }
            public bool IsPileLayoutIncluded { get; set; }
            public bool IsBeamsIncluded { get; set; }
        }

        public event EventHandler<MoveCopyEventArgs> MoveCopyCompleted;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitNumericInputs()) return;
            // 対象が一つも選択されていない場合のチェック
            if (!viewModel.IsInputNodesIncluded && !viewModel.IsPileLayoutIncluded && !viewModel.IsBeamsIncluded)
            {
                MessageService.Show("対象 (一般節点 / 杭配置 / 梁要素) が一つも選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // DX/DY/DZ の検証
            var inputProblem = MoveCopyValidation.DescribeProblem([], viewModel.DX, viewModel.DY,
                viewModel.DZ, viewModel.IsCopySelected ? viewModel.RepetitionNumber : 1);
            if (inputProblem != null)
            {
                MessageService.Show(inputProblem, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (viewModel.DX == 0 && viewModel.DY == 0 && viewModel.DZ == 0)
            {
                MessageService.Show("DX, DY, DZのすべてが0です。値を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validate against the current input before notifying the owner.
            var mainVm = Application.Current.MainWindow?.DataContext;
            if (mainVm is MainWindowViewModel typedVm)
            {
                var destinationProblem = typedVm.DescribeMoveCopyProblem(new MoveCopyEventArgs
                {
                    IsCopy = viewModel.IsCopySelected, DX = viewModel.DX, DY = viewModel.DY, DZ = viewModel.DZ,
                    RepetitionNumber = viewModel.RepetitionNumber,
                    IsInputNodesIncluded = viewModel.IsInputNodesIncluded,
                    IsPileLayoutIncluded = viewModel.IsPileLayoutIncluded, IsBeamsIncluded = viewModel.IsBeamsIncluded
                });
                if (destinationProblem != null)
                {
                    MessageService.Show(destinationProblem, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            MoveCopyEventArgs args = new()
            {
                IsMove = viewModel.IsMoveSelected,
                IsCopy = viewModel.IsCopySelected,
                DX = viewModel.DX,
                DY = viewModel.DY,
                DZ = viewModel.DZ,
                RepetitionNumber = viewModel.RepetitionNumber,
                IsInputNodesIncluded = viewModel.IsInputNodesIncluded,
                IsPileLayoutIncluded = viewModel.IsPileLayoutIncluded,
                IsBeamsIncluded = viewModel.IsBeamsIncluded
            };

            MoveCopyCompleted?.Invoke(this, args);
            if (args.Cancel) return;
            viewModel.ResetStatus();
            Close();
        }

        internal bool CommitNumericInputs()
        {
            var fields = viewModel.IsCopySelected
                ? new[] { DeltaXInput, DeltaYInput, DeltaZInput, TextBoxRepetitionNumber }
                : new[] { DeltaXInput, DeltaYInput, DeltaZInput };
            foreach (var field in fields)
                field.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            foreach (var field in fields)
                if (Validation.GetHasError(field))
                {
                    field.Focus();
                    return false;
                }
            return true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // キャンセルボタンのクリック時の処理を実装する
            viewModel.ResetStatus();
            Close();
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

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MoveCopyViewModel viewModel)
            {
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelButton_Click(null, new RoutedEventArgs());
                return;
            }
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                OkButton_Click(null, new RoutedEventArgs());
            }
        }
    }
}
