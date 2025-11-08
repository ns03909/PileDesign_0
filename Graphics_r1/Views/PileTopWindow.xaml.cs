using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using PileDesign.ViewModels;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// PileTopWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class PileTopWindow : Window
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private int PileBodyNo { get; set; }
        private string PileConstructionType { get; set; }
        private PileSection PileSection { get; set; }



        // コンストラクタ
        public PileTopWindow(
            MainWindowViewModel mainWindowViewModel,
            PileTop pileTop,
            int pileBodyNo,
            string pileBodyType,
            string pileTopType,
            string pileConstructionType,
            PileSection pileSection
            )
        {
            InitializeComponent();
            PileBodyNo = pileBodyNo;
            PileConstructionType = pileConstructionType;
            PileSection = pileSection;

            var viewModel = new PileTopViewModel(
                mainWindowViewModel,
                pileTop,
                pileBodyNo,
                pileBodyType,
                pileTopType,
                pileConstructionType,
                pileSection
            );

            DataContext = viewModel;

            viewModel.PileTopWindowInstance = this;

            viewModel.RequestClose += (s, e) =>
            {
                if (_isClosingHandled) return;
                _isClosingHandled = true;
                if (this.IsLoaded && this.Visibility == Visibility.Visible)
                {
                    this.Close();
                }
            };

            if (pileTopType == "キャプテンパイル工法")
            {
                viewModel.PileTop.CaptainPile = new(viewModel.PileTop.PileCapFc, viewModel.PileTop.PileCapEc);
            }
            else if (pileTopType == "FT-Pile構法")
            {
                viewModel.PileTop.FTPile = new(viewModel.PileTop.PileCapFc, viewModel.PileTop.PileCapEc);
            }
            else if (pileTopType == "鉄筋定着工法")
            {

            }

            //Chart関連
            ComboBoxBarNumberSquare.Visibility = Visibility.Visible;
            ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
        }

        private bool _isClosingHandled = false;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingHandled) return;
            _isClosingHandled = true;

            if (DataContext is PileTopViewModel viewModel)
            {
                viewModel.GetType().GetMethod("OnCancel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.Invoke(viewModel, null);
            }
        }

        private void PileTopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is PileTopViewModel vm)
            {
                // ViewModel に Window と Canvas を渡す
                vm.PileTopWindowInstance = this;

                // XAML の Canvas が x:Name="Canvas" なら以下
                vm.Canvas = this.Canvas;

                // Canvas が準備できたので描画を実行
                vm.RedrawShapes();

                // Canvas のサイズが変わったときも再描画する
                this.Canvas.SizeChanged -= Canvas_SizeChanged;
                this.Canvas.SizeChanged += Canvas_SizeChanged;
            }
        }

        // キャンバスのサイズが変更されたときに描画を行うメソッド
        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is PileSectionViewModel viewModel)
            {
                viewModel.RedrawShapes();
                viewModel.ChartUpdate();
            }
        }

        // キャンバス右クリック時のイベントハンドラ
        private void Canvas_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }


        // PCリングコンボボックスの選択が変更したときのイベントハンドラ
        private void ComboBoxPCRingChanged(object sender, SelectionChangedEventArgs e)
        {
            Recalculate();
        }

        private void Recalculate()
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;

            if (viewModel?.PileTopType == "キャプテンパイル工法")
            {
                if (ComboBoxPCRing.SelectedItem != null)
                {
                    string selectedPCRingName = ComboBoxPCRing.SelectedItem.ToString();
                    PCRing selectedPCRing = viewModel?.PileTop.CaptainPile.PCRings.FirstOrDefault(p => p.Name == selectedPCRingName);
                    viewModel.PileTop.CaptainPile.PCRing = selectedPCRing;

                    if (selectedPCRing != null)
                    {
                        viewModel.PileTop.SelectedPileTopSpecification = selectedPCRing.GetSpecs();
                    }

                    viewModel.PileTop.CaptainPile.D = selectedPCRing.D;

                    viewModel?.PileTop.CaptainPile.UpdateTDorTB();
                    viewModel?.PileTop.CaptainPile.Update();
                    viewModel?.RedrawShapes();
                    viewModel?.ChartUpdate();
                }
            }
            else if (viewModel?.PileTopType == "FT-Pile構法")
            {
                viewModel?.PileTop.FTPile.Update();
                viewModel?.RedrawShapes();
                viewModel?.ChartUpdate();
            }
        }

        // 絞り率コンボボックスの選択が変更したときのイベントハンドラ
        private void ComboBoxContractionRatioSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Recalculate();
        }

        // コンボボックスの選択が変更したときのイベントハンドラ
        private void ComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Recalculate();
        }

        // FTキャップコンボボックスの選択が変更したときのイベントハンドラ
        private void ComboBoxFTCapChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBoxFTCap.SelectedItem != null)
            {
                PileTopViewModel viewModel = (PileTopViewModel)DataContext;
                string selectedFTCapName = ComboBoxFTCap.SelectedItem.ToString();
                FTCap selectedFTCap = viewModel.PileTop.FTPile.FTCaps.FirstOrDefault(p => p.Phi.ToString() == selectedFTCapName);
                viewModel.PileTop.FTPile.FTCap = selectedFTCap;

                if (selectedFTCap != null)
                {
                    viewModel.PileTop.SelectedPileTopSpecification = selectedFTCap.GetSpecs();
                }
                Recalculate();

            }
        }

        // 画像保存アイテムクリック時のイベントハンドラ
        private void ImageCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            //PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            PileSectionViewModel.CopyCanvasToClipboard(Canvas);
        }

        // 画像保存アイテムクリック時のイベントハンドラ
        private void ImageSaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            //PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            PileSectionViewModel.SaveImage(Canvas);
        }

        // RadioButton_Checkedイベントハンドラ
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            if (sender == RadioButtonSquare)
            {
                if (ComboBoxBarNumberSquare != null || ComboBoxBarNumberCircle != null)
                {
                    ComboBoxBarNumberSquare.Visibility = Visibility.Visible;
                    ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
                    viewModel.PileTop.CaptainPile.CTPTensionRebars.IsSquareArrangement = true;
                    viewModel.PileTop.CaptainPile.CTPTensionRebars.IsCircleArrangement = false;
                }
            }
            else if (sender == RadioButtonCircle)
            {
                if (ComboBoxBarNumberSquare != null || ComboBoxBarNumberCircle != null)
                {
                    ComboBoxBarNumberSquare.Visibility = Visibility.Collapsed;
                    ComboBoxBarNumberCircle.Visibility = Visibility.Visible;
                    viewModel.PileTop.CaptainPile.CTPTensionRebars.IsSquareArrangement = false;
                    viewModel.PileTop.CaptainPile.CTPTensionRebars.IsCircleArrangement = true;
                }
            }
            Recalculate();
        }

        private void RadioButton_Unchecked(object sender, RoutedEventArgs e)
        {
            ComboBoxBarNumberSquare.Visibility = Visibility.Collapsed;
            ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
            Recalculate();
        }

        private void ComboBoxPCRing_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Recalculate();
        }

        private void TextBoxFTTensionNumChanged(object sender, TextChangedEventArgs e)
        {
            Recalculate();
        }

        private void CheckBoxHasTensionAnchorsChecked(object sender, RoutedEventArgs e)
        {
            Recalculate();
        }

        private void CheckBoxHasTensionAnchorsUnchecked(object sender, RoutedEventArgs e)
        {
            Recalculate();
        }
        private void CheckBoxHasFTTensionAnchorsChecked(object sender, RoutedEventArgs e)
        {
            Recalculate();
        }

        private void CheckBoxHasFTTensionAnchorsUnchecked(object sender, RoutedEventArgs e)
        {
            Recalculate();
        }

        private void TextBoxTDorTBTextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(TextBoxTDorTB.Text, out double enteredValue))
            {
                // ViewModelまたは適切なオブジェクトからTDorTBmaxの値を取得します。
                PileTopViewModel viewModel = (PileTopViewModel)DataContext;

                double maxTDorTB = viewModel.PileTop.CaptainPile.CTPTensionRebars.TDorTBmax;

                if (enteredValue > maxTDorTB)
                {
                    MessageBox.Show("tDmaxまたはtBmaxよりも大きな値が入力されました。tDmax、TBmax以下の数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    // テキストボックスのテキストをクリアまたは前の値に戻します。
                    TextBoxTDorTB.Text = maxTDorTB.ToString();
                }
                else
                {
                    Recalculate();
                }
            }
            else
            {
                // 入力が数値に変換できない場合の処理
                MessageBox.Show("数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                TextBoxTDorTB.Clear();
            }
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

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var viewModel = (PileTopViewModel)DataContext;
            viewModel._undoManager.SaveState(viewModel.PileTop.DeepCopy());
        }

        private void ComboBox_DropDownOpened(object sender, System.EventArgs e)
        {
            var viewModel = (PileTopViewModel)DataContext;
            viewModel._undoManager.SaveState(viewModel.PileTop.DeepCopy());
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var viewModel = (PileTopViewModel)DataContext;
                viewModel._undoManager.SaveState(viewModel.PileTop.DeepCopy());
            }
        }


        private void TextBox_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var viewModel = (PileTopViewModel)DataContext;
            viewModel._undoManager.SaveState(viewModel.PileTop.DeepCopy());
        }
    }
}
