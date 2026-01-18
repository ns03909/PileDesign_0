using HelixToolkit.Wpf;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesignCore
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private ApplicationViewModel _dataContext;

        public new ApplicationViewModel DataContext
        {
            get => _dataContext;
            set
            {
                _dataContext = value;
                base.DataContext = value;
            }
        }

        public ThreeDViewModel DataContext3D { get; set; }

        private PathGeometry drawingGeometry = new PathGeometry();
        private PathGeometry drawingGeometryNode = new PathGeometry();
        private List<PileLayoutDataItem> selectedItems = new List<PileLayoutDataItem>();

        public double CanvasHeight { get; set; }
        public double CanvasWidth { get; set; }
        private readonly double tickSpacing = 5.0;

        readonly double acturalNodeSize = 5.0;

        private Point previousMousePosition;
        private bool isMouseWheelPressed = false;
        private Point startPoint = new Point(0, 0);
        private Point endPoint = new Point(0, 0);
        private Rectangle selectionRectangle;

        private bool hasViewportAxes = true;
        private bool hasViewportGrid = true;
        private bool hasVIewportRendered = true;

        double ScaleCanvasOnBuilding = 10;
        Point CanvasCenterBuildingCoordinate = new Point(0, 0);

        // ツリーメニュー
        private List<CTreeViewData> CTreeViewDatas { get; } = new List<CTreeViewData>();
        //private System.Windows.Controls.TreeView TreeView { get; } = new System.Windows.Controls.TreeView();
        //// Embedment //

        private string LabelContent { get; set; } = "配置番号";
        private int LabelSize { get; set; } = 10;

        private bool isDataGridGridXCellEditEnding = false;
        private bool isDataGridGridYCellEditEnding = false;

        public MainWindow()
        {
            InitializeComponent();

            var itemsSourceList = Enumerable.Range(1, 5).ToArray();
            this.Resources["ItemsSourceList"] = itemsSourceList;

            UpdatePerspectiveView();

            var loadingMainWindow = new LoadingMainWindow();
            loadingMainWindow.ShowDialog();

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeViewModels();
            InitializeCanvasTransformGroup();
            SetupDataBindings();
            SetupEventHandlers();
            SetupTreeView();
            Others();
            Show();
        }

        private void InitializeViewModels()
        {
            var viewModelFundamental = new FundamentalViewModel();
            var viewModelLoadCases = new LoadCaseViewModel();
            var viewModelGround = new GroundLayerViewModel();
            var viewModelPileBody = new PileBodyViewModel();
            var viewModelPileLayout = new PileLayoutViewModel();
            var viewModelEmbedment = new EmbedmentViewModel();

            var appViewModel = new ApplicationViewModel
            {
                FundamentalViewModel = viewModelFundamental,
                LoadCaseViewModel = viewModelLoadCases,
                GroundLayerViewModel = viewModelGround,
                PileBodyViewModel = viewModelPileBody,
                PileLayoutViewModel = viewModelPileLayout,
                EmbedmentViewModel = viewModelEmbedment,
            };
            DataContext = appViewModel;

            DataContext3D = new ThreeDViewModel(DataContext);
        }

        private void InitializeCanvasTransformGroup()
        {
            CanvasHeight = CanvasLayout.ActualHeight;
            CanvasWidth = CanvasLayout.ActualWidth;
        }

        private void SetupDataBindings()
        {
            DataGridPileLayout.ItemsSource = DataContext.PileLayoutViewModel.PileLayoutCollection;
            DataGridEmbedment.ItemsSource = DataContext.EmbedmentViewModel.EmbedmentCollection;
        }

        private void SetupEventHandlers()
        {
            DataGridPileLayout.Loaded += DataGridPileLayout_Loaded;
            CanvasLayout.SizeChanged += CanvasLayout_SizeChanged;
            DataGridEmbedment.Loaded += DataGridEmbedment_Loaded;

            DataContext.PileLayoutViewModel.PileLayoutCollection.CollectionChanged += PileLayoutCollection_CollectionChanged;
            DataContext.PileLayoutViewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PileLayoutViewModel.PileLayoutCollection))
                {
                    UpdateCanvas(DataGridPileLayout);
                }
            };
            DataContext.EmbedmentViewModel.EmbedmentCollection.CollectionChanged += EmbedmentCollection_CollectionChanged;
        }

        private void Others()
        {
            DataContext.PileLayoutViewModel.PileLayoutCollection.CollectionChanged += PileLayoutCollection_CollectionChanged;
            DataGridPileLayout.Loaded += DataGridPileLayout_Loaded;

            selectedItems = new List<PileLayoutDataItem>();

            DataContext.EmbedmentViewModel.EmbedmentCollection.CollectionChanged += EmbedmentCollection_CollectionChanged;
            DataGridEmbedment.ItemsSource = DataContext.EmbedmentViewModel.EmbedmentCollection;
        }

        private void ButtonFundamental_Click(object sender, RoutedEventArgs e)
        {
            var fundamentalWindow = new FundamentalWindow(DataContext.FundamentalViewModel);
            fundamentalWindow.ShowDialog();
        }

        private void ButtonLoadCase_Click(object sender, RoutedEventArgs e)
        {
            var loadcaseWindow = new LoadCaseWindow(DataContext.LoadCaseViewModel);
            loadcaseWindow.ShowDialog();
        }

        private void ButtonGround_Click(object sender, RoutedEventArgs e)
        {
            var groundWindow = new GroundWindow(DataContext.GroundLayerViewModel, DataContext.FundamentalViewModel);
            groundWindow.ShowDialog();
        }

        private void ButtonPileBody_Click(object sender, RoutedEventArgs e)
        {
            var pileBodyWindow = new PileBodyWindow(DataContext.PileBodyViewModel);
            pileBodyWindow.ShowDialog();
        }

        private void ButtonPileLayout_Click(object sender, RoutedEventArgs e)
        {
            var pileLayoutWindow = new PileLayoutWindow(DataContext.PileLayoutViewModel, DataContext.FundamentalViewModel, DataContext.GroundLayerViewModel, DataContext.PileBodyViewModel);
            pileLayoutWindow.ShowDialog();
        }

        private void ButtonEmbedment_Click(object sender, RoutedEventArgs e)
        {
            var embedmentWindow = new EmbedmentWindow(DataContext.EmbedmentViewModel, DataContext.FundamentalViewModel, DataContext.GroundLayerViewModel);
            embedmentWindow.ShowDialog();
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Show();
        }

        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            DataContext.LoadStateWithDialog();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            DataContext.SaveStateWithDialog();
        }

        private void OnSaveWordFileClick(object sender, RoutedEventArgs e)
        {
            DataContext.SaveWordFileWithDialog();
        }

        private void DataGridEmbedment_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
                for (int i = 0; i < DataGridEmbedment.Items.Count; i++)
                {
                    if (i == 0)
                    {
                        viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude = viewModel.EmbedmentViewModel.TopAltitude;
                    }
                    else
                    {
                        viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude = viewModel.EmbedmentViewModel.EmbedmentCollection[i - 1].BottomAltitude;
                    }
                    viewModel.EmbedmentViewModel.EmbedmentCollection[i].BottomAltitude = viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude - viewModel.EmbedmentViewModel.EmbedmentCollection[i].LayerThickness;
                }
                UpdateCanvas(DataGridPileLayout);
                UpdatePerspectiveView();
            }
        }

        private void DataGridPileAxialForce_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                UpdateCanvas(DataGridPileAxialForce);
            }
        }

        private void DataGridPileLayout_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                if (DataGridPileLayout.SelectedItem != null && DataGridPileLayout.CurrentColumn != null)
                {
                    UpdateCanvas(DataGridPileLayout);
                    UpdatePerspectiveView();
                }
            }
        }

        private void DataGridIsFrontPile_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                UpdateCanvas(DataGridIsFrontPile);
            }
        }

        private void ToggleButtonXYGrid_Checked(object sender, RoutedEventArgs e)
        {
            hasViewportGrid = true;
            UpdatePerspectiveView();
        }

        private void ToggleButtonXYGrid_UnClicked(object sender, RoutedEventArgs e)
        {
            hasViewportGrid = false;
            UpdatePerspectiveView();
        }

        private void ToggleButtonXYZAxes_Checked(object sender, RoutedEventArgs e)
        {
            hasViewportAxes = true;
            UpdatePerspectiveView();
        }

        private void ToggleButtonXYZAxes_UnClicked(object sender, RoutedEventArgs e)
        {
            hasViewportAxes = false;
            UpdatePerspectiveView();
        }

        private void HelixViewComboBoxViewStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                hasVIewportRendered = selectedItem.Content.ToString() == "レンダリング";
                UpdateCanvas(DataGridPileLayout);
            }
        }

        private void ComboBoxLabelContent_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                LabelContent = selectedItem.Content.ToString();
                UpdateCanvas(DataGridPileLayout);
            }
        }

        private void ComboBoxLabelSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (int.TryParse(selectedItem.Content.ToString(), out int labelSize))
                {
                    LabelSize = labelSize;
                    UpdateCanvas(DataGridPileLayout);
                }
                else
                {
                    MessageBox.Show("選択したサイズを適切に変換できませんでした。");
                }
            }
        }

        private void ButtonAddGridX_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (ApplicationViewModel)DataContext;
            var collection = viewModel.PileLayoutViewModel.GridX;
            collection.Add(new GridDataItem());
            NumberingNewGrid(false, DataGridGridX, collection);
            RecalculateGrid(DataGridGridX, collection);
        }

        private void ButtonAddGridY_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (ApplicationViewModel)DataContext;
            var collection = viewModel.PileLayoutViewModel.GridY;
            collection.Add(new GridDataItem());
            NumberingNewGrid(false, DataGridGridY, collection);
            RecalculateGrid(DataGridGridY, collection);
        }

        private void NumberingNewGrid(bool isCopy, DataGrid dataGrid, Collection<GridDataItem> collection)
        {
            if (dataGrid.ItemsSource == null) return;

            var collectionView = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource) as IEditableCollectionView;
            if (collectionView == null) return;

            if (!collectionView.IsAddingNew && !collectionView.IsEditingItem)
            {
                bool isSolved = false;
                if (collection.Count == 1)
                {
                    collection[0].No = 1;
                }
                else
                {
                    for (int i = 0; i < collection.Count; i++)
                    {
                        for (int j = 0; j < collection.Count; j++)
                        {
                            if (collection[j].No == i + 1) break;
                            if (j == collection.Count - 1)
                            {
                                collection[collection.Count - 1].No = i + 1;
                                isSolved = true;
                                break;
                            }
                        }
                        if (isSolved) break;
                    }
                }
                if (!isCopy)
                {
                    collection[collection.Count - 1].Spacing = collection[collection.Count - 2].Spacing;
                    collection[collection.Count - 1].Name = StringTransformer.TransformLastCharacter(collection[collection.Count - 2].Name);
                }
                dataGrid.Items.Refresh();
            }
        }

        public class FirstRowConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var dataGridRow = parameter as DataGridRow;
                return dataGridRow != null && dataGridRow.GetIndex() == 0;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        private void DataGridGridY_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            HandleGridCellEditEnding(e, DataGridGridY, ref isDataGridGridYCellEditEnding);
        }

        private void DataGridGridX_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            HandleGridCellEditEnding(e, DataGridGridX, ref isDataGridGridXCellEditEnding);
        }

        private void HandleGridCellEditEnding(DataGridCellEditEndingEventArgs e, DataGrid dataGrid, ref bool isEditEnding)
        {
            isEditEnding = true;

            if (e.EditAction == DataGridEditAction.Commit)
            {
                var binding = e.EditingElement.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                var viewModel = (ApplicationViewModel)DataContext;
                var collection = (Collection<GridDataItem>)dataGrid.ItemsSource;

                RecalculateGrid(dataGrid, collection);

                isEditEnding = false;
            }
        }

        private void RecalculateGrid(DataGrid dataGrid, Collection<GridDataItem> collection)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (i == 0)
                {
                    collection[i].Spacing = 0;
                }
                else
                {
                    collection[i].Coord = collection[i - 1].Coord + collection[i].Spacing;
                }
            }

            UpdateCanvas(DataGridPileLayout);
            UpdatePerspectiveView();
        }

        private void ButtonGridYDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItem(DataGridGridY);
        }

        private void ButtonGridXDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItem(DataGridGridX);
        }

        private void DeleteSelectedItem(DataGrid dataGrid)
        {
            if (dataGrid.SelectedItem is GridDataItem selectedItem)
            {
                var viewModel = (ApplicationViewModel)DataContext;
                var collection = (Collection<GridDataItem>)dataGrid.ItemsSource;
                collection.Remove(selectedItem);
            }
            else
            {
                MessageBox.Show("選択されたアイテムの型が正しくありません。");
            }
        }

        private void DataGridPileLayout_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "AxialForceEX" || e.PropertyName == "AxialForceEY" ||
                e.PropertyName == "AxialForceLevel1s[0]" || e.PropertyName == "AxialForceLevel1s[1]" ||
                e.PropertyName == "AxialForceLevel1s[2]" || e.PropertyName == "AxialForceLevel1s[3]")
            {
                var dataGrid = sender as DataGrid;
                var viewModel = dataGrid.DataContext as ApplicationViewModel;

                if (viewModel != null)
                {
                    var binding = new Binding("PileLayoutViewModel.IsElastic")
                    {
                        Source = viewModel,
                        Mode = BindingMode.OneWay
                    };

                    if (e.Column is DataGridTextColumn dataGridColumn)
                    {
                        var bindingProxy = new BindingProxy { Data = viewModel.PileLayoutViewModel.IsElastic };
                        BindingOperations.SetBinding(bindingProxy, BindingProxy.DataProperty, binding);

                        dataGridColumn.Visibility = (Visibility)bindingProxy.Data;
                    }
                }
            }
        }

        private void DataGridGridY_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) || e.Key == Key.Right || e.Key == Key.Left)
            {
                var viewModel = (ApplicationViewModel)DataContext;
                var collection = viewModel.PileLayoutViewModel.GridY;

                RecalculateGrid(DataGridGridY, collection);

                isDataGridGridYCellEditEnding = false;
            }
        }
    }
}

