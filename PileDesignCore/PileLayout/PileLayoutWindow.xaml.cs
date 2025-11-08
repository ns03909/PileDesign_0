using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using static PileDesignCore.MoveCopyWindow;

namespace PileDesignCore
{
    public partial class PileLayoutWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();
        private readonly PileLayoutViewModel viewModel;

        // 以前の描画をクリアするためのパス
        private PathGeometry drawingGeometry = new PathGeometry();
        private PathGeometry drawingGeometryNode = new PathGeometry();

        public FundamentalViewModel DataContextFundamental { get; }
        public GroundLayerViewModel DataContextGroundLayer { get; }
        public PileBodyViewModel DataContextPileBody { get; }

        // DataGrid関連
        private List<PileLayoutDataItem> selectedItems = new List<PileLayoutDataItem>();

        // Canvas関連
        readonly double acturalNodeSize = 5.0;
        readonly double actualFrameWidth = 20.0;
        double CanvasHeight;
        double CanvasWidth;

        double minX;
        double maxX;
        double minY;
        double maxY;

        double scale;
        readonly double tickSpacing = 5.0; // m

        // 選択ボックス関連
        Point startPoint = new Point(0, 0);
        Point endPoint = new Point(0, 0);
        private Rectangle selectionRectangle;

        public PileLayoutWindow(PileLayoutViewModel sharedViewModel,
            FundamentalViewModel fundamentalViewModel,
            GroundLayerViewModel groundLayerViewModel,
            PileBodyViewModel pileBodyViewModel)
        {
            InitializeComponent();

            viewModel = sharedViewModel; // viewModel フィールドに値を設定する
            DataContext = sharedViewModel;
            DataContextFundamental = fundamentalViewModel;
            DataContextGroundLayer = groundLayerViewModel;
            DataContextPileBody = pileBodyViewModel;

            // DataGridPileLayout の ItemsSource を viewModel.PileLayoutCollection にバインドする
            DataGridPileLayout.Items.Clear();
            DataGridPileLayout.ItemsSource = viewModel.PileLayoutCollection;

            // 初期値を保存
            SavePreviousPropertyValues(sharedViewModel);
            
            // イベントハンドラ

            // PileLayoutCollection に CollectionChanged イベントハンドラを追加する
            sharedViewModel.PileLayoutCollection.CollectionChanged += PileLayoutCollection_CollectionChanged;
            DataGridPileLayout.Loaded += DataGridPileLayout_Loaded;

            // SizeChanged イベントハンドラの追加
            CanvasLayout.SizeChanged += Canvas_SizeChanged;

            // 
            sharedViewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PileLayoutViewModel.PileLayoutCollection))
                {
                    RenderPileLayout(DataGridPileLayout);
                }
            };

            // selectedItems リストの初期化
            selectedItems = new List<PileLayoutDataItem>();
        }
        
        // キャンバスサイズが変化したときのメソッド
        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // キャンバスのサイズが変更されたときに描画を行う
            RenderPileLayout(DataGridPileLayout);
        }

        // 初期値を保存するメソッド
        private void SavePreviousPropertyValues(PileLayoutViewModel viewModel)
        {
            PropertyInfo[] properties = typeof(PileLayoutViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        // 初期値に戻すメソッド
        private void RestorePreviousPropertyValues(PileLayoutViewModel viewModel)
        {
            PropertyInfo[] properties = typeof(PileLayoutViewModel).GetProperties();
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
                for (int i = 0; i < DataGridPileLayout.Items.Count; i++)
                {
                    var row = DataGridPileLayout.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                    if (row != null)
                    {
                        row.Header = (i + 1).ToString();
                    }
                }
            }
            // コレクションが変更された場合に DataGrid の表示を更新する
            DataGridPileLayout.Items.Refresh();
            RenderPileLayout(DataGridPileLayout);
        }

        private void DataGridPileLayout_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataGridPileLayout.ItemsSource is ObservableCollection<PileLayoutDataItem> observableCollection)
            {
                observableCollection.CollectionChanged += PileLayoutCollection_CollectionChanged;
            }
        }

        // Handle the button click event for the "削除" (Delete) button
        private void ButtonDelete_Click(object sender, RoutedEventArgs e)
        {
            if (DataGridPileLayout.SelectedItem != null)
            {
                // 選択されたアイテムが正しい型であることを確認する
                if (DataGridPileLayout.SelectedItem is PileLayoutDataItem selectedItem)
                {
                    viewModel.PileLayoutCollection.Remove(selectedItem);
                }
                else
                {
                    // キャストに失敗した場合はエラーを処理するか、適切な処理を行う
                    System.Windows.MessageBox.Show("選択されたアイテムの型が正しくありません。");
                }
            }
        }

        private void ButtonAddPile_Click(object sender, RoutedEventArgs e)
        {
            Collection<PileLayoutDataItem> _collection = viewModel.PileLayoutCollection;
            _collection.Add(new PileLayoutDataItem());
            NumberingNewPileNumber(false);
        }
        
        private void NumberingNewPileNumber(bool isCopy)
        {
            Collection<PileLayoutDataItem> _collection = viewModel.PileLayoutCollection;
            bool isSolved = false;
            if (_collection.Count == 1)
            {
                _collection[0].PileNumber = 1;
            }
            else
            {
                if(isCopy == false)
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
            RenderPileLayout(DataGridPileLayout);
        }

        //okボタンを押すメソッド
        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            PileLayoutViewModel viewModel = (PileLayoutViewModel)DataContext;
            RestorePreviousPropertyValues(viewModel);
            this.Close();
        }

        private void DataGridPileLayout_CurrentCellChanged(object sender, EventArgs e)
        {
            if (DataGridPileLayout.SelectedItem != null && DataGridPileLayout.CurrentColumn != null)
            {
                var currentColumn = DataGridPileLayout.CurrentColumn;

                if (currentColumn.Header.ToString() == "X" || currentColumn.Header.ToString() == "Y")
                {
                    RenderPileLayout(DataGridPileLayout);
                }
            }
        }

        // 杭レイアウト描画メソッド
        private void RenderPileLayout(System.Windows.Controls.DataGrid datagrid)
        {

            CanvasLayout.Children.Clear();
            // 描画用パスをクリア
            drawingGeometryNode.Clear();

            CanvasHeight = CanvasLayout.ActualHeight;
            CanvasWidth = CanvasLayout.ActualWidth;

            minX = double.MaxValue;
            maxX = double.MinValue;
            minY = double.MaxValue;
            maxY = double.MinValue;

            foreach (PileLayoutDataItem pilelocation in viewModel.PileLayoutCollection)
            {
                if (pilelocation.X < minX) { minX = pilelocation.X; }
                if (pilelocation.X > maxX) { maxX = pilelocation.X; }
                if (pilelocation.Y < minY) { minY = pilelocation.Y; }
                if (pilelocation.Y > maxY) { maxY = pilelocation.Y; }
            }

            if (maxX == minX && maxY == minY)
            {
                scale = 1.0;
            }
            else if (maxX == minX && maxY != minY)
            {
                scale = (CanvasHeight - 2 * actualFrameWidth) / (maxY - minY);
            }
            else if (maxX != minX && maxY == minY)
            {
                scale = (CanvasWidth - 2 * actualFrameWidth) / (maxX - minX);
            }
            else
            {
                scale = Math.Min((CanvasHeight - 2 * actualFrameWidth) / (maxY - minY), (CanvasWidth - 2 * actualFrameWidth) / (maxX - minX)); 
            }
            
            DrawTickMarks();
            // draw nodes:
            foreach (PileLayoutDataItem pilelocation in viewModel.PileLayoutCollection)
            {
                double actualX = CanvasWidth / 2.0 + (pilelocation.X - (maxX + minX) / 2.0) * scale;
                double actualY = CanvasHeight / 2.0 - (pilelocation.Y - (maxY + minY) / 2.0) * scale;
                AddEllipseToPath(drawingGeometryNode, actualX, actualY, acturalNodeSize);
                AddTextToGeometry(drawingGeometryNode, pilelocation.PileNumber.ToString(), actualX, actualY);
            }

            Path path = new Path
            {
                Stroke = Brushes.Red,
                StrokeThickness = 1,
                Data = drawingGeometryNode,
                Name = "Node"
            };
            
            CanvasLayout.Children.Add(path);

            // 選択された節点の描画
            drawingGeometry.Clear();
            //foreach (PileLayoutDataItem pilelocation in DataGridPileLayout.SelectedItems)
            foreach (PileLayoutDataItem pilelocation in datagrid.SelectedItems)
                {
                double actualX = CanvasWidth / 2.0 + (pilelocation.X - (maxX + minX) / 2.0) * scale;
                double actualY = CanvasHeight / 2.0 - (pilelocation.Y - (maxY + minY) / 2.0) * scale;
                AddEllipseToPath(drawingGeometry, actualX, actualY, acturalNodeSize);
                AddTextToGeometry(drawingGeometry, pilelocation.PileNumber.ToString(), actualX, actualY);
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

        // RickMarkを描くメソッド
        private void DrawTickMarks()
        {
            //CanvasLayout.Children.Clear();
            var elementToRemove = CanvasLayout.Children.OfType<Path>().FirstOrDefault(p => p.Name == "TickMark");
            if (elementToRemove != null)
            {
                CanvasLayout.Children.Remove(elementToRemove);
            }
            // 目盛りを描画する
            int minTickX = (int)Math.Floor(minX / tickSpacing);
            int maxTickX = (int)Math.Ceiling(maxX / tickSpacing);
            int minTickY = (int)Math.Floor(minY / tickSpacing);
            int maxTickY = (int)Math.Ceiling(maxY / tickSpacing);

            for (int i = minTickX; i <= maxTickX ; i++)
            {
                double actualX = CanvasWidth / 2.0 + (i * tickSpacing - (maxX + minX) / 2.0) * scale;
                Line line = new Line
                {
                    X1 = actualX,
                    Y1 = CanvasHeight,
                    X2 = actualX,
                    Y2 = CanvasHeight - 10, // 目盛りの長さを設定する
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Name = "TickMark"
                };
                CanvasLayout.Children.Add(line);

                TextBlock textBlock = new TextBlock
                {
                    Text = $"{i * tickSpacing}m",
                    FontSize = 10,
                    Foreground = Brushes.Black,
                    Name = "TickMark"
                };
                Canvas.SetLeft(textBlock, actualX);
                Canvas.SetTop(textBlock, CanvasHeight - 20);
                CanvasLayout.Children.Add(textBlock);
            }

            for (int i = minTickY; i <= maxTickY; i++)
            {
                double actualY = CanvasHeight / 2.0 - (i * tickSpacing - (maxY + minY) / 2.0) * scale;
                Line line = new Line
                {
                    X1 = 0,
                    Y1 = actualY,
                    X2 = 10,
                    Y2 = actualY,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Name = "TickMark"
                };
                CanvasLayout.Children.Add(line);

                TextBlock textBlock = new TextBlock
                {
                    Text = $"{i * tickSpacing}m",
                    FontSize = 10,
                    Foreground = Brushes.Black,
                    Name = "TickMark"
                };
                Canvas.SetLeft(textBlock, 0);
                Canvas.SetTop(textBlock, actualY);
                CanvasLayout.Children.Add(textBlock);
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
                FontSize = 12,
                Foreground = Brushes.Black
            };

            // Canvas内での位置を設定
            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y);

            // TextBlockをCanvasに追加
            CanvasLayout.Children.Add(textBlock);
        }

        private void DataGridPileLayout_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RenderPileLayout(DataGridPileLayout);
        }

        private void DataGridPileAxialForce_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RenderPileLayout(DataGridPileAxialForce);
        }

        private void DataGridIsFrontPile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RenderPileLayout(DataGridIsFrontPile);
        }

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
        }

        private void CanvasLayout_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
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

            foreach (PileLayoutDataItem pilelocation in viewModel.PileLayoutCollection)
            {
                double actualX = CanvasWidth / 2.0 + (pilelocation.X - (maxX + minX) / 2.0) * scale;
                double actualY = CanvasHeight / 2.0 - (pilelocation.Y - (maxY + minY) / 2.0) * scale;
                if (x1 <= actualX && actualX < x2 &&
                    y1 <= actualY && actualY < y2)
                {
                    DataGridPileLayout.SelectedItems.Add(pilelocation);
                }
            }
            RenderPileLayout(DataGridPileLayout);
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

        // 移動／複写メニュークリックメソッド
        private void MoveCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // MoveWindowをインスタンス化して表示
            MoveCopyWindow moveCopyWindow = new MoveCopyWindow();
            //moveWindow.ShowDialog(); // モーダルダイアログとして表示
            moveCopyWindow.MoveCopyCompleted += MoveCopyWindow_MoveCopyCompleted;
            moveCopyWindow.Show();
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
            foreach(PileLayoutDataItem pilelocation in DataGridPileLayout.SelectedItems)
            {
                pilelocation.X += dX;
                pilelocation.Y += dY;
            }
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();
            RenderPileLayout(DataGridPileLayout);
        }

        private void CopyNodes(double dX, double dY, int repetitionNumber)
        {
            // コピーを作成して操作を行う
            var pileLayoutCopy = viewModel.PileLayoutCollection.ToList();
            foreach (PileLayoutDataItem pilelocation in DataGridPileLayout.SelectedItems)
            {
                for (int i = 0; i < repetitionNumber; i++)
                {
                    // コピーしたコレクションに新しい要素を追加
                    viewModel.PileLayoutCollection.Add(new PileLayoutDataItem()
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
            RenderPileLayout(DataGridPileLayout);
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

        // メニューアイテム 軸力メソッド
        private void AxialForceItem_Click(object sender, RoutedEventArgs e)
        {

        }

        // メニューアイテム 前/後方杭メソッド
        private void IsFrontPileItem_Click(object sender, RoutedEventArgs e)
        {

        }



        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        //    // コピーを作成して操作を行う
        {
            for (int i = viewModel.PileLayoutCollection.Count - 1; i >= 0; i--)
            {
                PileLayoutDataItem pilelocation = viewModel.PileLayoutCollection[i];
                if (DataGridPileLayout.SelectedItems.Contains(pilelocation))
                {
                    viewModel.PileLayoutCollection.Remove(pilelocation);
                }
            }
            RenderPileLayout(DataGridPileLayout);
        }

        private void SelectionCancelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DataGridPileLayout.SelectedItems.Clear();
            DataGridPileAxialForce.SelectedItems.Clear();
            DataGridIsFrontPile.SelectedItems.Clear();
            RenderPileLayout(DataGridPileLayout);
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

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isVisible)
            {
                return isVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PileTopAltitudeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is PileLayoutWindow pileLayoutWindow)
            {
                if (double.TryParse(value.ToString(), out double altitude))
                {
                    if (altitude > 0)
                        return $"{pileLayoutWindow.DataContextFundamental.RefLevel}+{altitude:0.00}";
                    else if (altitude < 0)
                        return $"{pileLayoutWindow.DataContextFundamental.RefLevel}{altitude:0.00}";
                    else
                        return $"{pileLayoutWindow.DataContextFundamental.RefLevel}±{altitude:0.00}";
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is PileLayoutWindow)
            {
                string input = value.ToString();

                // Assuming the input format is "{RefLevel} {Value}"
                string[] parts = input.Split(' ');

                if (parts.Length == 2 && double.TryParse(parts[1], out double altitude))
                {
                    // Update your variable with the altitude value
                    // For example: pileLayoutWindow.DataContextFundamental.RefLevelVariable = altitude;
                    return altitude;
                }
            }
            return value;
        }
    }
}




