using PileDesignCore.PileSection;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;


namespace PileDesignCore
{
    /// <summary>
    /// PileSectionWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class PileSectionWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();
        private ShapeDrawer ShapeDrawer { get; set; }

        // コンストラクタ
        public PileSectionWindow(PileSectionViewModel sharedViewModel,
            int pileBodyNo,
            string selectedPileBodyType)
        {
            sharedViewModel.PileBodyNo = pileBodyNo;
            sharedViewModel.SelectedPileBodyType = selectedPileBodyType;
            InitializeComponent();

            // viewModel = sharedViewModel;
            DataContext = sharedViewModel;

            // 初期値を保存
            SavePreviousPropertyValues(sharedViewModel);

            // 断面図
            ShapeDrawer = new ShapeDrawer(sharedViewModel, Canvas);

            //Chart関連
            ChartMN.Child = sharedViewModel.Chart1;

            // SizeChanged イベントハンドラの追加
            Canvas.SizeChanged += Canvas_SizeChanged;

            // PileDiameterプロパティの変更イベントを購読し、円を再描画する
            sharedViewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PileSectionViewModel.PileDiameter) ||
                    e.PropertyName == nameof(PileSectionViewModel.ConcreteOutDia) ||
                    e.PropertyName == nameof(PileSectionViewModel.ConcreteGamma) ||
                    e.PropertyName == nameof(PileSectionViewModel.ConcreteGsi) ||
                    e.PropertyName == nameof(PileSectionViewModel.ConcreteFc) ||
                    e.PropertyName == nameof(PileSectionViewModel.PipeDia) ||
                    e.PropertyName == nameof(PileSectionViewModel.PipeTs) ||
                    e.PropertyName == nameof(PileSectionViewModel.MainBarNum) ||
                    e.PropertyName == nameof(PileSectionViewModel.MainBarSize) ||
                    e.PropertyName == nameof(PileSectionViewModel.MainBarCenterCover) ||
                    e.PropertyName == nameof(PileSectionViewModel.HoopSize) ||
                    e.PropertyName == nameof(PileSectionViewModel.HoopCenterCover) ||
                    e.PropertyName == nameof(PileSectionViewModel.SelectedPileSectionType) ||
                    e.PropertyName == nameof(PileSectionViewModel.SelectedPrecastPileName) ||
                    e.PropertyName == nameof(PileSectionViewModel.SelectedSteelPipePileName))
                {
                    if (e.PropertyName == nameof(PileSectionViewModel.ConcreteGamma) ||
                    e.PropertyName == nameof(PileSectionViewModel.ConcreteGsi) ||
                    e.PropertyName == nameof(PileSectionViewModel.ConcreteFc))
                    {
                        sharedViewModel.RecalculateConcreteE();
                    }

                    ShapeDrawer.RedrawShapes();
                    sharedViewModel.ChartUpdate();
                }
            };
        }

        // キャンバスのサイズが変更されたときに描画を行うメソッド
        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //RedrawShapes((PileSectionViewModel)DataContext);
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            ShapeDrawer.RedrawShapes();
            viewModel.ChartUpdate();
        }

        // 全てのプロパティの前回の値を保存メソッド
        private void SavePreviousPropertyValues(PileSectionViewModel viewModel)
        {
            PropertyInfo[] properties = typeof(PileSectionViewModel).GetProperties();
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
            PropertyInfo[] properties = typeof(PileSectionViewModel).GetProperties();
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
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            RestorePreviousPropertyValues(viewModel);
            this.Close();
        }

        // キャンバス右クリック時のイベントハンドラ
        private void Canvas_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e){}

        //画像保存アイテムクリック時のイベントハンドラ
        private void ImageCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.CopyCanvasToClipboard(Canvas);
        }

        //画像保存アイテムクリック時のイベントハンドラ
        private void ImageSaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            PileSectionViewModel viewModel = (PileSectionViewModel)DataContext;
            viewModel.SaveImage(Canvas);
        }
    }
}


