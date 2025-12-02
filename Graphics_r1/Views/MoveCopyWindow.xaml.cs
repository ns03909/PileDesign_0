using DocumentFormat.OpenXml.Vml.Spreadsheet;
using PileDesign.ViewModels;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


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
            public bool IsMove { get; set; }
            public bool IsCopy { get; set; }
            public double DX { get; set; }
            public double DY { get; set; }
            public int RepetitionNumber { get; set; }
        }

        public event EventHandler<MoveCopyEventArgs> MoveCopyCompleted;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // DX/DY の検証
            if (viewModel.DX == 0 && viewModel.DY == 0)
            {
                MessageBox.Show("DXとDYの両方が0です。値を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // MainWindowViewModel の存在チェック（ConfirmDeleteAnalysisModel 呼び出しまたはフォールバック処理）
            var mainVm = Application.Current.MainWindow?.DataContext;
            if (mainVm != null)
            {
                try
                {
                    bool needConfirm = false;
                    // フラグプロパティを安全に参照
                    foreach (var propName in new[]
                    {
                        "IsHorizontalAnalysisDone",
                        "IsVerticalAnalysisDone",
                        "IsGroupPileSettlementAnalysisDone",
                        "IsAnalysisResultVisible"
                    })
                    {
                        var pi = mainVm.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pi != null && pi.PropertyType == typeof(bool))
                        {
                            var val = (bool?)pi.GetValue(mainVm);
                            if (val == true) { needConfirm = true; break; }
                        }
                    }

                    if (needConfirm)
                    {
                        // まずは MainWindowViewModel に共通ヘルパがあるか探して呼ぶ（public/private問わず）
                        var mi = mainVm.GetType().GetMethod("ConfirmDeleteAnalysisModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mi != null)
                        {
                            // シグネチャにデフォルト引数がある想定だが、リフレクションでは引数を渡す
                            object[] argsForHelper = new object[] { "解析結果を削除します。よろしいですか？", "確認", MessageBoxImage.Question, true };
                            var ret = mi.Invoke(mainVm, argsForHelper);
                            if (ret is bool b && !b)
                            {
                                // ユーザーがキャンセルしたので処理中止
                                return;
                            }
                        }
                        else
                        {
                            // ヘルパが見つからない場合はフォールバックで同等の確認とリセットを実行
                            var res = MessageBox.Show("解析結果を削除します。よろしいですか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (res != MessageBoxResult.Yes) return;

                            // フラグをリセット（リフレクションで安全にセット）
                            foreach (var propName in new[]
                            {
                                "IsElementSplit",
                                "IsHorizontalAnalysisDone",
                                "IsVerticalAnalysisDone",
                                "IsGroupPileSettlementAnalysisDone",
                                "IsAnalysisResultVisible"
                            })
                            {
                                var pi = mainVm.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (pi != null && pi.PropertyType == typeof(bool) && pi.CanWrite)
                                {
                                    pi.SetValue(mainVm, false);
                                }
                            }

                            // CurrentModel = null
                            var cmProp = mainVm.GetType().GetProperty("CurrentModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (cmProp != null && cmProp.CanWrite)
                                cmProp.SetValue(mainVm, null);

                            // UpdateWindowAction?.Invoke()
                            var upActionProp = mainVm.GetType().GetProperty("UpdateWindowAction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var upAction = upActionProp?.GetValue(mainVm) as Action;
                            upAction?.Invoke();

                            // UpdateTreeView() があれば呼ぶ
                            var updateTreeMi = mainVm.GetType().GetMethod("UpdateTreeView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            updateTreeMi?.Invoke(mainVm, null);
                        }
                    }
                }
                catch
                {
                    // 保険: 何か失敗しても進める（ここで例外をユーザーに見せない）
                }
            }

            // イベント生成と通知
            MoveCopyEventArgs args = new()
            {
                IsMove = viewModel.IsMoveSelected,
                IsCopy = viewModel.IsCopySelected,
                DX = viewModel.DX,
                DY = viewModel.DY,
                RepetitionNumber = viewModel.RepetitionNumber
            };

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
                    viewModel.RepetitionNumber = result; // ビューモデルのプロパティに値を設定
                }
            }
            else
            {
                MessageBox.Show("自然数を入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MoveCopyViewModel viewModel)
            {
                Console.WriteLine($"IsCopySelected: {viewModel.IsCopySelected}, IsMoveSelected: {viewModel.IsMoveSelected}");
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
