using PileDesign.Common;
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
            InitializePaneMaximizer();
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
                // CaptainPileが存在しない場合のみ新規作成（ユーザーの選択を保持するため）
                if (viewModel.PileTop.CaptainPile == null)
                {
                    viewModel.PileTop.CaptainPile = new(viewModel.PileTop.PileCapFc, viewModel.PileTop.PileCapEc);
                }
                // PCリング自動選定は杭径が変わった場合のみLoaded後に実行
            }
            else if (pileTopType == "FT-Pile構法")
            {
                // FTPileが存在しない場合のみ新規作成（ユーザーの選択を保持するため）
                if (viewModel.PileTop.FTPile == null)
                {
                    viewModel.PileTop.FTPile = new(viewModel.PileTop.PileCapFc, viewModel.PileTop.PileCapEc);
                }
            }
            else if (pileTopType == "鉄筋定着工法")
            {

            }

            //Chart関連
            ComboBoxBarNumberSquare.Visibility = Visibility.Visible;
            ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
        }

        private bool _isClosingHandled = false;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is PileTopViewModel vm && vm.OkCommand?.CanExecute(null) == true)
                {
                    vm.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

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

                // FT-Pile構法の場合、MNインタラクショングラフを非表示
                if (vm.PileTopType == "FT-Pile構法")
                {
                    MNInteractionPane.Hide();
                }

                // Canvas が準備できたので描画を実行
                vm.RedrawShapes();

                // Canvas のサイズが変わったときも再描画する
                this.Canvas.SizeChanged -= Canvas_SizeChanged;
                this.Canvas.SizeChanged += Canvas_SizeChanged;

                // CSVエクスポートメニューを追加
                PlotHelper.AddCsvExportMenu(wpfPlotMN, "MN曲線");
                PlotHelper.AddCsvExportMenu(wpfPlotThetaM, "θM曲線");

                // キャプテンパイル工法の場合、杭径が変わった時のみPCリングを自動選定
                // 初回の自動選定は杭頭タイプ選択時（PileBodyViewModel.OnPileTopTypeSelectionChanged）で実行
                if (vm.PileTopType == "キャプテンパイル工法" && PileSection != null)
                {
                    var captainPile = vm.PileTop?.CaptainPile;
                    if (captainPile != null && captainPile.D > 0)
                    {
                        double pileDia = PileSection.PileDiameter;

                        // 杭径に対する最適なPCリングサイズを計算
                        int optimalSize = (int)System.Math.Ceiling(pileDia / 100.0) * 100;
                        optimalSize = System.Math.Max(optimalSize, 800);
                        optimalSize = System.Math.Min(optimalSize, 3000);

                        // 杭径が変わった場合のみ自動選定
                        // D != optimalSize: 杭径が変わったので自動選定
                        if (System.Math.Abs(captainPile.D - optimalSize) > 1)
                        {
                            AutoSelectPCRing(vm, PileSection);
                            Recalculate();
                        }
                    }
                }

                // FT-Pile構法の場合、杭径が変わった時のみFTキャップを自動選定
                // 初回の自動選定は杭頭タイプ選択時（PileBodyViewModel.OnPileTopTypeSelectionChanged）で実行
                if (vm.PileTopType == "FT-Pile構法" && PileSection != null)
                {
                    var ftPile = vm.PileTop?.FTPile;
                    if (ftPile?.FTCap != null && ftPile.FTCap.Phi > 0)
                    {
                        double pileDia = PileSection.PileDiameter;

                        // 杭径が変わった場合のみ自動選定
                        if (System.Math.Abs(ftPile.FTCap.Phi - pileDia) > 1)
                        {
                            AutoSelectFTCap(vm, PileSection);
                            Recalculate();
                        }
                    }
                }
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

            if (viewModel == null) return;
            if (viewModel.PileTopType == "キャプテンパイル工法")
            {
                if (ComboBoxPCRing.SelectedItem != null)
                {
                    string selectedPCRingName = ComboBoxPCRing.SelectedItem.ToString() ?? string.Empty;
                    PCRing? selectedPCRing = viewModel.PileTop.CaptainPile.PCRings.FirstOrDefault(p => p.Name == selectedPCRingName);
                    viewModel.PileTop.CaptainPile.PCRing = selectedPCRing;

                    if (selectedPCRing != null)
                    {
                        viewModel.PileTop.SelectedPileTopSpecification = selectedPCRing.GetSpecs();
                        viewModel.PileTop.CaptainPile.D = selectedPCRing.D;
                    }

                    viewModel.PileTop.CaptainPile.UpdateTDorTB();
                    viewModel.PileTop.CaptainPile.Update();
                    viewModel.RedrawShapes();
                    viewModel.ChartUpdate();
                }
            }
            else if (viewModel.PileTopType == "FT-Pile構法")
            {
                viewModel.PileTop.FTPile.Update();
                viewModel.RedrawShapes();
                viewModel.ChartUpdate();
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

                    // 杭の寸法を設定（外径と内径）
                    if (viewModel.PileSection != null)
                    {
                        double outerDia = viewModel.PileSection.PileDiameter;
                        double thickness = viewModel.PileSection.ConcreteThickness;
                        double innerDia = thickness > 0 ? outerDia - 2 * thickness : outerDia * 0.6;
                        viewModel.PileTop.FTPile.FTPilePile.SetDimensions(outerDia, innerDia);
                    }
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
                if (ComboBoxBarNumberSquare != null && ComboBoxBarNumberCircle != null)
                {
                    ComboBoxBarNumberSquare.Visibility = Visibility.Visible;
                    ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
                    viewModel.PileTop.CaptainPile.CTPTensionRebars.IsSquareArrangement = true;
                    viewModel.PileTop.CaptainPile.CTPTensionRebars.IsCircleArrangement = false;
                }
            }
            else if (sender == RadioButtonCircle)
            {
                if (ComboBoxBarNumberSquare != null && ComboBoxBarNumberCircle != null)
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

        /// <summary>
        /// キャプテンパイル工法選択時にPCリングを自動選定
        /// 杭径以上で最小のPCリング(-N)を選定
        /// </summary>
        private static void AutoSelectPCRing(PileTopViewModel viewModel, PileSection pileSection)
        {
            if (pileSection == null) return;

            var captainPile = viewModel.PileTop?.CaptainPile;
            if (captainPile?.PCRings == null || captainPile.PCRings.Count == 0) return;

            // 杭径を取得
            double pileDia = pileSection.PileDiameter;
            if (pileDia <= 0) return;

            // 杭径以上で最小のPCリングサイズを計算
            // PCリングは800mmから3000mmまで100mm刻み
            int targetSize = (int)System.Math.Ceiling(pileDia / 100.0) * 100;
            targetSize = System.Math.Max(targetSize, 800);   // 最小800mm
            targetSize = System.Math.Min(targetSize, 3000);  // 最大3000mm

            // 標準タイプ名を生成 (例: "800-N", "1200-N")
            string targetPCRingName = $"{targetSize}-N";

            var targetPCRing = captainPile.PCRings.FirstOrDefault(r => r.Name == targetPCRingName);

            if (targetPCRing != null)
            {
                // PCRingオブジェクトと選択名の両方を設定
                captainPile.PCRing = targetPCRing;
                captainPile.SelectedPCRingName = targetPCRingName;
                captainPile.D = targetPCRing.D;

                // Update()を呼ぶとCTPConcreteが作成され、SetBasicPropertiesも内部で呼ばれる
                captainPile.Update();

                // 引張定着筋のtD/tB最大値を更新
                captainPile.UpdateTDorTB();

                // 諸元表示を更新
                viewModel.PileTop.SelectedPileTopSpecification = targetPCRing.GetSpecs();
            }
        }

        /// <summary>
        /// FT-Pile構法選択時にFTキャップを自動選定
        /// 杭径に一致するFTキャップを選定
        /// </summary>
        private static void AutoSelectFTCap(PileTopViewModel viewModel, PileSection pileSection)
        {
            if (pileSection == null) return;

            var ftPile = viewModel.PileTop?.FTPile;
            if (ftPile?.FTCaps == null || ftPile.FTCaps.Count == 0) return;

            // 杭径を取得
            double pileDia = pileSection.PileDiameter;
            if (pileDia <= 0) return;

            // 杭径に一致するFTキャップを検索（FTキャップは300-1200mm）
            // 完全一致がなければ、杭径以上で最小のものを選択
            var targetFTCap = ftPile.FTCaps.FirstOrDefault(c => System.Math.Abs(c.Phi - pileDia) < 1);
            if (targetFTCap == null)
            {
                // 完全一致がない場合、杭径以上で最小のものを選択
                targetFTCap = ftPile.FTCaps
                    .Where(c => c.Phi >= pileDia)
                    .OrderBy(c => c.Phi)
                    .FirstOrDefault();
            }
            // それでもない場合、最大のものを選択
            if (targetFTCap == null)
            {
                targetFTCap = ftPile.FTCaps.OrderByDescending(c => c.Phi).FirstOrDefault();
            }

            if (targetFTCap != null && viewModel.PileTop != null)
            {
                // FTCapを設定
                ftPile.FTCap = targetFTCap;
                ftPile.SelectedFTCapName = targetFTCap.Phi.ToString();
                viewModel.PileTop.SelectedFTCap = (int)targetFTCap.Phi;

                // 杭の寸法を設定（外径と内径）
                double outerDia = pileDia;
                double innerDia = 0;
                double thickness = pileSection.ConcreteThickness;
                if (thickness > 0)
                {
                    innerDia = outerDia - 2 * thickness;
                }
                else
                {
                    innerDia = outerDia * 0.6; // 仮の値
                }
                ftPile.FTPilePile.SetDimensions(outerDia, innerDia);

                // FTPileを更新
                ftPile.Update();

                // 諸元表示を更新
                viewModel.PileTop.SelectedPileTopSpecification = targetFTCap.GetSpecs();
            }
        }
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu contextMenu && contextMenu.PlacementTarget is DataGrid dataGrid)
            {
                foreach (var item in contextMenu.Items)
                {
                    if (item is MenuItem menuItem)
                        menuItem.CommandParameter = dataGrid;
                }
            }
        }

        private void CopyToClipboardFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is DataGrid dataGrid)
            {
                Output.DataGridCsv.CopyToClipboard(dataGrid);
            }
        }

        private void ExportCsvFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is DataGrid dataGrid)
            {
                var data = dataGrid.ItemsSource.Cast<object>();
                Output.DataGridCsv.Export(data, dataGrid);
            }
        }

        private PileDesign.Common.PaneFloatMaximizer? _paneMaximizer;

        private void InitializePaneMaximizer()
        {
            _paneMaximizer = new PileDesign.Common.PaneFloatMaximizer(dockingManager);
            _paneMaximizer.Register("Input",   InputTab,             ButtonMaximizeInput,   "入力");
            _paneMaximizer.Register("Specs",   SpecsTab,             ButtonMaximizeSpecs,   "諸元");
            _paneMaximizer.Register("TopView", TopViewTab,           ButtonMaximizeTopView, "上面図");
            _paneMaximizer.Register("MN",      MNInteractionPane,    ButtonMaximizeMN,      "MN");
            _paneMaximizer.Register("ThetaM",  ThetaMInteractionTab, ButtonMaximizeThetaM,  "θM");
        }
    }
}
