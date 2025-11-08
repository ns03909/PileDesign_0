using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Forms;

namespace PileDesignCore
{
    /// <summary>
    /// GroundWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class GroundWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();
        private readonly GroundLayerViewModel viewModel;

        // インストラクタ
        public GroundWindow(GroundLayerViewModel sharedViewModel,
               FundamentalViewModel fundamentalViewModel)
        {
            InitializeComponent();

            viewModel = sharedViewModel;
            DataContext = sharedViewModel;

            viewModel.SelectedGroundMassCollection = viewModel.SelectedGroundMassCollection2;

            viewModel.DataContextFundamental = fundamentalViewModel;

            // DataGridGroundLayer の Loaded イベントハンドラを設定
            DataGridGroundLayer.Loaded += DataGridGroundLayer_Loaded;

            // 初期値を保存
            SavePreviousPropertyValues(sharedViewModel);

            //Chart関連
            Chart1v.Child = viewModel.chart1;
            Chart2v.Child = viewModel.chart2;
        }

        private void SavePreviousPropertyValues(GroundLayerViewModel viewModel)
        {
            // 全てのプロパティの前回の値を保存
            PropertyInfo[] properties = typeof(GroundLayerViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        private void RestorePreviousPropertyValues(GroundLayerViewModel viewModel)
        {
            // 全てのプロパティを前回の値に戻す
            PropertyInfo[] properties = typeof(GroundLayerViewModel).GetProperties();
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

        private void DataGridGroundLayer_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void DataGridMassLayer_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void GroundLayerCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // 行が削除された場合、すべての行番号を再計算する
                for (int i = 0; i < DataGridGroundLayer.Items.Count; i++)
                {
                    DataGridRow row = DataGridGroundLayer.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                    if (row != null)
                    {
                        row.Header = (i + 1).ToString();
                    }
                }
            }
        }

        private void DataGridGroundLayer_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataGridGroundLayer.ItemsSource is ObservableCollection<GroundLayerDataItem> observableCollection)
            {
                observableCollection.CollectionChanged += GroundLayerCollection_CollectionChanged;
            }
        }

        // Handle the button click event for the "削除" (Delete) button
        private void ButtonDeleteGroundLayer_Click(object sender, RoutedEventArgs e)
        {
            if (DataGridGroundLayer.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridGroundLayer.SelectedItem is GroundLayerDataItem selectedItem)
                {
                    viewModel.SelectedGroundLayerCollection.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    System.Windows.MessageBox.Show("選択されたアイテムの型が正しくありません。");
                }
                DataGridGroundLayer.Items.Refresh();
                viewModel.Update();
            }
        }

        private void ButtonDeleteGroundMass_Click(object sender, RoutedEventArgs e)
        {
            if (DataGridGroundMass.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridGroundMass.SelectedItem is GroundMassDataItem selectedItem)
                {
                    viewModel.SelectedGroundMassCollection1.Remove(selectedItem);
                    viewModel.SelectedGroundMassCollection2.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    System.Windows.MessageBox.Show("選択されたアイテムの型が正しくありません。");
                }
                DataGridGroundMass.Items.Refresh();
                viewModel.Update();
            }
        }

        private void ButtonAddGroundLayer_Click(object sender, RoutedEventArgs e)
        {
            if (viewModel.SelectedGroundLayerCollection == null)
            {
                viewModel.SelectedGroundLayerCollection = new ObservableCollection<GroundLayerDataItem>();
            }
            viewModel.SelectedGroundLayerCollection.Add(new GroundLayerDataItem(viewModel));
            viewModel.Update();
        }

        private void ButtonAddGroundMass_Click(object sender, RoutedEventArgs e)
        {
            if (viewModel.SelectedGroundMassCollection1 == null)
            {
                viewModel.SelectedGroundMassCollection1 = new ObservableCollection<GroundMassDataItem>();
            }
            viewModel.SelectedGroundMassCollection1.Add(new GroundMassDataItem(viewModel));
            viewModel.Update();

            if (viewModel.SelectedGroundMassCollection2 == null)
            {
                viewModel.SelectedGroundMassCollection2 = new ObservableCollection<GroundMassDataItem>();
            }
            viewModel.SelectedGroundMassCollection2.Add(new GroundMassDataItem(viewModel));
            viewModel.Update();
        }

        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            GroundLayerViewModel viewModel = (GroundLayerViewModel)DataContext;
            RestorePreviousPropertyValues(viewModel);
            this.Close();
        }

        private bool initialSelection = true;

        private void ComboBoxGroundNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initialSelection)
            {
                initialSelection = false;
                return;
            }

            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (ComboBoxGroundNo.SelectedItem != null)
                {
                    int selectedGroundNo = (int)ComboBoxGroundNo.SelectedItem;
                    int selectedLevel = viewModel.SelectedLevels[selectedGroundNo - 1];

                    string selectedGroundRef = viewModel.GroundRefs[selectedGroundNo - 1];
                    double selectedTopAltitude = viewModel.GroundTopAltitudes[selectedGroundNo - 1];
                    double selectedWaterTableAltitude = viewModel.GroundWaterTableAltitudes[selectedGroundNo - 1];
                    double selectedStressAltitude = viewModel.StressAltitudes[selectedGroundNo - 1];

                    double selectedGroundAcceleration1 = viewModel.GroundAccelerations1[selectedGroundNo - 1];
                    double selectedGroundAcceleration2 = viewModel.GroundAccelerations2[selectedGroundNo - 1];

                    string selectedCalculationMethod = viewModel.CalculationMethods[selectedGroundNo - 1];
                    string selectedShallowSoilType = viewModel.ShallowSoilTypes[selectedGroundNo - 1];
                    double selectedBedrockDensity = viewModel.BedrockDensities[selectedGroundNo - 1];
                    double selectedBedrockShearWaveVelocity = viewModel.BedrockShearWaveVelocities[selectedGroundNo - 1];

                    string selectedChart1Content = viewModel.Chart1Contents[selectedGroundNo - 1];
                    string selectedChart2Content = viewModel.Chart2Contents[selectedGroundNo - 1];


                    viewModel.GroundRef = selectedGroundRef;
                    viewModel.GroundTopAltitude = selectedTopAltitude;
                    viewModel.GroundWaterTableAltitude = selectedWaterTableAltitude;
                    viewModel.StressAltitude = selectedStressAltitude;

                    viewModel.GroundAcceleration1 = selectedGroundAcceleration1;
                    viewModel.GroundAcceleration2 = selectedGroundAcceleration2;

                    viewModel.CalculationMethod = selectedCalculationMethod;
                    viewModel.ShallowSoilType = selectedShallowSoilType;
                    viewModel.BedrockDensity = selectedBedrockDensity;
                    viewModel.BedrockShearWaveVelocity = selectedBedrockShearWaveVelocity;

                    viewModel.Chart1Content = selectedChart1Content;
                    viewModel.Chart2Content = selectedChart2Content;

                    // 選択された地盤番号に対応する ObservableCollection を DataGrid の ItemsSource に設定
                    viewModel.SelectedGroundLayerCollection = viewModel.DataGridGroundLayers[selectedGroundNo - 1];
                    viewModel.SelectedGroundMassCollection1 = viewModel.DataGridMassLayers[selectedGroundNo - 1][0]; // レベル1
                    viewModel.SelectedGroundMassCollection2 = viewModel.DataGridMassLayers[selectedGroundNo - 1][1]; // レベル2
                    
                    if (selectedLevel == 1)
                    { viewModel.SelectedGroundMassCollection = viewModel.SelectedGroundMassCollection1; }
                    else if (selectedLevel == 2)
                    { viewModel.SelectedGroundMassCollection = viewModel.SelectedGroundMassCollection2; }

                    TextBoxGroundRef.Text = selectedGroundRef;
                    TextBoxGroundTopAltitude.Text = selectedTopAltitude.ToString();
                    TextBoxGroundWaterTableAltitude.Text = selectedWaterTableAltitude.ToString();
                    TextBoxGroundStressAltitude.Text = selectedStressAltitude.ToString();

                    ComboBoxShallowSoilType.SelectedItem = selectedShallowSoilType;
                    ComboBoxCalculationMethod.SelectedItem = selectedCalculationMethod;

                    TextBoxGroundAcceleration1.Text = selectedGroundAcceleration1.ToString();
                    TextBoxGroundAcceleration2.Text = selectedGroundAcceleration2.ToString();

                    TextBoxBedrockDensity.Text = selectedBedrockDensity.ToString();
                    TextBoxBedrockShearWaveVelocity.Text = selectedBedrockShearWaveVelocity.ToString();

                    ComboBoxChart1Content.SelectedItem = selectedChart1Content;
                    ComboBoxChart2Content.SelectedItem = selectedChart2Content;

                    viewModel.Update();
                }
            }
        }

        private void ComboBoxLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (viewModel != null)
            {
                if (ComboBoxLevel.SelectedItem != null)
                { 
                    int selectedLevel = (int)ComboBoxLevel.SelectedItem;
                    viewModel.SelectedGroundMassCollection1 = viewModel.DataGridMassLayers[viewModel.SelectedGroundNo - 1][0];
                    viewModel.SelectedGroundMassCollection2 = viewModel.DataGridMassLayers[viewModel.SelectedGroundNo - 1][1];
                    if (selectedLevel == 1)
                    { viewModel.SelectedGroundMassCollection = viewModel.SelectedGroundMassCollection1; }
                    else if (selectedLevel == 2)
                    { viewModel.SelectedGroundMassCollection = viewModel.SelectedGroundMassCollection2; }

                    viewModel.Update();
                    DataGridGroundMass?.Items.Refresh();
                }
            }
        }

        private void GroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
                {
                    UpdateViewModelCollection(viewModel.ShallowSoilTypes, selectedGroundNo, viewModel.ShallowSoilType);
                    UpdateViewModelCollection(viewModel.CalculationMethods, selectedGroundNo, viewModel.CalculationMethod);
                    UpdateViewModelCollection(viewModel.Chart1Contents, selectedGroundNo, viewModel.Chart1Content);
                    UpdateViewModelCollection(viewModel.Chart2Contents, selectedGroundNo, viewModel.Chart2Content);
                }
                viewModel.Update();
                DataGridGroundMass?.Items.Refresh();
            }
        }

        private void GroundTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
                {
                    if (sender == TextBoxGroundRef)
                    {
                        UpdateViewModelCollection(viewModel.GroundRefs, selectedGroundNo, TextBoxGroundRef.Text);
                    }
                    else if (sender == TextBoxGroundTopAltitude && double.TryParse(TextBoxGroundTopAltitude.Text, out double altitude))
                    {
                        UpdateViewModelCollection(viewModel.GroundTopAltitudes, selectedGroundNo, altitude);
                    }
                    else if (sender == TextBoxGroundWaterTableAltitude && double.TryParse(TextBoxGroundWaterTableAltitude.Text, out double groundWaterTableAltitude))
                    {
                        UpdateViewModelCollection(viewModel.GroundWaterTableAltitudes, selectedGroundNo, groundWaterTableAltitude);
                    }
                    else if (sender == TextBoxGroundStressAltitude && double.TryParse(TextBoxGroundStressAltitude.Text, out double stressAltitude))
                    {
                        UpdateViewModelCollection(viewModel.StressAltitudes, selectedGroundNo, stressAltitude);
                    }
                    else if (sender == TextBoxGroundAcceleration1 && double.TryParse(TextBoxGroundAcceleration1.Text, out double groundAcceleration1))
                    {
                        UpdateViewModelCollection(viewModel.GroundAccelerations1, selectedGroundNo, groundAcceleration1);
                    }
                    else if (sender == TextBoxGroundAcceleration2 && double.TryParse(TextBoxGroundAcceleration2.Text, out double groundAcceleration2))
                    {
                        UpdateViewModelCollection(viewModel.GroundAccelerations2, selectedGroundNo, groundAcceleration2);
                    }
                    viewModel.Update();
                    DataGridGroundMass?.Items.Refresh();
                }
            }
        }

        private bool _isUpdatingValues = true;

        private void GroundTopAltitudeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel && _isUpdatingValues == true)
            {
                _isUpdatingValues = false;
                viewModel.GroundWaterGLDepth = viewModel.GroundWaterTableAltitude - viewModel.GroundTopAltitude;
                viewModel.StressGLDepth = viewModel.StressAltitude - viewModel.GroundTopAltitude;
                if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
                {
                    UpdateViewModelCollection(viewModel.GroundTopAltitudes, selectedGroundNo, viewModel.GroundTopAltitude);
                    UpdateViewModelCollection(viewModel.GroundWaterTableAltitudes, selectedGroundNo, viewModel.GroundWaterTableAltitude);
                    UpdateViewModelCollection(viewModel.StressAltitudes, selectedGroundNo, viewModel.StressAltitude);
                }
                viewModel.RecalculateAltitude();
                _isUpdatingValues = true;
                viewModel.Update();
            }
        }

        private void TextBoxGroundWaterTableAltitude_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel && _isUpdatingValues == true)
            {
                _isUpdatingValues = false;
                viewModel.GroundWaterGLDepth = viewModel.GroundWaterTableAltitude - viewModel.GroundTopAltitude;
                if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
                {
                    UpdateViewModelCollection(viewModel.GroundTopAltitudes, selectedGroundNo, viewModel.GroundTopAltitude);
                    UpdateViewModelCollection(viewModel.GroundWaterTableAltitudes, selectedGroundNo, viewModel.GroundWaterTableAltitude);
                    UpdateViewModelCollection(viewModel.StressAltitudes, selectedGroundNo, viewModel.StressAltitude);
                }
                _isUpdatingValues = true;
                viewModel.Update();
            }
        }

        private void TextBoxGroundStressAltitude_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel && _isUpdatingValues == true)
            {
                _isUpdatingValues = false;
                viewModel.StressGLDepth = viewModel.StressAltitude - viewModel.GroundTopAltitude;
                if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
                {
                    UpdateViewModelCollection(viewModel.GroundTopAltitudes, selectedGroundNo, viewModel.GroundTopAltitude);
                    UpdateViewModelCollection(viewModel.GroundWaterTableAltitudes, selectedGroundNo, viewModel.GroundWaterTableAltitude);
                    UpdateViewModelCollection(viewModel.StressAltitudes, selectedGroundNo, viewModel.StressAltitude);
                }
                _isUpdatingValues = true;
                viewModel.Update();
            }
        }

        private void TextBoxGroundWaterGLDepth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel && _isUpdatingValues == true)
            {
                _isUpdatingValues = false;
                viewModel.GroundWaterTableAltitude = viewModel.GroundWaterGLDepth + viewModel.GroundTopAltitude;
                if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
                {
                    UpdateViewModelCollection(viewModel.GroundTopAltitudes, selectedGroundNo, viewModel.GroundTopAltitude);
                    UpdateViewModelCollection(viewModel.GroundWaterTableAltitudes, selectedGroundNo, viewModel.GroundWaterTableAltitude);
                    UpdateViewModelCollection(viewModel.StressAltitudes, selectedGroundNo, viewModel.StressAltitude);
                }
                _isUpdatingValues = true;
                viewModel.Update();
            }
        }

        private void TextBoxStressGLDepth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is GroundLayerViewModel viewModel && _isUpdatingValues == true)
            {
                _isUpdatingValues = false;
                viewModel.StressAltitude = viewModel.StressGLDepth + viewModel.GroundTopAltitude;
                if (int.TryParse(ComboBoxGroundNo.SelectedItem?.ToString(), out int selectedGroundNo))
                {
                    UpdateViewModelCollection(viewModel.GroundTopAltitudes, selectedGroundNo, viewModel.GroundTopAltitude);
                    UpdateViewModelCollection(viewModel.GroundWaterTableAltitudes, selectedGroundNo, viewModel.GroundWaterTableAltitude);
                    UpdateViewModelCollection(viewModel.StressAltitudes, selectedGroundNo, viewModel.StressAltitude);
                }
                _isUpdatingValues = true;
                viewModel.Update();
            }
        }

        private void UpdateViewModelCollection<T>(IList<T> collection, int selectedGroundNo, T value)
        {
            if (selectedGroundNo >= 1 && selectedGroundNo <= collection.Count)
            {
                collection[selectedGroundNo - 1] = value;
            }
        }

        private void SliderEngineeringBedrock_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DataContext is GroundLayerViewModel viewModel)
            {
                int value = (int)SliderEngineeringBedrock.Value;
                int n = viewModel.SelectedGroundLayerCollection.Count;

                // i行のチェックボックスの状態が変更されたとき、1～i-1行のチェックボックスを有効化、i+1行目以降のチェックボックスを無効化
                for (int i = 0; i < n; i++)
                {
                    if (n - 1 - i < value)
                    {
                        viewModel.SelectedGroundLayerCollection[i].IsEngineeringBedrock = true;
                    }
                    else
                    {
                        viewModel.SelectedGroundLayerCollection[i].IsEngineeringBedrock = false;
                    }
                }
                viewModel.Update();
            }
        }

        private void DataGridGroundMass_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)

        {
            // 変更が行われたセルの行のインデックスを取得
            int rowIndex = e.Row.GetIndex();

            if (e.EditAction == DataGridEditAction.Commit)
            {
                var editedTextBox = e.EditingElement as TextBox;
                if (editedTextBox != null)
                {
                    var editedValue = editedTextBox.Text;

                    // 編集された値が null もしくは空の場合に備える
                    if (!string.IsNullOrEmpty(editedValue))
                    {
                        if (double.TryParse(editedValue, out double doubleValue))
                        {
                            DataGridColumn column = e.Column;
                            if (column != null && column is DataGridBoundColumn boundColumn)
                            {
                                System.Windows.Data.Binding binding = (boundColumn.Binding as System.Windows.Data.Binding);
                                if (binding != null)
                                {
                                    string bindingPath = binding.Path.Path;
                                    // bindingPath にはバインディング名が含まれます
                                    Console.WriteLine($"Binding Name: {bindingPath}");

                                    int levelIndex = 0;
                                    if (viewModel.SelectedLevel == 1) { levelIndex = 1; }
                                    else if (viewModel.SelectedLevel == 2) { levelIndex = 0; }

                                    if (bindingPath == "Spacing")
                                    {
                                        viewModel.DataGridMassLayers[viewModel.SelectedGroundNo - 1][levelIndex][rowIndex].Spacing = doubleValue;
                                    }
                                    else if (bindingPath == "Fc")
                                    {
                                        viewModel.DataGridMassLayers[viewModel.SelectedGroundNo - 1][levelIndex][rowIndex].Fc = doubleValue;
                                    }
                                    else if (bindingPath == "NValue")
                                    {
                                        viewModel.DataGridMassLayers[viewModel.SelectedGroundNo - 1][levelIndex][rowIndex].NValue = doubleValue;
                                    }
                                    else if (bindingPath == "VS0")
                                    {
                                        viewModel.DataGridMassLayers[viewModel.SelectedGroundNo - 1][levelIndex][rowIndex].VS0 = doubleValue;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
