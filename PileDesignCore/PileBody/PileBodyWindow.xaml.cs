using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using static PileDesignCore.PileBodyViewModel;
using System;
using System.Windows.Media;
using System.Windows.Shapes;
using PileDesignCore.Shared;

namespace PileDesignCore
{
    /// <summary>
    /// PileBodyWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class PileBodyWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();
        private PileBodyViewModel viewModel;

        public PileBodyWindow(PileBodyViewModel sharedViewModel)
        {
            InitializeComponent();
            viewModel = sharedViewModel;  // Initialize viewModel here
            DataContext = sharedViewModel;
            viewModel.SelectedPileBodyCollection = viewModel.DataGridPileBody[viewModel.SelectedPileBodyNo - 1];

            DataGridPileBody.Loaded += DataGridPileBody_Loaded;
            viewModel.RecalculateDataGridPileBodyCompleted += ViewModel_RecalculateDataGridPileBodyCompleted;

            // 初期値を保存
            SavePreviousPropertyValues(sharedViewModel);

            //Chart関連
            Chart1v.Child = sharedViewModel.chart1;

            // コンボボックスにデータをバインド
            ComboBoxPresetSettlementParameters.ItemsSource = sharedViewModel.PileTipSettlementPresetParameterNames;
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

        private void SavePreviousPropertyValues(PileBodyViewModel viewModel)
        {
            // 全てのプロパティの前回の値を保存
            PropertyInfo[] properties = typeof(PileBodyViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        private void RestorePreviousPropertyValues(PileBodyViewModel viewModel)
        {
            // 全てのプロパティを前回の値に戻す
            PropertyInfo[] properties = typeof(PileBodyViewModel).GetProperties();
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

        private void DataGridPileBody_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        private void PileBodyCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // 行が削除された場合、すべての行番号を再計算する
                for (int i = 0; i < DataGridPileBody.Items.Count; i++)
                {
                    DataGridRow row = DataGridPileBody.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                    if (row != null)
                    {
                        row.Header = (i + 1).ToString();
                    }
                }
            }
        }

        private void DataGridPileBody_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataGridPileBody.ItemsSource is ObservableCollection<PileBodyDataItem> observableCollection)
            {
                observableCollection.CollectionChanged += PileBodyCollection_CollectionChanged;
            }
        }

