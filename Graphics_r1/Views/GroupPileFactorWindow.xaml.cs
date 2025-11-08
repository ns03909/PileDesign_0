using PileDesign.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Views
{
    /// <summary>
    /// GroupPileFactor.xaml の相互作用ロジック
    /// </summary>
    public partial class GroupPileFactorWindow : Window
    {
        private bool _isClosingHandled = false;

        // コンストラクタ
        public GroupPileFactorWindow(MainWindowViewModel _mainWindowViewModel)
        {
            InitializeComponent();
            var viewModel = new GroupPileFactorViewModel(_mainWindowViewModel);
            this.DataContext = viewModel;
            viewModel.WpfPlot = wpfPlot;
            viewModel.UpdateGraph(); // 初期表示
            viewModel.RequestClose += (s, e) =>
            {
                {
                    // すでにクローズ処理中なら何もしない
                    if (_isClosingHandled) return;
                    _isClosingHandled = true;

                    if (this.IsLoaded && this.IsVisible)
                    {
                        this.Close();
                    }
                }
                ;
            };

        }

        // 全杭本数を入力した場合のメソッド
        private void TextBoxPileNumberTextChanged(object sender, TextChangedEventArgs e)
        {
            GroupPileFactorViewModel viewModel = DataContext as GroupPileFactorViewModel;
            if (viewModel.TotalPileCount > 0 &&
                viewModel.PileSpacingDiaRatio.HasValue && viewModel.PileSpacingDiaRatio > 0)
            {
                viewModel.ComputePileGroupFactor();
                viewModel.ChartUpdate();
            }
        }

        // 杭間隔を入力した場合のメソッド
        private void TextBoxPileSpacingTextChanged(object sender, TextChangedEventArgs e)
        {
            GroupPileFactorViewModel viewModel = DataContext as GroupPileFactorViewModel;
            if (viewModel.PileDia.HasValue && viewModel.PileDia > 0 &&
                viewModel.PileSpacing.HasValue && viewModel.PileSpacing > 0)
            {
                viewModel.PileSpacingDiaRatio = viewModel.PileSpacing / viewModel.PileDia;
            }
        }

        // 杭径を入力した場合のメソッド
        private void TextBoxPileDiaTextChanged(object sender, TextChangedEventArgs e)
        {
            GroupPileFactorViewModel viewModel = DataContext as GroupPileFactorViewModel;
            if (viewModel.PileDia.HasValue && viewModel.PileDia > 0 &&
                viewModel.PileSpacing.HasValue && viewModel.PileSpacing > 0)
            {
                viewModel.PileSpacingDiaRatio = viewModel.PileSpacing / viewModel.PileDia;
            }
        }

        // 杭間隔比を入力した場合のメソッド
        private void TextBoxPileSpacingDiaRatioTextChanged(object sender, TextChangedEventArgs e)
        {
            GroupPileFactorViewModel viewModel = DataContext as GroupPileFactorViewModel;
            if (viewModel.PileSpacingDiaRatio != viewModel.PileSpacing / viewModel.PileDia)
            {
                TextBoxPileDia.Text = null;
                TextBoxPileSpacing.Text = null;
            }

            if (viewModel.PileSpacingDiaRatio.HasValue && viewModel.PileSpacingDiaRatio > 0)
            {
                viewModel.ComputePileGroupFactor();
                viewModel.ChartUpdate();
            }
        }

        // 群杭係数を入力した場合のメソッド
        private void TextBoxPileGroupFactorTextChanged(object sender, TextChangedEventArgs e)
        {
            //GroupPileFactorViewModel viewModel = DataContext as GroupPileFactorViewModel;
            //if (viewModel.PileSpacingDiaRatio != viewModel.PileSpacing / viewModel.PileDia)
            //{
            //    TextBoxPileDia.Text = null;
            //    TextBoxPileSpacing.Text = null;
            //}


            //if (viewModel.PileSpacingDiaRatio.HasValue && viewModel.PileSpacingDiaRatio > 0)
            //{
            //    viewModel.ChartUpdate();
            //    //ForceChartRedraw(); // 追加
            //}

        }
    }
}
