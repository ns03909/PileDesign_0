using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PileDesignCore
{
    /// <summary>
    /// FundamentalWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class FundamentalWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues
            = new Dictionary<string, object>();

        public FundamentalWindow(FundamentalViewModel sharedViewModel)
        {
            InitializeComponent();
            DataContext = sharedViewModel;
            // 初期値を保存
            SavePreviousPropertyValues(sharedViewModel);
            DrawShapes();
        }

        private void SavePreviousPropertyValues(FundamentalViewModel viewModel)
        {
            // 全てのプロパティの前回の値を保存
            PropertyInfo[] properties = typeof(FundamentalViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        private void RestorePreviousPropertyValues(FundamentalViewModel viewModel)
        {
            // 全てのプロパティを前回の値に戻す
            PropertyInfo[] properties = typeof(FundamentalViewModel).GetProperties();
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

        private void TextBoxLoadCombinationFactor_TextInput(object sender, TextCompositionEventArgs e)
        {
            TextBox textBox = (TextBox)sender;

            // 現在のテキストボックスの内容と新しい入力を結合して、数値に変換できるか確認
            string newText = textBox.Text + e.Text;
            if (double.TryParse(newText, out double result))
            {
                // 数値が 0.0 以上 1.0 以下の範囲内でない場合、処理済みにする
                if (result < 0.5 || result > 1.0)
                {
                    e.Handled = false;
                }
            }
            else
            {
                // 数値に変換できない場合も処理済みにする
                e.Handled = true;
            }
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            // OKボタンがクリックされたときの処理
            this.DialogResult = true;

            this.Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            //// Cancelボタンがクリックされたときの処理

            //// プロパティを前回の保存時の値に戻す
            FundamentalViewModel viewModel = (FundamentalViewModel)DataContext;

            // プロパティを前回の保存時の値に戻す
            RestorePreviousPropertyValues(viewModel);
            this.Close();
        }

        private void TextBoxLoadCombinationFactor_TextChanged(object sender, TextChangedEventArgs e)
        {
            double inputValue;
            if (double.TryParse(TextBoxLoadCombinationFactor.Text.ToString(), out inputValue))
            {
                FundamentalViewModel viewModel = (FundamentalViewModel)DataContext;
                if (inputValue == 1.0)
                {
                    // LoadCombinations リストを空にする
                    viewModel.LoadCombinations.Clear();

                    // 新しい LoadCombinations を追加する
                    viewModel.LoadCombinations.Add(new LoadCombinaiton(1.0, 1.0, 1.0));
                }
                else if (0.5 < inputValue || inputValue < 1.0)
                {
                    // LoadCombinations リストを空にする
                    viewModel.LoadCombinations.Clear();

                    // 新しい LoadCombinations を追加する
                    viewModel.LoadCombinations.Add(new LoadCombinaiton(-inputValue, 1.0, -inputValue));
                    viewModel.LoadCombinations.Add(new LoadCombinaiton(-1.0, inputValue, -1.0));
                    viewModel.LoadCombinations.Add(new LoadCombinaiton(inputValue, 1.0, inputValue));
                    viewModel.LoadCombinations.Add(new LoadCombinaiton(1.0, inputValue, 1.0));
                }
                // LoadCombinations プロパティの変更を通知する
                //PropertyChanged(nameof(viewModel.LoadCombinations));
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

        private void DrawShapes()
        {
            // Canvas上に図形を描画するためのサイズと位置を定義します。
            double canvasWidth = CanvasLoadCombination.ActualWidth;
            double canvasHeight = CanvasLoadCombination.ActualHeight;

            // 円の描画
            Ellipse ellipse = new Ellipse();
            ellipse.Width = 50;
            ellipse.Height = 50;
            ellipse.Fill = Brushes.SkyBlue;
            Canvas.SetLeft(ellipse, canvasWidth / 2 - ellipse.Width / 2); // X座標
            Canvas.SetTop(ellipse, 0); // Y座標
            CanvasLoadCombination.Children.Add(ellipse); // Canvasに追加

            // ばね長方形の描画
            Rectangle rectangle1 = new Rectangle();
            rectangle1.Width = 5;
            rectangle1.Height = 20;
            rectangle1.Fill = Brushes.SkyBlue;
            Canvas.SetLeft(rectangle1, canvasWidth / 2 - rectangle1.Width / 2); // X座標
            Canvas.SetTop(rectangle1, ellipse.Height); // Y座標
            CanvasLoadCombination.Children.Add(rectangle1); // Canvasに追加

            // 基礎長方形の描画
            Rectangle rectangle2 = new Rectangle();
            rectangle2.Width = 70;
            rectangle2.Height = 30;
            rectangle2.Fill = Brushes.SkyBlue;
            Canvas.SetLeft(rectangle2, canvasWidth / 2 - rectangle2.Width / 2); // X座標
            Canvas.SetTop(rectangle2, rectangle1.Height + ellipse.Height); // Y座標
            CanvasLoadCombination.Children.Add(rectangle2); // Canvasに追加

            // 杭長方形の描画
            int spacing = 45;
            Rectangle rectangle3 = new Rectangle();
            rectangle3.Width = 9;
            rectangle3.Height = 100;
            rectangle3.Fill = Brushes.SkyBlue;
            Canvas.SetLeft(rectangle3, canvasWidth / 2 - spacing / 2 - rectangle3.Width / 2); // X座標
            Canvas.SetTop(rectangle3, rectangle1.Height + ellipse.Height + rectangle2.Height); // Y座標
            CanvasLoadCombination.Children.Add(rectangle3); // Canvasに追加

            // 杭長方形の描画
            Rectangle rectangle4 = new Rectangle();
            rectangle4.Width = 9;
            rectangle4.Height = 100;
            rectangle4.Fill = Brushes.SkyBlue;
            Canvas.SetLeft(rectangle4, canvasWidth / 2 + spacing / 2 - rectangle4.Width / 2); // X座標
            Canvas.SetTop(rectangle4, rectangle1.Height + ellipse.Height + rectangle2.Height); // Y座標
            CanvasLoadCombination.Children.Add(rectangle4); // Canvasに追加

            // 放物線の描画
            PathGeometry parabolaGeometry = new PathGeometry();
            PathFigure pathFigure = new PathFigure();
            pathFigure.StartPoint = new Point(canvasWidth / 2 - rectangle2.Width / 2 - rectangle2.Width / 2, rectangle1.Height + ellipse.Height); // 開始点
            QuadraticBezierSegment quadraticBezierSegment = new QuadraticBezierSegment();
            quadraticBezierSegment.Point1 = new Point(canvasWidth / 2 - rectangle2.Width / 2 - rectangle2.Width / 2, rectangle1.Height + ellipse.Height + rectangle4.Height / 2); // 制御点
            quadraticBezierSegment.Point2 = new Point(canvasWidth / 2 - rectangle2.Width - rectangle2.Width / 2, rectangle1.Height + ellipse.Height + rectangle2.Height + rectangle4.Height); // 終点
            pathFigure.Segments.Add(quadraticBezierSegment); // 放物線を追加
            parabolaGeometry.Figures.Add(pathFigure); // 放物線を追加

            Path path = new Path();
            path.Stroke = Brushes.SkyBlue;
            path.StrokeThickness = 1;
            path.Data = parabolaGeometry; // 放物線をPathに追加
            CanvasLoadCombination.Children.Add(path); // Canvasに追加

            // Line要素を作成します
            Point point2 = new Point(canvasWidth / 2 - rectangle2.Width - rectangle2.Width / 2 - rectangle2.Width / 4, rectangle1.Height + ellipse.Height);
            Point point3 = new Point(canvasWidth / 2 - rectangle2.Width - rectangle2.Width / 2 - rectangle2.Width / 4, rectangle1.Height + ellipse.Height + rectangle2.Height + rectangle4.Height);
            DrawLine(pathFigure.StartPoint, point2);
            DrawLine(point2, point3);
            DrawLine(point3, quadraticBezierSegment.Point2);


            DrawText("β1・Ps", new Point(0, ellipse.Height / 2));
            DrawText("β2・Pf", new Point(0, rectangle2.Height / 2 + rectangle1.Height + ellipse.Height));
            DrawText("α1・D", new Point(canvasWidth / 2 - rectangle2.Width - rectangle2.Width / 2 - rectangle2.Width / 4,
                rectangle1.Height + ellipse.Height + rectangle2.Height / 2 + rectangle4.Height / 2));
            DrawArrow(new Point(0, ellipse.Height / 2), new Point(canvasWidth + ellipse.Width, ellipse.Height / 2), 5);
            DrawArrow(new Point(0, rectangle2.Height / 2 + rectangle1.Height + ellipse.Height), new Point(ellipse.Height / 3, rectangle2.Height / 2 + rectangle1.Height + ellipse.Height), 5);
        }

        private void DrawRectangle(int width, int height, int xCoord, int yCoord)
        {
            Rectangle rectangle = new Rectangle();
            rectangle.Width = width;
            rectangle.Height = height;
            rectangle.Fill = Brushes.SkyBlue;
            Canvas.SetLeft(rectangle, xCoord); // X座標
            Canvas.SetTop(rectangle, yCoord); // Y座標
            CanvasLoadCombination.Children.Add(rectangle); // Canvasに追加}
        }

        private void DrawLine(Point startPoint, Point endPoint)
        {
            // Line要素を作成します
            Line line = new Line();
            line.Stroke = Brushes.SkyBlue; // 線の色
            line.StrokeThickness = 1; // 線の太さ
            line.X1 = startPoint.X; // 始点のX座標
            line.Y1 = startPoint.Y; // 始点のY座標
            line.X2 = endPoint.X; // 終点のX座標
            line.Y2 = endPoint.Y; // 終点のY座標

            // CanvasにLineを追加します
            CanvasLoadCombination.Children.Add(line);
        }

        private void DrawArrow(Point startPoint, Point endPoint, double arrowSize)
        {
            // 矢印の座標を計算します
            double deltaX = endPoint.X - startPoint.X;
            double deltaY = endPoint.Y - startPoint.Y;
            double angle = Math.Atan2(deltaY, deltaX);

            // 矢印の先端
            Point arrowTip = new Point(
                endPoint.X - arrowSize * Math.Cos(angle),
                endPoint.Y - arrowSize * Math.Sin(angle)
            );

            // 矢印の左側の座標
            Point arrowLeft = new Point(
                arrowTip.X + arrowSize * Math.Cos(angle + Math.PI / 2),
                arrowTip.Y + arrowSize * Math.Sin(angle + Math.PI / 2)
            );

            // 矢印の右側の座標
            Point arrowRight = new Point(
                arrowTip.X + arrowSize * Math.Cos(angle - Math.PI / 2),
                arrowTip.Y + arrowSize * Math.Sin(angle - Math.PI / 2)
            );

            // 矢印を描画するためのPathGeometryを作成します
            PathGeometry arrowGeometry = new PathGeometry();
            PathFigure pathFigure = new PathFigure();
            pathFigure.StartPoint = endPoint;
            pathFigure.Segments.Add(new LineSegment(arrowLeft, true));
            pathFigure.Segments.Add(new LineSegment(arrowRight, true));
            pathFigure.Segments.Add(new LineSegment(endPoint, true));
            arrowGeometry.Figures.Add(pathFigure);

            // 矢印を表示するPathを作成します
            Path path = new Path();
            path.Stroke = Brushes.BlueViolet;
            path.Fill = Brushes.BlueViolet;
            path.StrokeThickness = 1;
            path.Data = arrowGeometry;

            // Canvasに矢印を追加します
            CanvasLoadCombination.Children.Add(path);
        }

        private void DrawText(string text, Point position)
        {
            // TextBlockを作成します
            TextBlock textBlock = new TextBlock();
            textBlock.Text = text; // テキストの内容
            textBlock.FontSize = 12; // フォントサイズ
            textBlock.Foreground = Brushes.Black; // テキストの色

            // Canvas上の位置を設定します
            Canvas.SetLeft(textBlock, position.X);
            Canvas.SetTop(textBlock, position.Y);

            // CanvasにTextBlockを追加します
            CanvasLoadCombination.Children.Add(textBlock);
        }
    }

}

