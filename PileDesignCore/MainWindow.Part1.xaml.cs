using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static PileDesignCore.MoveCopyWindow;
using static PileDesignCore.AxialForceWindow;
using static PileDesignCore.EditPileLayoutWindow;

namespace PileDesignCore
{
    /// <summary>
    /// 杭配置のコードビハインド
    /// </summary>
    public partial class MainWindow : Window
    {
        // キャンバスサイズ変化時のイベントハンドラ 
        private void CanvasLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CanvasHeight = CanvasLayout.ActualHeight;
            CanvasWidth = CanvasLayout.ActualWidth;
            UpdateCanvas(DataGridPileLayout);
        }

        // 行番号を設定するメソッド
        private void DataGridPileLayout_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        //杭レイアウトコレクションが変化した場合のメソッド
        private void PileLayoutCollection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // 行が削除された場合、すべての行番号を再計算する
            }

            if (e.Action != NotifyCollectionChangedAction.Add)
            {
                // コレクションが変更された場合に DataGrid の表示を更新する
                DataGridPileLayout.Items.Refresh();
            }
            //RenderPileLayout(DataGridPileLayout);
            UpdateCanvas(DataGridPileLayout);
        }

        // 杭レイアウトデータグリッドがロードされた場合のメソッド
        private void DataGridPileLayout_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataGridPileLayout.ItemsSource is ObservableCollection<PileLayoutDataItem> observableCollection)
            {
                observableCollection.CollectionChanged += PileLayoutCollection_CollectionChanged;
            }
        }

        // データグリッドのセルが編集された場合のメソッド
        private void ButtonPileLayoutDelete_Click(object sender, RoutedEventArgs e)
        {
            if (DataGridPileLayout.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridPileLayout.SelectedItem is PileLayoutDataItem selectedItem)
                {
                    ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
                    viewModel.PileLayoutViewModel.PileLayoutCollection.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    MessageBox.Show("選択されたアイテムの型が正しくありません。");
                }
            }
        }

        // データグリッドのセルが編集された場合のメソッド
        private void ButtonAddPile_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as ApplicationViewModel;
            viewModel.PileLayoutViewModel.PileLayoutCollection.Add(new PileLayoutDataItem());
            NumberingNewPileNumber(false);
        }

        // データグリッドのセルが編集された場合のメソッド
        private void NumberingNewPileNumber(bool isCopy)
        {

            // ObservableCollection<PileLayoutDataItem> を取得
            var collectionView = CollectionViewSource.GetDefaultView(DataGridPileLayout.ItemsSource) as IEditableCollectionView;

            // コレクションビューがトランザクション中でないかをチェック
            if (!collectionView.IsAddingNew && !collectionView.IsEditingItem)
            {
                ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
                Collection<PileLayoutDataItem> _collection = viewModel.PileLayoutViewModel.PileLayoutCollection;
                bool isSolved = false;
                if (_collection.Count == 1)
                {
                    _collection[0].PileNumber = 1;
                }
                else
                {
                    if (isCopy == false)
                    {
                        _collection[_collection.Count - 1].X = _collection[_collection.Count - 2].X + 10;
                        _collection[_collection.Count - 1].Y = _collection[_collection.Count - 2].Y;
                    }

                    for (int i = 0; i < _collection.Count; i++) // 番号0から
                    {
                        for (int j = 0; j < _collection.Count; j++)
                        {
                            if (_collection[j].PileNumber == i + 1) { break; }
                            if (j == _collection.Count - 1)
                            {
                                //_collection[_collection.Count - 1].PileNumber = _collection.Count;
                                _collection[_collection.Count - 1].PileNumber = i + 1;
                                isSolved = true;
                                break;
                            }
                        }
                        if (isSolved == true) { break; }
                    }
                }
                DataGridPileLayout.Items.Refresh();
            }
            //RenderPileLayout(DataGridPileLayout);
            UpdateCanvas(DataGridPileLayout);
            UpdatePerspectiveView();
        }

        //okボタンを押すメソッド
        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void UpdateCanvas(System.Windows.Controls.DataGrid datagrid)
        {
            if (CanvasLayout == null)
            { return; }

            CanvasLayout.Children.Clear();
            // 描画用パスをクリア
            drawingGeometryNode.Clear();
            UpdateAxes();
            UpdateEmbedment();
            UpdateNodes();
            UpdateSelectedNodes(datagrid);
            UpdateTickMarks();
            UpdateGridLines();
            UpdateDimensionLines();
        }

        private void UpdateEmbedment()
        {
            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
            if (viewModel == null || viewModel.EmbedmentViewModel == null || viewModel.EmbedmentViewModel.EmbedmentCollection == null)
            { return; }

            for (int i = 0; i < viewModel.EmbedmentViewModel.EmbedmentCollection.Count; i++)
            {
                double x1 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].X1;
                double x2 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].X2;
                double y1 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].Y1;
                double y2 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].Y2;

                double canvasX1 = 0.5 * CanvasWidth + (x1 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasX2 = 0.5 * CanvasWidth + (x2 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasY1 = 0.5 * CanvasHeight - (y1 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                double canvasY2 = 0.5 * CanvasHeight - (y2 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;

                // RectangleGeometryを作成
                Rect rect = new Rect(Math.Min(canvasX1, canvasX2), Math.Min(canvasY1, canvasY2), Math.Abs(canvasX2 - canvasX1), Math.Abs(canvasY2 - canvasY1));
                RectangleGeometry rectangleGeometry = new RectangleGeometry(rect);

                // Pathを使用してRectangleGeometryを描画
                Path path = new Path
                {
                    Data = rectangleGeometry,
                    Stroke = Brushes.Orange,
                    StrokeThickness = 1,
                    Name = $"Embedment_{i}"
                };

                // CanvasにPathを追加
                CanvasLayout.Children.Add(path);

                DrawDiagonal(canvasX1, canvasX2, canvasY1, canvasY2, $"Embedment_Diagonal_{i}_1");
                DrawDiagonal(canvasX1, canvasX2, canvasY2, canvasY1, $"Embedment_Diagonal_{i}_2");

                TextBlock textBlock = new TextBlock
                {
                    Text = $"{i + 1}",
                    FontSize = LabelSize,
                    Foreground = Brushes.Orange,
                    Name = $"Embedment_Label_{i}"
                };
                Canvas.SetLeft(textBlock, 0.5 * (canvasX1 + canvasX2));
                Canvas.SetTop(textBlock, 0.5 * (canvasY1 + canvasY2));
                CanvasLayout.Children.Add(textBlock);
            }
        }

        // 対角線を引くメソッド
        private void DrawDiagonal(double x1, double x2, double y1, double y2, string name)
        {
            Line line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2, // 目盛りの長さを設定する
                Stroke = Brushes.Orange,
                StrokeThickness = 0.5,
                Name = name,
                StrokeDashArray = new DoubleCollection() { 4, 2 } // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            };
            CanvasLayout.Children.Add(line);
        }

        private void UpdateSpecificEmbedment(int index, double newX1, double newY1, double newX2, double newY2)
        {
            // Embedment Path
            var embedmentPath = CanvasLayout.Children.OfType<Path>().FirstOrDefault(p => p.Name == $"Embedment_{index}");
            if (embedmentPath != null)
            {
                double canvasX1 = 0.5 * CanvasWidth + (newX1 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasX2 = 0.5 * CanvasWidth + (newX2 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasY1 = 0.5 * CanvasHeight - (newY1 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                double canvasY2 = 0.5 * CanvasHeight - (newY2 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;

                Rect rect = new Rect(Math.Min(canvasX1, canvasX2), Math.Min(canvasY1, canvasY2), Math.Abs(canvasX2 - canvasX1), Math.Abs(canvasY2 - canvasY1));
                embedmentPath.Data = new RectangleGeometry(rect);
            }

            // Embedment Diagonals
            var diagonal1 = CanvasLayout.Children.OfType<Line>().FirstOrDefault(l => l.Name == $"Embedment_Diagonal_{index}_1");
            var diagonal2 = CanvasLayout.Children.OfType<Line>().FirstOrDefault(l => l.Name == $"Embedment_Diagonal_{index}_2");
            if (diagonal1 != null)
            {
                double canvasX1 = 0.5 * CanvasWidth + (newX1 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasX2 = 0.5 * CanvasWidth + (newX2 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasY1 = 0.5 * CanvasHeight - (newY1 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                double canvasY2 = 0.5 * CanvasHeight - (newY2 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;

                diagonal1.X1 = canvasX1;
                diagonal1.Y1 = canvasY1;
                diagonal1.X2 = canvasX2;
                diagonal1.Y2 = canvasY2;
            }

            if (diagonal2 != null)
            {
                double canvasX1 = 0.5 * CanvasWidth + (newX1 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasX2 = 0.5 * CanvasWidth + (newX2 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasY1 = 0.5 * CanvasHeight - (newY1 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                double canvasY2 = 0.5 * CanvasHeight - (newY2 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;

                diagonal2.X1 = canvasX1;
                diagonal2.Y1 = canvasY2;
                diagonal2.X2 = canvasX2;
                diagonal2.Y2 = canvasY1;
            }

            // Embedment Label
            var embedmentLabel = CanvasLayout.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Name == $"Embedment_Label_{index}");
            if (embedmentLabel != null)
            {
                double canvasX1 = 0.5 * CanvasWidth + (newX1 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasX2 = 0.5 * CanvasWidth + (newX2 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasY1 = 0.5 * CanvasHeight - (newY1 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                double canvasY2 = 0.5 * CanvasHeight - (newY2 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;

                Canvas.SetLeft(embedmentLabel, 0.5 * (canvasX1 + canvasX2));
                Canvas.SetTop(embedmentLabel, 0.5 * (canvasY1 + canvasY2));
            }
        }

        // XYZ軸を加えるメソッド
        private void UpdateAxes()
        {
            double LineEndPos = 65;
            double canvasOX = 0.5 * CanvasWidth + (0.0 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
            double canvasOY = 0.5 * CanvasHeight - (0.0 - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
            Line lineX = new Line
            {
                X1 = 0,
                Y1 = canvasOY,
                X2 = CanvasWidth - LineEndPos,
                Y2 = canvasOY, // 目盛りの長さを設定する
                Stroke = Brushes.Red,
                StrokeThickness = 0.5,
                Name = "AxisX"
            };
            CanvasLayout.Children.Add(lineX);

            Line lineY = new Line
            {
                X1 = canvasOX,
                Y1 = 0,
                X2 = canvasOX,
                Y2 = CanvasHeight - LineEndPos, // 目盛りの長さを設定する
                Stroke = Brushes.Green,
                StrokeThickness = 0.5,
                Name = "AxisY"
            };
            CanvasLayout.Children.Add(lineY);
        }

        // 節点更新メソッド
        private void UpdateNodes()
        {
            if ((ApplicationViewModel)DataContext == null)
            { return; }
            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;

            foreach (PileLayoutDataItem pilelocation in viewModel.PileLayoutViewModel.PileLayoutCollection)
            {
                double canvasX = 0.5 * CanvasWidth + (pilelocation.X - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasY = 0.5 * CanvasHeight - (pilelocation.Y - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                AddEllipseToPath(drawingGeometryNode, canvasX, canvasY, acturalNodeSize);
                AddTextToGeometry(drawingGeometryNode, GetLabelText(pilelocation), canvasX, canvasY);
            }

            Path path = new Path
            {
                Stroke = Brushes.Red,
                StrokeThickness = 1,
                Data = drawingGeometryNode,
                Name = "Node"
            };

            CanvasLayout.Children.Add(path);
        }

        // 選択節点更新メソッド
        private void UpdateSelectedNodes(System.Windows.Controls.DataGrid datagrid)
        {
            // 選択された節点の描画
            drawingGeometry.Clear();

            foreach (PileLayoutDataItem pilelocation in datagrid.SelectedItems)
            {
                double canvasX = 0.5 * CanvasWidth + (pilelocation.X - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasY = 0.5 * CanvasHeight - (pilelocation.Y - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                AddEllipseToPath(drawingGeometry, canvasX, canvasY, acturalNodeSize);

                AddTextToGeometry(drawingGeometry, GetLabelText(pilelocation), canvasX, canvasY);
            }

            Path path0 = new Path
            {
                Fill = Brushes.Red,
                //StrokeThickness = 1,
                Data = drawingGeometry,
                Name = "Selection"
            };

            CanvasLayout.Children.Add(path0);
        }

        // ラベル取得メソッド
        private string GetLabelText(PileLayoutDataItem pilelocation)
        {
            if (LabelContent == "配置番号")
            {
                return pilelocation.PileNumber.ToString();
            }
            else if (LabelContent == "杭頭レベル(m)")
            {
                return pilelocation.PileTopAltitude.ToString("N3");
            }
            else if (LabelContent == "杭符号")
            {
                return pilelocation.PileBodyNo.ToString();
            }
            else if (LabelContent == "地盤符号")
            {
                return pilelocation.GroundNo.ToString();
            }
            else if (LabelContent == "群杭係数")
            {
                return pilelocation.GroupPileFactor.ToString("N3");
            }
            else if (LabelContent == "VL(kN)")
            {
                return pilelocation.AxialForceVL.ToString("N1");
            }
            else if (LabelContent == "VLadd(kN)")
            {
                return pilelocation.AxialForceVLAdditional.ToString("N1");
            }
            else if (LabelContent == "E1(kN)")
            {
                return pilelocation.AxialForceEX.ToString("N1");
            }
            else if (LabelContent == "E2(kN)")
            {
                return pilelocation.AxialForceEY.ToString("N1");
            }
            else if (LabelContent == "1-1(kN)")
            {
                return pilelocation.AxialForceLevel1s[0].ToString("N1");
            }
            else if (LabelContent == "1-2(kN)")
            {
                return pilelocation.AxialForceLevel1s[1].ToString("N1");
            }
            else if (LabelContent == "1-3(kN)")
            {
                return pilelocation.AxialForceLevel1s[2].ToString("N1");
            }
            else if (LabelContent == "1-4(kN)")
            {
                return pilelocation.AxialForceLevel1s[3].ToString("N1");
            }
            else if (LabelContent == "2-1(kN)")
            {
                return pilelocation.AxialForceLevel2s[0].ToString("N1");
            }
            else if (LabelContent == "2-2(kN)")
            {
                return pilelocation.AxialForceLevel2s[1].ToString("N1");
            }
            else if (LabelContent == "2-3(kN)")
            {
                return pilelocation.AxialForceLevel2s[2].ToString("N1");
            }
            else if (LabelContent == "2-4(kN)")
            {
                return pilelocation.AxialForceLevel2s[3].ToString("N1");
            }
            else if (LabelContent == "前後1")
            {
                if (pilelocation.IsFrontPiles[0] == true)
                { return "前"; }
                else { return "後"; }
            }
            else if (LabelContent == "前後2")
            {
                if (pilelocation.IsFrontPiles[1] == true)
                { return "前"; }
                else { return "後"; }
            }
            else if (LabelContent == "前後3")
            {
                if (pilelocation.IsFrontPiles[2] == true)
                { return "前"; }
                else { return "後"; }
            }
            else if (LabelContent == "前後4")
            {
                if (pilelocation.IsFrontPiles[3] == true)
                { return "前"; }
                else { return "後"; }
            }
            else { return ""; }
        }

        // RickMarkを描くメソッド
        private void UpdateTickMarks()
        {
            double TickLength = 35;
            double TextPos = TickLength - 5;
            //CanvasLayout.Children.Clear();
            var elementToRemove = CanvasLayout.Children.OfType<Path>().FirstOrDefault(p => p.Name == "TickMark");
            if (elementToRemove != null)
            {
                CanvasLayout.Children.Remove(elementToRemove);
            }
            double minX = CanvasCenterBuildingCoordinate.X - 0.5 * CanvasWidth / ScaleCanvasOnBuilding;
            double maxX = CanvasCenterBuildingCoordinate.X + 0.5 * CanvasWidth / ScaleCanvasOnBuilding;
            double minY = CanvasCenterBuildingCoordinate.Y - 0.5 * CanvasHeight / ScaleCanvasOnBuilding;
            double maxY = CanvasCenterBuildingCoordinate.Y + 0.5 * CanvasHeight / ScaleCanvasOnBuilding;

            // 目盛りを描画する
            int minTickX = (int)Math.Floor(minX / tickSpacing);
            int maxTickX = (int)Math.Ceiling(maxX / tickSpacing);
            int minTickY = (int)Math.Floor(minY / tickSpacing);
            int maxTickY = (int)Math.Ceiling(maxY / tickSpacing);

            for (int i = minTickX; i <= maxTickX; i++)
            {
                double actualX = 0.5 * CanvasWidth + (i * tickSpacing - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                Line line = new Line
                {
                    X1 = actualX,
                    Y1 = CanvasHeight - TickLength,
                    X2 = actualX,
                    Y2 = CanvasHeight, // 目盛りの長さを設定する
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Name = "TickMark"
                };
                CanvasLayout.Children.Add(line);

                SolidColorBrush solidColorBrush = Brushes.Gray;
                AddText(drawingGeometryNode, solidColorBrush, $"{i * tickSpacing}m", actualX, CanvasHeight - TextPos, "R", "B", -90.0);
                Path path = new Path
                {
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Data = drawingGeometryNode,
                    Name = "TickMark"
                };
                CanvasLayout.Children.Add(path);
            }

            for (int i = minTickY; i <= maxTickY; i++)
            {
                double actualY = 0.5 * CanvasHeight - (i * tickSpacing - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                Line line = new Line
                {
                    X1 = CanvasWidth - TickLength,
                    Y1 = actualY,
                    X2 = CanvasWidth,
                    Y2 = actualY,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Name = "TickMark"
                };
                CanvasLayout.Children.Add(line);

                SolidColorBrush solidColorBrush= Brushes.Gray;
                AddText(drawingGeometryNode, solidColorBrush, $"{i * tickSpacing}m", CanvasWidth, actualY, "R", "B", 0.0);
                Path path = new Path
                {
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Data = drawingGeometryNode,
                    Name = "TickMark"
                };
                CanvasLayout.Children.Add(path);
            }
        }

        // 通り心描画メソッド
        private void UpdateGridLines()
        {
            if (DataContext == null)
            { return; }
            ApplicationViewModel viewModel = DataContext;
            SolidColorBrush solidColorBrush = Brushes.Purple;
            double LineEndPos = 65;
            double SymbolPos = LineEndPos - 15;
            double SymbolCircleDia = 20.0;

            foreach (GridDataItem gridX in viewModel.PileLayoutViewModel.GridX)
            {
                double canvasX = 0.5 * CanvasWidth + (gridX.Coord - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double canvasOX = 0.5 * CanvasWidth + (0.0 - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;

                Line lineY = new Line
                {
                    X1 = canvasX,
                    Y1 = 0,
                    X2 = canvasX,
                    Y2 = CanvasHeight - LineEndPos,
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Name = "GridY",
                    StrokeDashArray = new DoubleCollection { 30, 2, 2, 2 } // 4ユニットの線と2ユニットのスペース
                };
                CanvasLayout.Children.Add(lineY);

                AddEllipseToPath(drawingGeometryNode, canvasX, CanvasHeight - SymbolPos, SymbolCircleDia);
                AddText(drawingGeometryNode, solidColorBrush, gridX.Name, canvasX, CanvasHeight - SymbolPos, "C", "C", 0.0);

                Path path = new Path
                {
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Data = drawingGeometryNode,
                    Name = "GridY"
                };

                CanvasLayout.Children.Add(path);

            }

            foreach (GridDataItem gridY in viewModel.PileLayoutViewModel.GridY)
            {
                double canvasY = 0.5 * CanvasHeight - (gridY.Coord - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
            
                Line lineX = new Line
                {
                    X1 = 0,
                    Y1 = canvasY,
                    X2 = CanvasWidth - LineEndPos,
                    Y2 = canvasY, // 目盛りの長さを設定する
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Name = "GirdX",
                    StrokeDashArray = new DoubleCollection { 30, 2, 2, 2 } // 4ユニットの線と2ユニットのスペース
                };
                CanvasLayout.Children.Add(lineX);

                AddEllipseToPath(drawingGeometryNode, CanvasWidth - SymbolPos, canvasY, SymbolCircleDia);
                AddText(drawingGeometryNode, solidColorBrush, gridY.Name, CanvasWidth - SymbolPos, canvasY, "C", "C", 0.0);
                Path path = new Path
                {
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Data = drawingGeometryNode,
                    Name = "GridY"
                };
                CanvasLayout.Children.Add(path);
            }
        }

        // 寸法線描画メソッド
        private void UpdateDimensionLines()
        {
            if(DataContext == null) { return; }
            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
            SolidColorBrush solidColorBrush = Brushes.Purple;
            double LineEndPos = 65;

            bool first = true;
            for (int i = 0; i < viewModel.PileLayoutViewModel.GridX.Count; i++)
            {
                GridDataItem gridX = viewModel.PileLayoutViewModel.GridX[i];
                double canvasX = 0.5 * CanvasWidth + (gridX.Coord - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;

                AddEllipseToPath(drawingGeometryNode, canvasX, CanvasHeight - LineEndPos, 2);

                if (first)
                {
                    first = false;
                    continue; // 最初のループをスキップ
                }
                else
                {
                    double canvasX0 = 0.5 * CanvasWidth + (viewModel.PileLayoutViewModel.GridX[i-1].Coord
                        - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                    Line lineY = new Line
                    {
                        X1 = canvasX0,
                        Y1 = CanvasHeight - LineEndPos,
                        X2 = canvasX,
                        Y2 = CanvasHeight - LineEndPos, // 目盛りの長さを設定する
                        Stroke = solidColorBrush,
                        StrokeThickness = 0.5,
                        Name = "GridY",
                    
                    };
                    CanvasLayout.Children.Add(lineY);
                    double position = 0.5 * (viewModel.PileLayoutViewModel.GridX[i - 1].Coord + viewModel.PileLayoutViewModel.GridX[i].Coord);
                    double canvasXpos = 0.5 * CanvasWidth + (position - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                    string spacing = (viewModel.PileLayoutViewModel.GridX[i].Spacing * 1000).ToString();
                    AddText(drawingGeometryNode, solidColorBrush, spacing, canvasXpos, CanvasHeight - LineEndPos, "C", "B", 0.0);
                }

                Path path = new Path
                {
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Data = drawingGeometryNode,
                    Name = "GridY"
                };

                CanvasLayout.Children.Add(path);

            }

            first = true;
            for (int i = 0; i < viewModel.PileLayoutViewModel.GridY.Count; i++)
            {
                GridDataItem gridY = viewModel.PileLayoutViewModel.GridY[i];
                double canvasY = 0.5 * CanvasHeight - (gridY.Coord - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;

                AddEllipseToPath(drawingGeometryNode, CanvasWidth - LineEndPos, canvasY, 2);

                if (first)
                {
                    first = false;
                    continue; // 最初のループをスキップ
                }
                else
                {
                    double canvasY0 = 0.5 * CanvasHeight - (viewModel.PileLayoutViewModel.GridY[i-1].Coord
                        - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                    Line lineX = new Line
                    {
                        X1 = CanvasWidth - LineEndPos,
                        Y1 = canvasY0,
                        X2 = CanvasWidth - LineEndPos,
                        Y2 = canvasY, // 目盛りの長さを設定する
                        Stroke = solidColorBrush,
                        StrokeThickness = 0.5,
                        Name = "GirdX",
                    
                    };
                    CanvasLayout.Children.Add(lineX);
                    double position = 0.5 * (viewModel.PileLayoutViewModel.GridY[i - 1].Coord + viewModel.PileLayoutViewModel.GridY[i].Coord);
                    double canvasYpos = 0.5 * CanvasHeight - (position - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                    string spacing = (viewModel.PileLayoutViewModel.GridY[i].Spacing * 1000).ToString();
                    //AddTextToGeometryAtBottomCenter(drawingGeometryNode, solidColorBrush, spacing, 35, canvasYpos, -90);

                    AddText(drawingGeometryNode, solidColorBrush, spacing, CanvasWidth - LineEndPos, canvasYpos, "C", "B", -90);
                }

                Path path = new Path
                {
                    Stroke = solidColorBrush,
                    StrokeThickness = 0.5,
                    Data = drawingGeometryNode,
                    Name = "GridY"
                };
                CanvasLayout.Children.Add(path);
            }
        }

        // 円を PathGeometry に追加するメソッド
        private void AddEllipseToPath(PathGeometry geometry, double x, double y, double dia)
        {
            EllipseGeometry ellipse = new EllipseGeometry(new Point(x, y), dia / 2, dia / 2);
            geometry.AddGeometry(ellipse);
        }

        // textを PathGeometry に追加するメソッド
        private void AddTextToGeometry(PathGeometry geometry, string text, double x, double y)
        {
            // TextBlockを作成
            TextBlock textBlock = new TextBlock
            {
                Text = text,
                FontSize = LabelSize,
                Foreground = Brushes.Black
            };

            // Canvas内での位置を設定
            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y);

            // TextBlockをCanvasに追加
            CanvasLayout.Children.Add(textBlock);
        }

        private void AddText(PathGeometry geometry, SolidColorBrush solidColorBrush, string text, double x, double y, string horizontalPos, string verticalPos, double textAngle)
        {
            // TextBlockを作成
            TextBlock textBlock = new TextBlock
            {
                Text = text,
                FontSize = LabelSize,
                Foreground = solidColorBrush
            };

            // テキストの幅と高さを測定するために、TextBlockを一時的にCanvasに追加
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size textSize = textBlock.DesiredSize;

            double adjustX;
            if(horizontalPos == "L")
            { adjustX = 0; }
            else if (horizontalPos == "C")
            { adjustX = textSize.Width / 2; }
            else if (horizontalPos == "R")
            { adjustX = textSize.Width; }
            else
            { adjustX = 0; }

            double adjustY;
            if (verticalPos == "T")
            { adjustY = 0; }
            else if (verticalPos == "C")
            { adjustY = textSize.Height / 2; }
            else if (verticalPos == "B")
            { adjustY = textSize.Height; }
            else
            { adjustY = 0; }

            // TextBlockの回転を設定
            RotateTransform rotateTransform = new RotateTransform(textAngle);
            textBlock.RenderTransform = rotateTransform;

            // 位置を設定
            Canvas.SetLeft(textBlock, x - adjustX * Math.Cos(textAngle * Math.PI / 180) + adjustY * Math.Sin(textAngle * Math.PI / 180));
            Canvas.SetTop(textBlock, y - adjustY * Math.Cos(textAngle * Math.PI / 180) - adjustX * Math.Sin(textAngle * Math.PI / 180));

            // TextBlockをCanvasに追加
            CanvasLayout.Children.Add(textBlock);
        }


        // 建物座標からCanvas座標への変換
        private Point FromBuildingToCanvasCoordinate(Point buildingCoordinate)
        {
            Point canvasCoordinate = new Point(
                  (buildingCoordinate.X - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding + 0.5 * CanvasWidth,
                 -(buildingCoordinate.Y - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding + 0.5 * CanvasHeight
             );
            return canvasCoordinate;
        }

        // canvas mouse event //

        private void ClearCanvasSelection()
        {
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();
        }

        // マウスの左ボタンが押された時のメソッド
        private void CanvasLayout_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ctrlキーが押されている場合の処理
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // ここにCtrlキーが押しながら左クリックしたときの処理を記述する
            }
            // Ctrlキーが押されていない場合の処理
            else
            {
                ClearCanvasSelection();
                // マウスの左ボタンが押された時の処理

                var elementToRemove = CanvasLayout.Children.OfType<Path>().FirstOrDefault(p => p.Name == "Selection");
                if (elementToRemove != null)
                {
                    CanvasLayout.Children.Remove(elementToRemove);
                }
                startPoint = e.GetPosition(CanvasLayout);
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                startPoint = e.GetPosition(CanvasLayout);
                selectionRectangle = new Rectangle
                {
                    //Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Opacity = 0.5,
                    Fill = Brushes.LightBlue
                };

                Canvas.SetLeft(selectionRectangle, startPoint.X);
                Canvas.SetTop(selectionRectangle, startPoint.Y);
                CanvasLayout.Children.Add(selectionRectangle);
            }
        }


        // マウスが移動した時のメソッド
        private void CanvasLayout_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // マウスが移動した時の処理
            if (e.LeftButton == MouseButtonState.Pressed && selectionRectangle != null)
            {
                Point currentPoint = e.GetPosition(CanvasLayout);

                // Canvas内でマウスが移動している場合、選択範囲を更新する
                double x = startPoint.X < currentPoint.X ? startPoint.X : currentPoint.X;
                double y = startPoint.Y < currentPoint.Y ? startPoint.Y : currentPoint.Y;

                double width = Math.Abs(currentPoint.X - startPoint.X);
                double height = Math.Abs(currentPoint.Y - startPoint.Y);

                selectionRectangle.Width = width;
                selectionRectangle.Height = height;

                Canvas.SetLeft(selectionRectangle, x);
                Canvas.SetTop(selectionRectangle, y);
            }

            if (isMouseWheelPressed)
            {
                Point currentMousePosition = e.GetPosition(CanvasLayout);

                // 前回のマウス位置と現在のマウス位置との差分を取得
                System.Windows.Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

                // スケールを考慮して平行移動を調整
                //translateTransform.X += delta.X / 2.5; // 移動速度を調整
                //translateTransform.Y += delta.Y / 2.5; // 移動速度を調整

                CanvasCenterBuildingCoordinate.X -= delta.X / ScaleCanvasOnBuilding;
                CanvasCenterBuildingCoordinate.Y += delta.Y / ScaleCanvasOnBuilding;
                previousMousePosition = currentMousePosition;
                UpdateCanvas(DataGridPileLayout);
            }
        }

        // マウスがCanvas範囲から外れた時のイベント
        private void CanvasLayout_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            //isShiftPressed= false;
            isMouseWheelPressed= false;
            
            // マウスがCanvasの範囲外に出た時の処理
            if (selectionRectangle != null)
            {
                // 選択範囲を確定する
                endPoint = e.GetPosition(CanvasLayout);
                ConfirmSelection();

                // SelectionRectangleを消す
                CanvasLayout.Children.Remove(selectionRectangle);
                selectionRectangle = null;
            }
        }
        // マウス左ボタンが離された時のメソッド
        private void CanvasLayout_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // マウスの左ボタンが離された時の処理
            endPoint = e.GetPosition(CanvasLayout);
            ConfirmSelection();

            // SelectionRectangleを消す
            CanvasLayout.Children.Remove(selectionRectangle);
            selectionRectangle = null;
        }

        // 選択完了メソッド
        private void ConfirmSelection()
        {
            //ClearCanvasSelection();

            double x1 = Math.Min(startPoint.X, endPoint.X);
            double x2 = Math.Max(startPoint.X, endPoint.X);
            double y1 = Math.Min(startPoint.Y, endPoint.Y);
            double y2 = Math.Max(startPoint.Y, endPoint.Y);

            drawingGeometry.Clear(); // 古い情報をクリアする
            drawingGeometryNode.Clear();

            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
            foreach (PileLayoutDataItem pilelocation in viewModel.PileLayoutViewModel.PileLayoutCollection)
            {
                double actualX = 0.5 * CanvasWidth + (pilelocation.X - CanvasCenterBuildingCoordinate.X) * ScaleCanvasOnBuilding;
                double actualY = 0.5 * CanvasHeight - (pilelocation.Y - CanvasCenterBuildingCoordinate.Y) * ScaleCanvasOnBuilding;
                if (x1 <= actualX && actualX < x2 &&
                    y1 <= actualY && actualY < y2)
                {
                    DataGridPileLayout.SelectedItems.Add(pilelocation);
                    DataGridPileAxialForce.SelectedItems.Add(pilelocation);
                    DataGridIsFrontPile.SelectedItems.Add(pilelocation);
                }
            }
            //RenderPileLayout(DataGridPileLayout);
            UpdateCanvas(DataGridPileLayout);
        }

        // 右クリックメソッド
        private void CanvasLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                // マウス位置で ContextMenu を表示
                CanvasLayout.ContextMenu = FindResource("NodeContextMenu") as System.Windows.Controls.ContextMenu;
                startPoint = e.GetPosition(CanvasLayout);
            }
            else
            {
                // 右クリック以外の場合は ContextMenu を非表示にする
                CanvasLayout.ContextMenu = null;
            }
        }

        // マウスホイールプレス時のメソッド
        private void CanvasLayout_MouseDown(object sender, MouseButtonEventArgs e)
        {
            //isMouseWheelPressed = true;
            //previousMousePosition = e.GetPosition(CanvasLayout);
            //CanvasLayout.CaptureMouse();
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                isMouseWheelPressed = true;
                previousMousePosition = e.GetPosition(CanvasLayout);
                //CanvasLayout.CaptureMouse();
            }
        }
        // マウスホイールドラッグ完了時のメソッド
        private void CanvasLayout_MouseUp(object sender, MouseButtonEventArgs e)
        {
            //isMouseWheelPressed = false;
            //CanvasLayout.ReleaseMouseCapture();
            if (e.MiddleButton == MouseButtonState.Released)
            {
                isMouseWheelPressed = false;
                //CanvasLayout.ReleaseMouseCapture();
            }
        }

        private void CanvasLayout_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point mousePosition = e.GetPosition(CanvasLayout);

            double previousScaleCanvasOnBuilding = ScaleCanvasOnBuilding;
            ScaleCanvasOnBuilding *= e.Delta > 0 ? 1.1 : 1.0 / 1.1;

            CanvasCenterBuildingCoordinate.X +=   (mousePosition.X - 0.5 * CanvasWidth) * (1 / previousScaleCanvasOnBuilding - 1 / ScaleCanvasOnBuilding);
            //- (mousePosition.X - 0.5 * CanvasWidth) / (ScaleCanvasOnBuilding);
            CanvasCenterBuildingCoordinate.Y += - (mousePosition.Y - 0.5 * CanvasHeight) * (1 / previousScaleCanvasOnBuilding - 1 / ScaleCanvasOnBuilding);
            //+ (mousePosition.Y - 0.5 * CanvasHeight) / (ScaleCanvasOnBuilding);
            //CanvasCenterBuildingCoordinate.Y += -(0.5 * CanvasHeight - mousePosition.Y) * (ScaleCanvasOnBuilding - previousScaleCanvasOnBuilding);
            previousMousePosition = mousePosition;
            UpdateCanvas(DataGridPileLayout);
        }

        // ウィンドウをマウスポインタ位置に設定するメソッド
        private void SetWindowPosition(Window window)
        {
            // マウスの現在位置を取得
            System.Windows.Point mousePosition = System.Windows.Input.Mouse.GetPosition(System.Windows.Application.Current.MainWindow);
            // マウス位置をスクリーン座標系に変換
            System.Windows.Point screenPosition = System.Windows.Application.Current.MainWindow.PointToScreen(mousePosition);

            // ウィンドウの位置を設定
            window.Left = screenPosition.X - (window.ActualWidth / 2);
            window.Top = screenPosition.Y - (window.ActualHeight / 2);
        }

        // 移動／複写メニュークリックメソッド
        private void MoveCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // MoveWindowをインスタンス化して表示
            MoveCopyWindow moveCopyWindow = new MoveCopyWindow();

            moveCopyWindow.MoveCopyCompleted += MoveCopyWindow_MoveCopyCompleted;

            SetWindowPosition(moveCopyWindow);

            moveCopyWindow.ShowDialog(); // モーダルダイアログとして表示
            // PileLayoutDocument タブを前面に表示
            PileLayoutDocument.IsActive = true;
        }

        private void MoveCopyWindow_MoveCopyCompleted(object sender, MoveCopyEventArgs e)
        {
            // 新しいウィンドウでの操作の結果を処理する
            if (e.IsMove)
            {
                // 移動操作の処理
                MoveNodes(e.DX, e.DY);
            }
            else if (e.IsCopy)
            {
                // 複製操作の処理
                CopyNodes(e.DX, e.DY, e.RepetitionNumber);
            }
        }

        private void MoveNodes(double dX, double dY)
        {
            foreach (PileLayoutDataItem pilelocation in DataGridPileLayout.SelectedItems)
            {
                pilelocation.X += dX;
                pilelocation.Y += dY;
            }
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();
            //RenderPileLayout(DataGridPileLayout);
            UpdateCanvas(DataGridPileLayout);
            UpdatePerspectiveView();
        }

        private void CopyNodes(double dX, double dY, int repetitionNumber)
        {
            // コピーを作成して操作を行う
            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
            var pileLayoutCopy = viewModel.PileLayoutViewModel.PileLayoutCollection.ToList();
            foreach (PileLayoutDataItem pilelocation in DataGridPileLayout.SelectedItems)
            {
                for (int i = 0; i < repetitionNumber; i++)
                {
                    // コピーしたコレクションに新しい要素を追加
                    viewModel.PileLayoutViewModel.PileLayoutCollection.Add(new PileLayoutDataItem()
                    {
                        X = pilelocation.X + dX * (i + 1),
                        Y = pilelocation.Y + dY * (i + 1)
                    });
                    NumberingNewPileNumber(true);
                }
            }
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();
            //RenderPileLayout(DataGridPileLayout);
            UpdateCanvas(DataGridPileLayout);
            UpdatePerspectiveView();
        }

        // 杭頭レベルメソッド
        private void PileTopLevelMenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

        // メニューアイテム 杭符号メソッド
        private void PileReferenceMenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

        // メニューアイテム 地盤符号メソッド
        private void GroundReferenceMenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

        // メニューアイテム 群杭係数メソッド
        private void PileGroupFactorMenuItem_Click(object sender, RoutedEventArgs e)
        {

        }


        private void EditPileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var appViewModel = DataContext as ApplicationViewModel;
            if (appViewModel != null)
            {
                // MoveWindowをインスタンス化して表示
                EditPileLayoutWindow editPileLayoutWindow = new EditPileLayoutWindow(appViewModel);

                editPileLayoutWindow.EditPileLayoutCompleted += EditPileLayoutWindow_EditPileLayoutCompleted;

                SetWindowPosition(editPileLayoutWindow);

                editPileLayoutWindow.ShowDialog(); // モーダルダイアログとして表示

                // PileAxialForceDocument タブを前面に表示
                PileAxialForceDocument.IsActive = true;
            }
        }

        private void EditPileLayoutWindow_EditPileLayoutCompleted(object sender, EditPileLayoutEventArgs e)
        {
            var viewModel = (ApplicationViewModel)DataContext;
            var selectedItems = DataGridPileLayout.SelectedItems.Cast<PileLayoutDataItem>().ToList();

            // 杭体
            if (e.IsApplicablePileRefNo)
            {
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.PileBodyNo = e.SelectedPileRefNo;
                }
            }

            // 地盤
            if (e.IsApplicableGroundRefNo)
            {
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.GroundNo = e.SelectedGroundRefNo;
                }
            }

            // 杭頭レベル
            if (e.IsApplicablePileTopLevel)
            {
                bool isAdd;
                if (e.IsAddPileTopLevel) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.PileTopAltitude = isAdd ? selectedItem.PileTopAltitude + e.PileTopLevel : e.PileTopLevel;
                }
            }

            // 群杭係数
            if (e.IsApplicablePileGroupFactor)
            {
                bool isAdd;
                if (e.IsAddPileGroupFactor) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.GroupPileFactor = isAdd ? selectedItem.GroupPileFactor + e.PileGroupFactor : e.PileGroupFactor;
                }
            }

            // VL
            if (e.IsApplicableVL)
            {
                bool isAdd;
                if (e.IsAddVL) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceVL = isAdd ? selectedItem.AxialForceVL + e.VL: e.VL;
                }
            }

            // VLadd
            if (e.IsApplicableVLadd)
            {
                bool isAdd;
                if (e.IsAddVLadd) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceVLAdditional = isAdd ? selectedItem.AxialForceVLAdditional + e.VLadd: e.VLadd;
                }
            }

            // E1
            if (e.IsApplicableE1)
            {
                bool isAdd;
                if (e.IsAddE1) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceEX = isAdd ? selectedItem.AxialForceEX + e.E1 : e.E1;
                }
            }

            // E2
            if (e.IsApplicableE2)
            {
                bool isAdd;
                if (e.IsAddE2) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceEY = isAdd ? selectedItem.AxialForceEY + e.E2 : e.E2;
                }
            }

            // E1_1
            if (e.IsApplicableE1_1)
            {
                bool isAdd;
                if (e.IsAddE1_1) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel1s[0] = isAdd ? selectedItem.AxialForceLevel1s[0] + e.E1_1 : e.E1_1;
                }
            }

            // E1_2
            if (e.IsApplicableE1_2)
            {
                bool isAdd;
                if (e.IsAddE1_2) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel1s[1] = isAdd ? selectedItem.AxialForceLevel1s[1] + e.E1_2 : e.E1_2;
                }
            }

            // E1_3
            if (e.IsApplicableE1_3)
            {
                bool isAdd;
                if (e.IsAddE1_3) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel1s[2] = isAdd ? selectedItem.AxialForceLevel1s[2] + e.E1_3 : e.E1_3;
                }
            }

            // E1_4
            if (e.IsApplicableE1_4)
            {
                bool isAdd;
                if (e.IsAddE1_4) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel1s[3] = isAdd ? selectedItem.AxialForceLevel1s[3] + e.E1_4 : e.E1_4;
                }
            }

            // E2_1
            if (e.IsApplicableE2_1)
            {
                bool isAdd;
                if (e.IsAddE2_1) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel2s[0] = isAdd ? selectedItem.AxialForceLevel2s[0] + e.E2_1 : e.E2_1;
                }
            }

            // E2_2
            if (e.IsApplicableE2_2)
            {
                bool isAdd;
                if (e.IsAddE2_2) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel2s[1] = isAdd ? selectedItem.AxialForceLevel2s[1] + e.E2_2 : e.E2_2;
                }
            }

            // E2_3
            if (e.IsApplicableE2_3)
            {
                bool isAdd;
                if (e.IsAddE2_3) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel2s[2] = isAdd ? selectedItem.AxialForceLevel2s[2] + e.E2_3 : e.E2_3;
                }
            }

            // E2_4
            if (e.IsApplicableE2_4)
            {
                bool isAdd;
                if (e.IsAddE2_4) { isAdd = true; }
                else { isAdd = false; }
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.AxialForceLevel2s[3] = isAdd ? selectedItem.AxialForceLevel2s[3] + e.E2_4 : e.E2_4;
                }
            }

            // IsFrontPile1
            if (e.IsApplicableIsFrontPile1)
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.IsFrontPiles[0] = e.IsFrontPile1 ? true : false;
                }

            // IsFrontPile2
            if (e.IsApplicableIsFrontPile2)
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.IsFrontPiles[1] = e.IsFrontPile2 ? true : false;
                }

            // IsFrontPile3
            if (e.IsApplicableIsFrontPile3)
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.IsFrontPiles[2] = e.IsFrontPile3 ? true : false;
                }

            // IsFrontPile4
            if (e.IsApplicableIsFrontPile4)
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.IsFrontPiles[3] = e.IsFrontPile4 ? true : false;
                }

        }

        private void UpdateAxialForce2(double axialForce, string loadCase, bool isAdd)
        {
            var viewModel = (ApplicationViewModel)DataContext;
            var selectedItems = DataGridPileLayout.SelectedItems.Cast<PileLayoutDataItem>().ToList();

            foreach (var pileLocation in selectedItems)
            {
                if (loadCase == "VL")
                {
                    pileLocation.AxialForceVL = isAdd ? pileLocation.AxialForceVL + axialForce : axialForce;
                }
                else if (loadCase == "VLadd")
                {
                    pileLocation.AxialForceVLAdditional = isAdd ? pileLocation.AxialForceVLAdditional + axialForce : axialForce;
                }
                else
                {
                    var loadCase1 = viewModel.LoadCaseViewModel.LoadCases1.FirstOrDefault(lc => lc.LoadName == loadCase);
                    if (loadCase1 != null)
                    {
                        int index = viewModel.LoadCaseViewModel.LoadCases1.IndexOf(loadCase1);
                        pileLocation.AxialForceLevel1s[index] = isAdd ? pileLocation.AxialForceLevel1s[index] + axialForce : axialForce;
                        continue;
                    }

                    var loadCase2 = viewModel.LoadCaseViewModel.LoadCases2.FirstOrDefault(lc => lc.LoadName == loadCase);
                    if (loadCase2 != null)
                    {
                        int index = viewModel.LoadCaseViewModel.LoadCases2.IndexOf(loadCase2);
                        pileLocation.AxialForceLevel2s[index] = isAdd ? pileLocation.AxialForceLevel2s[index] + axialForce : axialForce;
                        continue;
                    }

                    // エラーハンドリング: 無効なloadCaseが渡された場合
                    System.Windows.MessageBox.Show($"無効な荷重ケース: {loadCase}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 選択項目をクリア
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();

            // UIの更新
            UpdateCanvas(DataGridPileLayout);
            UpdatePerspectiveView();
        }

        // メニューアイテム 軸力メソッド
        private void AxialForceItem_Click(object sender, RoutedEventArgs e)
        {
            var appViewModel = DataContext as ApplicationViewModel;
            if (appViewModel != null)
            {
                // MoveWindowをインスタンス化して表示
                AxialForceWindow axialForceWindow = new AxialForceWindow(appViewModel);

                axialForceWindow.AxialForceCompleted += AxialForceWindow_AxialForceCompleted;

                SetWindowPosition(axialForceWindow);

                axialForceWindow.ShowDialog(); // モーダルダイアログとして表示

                // PileAxialForceDocument タブを前面に表示
                PileAxialForceDocument.IsActive = true;
            }
        }

        private void AxialForceWindow_AxialForceCompleted(object sender, AxialForceEventArgs e)
        {
            // 新しいウィンドウでの操作の結果を処理する
            if (e.IsReplace)
            {
                // 移動操作の処理
                ReplaceAxialForce(e.AxialForce, e.SelectedLoadCase);
            }
            else if (e.IsAdd)
            {
                // 複製操作の処理
                AddAxialForce(e.AxialForce, e.SelectedLoadCase);
            }
        }

        private void ReplaceAxialForce(double axialForce, string loadCase)
        {
            UpdateAxialForce(axialForce, loadCase, isAdd: false);
        }

        private void AddAxialForce(double axialForce, string loadCase)
        {
            UpdateAxialForce(axialForce, loadCase, isAdd: true);
        }

        private void UpdateAxialForce(double axialForce, string loadCase, bool isAdd)
        {
            var viewModel = (ApplicationViewModel)DataContext;
            var selectedItems = DataGridPileLayout.SelectedItems.Cast<PileLayoutDataItem>().ToList();

            foreach (var pileLocation in selectedItems)
            {
                if (loadCase == "VL")
                {
                    pileLocation.AxialForceVL = isAdd ? pileLocation.AxialForceVL + axialForce : axialForce;
                }
                else if (loadCase == "VLadd")
                {
                    pileLocation.AxialForceVLAdditional = isAdd ? pileLocation.AxialForceVLAdditional + axialForce : axialForce;
                }
                else
                {
                    var loadCase1 = viewModel.LoadCaseViewModel.LoadCases1.FirstOrDefault(lc => lc.LoadName == loadCase);
                    if (loadCase1 != null)
                    {
                        int index = viewModel.LoadCaseViewModel.LoadCases1.IndexOf(loadCase1);
                        pileLocation.AxialForceLevel1s[index] = isAdd ? pileLocation.AxialForceLevel1s[index] + axialForce : axialForce;
                        continue;
                    }

                    var loadCase2 = viewModel.LoadCaseViewModel.LoadCases2.FirstOrDefault(lc => lc.LoadName == loadCase);
                    if (loadCase2 != null)
                    {
                        int index = viewModel.LoadCaseViewModel.LoadCases2.IndexOf(loadCase2);
                        pileLocation.AxialForceLevel2s[index] = isAdd ? pileLocation.AxialForceLevel2s[index] + axialForce : axialForce;
                        continue;
                    }

                    // エラーハンドリング: 無効なloadCaseが渡された場合
                    System.Windows.MessageBox.Show($"無効な荷重ケース: {loadCase}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 選択項目をクリア
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();

            // UIの更新
            UpdateCanvas(DataGridPileLayout);
            UpdatePerspectiveView();
        }


        // メニューアイテム 前/後方杭メソッド
        private void IsFrontPileItem_Click(object sender, RoutedEventArgs e)
        {

        }



        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        //    // コピーを作成して操作を行う
        {
            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
            for (int i = viewModel.PileLayoutViewModel.PileLayoutCollection.Count - 1; i >= 0; i--)
            {
                PileLayoutDataItem pilelocation = viewModel.PileLayoutViewModel.PileLayoutCollection[i];
                if (DataGridPileLayout.SelectedItems.Contains(pilelocation))
                {
                    viewModel.PileLayoutViewModel.PileLayoutCollection.Remove(pilelocation);
                }
            }
            //RenderPileLayout(DataGridPileLayout);
            UpdateCanvas(DataGridPileLayout);
        }

        private void SelectionCancelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();
            //RenderPileLayout(DataGridPileLayout);
            UpdateCanvas(DataGridPileLayout);
        }

        private void DataGridPileLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                // マウス位置で ContextMenu を表示
                DataGridPileLayout.ContextMenu = FindResource("NodeContextMenu") as System.Windows.Controls.ContextMenu;
                startPoint = e.GetPosition(DataGridPileLayout);
            }
            else
            {
                // 右クリック以外の場合は ContextMenu を非表示にする
                DataGridPileLayout.ContextMenu = null;
            }
        }

        private void RadioButtonIsElastic_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton radioButton)
            {
                // RadioButtonが選択されているかどうかをチェックし、それに応じてIsElasticプロパティを設定する
                if (radioButton.IsChecked == true) { }
                //{ viewModel.IsElastic = true; }
                else if (radioButton.IsChecked == false) { }
                //{ viewModel.IsElastic = false; }

                //bool isElastic = radioButton.IsChecked ?? false;
                //UpdateColumnVisibility(isElastic);
            }
        }
    }
}

