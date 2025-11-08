using PileDesignCore.PileLibrary;
using PileDesignCore.PileSection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;


namespace PileDesignCore
{
    /// <summary>
    /// PileTopWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class PileTopWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();
        private PileTopTopViewDrawer ShapeDrawer { get; set; }
        //private string SelectedPileTopType { get; set; }
        private int PileBodyNo { get; set; }

        // コンストラクタ
        public PileTopWindow(PileTopViewModel sharedViewModel,
            int pileBodyNo,
            string selectedPileTopType)//,
            //PileSectionViewModel pileSecitonViewModel )
        {

            // pileSecitonViewModelをPileTopViewModelに読み込むには。。。
            sharedViewModel.SelectedPileTopType = selectedPileTopType;

            //SelectedPileTopType = selectedPileTopType;
            PileBodyNo = pileBodyNo;
            InitializeComponent();

            // viewModel = sharedViewModel;
            DataContext = sharedViewModel;

            // 初期値を保存
            SavePreviousPropertyValues(sharedViewModel);

            // 断面図
            ShapeDrawer = new PileTopTopViewDrawer(sharedViewModel, Canvas);

            //Chart関連
            ChartThetaM.Child = sharedViewModel.ChartThetaM;

            ComboBoxBarNumberSquare.Visibility = Visibility.Visible;
            ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
        }

        // キャンバスのサイズが変更されたときに描画を行うメソッド
        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //RedrawShapes((PileSectionViewModel)DataContext);
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }

        // 全てのプロパティの前回の値を保存メソッド
        private void SavePreviousPropertyValues(PileTopViewModel viewModel)
        {
            PropertyInfo[] properties = typeof(PileTopViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        // 全てのプロパティを前回の値に戻すメソッド
        private void RestorePreviousPropertyValues(PileSectionViewModel viewModel)
        {
            PropertyInfo[] properties = typeof(PileTopViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanWrite)
                {
                    if (previousPropertyValues.TryGetValue(property.Name, out object value))
                    {
                        property.SetValue(viewModel, value);
                    }
                }
            }
        }

        // OKボタンがクリックされたときの処理メソッド
        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        // キャンセルボタンがクリックされたときの処理メソッド
        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            //// プロパティを前回の保存時の値に戻す
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            //RestorePreviousPropertyValues(viewModel);
            this.Close();
        }

        // キャンバス右クリック時のイベントハンドラ
        private void Canvas_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

        // PCリングコンボボックスの選択が変更したときのイベントハンドラ
        private void ComboBoxPCRingChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBoxPCRing.SelectedItem != null)
            {
                PileTopViewModel viewModel = (PileTopViewModel)DataContext;
                string selectedPCRingName = ComboBoxPCRing.SelectedItem.ToString();
                PCRing selectedPCRing = viewModel.PCRings.FirstOrDefault(p => p.Name == selectedPCRingName);
                viewModel.CaptainPile.PCRing = selectedPCRing;

                if (selectedPCRing != null)
                {
                    viewModel.SelectedPileTopSpecification = selectedPCRing.GetSpecs();
                }

                viewModel.CaptainPile.D = selectedPCRing.D;
                viewModel.CaptainPile.UpdateTDorTB();
                ShapeDrawer?.RedrawShapes();
                viewModel.ChartUpdate();
            }
        }

        // 絞り率コンボボックスの選択が変更したときのイベントハンドラ
        private void ComboBoxContractionRatioSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            viewModel.CaptainPile.UpdateTDorTB();
            ShapeDrawer?.RedrawShapes();
            
            viewModel.ChartUpdate();
        }

        

        // 
        private void ComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }

        // FTキャップコンボボックスの選択が変更したときのイベントハンドラ
        private void ComboBoxFTCapChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBoxFTCap.SelectedItem != null)
            {
                PileTopViewModel viewModel = (PileTopViewModel)DataContext;
                string selectedFTCapName = ComboBoxFTCap.SelectedItem.ToString();
                FTCap selectedFTCap = viewModel.FTCaps.FirstOrDefault(p => p.Phi.ToString() == selectedFTCapName);
                viewModel.FTPile.FTCap = selectedFTCap;

                if (selectedFTCap != null)
                {
                    viewModel.SelectedPileTopSpecification = selectedFTCap.GetSpecs();
                }

                ShapeDrawer?.RedrawShapes();
                viewModel.ChartUpdate();
            }
        }

        // 画像保存アイテムクリック時のイベントハンドラ
        private void ImageCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.CopyCanvasToClipboard(Canvas);
        }

        // 画像保存アイテムクリック時のイベントハンドラ
        private void ImageSaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.SaveImage(Canvas);
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            if (sender == RadioButtonSquare)
            {
                if(ComboBoxBarNumberSquare != null || ComboBoxBarNumberCircle != null)
                {
                    ComboBoxBarNumberSquare.Visibility = Visibility.Visible;
                    ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
                    viewModel.CaptainPile.CTPTensionRebars.IsSquareArrangement = true;
                    viewModel.CaptainPile.CTPTensionRebars.IsCircleArrangement = false;
                }

            }
            else if (sender == RadioButtonCircle)
            {
                if (ComboBoxBarNumberSquare != null || ComboBoxBarNumberCircle != null)
                {
                    ComboBoxBarNumberSquare.Visibility = Visibility.Collapsed;
                    ComboBoxBarNumberCircle.Visibility = Visibility.Visible;
                    viewModel.CaptainPile.CTPTensionRebars.IsSquareArrangement = false;
                    viewModel.CaptainPile.CTPTensionRebars.IsCircleArrangement = true;
                }

            }
            viewModel.CaptainPile.UpdateTDorTB();
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }

        private void RadioButton_Unchecked(object sender, RoutedEventArgs e)
        {
            ComboBoxBarNumberSquare.Visibility = Visibility.Collapsed;
            ComboBoxBarNumberCircle.Visibility = Visibility.Collapsed;
        }

        private void ComboBoxPCRing_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }

        private void TextBoxFTTensionNumChanged(object sender, TextChangedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }

        private void CheckBoxHasTensionAnchorsChecked(object sender, RoutedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }

        private void CheckBoxHasTensionAnchorsUnchecked(object sender, RoutedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }
        private void CheckBoxHasFTTensionAnchorsChecked(object sender, RoutedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }

        private void CheckBoxHasFTTensionAnchorsUnchecked(object sender, RoutedEventArgs e)
        {
            PileTopViewModel viewModel = (PileTopViewModel)DataContext;
            ShapeDrawer?.RedrawShapes();
            viewModel.ChartUpdate();
        }
    }
    
}
