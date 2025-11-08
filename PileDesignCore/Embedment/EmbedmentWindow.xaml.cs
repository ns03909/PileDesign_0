using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace PileDesignCore
{
    /// <summary>
    /// EmbedmentWindow.xaml の相互作用ロジック
    /// </summary>

    public partial class EmbedmentWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();
        private readonly EmbedmentViewModel viewModel;

        // コンストラクタ
        public EmbedmentWindow(EmbedmentViewModel sharedViewModel,
            FundamentalViewModel fundamentalViewModel,
            GroundLayerViewModel groundLayerViewModel)
        {
            InitializeComponent();
            viewModel = sharedViewModel;  // Initialize viewModel here
            DataContext = sharedViewModel;
            viewModel.DataContextFundamental = fundamentalViewModel;
            viewModel.DataContextGroundLayer = groundLayerViewModel;

            DataGridEmbedment.Loaded += DataGridEmbedment_Loaded;

            // EmbedmentCollection に CollectionChanged イベントハンドラを追加する
            sharedViewModel.EmbedmentCollection.CollectionChanged += EmbedmentCollection_CollectionChanged;

            // DataGridEmbedment の ItemsSource を viewModel.PileLayoutCollection にバインドする
            DataGridEmbedment.ItemsSource = viewModel.EmbedmentCollection;

            // 初期値を保存
            SavePreviousPropertyValues(sharedViewModel);
        }

        private void SavePreviousPropertyValues(EmbedmentViewModel viewModel)
        {
            // 全てのプロパティの前回の値を保存
            PropertyInfo[] properties = typeof(EmbedmentViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        private void RestorePreviousPropertyValues(EmbedmentViewModel viewModel)
        {
            // 全てのプロパティを前回の値に戻す
            PropertyInfo[] properties = typeof(EmbedmentViewModel).GetProperties();
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

        private void DataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // DataGridの高さを行数に合わせて調整する
            DataGrid grid = sender as DataGrid;
            if (grid != null && grid.Items.Count > 0)
            {
                double rowHeight = grid.RowHeight;
                int rowCount = grid.Items.Count;
                double totalHeight = rowHeight * rowCount;
                grid.Height = totalHeight;
            }
        }

        private void DataGridEmbedment_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void EmbedmentCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // 行が削除された場合、すべての行番号を再計算する
                for (int i = 0; i < DataGridEmbedment.Items.Count; i++)
                {
                    var row = DataGridEmbedment.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                    if (row != null)
                    {
                        row.Header = (i + 1).ToString();
                    }
                }
            }
            // コレクションが変更された場合に DataGrid の表示を更新する
            DataGridEmbedment.Items.Refresh();
        }

        private void DataGridEmbedment_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataGridEmbedment.ItemsSource is ObservableCollection<EmbedmentDataItem> observableCollection)
            {
                observableCollection.CollectionChanged += EmbedmentCollection_CollectionChanged;
            }
        }

        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            // OKボタンがクリックされたときの処理
            this.DialogResult = true;
            this.Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            //// プロパティを前回の保存時の値に戻す
            EmbedmentViewModel viewModel = (EmbedmentViewModel)DataContext;
            RestorePreviousPropertyValues(viewModel);
            this.Close();
        }
        private void ComboBoxEmbedmentNums_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBoxEmbedmentNums.SelectedItem is ComboBoxItem selectedItem)
            {
                // Access the Content property to get the integer value
                if (int.TryParse(selectedItem.Content?.ToString(), out int selectedValue))
                {
                    int currentCollectionSize = viewModel.EmbedmentCollection.Count;

                    // Remove excess items if selectedValue is less than the current collection size
                    for (int i = currentCollectionSize - 1; i >= selectedValue; i--)
                    {
                        viewModel.EmbedmentCollection.RemoveAt(i);
                    }

                    // Add new rows only if selectedValue is greater than the current collection size
                    for (int i = currentCollectionSize; i < selectedValue; i++)
                    {
                        EmbedmentDataItem newItem;

                        // Check if there are existing rows to copy from
                        if (currentCollectionSize > 0 && i > 0)
                        {
                            // Copy the content of the last row
                            EmbedmentDataItem lastItem = viewModel.EmbedmentCollection[i - 1];
                            newItem = new EmbedmentDataItem
                            {
                                No = lastItem.No,
                                LayerThickness = lastItem.LayerThickness,
                                TopAltitude = lastItem.TopAltitude,
                                BottomAltitude = lastItem.BottomAltitude,
                                X1 = lastItem.X1,
                                X2 = lastItem.X2,
                                Y1 = lastItem.Y1,
                                Y2 = lastItem.Y2,
                            };
                        }
                        else
                        {
                            // Create a new row with default values
                            newItem = new EmbedmentDataItem
                            {
                                No = i + 1, // Assuming No starts from 1
                                LayerThickness = 0.0, // Set default values or user input
                                TopAltitude = 0.0,
                                BottomAltitude = 0.0,
                                X1 = 0.0,
                                X2 = 30.0,
                                Y1 = 0.0,
                                Y2 = 30.0,
                            };
                        }

                        // Add new items to the data source (EmbedmentCollection)
                        viewModel.EmbedmentCollection.Add(newItem);
                    }
                }
            }
        }

    }
}