        // Handle the button click event for the "削除" (Delete) button
        private void ButtonDelete_Click(object sender, RoutedEventArgs e)
        {
            if (DataGridPileBody.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridPileBody.SelectedItem is PileBodyDataItem selectedItem)
                {
                    viewModel.SelectedPileBodyCollection.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    System.Windows.MessageBox.Show("選択されたアイテムの型が正しくありません。");
                }
                viewModel.RecalculateDataGridPileBody();
                
                DataGridPileBody.Items.Refresh();
                DrawShapes();
            }
        }

        // 区間追加ボタンがクリックされたときの処理
        private void ButtonAddPileBody_Click(object sender, RoutedEventArgs e)
        {
            {
                if (viewModel.SelectedPileBodyCollection == null)
                {
                    viewModel.SelectedPileBodyCollection = new ObservableCollection<PileBodyDataItem>();
                }
                viewModel.SelectedPileBodyCollection.Add(new PileBodyDataItem(viewModel));

                viewModel.RecalculateDataGridPileBody();
                DrawShapes();
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
            PileBodyViewModel viewModel = (PileBodyViewModel)DataContext;
            RestorePreviousPropertyValues(viewModel);
            this.Close();
        }

        private bool initialSelection = true;

        private void ComboBoxPileBodyNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initialSelection)
            {
                initialSelection = false;
                return;
            }

            if (DataContext is PileBodyViewModel viewModel)
            {
                if (ComboBoxPileBodyNo.SelectedItem != null)
                {
                    int selectedPileBodyNo = (int)ComboBoxPileBodyNo.SelectedItem;
                    string selectedPileBodyRef = viewModel.PileBodyRefs[selectedPileBodyNo - 1];
                    string selectedPileBodyType = viewModel.SelectedPileBodyTypes[selectedPileBodyNo - 1];
                    string selectedPileConstructionType = viewModel.SelectedPileConstructionTypes[selectedPileBodyNo - 1];
                    string selectedPileTopType = viewModel.SelectedPileTopTypes[selectedPileBodyNo - 1];
                    double selectedPileToeDia = viewModel.PileToeDias[selectedPileBodyNo - 1];
                    double selectedTipNonPermability = viewModel.TipNonPermabilities[selectedPileBodyNo - 1];
                    string selectedTipStyle = viewModel.SelectedTipStyles[selectedPileBodyNo - 1];
                    double selectedEmbedmentIntoBearingSoil = viewModel.EmbedmentIntoBearingSoils[selectedPileBodyNo - 1];
                    double selectedPileInnerDia = viewModel.PileInnerDias[selectedPileBodyNo - 1];
                    double selectedSettlePileToeDia = viewModel.SettlePileToeDias[selectedPileBodyNo - 1];
                    double selectedSettleAlpha = viewModel.SettleAlphas[selectedPileBodyNo - 1];
                    double selectedSettleN = viewModel.SettleNs[selectedPileBodyNo - 1];

                    viewModel.PileBodyRef = selectedPileBodyRef;
                    viewModel.SelectedPileBodyType = selectedPileBodyType;
                    viewModel.SelectedPileConstructionType = selectedPileConstructionType;
                    viewModel.SelectedPileTopType = selectedPileTopType;
                    viewModel.PileToeDia = selectedPileToeDia;
                    viewModel.TipNonPermability = selectedTipNonPermability;
                    viewModel.SelectedTipStyle = selectedTipStyle;
                    viewModel.EmbedmentIntoBearingSoil = selectedEmbedmentIntoBearingSoil;
                    viewModel.PileInnerDia = selectedPileInnerDia;

                    viewModel.SettlePileToeDia = selectedSettlePileToeDia;
                    viewModel.SettleAlpha = selectedSettleAlpha;
                    viewModel.SettleN = selectedSettleN;

                    // 選択された番号に対応する ObservableCollection を DataGrid の ItemsSource に設定
                    viewModel.SelectedPileBodyCollection = viewModel.DataGridPileBody[selectedPileBodyNo - 1];

                    TextBoxPileBodyRef.Text = selectedPileBodyRef;

                    ComboBoxPileBodyType.Text = selectedPileBodyType;
                    TextBoxPileToeDia.Text = selectedPileToeDia.ToString();
                    TextBoxPrecastPileTipNonPermability.Text = selectedTipNonPermability.ToString();
                    ComboBoxConcreteTipStyle.Text = selectedTipStyle;
                    ComboBoxSteelTipStyle.Text = selectedTipStyle;
                    TextBoxSteelPileTipNonPermability.Text = selectedTipNonPermability.ToString();
                    TextBoxEmbedmentIntoBearingSoil.Text = selectedEmbedmentIntoBearingSoil.ToString();
                    TextBoxPileInnerDia.Text = selectedPileInnerDia.ToString();
                    TextBoxSettlePileToeDia.Text = selectedSettlePileToeDia.ToString();
                    TextBoxSettleAlpha.Text = selectedSettleAlpha.ToString();
                    TextBoxSettleN.Text = selectedSettleN.ToString();
                }
            }
        }

        private void ButtonEditPileHead_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PileBodyViewModel)
            {
                if (int.TryParse(ComboBoxPileBodyNo.SelectedItem?.ToString(), out int selectedPileBodyNo))
                {
                    var pileTopWindow = new PileTopWindow(viewModel.PileTop,
                        selectedPileBodyNo,
                        viewModel.SelectedPileTopTypes[selectedPileBodyNo - 1]//,
                        //viewModel.SelectedPileBodyCollection[0].PileSection
                        );
                    pileTopWindow.ShowDialog();
                }
            }
        }

        private void ButtonPileSectionEdit_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PileBodyViewModel)
            {
                if (int.TryParse(ComboBoxPileBodyNo.SelectedItem?.ToString(), out int selectedPileBodyNo))
                {
                    if (viewModel.SelectedPileBodyTypes[selectedPileBodyNo - 1] == "場所打ち鉄筋コンクリート杭" ||
                        viewModel.SelectedPileBodyTypes[selectedPileBodyNo - 1] == "場所打ち鋼管コンクリート杭" ||
                        viewModel.SelectedPileBodyTypes[selectedPileBodyNo - 1] == "既製コンクリート杭" ||
                        viewModel.SelectedPileBodyTypes[selectedPileBodyNo - 1] == "鋼管杭")
                    {
                        // イベントの送信元であるボタンを取得します
                        System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
                        if (button != null)
                        {
                            // ボタンが含まれる親要素である行を取得します
                            DataGridRow row = FindVisualParent<DataGridRow>(button);
                            if (row != null)
                            {
                                // データグリッド内の行インデックスを取得します
                                int rowIndex = DataGridPileBody.ItemContainerGenerator.IndexFromContainer(row);

                                var pileSectionWindow = new PileSectionWindow(
                                    viewModel.SelectedPileBodyCollection[rowIndex].PileSection,
                                    selectedPileBodyNo,
                                    viewModel.SelectedPileBodyTypes[selectedPileBodyNo - 1]);
                                pileSectionWindow.ShowDialog();

                                DrawShapes();
                            }
                        }
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show("杭体タイプを選択してください。");
                    }
                }
            }
        }

        private T FindVisualParent<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T parent)
                {
                    return parent;
                }
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        private void PileBodyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
        }


        private void PileBodyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is PileBodyViewModel viewModel)
            {
                if (int.TryParse(ComboBoxPileBodyNo.SelectedItem?.ToString(), out int selectedPileBodyNo))
                {
                    if (sender == TextBoxPileBodyRef)
                    {
                        UpdateViewModelCollection(viewModel.PileBodyRefs, selectedPileBodyNo, TextBoxPileBodyRef.Text);
                    }
                    else if (sender == TextBoxPileToeDia && double.TryParse(TextBoxPileToeDia.Text, out double toeDia))
                    {
                        UpdateViewModelCollection(viewModel.PileToeDias, selectedPileBodyNo, toeDia);
                        DrawShapes();
                    }
                    else if (sender == TextBoxPrecastPileTipNonPermability && double.TryParse(TextBoxPrecastPileTipNonPermability.Text, out double precastPileTipNonPermability))
                    {
                        UpdateViewModelCollection(viewModel.TipNonPermabilities, selectedPileBodyNo, precastPileTipNonPermability);
                    }
                    else if (sender == TextBoxSteelPileTipNonPermability && double.TryParse(TextBoxSteelPileTipNonPermability.Text, out double steelPileTipNonPermability))
                    {
                        UpdateViewModelCollection(viewModel.TipNonPermabilities, selectedPileBodyNo, steelPileTipNonPermability);
                    }
                    else if (sender == TextBoxSettleAlpha && double.TryParse(TextBoxSettleAlpha.Text, out double settleAlpha))
                    {
                        UpdateViewModelCollection(viewModel.SettleAlphas, selectedPileBodyNo, settleAlpha);
                    }
                    else if (sender == TextBoxSettleN && double.TryParse(TextBoxSettleN.Text, out double settleN))
                    {
                        UpdateViewModelCollection(viewModel.SettleNs, selectedPileBodyNo, settleN);
                    }

                    //杭先端沈下チャート要素クリアコマンド
                    viewModel.ChartClearCmd();
                    viewModel.AddComponent(viewModel.SettleAlphas[selectedPileBodyNo - 1], viewModel.SettleNs[selectedPileBodyNo - 1]);

                }
            }
        }

        private void ComboBoxPileTopType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is PileBodyViewModel viewModel)
            {
                if (int.TryParse(ComboBoxPileBodyNo.SelectedItem?.ToString(), out int selectedPileBodyNo))
                {
                    viewModel.SelectedPileTopTypes[selectedPileBodyNo - 1] = (string)ComboBoxPileTopType.SelectedItem;
                    viewModel.PileTop.SelectedPileTopType = (string)ComboBoxPileTopType.SelectedItem;
                }
            }
        }

        private void RecalculateTipNonPermability_TextChanged(object sender, TextChangedEventArgs e)
        {
            RecalculateNonPermability();
        }
        private void RecalculateTipNonPermability_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RecalculateNonPermability();
        }

        internal void RecalculateNonPermability()
        {
            if (viewModel != null)
            {
                // viewModelの型を取得する
                Type viewModelType = viewModel.GetType();

                // SelectedPileBodyTypeプロパティを取得する
                PropertyInfo propertyInfo1 = viewModelType.GetProperty("SelectedPileBodyType");
                PropertyInfo propertyInfo2 = viewModelType.GetProperty("SelectedPileConstructionType");

                // SelectedPileBodyTypeプロパティが存在するかどうかを確認する
                if (propertyInfo1 != null && propertyInfo2 != null)

                {
                    if (viewModel.SelectedPileBodyType == "既製コンクリート杭" &&
                        viewModel.SelectedPileConstructionType == "打込み杭")
                    {
                        if (viewModel.SelectedTipStyle == "閉端杭")
                        {
                            viewModel.TipNonPermability = 1.0;
                        }
                        else if (viewModel.SelectedTipStyle == "開端杭")
                        {
                            double _lB = viewModel.EmbedmentIntoBearingSoil;
                            double _dI = viewModel.PileInnerDia;
                            if (_dI < 0.01) { _dI = 0.01; }
                            if (_lB < 0.01) { _lB = 0.01; }
                            if (_lB / _dI <= 5)
                            {
                                viewModel.TipNonPermability = 0.16 * (_lB / _dI);
                            }
                            else
                            {
                                viewModel.TipNonPermability = 0.80;
                            }
                        }
                    }
                    if (viewModel.SelectedPileBodyType == "鋼管杭" &&
                        viewModel.SelectedPileConstructionType == "回転貫入杭")
                    {
                        if (viewModel.SelectedTipStyle == "閉端杭")
                        {
                            viewModel.TipNonPermability = 1.0;
                        }
                        else if (viewModel.SelectedTipStyle == "開端杭")
                        {
                            viewModel.TipNonPermability = 0.80;
                        }
                    }
                }
            }
        }

        private void ComboBoxPileBodyType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is PileBodyViewModel)
            {
                if (int.TryParse(ComboBoxPileBodyNo.SelectedItem?.ToString(), out int selectedPileBodyNo))
                {
                    viewModel.SelectedPileBodyTypes[selectedPileBodyNo - 1] = viewModel.SelectedPileBodyType;
                }
                // 選択肢の更新はViewModelのsetterで自動的に行われる
            }
        }

        private void ComboBoxPileConstructionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is PileBodyViewModel)
            {
                if (int.TryParse(ComboBoxPileBodyNo.SelectedItem?.ToString(), out int selectedPileBodyNo))
                {
                    viewModel.SelectedPileConstructionTypes[selectedPileBodyNo - 1] = viewModel.SelectedPileConstructionType;
                }
            }
        }

        private void ComboBoxPresetSettlementParameters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (int.TryParse(ComboBoxPileBodyNo.SelectedItem?.ToString(), out int selectedPileBodyNo))
            {
                foreach (PileTipSettlementPresetParameter parameter in viewModel.PileTipSettlementPresetParameters)
                {
                    if (ComboBoxPresetSettlementParameters.SelectedItem.ToString().Contains(parameter.Name) &&
                        ComboBoxPresetSettlementParameters.SelectedItem.ToString().Contains(parameter.SoilType))
                    {
                        // dataSet.Name と dataSet.SoilType を含む場合に dataSets.Alpha と dataSets.N を取得する
                        viewModel.SettleAlphas[selectedPileBodyNo - 1] = parameter.Alpha;
                        viewModel.SettleAlpha = parameter.Alpha;
                        viewModel.SettleNs[selectedPileBodyNo - 1] = parameter.N;
                        viewModel.SettleN = parameter.N;
                        break;
                    }
                }
                viewModel.ChartClearCmd();
                viewModel.AddComponent(viewModel.SettleAlphas[selectedPileBodyNo - 1], viewModel.SettleNs[selectedPileBodyNo - 1]);
            }
        }

        private void UpdateViewModelCollection<T>(IList<T> collection, int selectedIndex, T value)
        {
            if (selectedIndex >= 1 && selectedIndex <= collection.Count)
            {
                collection[selectedIndex - 1] = value;
            }
        }

        private void ViewModel_RecalculateDataGridPileBodyCompleted(object sender, EventArgs e)
        {
            DrawShapes();
        }

        private void DrawShapes()
        {
            if (Canvas != null)
            {
                if( Canvas.Children != null)
                {
                    Canvas.Children.Clear();
                }
                
                // Canvas上に図形を描画するためのサイズと位置を定義します。
                double canvasWidth = Canvas.ActualWidth;
                double canvasHeight = Canvas.ActualHeight;
                if(viewModel.SelectedPileBodyCollection.Count != 0)
                {
                    double pileLength = viewModel.SelectedPileBodyCollection[viewModel.SelectedPileBodyCollection.Count - 1].SegmentDepth;
                    double ratio = canvasHeight / pileLength;

                    for (int i = 0; i < viewModel.SelectedPileBodyCollection.Count; i++)
                    {
                        double _segmentLength = viewModel.SelectedPileBodyCollection[i].SegmentLength;
                        double _segmentDepth = viewModel.SelectedPileBodyCollection[i].SegmentDepth;
                        double _pilediameter = 0.0;
                        if (viewModel.SelectedPileBodyCollection[i].PileSection != null)
                        {
                            _pilediameter = viewModel.SelectedPileBodyCollection[i].PileSection.PileDiameter / 1000.0;
                        }

                        if (i == 0)
                        {
                            System.Windows.Shapes.Line horLine0 = new System.Windows.Shapes.Line
                            {
                                Stroke = NikkenBrush.SkyBlue, // 線の色
                                StrokeThickness = 1, // 線の太さ
                                X1 = canvasWidth / 2.0 - 0.1 * ratio,
                                X2 = canvasWidth / 2.0 + 0.1 * ratio,
                                Y1 = 0.0,
                                Y2 = 0.0
                            };
                            // CanvasにLineを追加します
                            Canvas.Children.Add(horLine0);
                        }

                        System.Windows.Shapes.Line horLine = new System.Windows.Shapes.Line
                        {
                            Stroke = NikkenBrush.SkyBlue, // 線の色
                            StrokeThickness = 1, // 線の太さ
                            X1 = canvasWidth / 2.0 - 0.1 * ratio,
                            X2 = canvasWidth / 2.0 + 0.1 * ratio,
                            Y1 = _segmentDepth * ratio,
                            Y2 = _segmentDepth * ratio
                        };
                        // CanvasにLineを追加します
                        Canvas.Children.Add(horLine);


                        if (_pilediameter < 0.01) // 杭径が極小の場合
                        {
                            System.Windows.Shapes.Line vertLine = new System.Windows.Shapes.Line
                            {
                                Stroke = NikkenBrush.SkyBlue, // 線の色
                                StrokeThickness = 1, // 線の太さ
                                X1 = canvasWidth / 2.0,
                                X2 = canvasWidth / 2.0,
                                Y1 = _segmentDepth * ratio,
                                Y2 = (_segmentDepth - _segmentLength) * ratio
                            };
                            // CanvasにLineを追加します
                            Canvas.Children.Add(vertLine);
                        }

                        else // 杭径が一般の場合
                        {
                            Rectangle rectangle = new Rectangle
                            {
                                Width = _pilediameter * ratio,
                                Height = _segmentLength * ratio,
                                //Fill = Brushes.White,
                                Stroke = NikkenBrush.SkyBlue, // 線の色
                            };
                            Canvas.SetLeft(rectangle, canvasWidth / 2.0 - _pilediameter / 2.0 * ratio); // X座標
                            Canvas.SetTop(rectangle, (_segmentDepth - _segmentLength) * ratio); // Y座標
                            Canvas.Children.Add(rectangle); // Canvasに追加
                        }
                    }

                    if (viewModel.SelectedPileBodyCollection.Count > 0)
                    {
                        int lastIndex = viewModel.SelectedPileBodyCollection.Count - 1;
                        if (viewModel.SelectedPileBodyCollection[lastIndex]?.PileSection?.PileDiameter != null)
                        {
                            double _bottomSegmentDia = viewModel.SelectedPileBodyCollection[lastIndex].PileSection.PileDiameter / 1000.0;
                            double _pileToeDia = viewModel.PileToeDia / 1000.0;
                            if (viewModel.SelectedPileConstructionType == "場所打ちコンクリート杭" && _pileToeDia > _bottomSegmentDia)
                            {
                                // ポリラインの各頂点の座標を設定します
                                PointCollection points = new PointCollection();
                                double _height = (_pileToeDia - _bottomSegmentDia) / 2.0 * Math.Tan(78 * Math.PI / 180);
                                points.Add(new Point(canvasWidth / 2.0 - _bottomSegmentDia / 2.0 * ratio, (pileLength - 0.3 - _height) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, (pileLength - 0.3) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, (pileLength - 0.3) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio, (pileLength - 0.3 - _height) * ratio));
                                // ポリラインを作成します
                                System.Windows.Shapes.Polygon polygon = new System.Windows.Shapes.Polygon
                                {
                                    Stroke = NikkenBrush.SkyBlue, // 線の色
                                    Fill = Brushes.White, // 内部の色
                                    StrokeThickness = 1, // 線の太さ
                                    Points = points // ポリラインの頂点座標を設定
                                };
                                // Canvasにポリラインを追加
                                Canvas.Children.Add(polygon);

                                {
                                    System.Windows.Shapes.Line line = new System.Windows.Shapes.Line
                                    {
                                        Stroke = NikkenBrush.SkyBlue, // 線の色
                                        StrokeThickness = 1, // 線の太さ
                                        X1 = canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio,
                                        X2 = canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio,
                                        Y1 = (pileLength - 0.3) * ratio,
                                        Y2 = (pileLength - 0.3) * ratio
                                    };
                                    // CanvasにLineを追加します
                                    Canvas.Children.Add(line);
                                }

                                for (int i = -1; i < 2; i += 2)
                                {
                                    System.Windows.Shapes.Line line = new System.Windows.Shapes.Line
                                    {
                                        Stroke = NikkenBrush.SkyBlue, // 線の色
                                        StrokeThickness = 1, // 線の太さ
                                        StrokeDashArray = new DoubleCollection(new double[] { 2 }), // 破線の設定
                                        X1 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        X2 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        Y1 = (pileLength - 0.3 - _height) * ratio,
                                        Y2 = (pileLength) * ratio
                                    };
                                    // CanvasにLineを追加します
                                    Canvas.Children.Add(line);
                                }

                            }

                            if ((viewModel.SelectedPileConstructionType == "埋込み杭（プレボーリング）" && _pileToeDia > _bottomSegmentDia) ||
                                (viewModel.SelectedPileConstructionType == "埋込み杭（中掘り）" && _pileToeDia > _bottomSegmentDia))
                            {
                                // ポリラインの各頂点の座標を設定します
                                PointCollection points = new PointCollection();
                                double _height = _pileToeDia * 2;
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, (pileLength - _height) * ratio));
                                points.Add(new Point(canvasWidth / 2.0 - _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, pileLength * ratio));
                                points.Add(new Point(canvasWidth / 2.0 + _pileToeDia / 2.0 * ratio, (pileLength - _height) * ratio));
                                // ポリラインを作成します
                                System.Windows.Shapes.Polygon polygon = new System.Windows.Shapes.Polygon
                                {
                                    Stroke = NikkenBrush.SkyBlue, // 線の色
                                    Fill = Brushes.White, // 内部の色
                                    StrokeThickness = 1, // 線の太さ
                                    Points = points // ポリラインの頂点座標を設定
                                };
                                // Canvasにポリラインを追加
                                Canvas.Children.Add(polygon);


                                for (int i = -1; i < 2; i += 2)
                                {
                                    System.Windows.Shapes.Line line = new System.Windows.Shapes.Line
                                    {
                                        Stroke = NikkenBrush.SkyBlue, // 線の色
                                        StrokeThickness = 1, // 線の太さ
                                        StrokeDashArray = new DoubleCollection(new double[] { 2 }), // 破線の設定
                                        X1 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        X2 = canvasWidth / 2.0 + _bottomSegmentDia / 2.0 * ratio * i,
                                        Y1 = (pileLength - _height) * ratio,
                                        Y2 = (pileLength) * ratio
                                    };
                                    // CanvasにLineを追加します
                                    Canvas.Children.Add(line);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

