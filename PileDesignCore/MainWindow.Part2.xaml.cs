using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace PileDesignCore
{
    /// <summary>
    /// 根入れ部のコードビハインド
    /// </summary>
    public partial class MainWindow
    {
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
                    if (DataGridEmbedment.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                    {
                        row.Header = (i + 1).ToString();
                    }

                    ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
                    if (i == 0)
                    {
                        viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude = viewModel.EmbedmentViewModel.TopAltitude;
                    }
                    else
                    {
                        viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude = viewModel.EmbedmentViewModel.EmbedmentCollection[i - 1].BottomAltitude;
                    }
                    viewModel.EmbedmentViewModel.EmbedmentCollection[i].BottomAltitude
                        = viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude - viewModel.EmbedmentViewModel.EmbedmentCollection[i].LayerThickness;
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

        private void ComboBoxEmbedmentNums_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HelixViewport == null)
            {
                // HelixViewportが初期化されていない場合の処理をスキップする
                return;
            }

            if (ComboBoxEmbedmentNums.SelectedItem is ComboBoxItem selectedItem)
            {
                // Access the Content property to get the integer value
                if (int.TryParse(selectedItem.Content?.ToString(), out int selectedValue))
                {
                    ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
                    int currentCollectionSize = viewModel.EmbedmentViewModel.EmbedmentCollection.Count;

                    // Remove excess items if selectedValue is less than the current collection size
                    for (int i = currentCollectionSize - 1; i >= selectedValue; i--)
                    {
                        viewModel.EmbedmentViewModel.EmbedmentCollection.RemoveAt(i);
                    }

                    // Add new rows only if selectedValue is greater than the current collection size
                    for (int i = currentCollectionSize; i < selectedValue; i++)
                    {
                        EmbedmentDataItem newItem;

                        // Check if there are existing rows to copy from
                        if (currentCollectionSize > 0 && i > 0)
                        {
                            // Copy the content of the last row
                            EmbedmentDataItem lastItem = viewModel.EmbedmentViewModel.EmbedmentCollection[i - 1];
                            newItem = new EmbedmentDataItem
                            {
                                No = i + 1,
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
                                X2 = 50.0,
                                Y1 = 0.0,
                                Y2 = 50.0,
                            };
                        }
                        // Add new items to the data source (EmbedmentCollection)
                        viewModel.EmbedmentViewModel.EmbedmentCollection.Add(newItem);
                    }
                }

                
                for (int i = 0; i < DataGridEmbedment.Items.Count; i++)
                {
                    ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
                    if (i == 0)
                    {
                        viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude = viewModel.EmbedmentViewModel.TopAltitude;
                    }
                    else
                    {
                        viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude = viewModel.EmbedmentViewModel.EmbedmentCollection[i - 1].BottomAltitude;
                    }
                    viewModel.EmbedmentViewModel.EmbedmentCollection[i].BottomAltitude
                        = viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude - viewModel.EmbedmentViewModel.EmbedmentCollection[i].LayerThickness;
                }
                UpdateEmbedment();
                UpdatePerspectiveView();
            }
        }

        //private void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        //{
        //    if (e.PropertyType == typeof(string) && e.PropertyName == "GroundRef")
        //    {
        //        var column = e.Column as DataGridComboBoxColumn;
        //        if (column != null)
        //        {
        //            column.ItemsSource = ((ApplicationViewModel)DataContext).GroundLayerViewModel.GroundRefs;
        //        }
        //    }

        //    if (e.PropertyType == typeof(string) && e.PropertyName == "PileBodyRef")
        //    {
        //        var column = e.Column as DataGridComboBoxColumn;
        //        if (column != null)
        //        {
        //            column.ItemsSource = ((ApplicationViewModel)DataContext).PileBodyViewModel.PileBodyRefs;
        //        }


        //    }
        //}
    


}
}