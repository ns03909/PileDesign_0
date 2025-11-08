using PileDesign.ViewModels;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PileDesign.Views
{
    /// <summary>
    /// FundamentalWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class FundamentalWindow : Window
    {
        // コンストラクタ
        public FundamentalWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                if (DataContext is FundamentalViewModel vm)
                {
                    // 多重登録防止のため一度解除してから登録
                    vm.RequestClose -= FundamentalViewModel_RequestClose;
                    vm.RequestClose += FundamentalViewModel_RequestClose;
                }
            };
        }

        private void FundamentalViewModel_RequestClose(object sender, System.EventArgs e)
        {
            // すでに閉じ処理中なら何もしない
            if (!this.IsLoaded || !this.IsVisible) return;
            this.Close();
        }

        private static void RestorePreviousPropertyValues(FundamentalViewModel viewModel)
        {
            // 全てのプロパティを前回の値に戻す
            PropertyInfo[] properties = typeof(FundamentalViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanWrite)
                {
                    //if (previousPropertyValues.TryGetValue(property.Name, out object value))
                    //{
                    //    property.SetValue(viewModel, value);
                    //}
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

        private void DataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // DataGridの高さを行数に合わせて調整する
            if (sender is DataGrid grid && grid.Items.Count > 0)
            {
                double rowHeight = grid.RowHeight;
                int rowCount = grid.Items.Count;
                double totalHeight = rowHeight * rowCount;
                grid.Height = totalHeight;
            }
        }

        //private static Canvas DrawLoadCombination(Canvas canvas)
        //{
        //    double canvasWidth = canvas.ActualWidth;

        //    // 円の描画
        //    Ellipse ellipse = new()
        //    {
        //        Width = 50,
        //        Height = 50,
        //        Fill = Brushes.SkyBlue
        //    };
        //    Canvas.SetLeft(ellipse, canvasWidth * 0.5 - ellipse.Width * 0.5); // X座標
        //    Canvas.SetTop(ellipse, 0); // Y座標
        //    canvas.Children.Add(ellipse); // Canvasに追加

        //    // ばね長方形の描画
        //    Rectangle rectangle1 = new()
        //    {
        //        Width = 5,
        //        Height = 20,
        //        Fill = Brushes.SkyBlue
        //    };
        //    Canvas.SetLeft(rectangle1, canvasWidth * 0.5 - rectangle1.Width * 0.5); // X座標
        //    Canvas.SetTop(rectangle1, ellipse.Height); // Y座標
        //    canvas.Children.Add(rectangle1); // Canvasに追加

        //    // 基礎長方形の描画
        //    Rectangle rectangle2 = new()
        //    {
        //        Width = 70,
        //        Height = 30,
        //        Fill = Brushes.SkyBlue
        //    };
        //    Canvas.SetLeft(rectangle2, canvasWidth * 0.5 - rectangle2.Width * 0.5); // X座標
        //    Canvas.SetTop(rectangle2, rectangle1.Height + ellipse.Height); // Y座標
        //    canvas.Children.Add(rectangle2); // Canvasに追加

        //    // 杭長方形の描画
        //    int spacing = 45;
        //    Rectangle rectangle = new()
        //    {
        //        Width = 9,
        //        Height = 100,
        //        Fill = Brushes.SkyBlue
        //    };
        //    Rectangle rectangle3 = rectangle;
        //    Canvas.SetLeft(rectangle3, canvasWidth * 0.5 - spacing * 0.5 - rectangle3.Width * 0.5); // X座標
        //    Canvas.SetTop(rectangle3, rectangle1.Height + ellipse.Height + rectangle2.Height); // Y座標
        //    canvas.Children.Add(rectangle3); // Canvasに追加

        //    // 杭長方形の描画
        //    Rectangle rectangle4 = new()
        //    {
        //        Width = 9,
        //        Height = 100,
        //        Fill = Brushes.SkyBlue
        //    };
        //    Canvas.SetLeft(rectangle4, canvasWidth * 0.5 + spacing * 0.5 - rectangle4.Width * 0.5); // X座標
        //    Canvas.SetTop(rectangle4, rectangle1.Height + ellipse.Height + rectangle2.Height); // Y座標
        //    canvas.Children.Add(rectangle4); // Canvasに追加

        //    // 放物線の描画
        //    PathGeometry parabolaGeometry = new();
        //    PathFigure pathFigure = new()
        //    {
        //        StartPoint = new Point((canvasWidth * 0.5) - rectangle2.Width * 0.5 - rectangle2.Width * 0.5, rectangle1.Height + ellipse.Height) // 開始点
        //    };
        //    QuadraticBezierSegment quadraticBezierSegment = new()
        //    {
        //        Point1 = new Point(canvasWidth * 0.5 - rectangle2.Width * 0.5 - rectangle2.Width * 0.5, rectangle1.Height + ellipse.Height + rectangle4.Height * 0.5), // 制御点
        //        Point2 = new Point(canvasWidth * 0.5 - rectangle2.Width - rectangle2.Width * 0.5, rectangle1.Height + ellipse.Height + rectangle2.Height + rectangle4.Height) // 終点
        //    };
        //    pathFigure.Segments.Add(quadraticBezierSegment); // 放物線を追加
        //    parabolaGeometry.Figures.Add(pathFigure); // 放物線を追加

        //    Path path = new()
        //    {
        //        Stroke = Brushes.SkyBlue,
        //        StrokeThickness = 1,
        //        Data = parabolaGeometry // 放物線をPathに追加
        //    };
        //    canvas.Children.Add(path); // Canvasに追加

        //    // Line要素を作成します
        //    Point point2 = new(canvasWidth * 0.5 - rectangle2.Width - rectangle2.Width * 0.5 - rectangle2.Width / 4.0, rectangle1.Height + ellipse.Height);
        //    Point point3 = new(canvasWidth * 0.5 - rectangle2.Width - rectangle2.Width * 0.5 - rectangle2.Width / 4.0, rectangle1.Height + ellipse.Height + rectangle2.Height + rectangle4.Height);
        //    canvas = DrawLine(canvas, pathFigure.StartPoint, point2);
        //    canvas = DrawLine(canvas, point2, point3);
        //    canvas = DrawLine(canvas, point3, quadraticBezierSegment.Point2);

        //    canvas = DrawText(canvas, "β1・Ps", new Point(0, ellipse.Height * 0.5));
        //    canvas = DrawText(canvas, "β2・Pf", new Point(0, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height));
        //    canvas = DrawText(canvas, "α1・D", new Point(canvasWidth * 0.5 - rectangle2.Width - rectangle2.Width * 0.5 - rectangle2.Width / 4.0,
        //        rectangle1.Height + ellipse.Height + rectangle2.Height * 0.5 + rectangle4.Height * 0.5));
        //    canvas = DrawArrow(canvas, new Point(0, ellipse.Height * 0.5), new Point(canvasWidth + ellipse.Width, ellipse.Height * 0.5), 5);
        //    canvas = DrawArrow(canvas, new Point(0, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height), new Point(ellipse.Height / 3.0, rectangle2.Height * 0.5 + rectangle1.Height + ellipse.Height), 5);

        //    return canvas;
        //}

        //private static Canvas DrawLine(Canvas canvas, Point startPoint, Point endPoint)
        //{
        //    // Line要素を作成します
        //    Line line = new()
        //    {
        //        Stroke = Brushes.SkyBlue, // 線の色
        //        StrokeThickness = 1, // 線の太さ
        //        X1 = startPoint.X, // 始点のX座標
        //        Y1 = startPoint.Y, // 始点のY座標
        //        X2 = endPoint.X, // 終点のX座標
        //        Y2 = endPoint.Y // 終点のY座標
        //    };

        //    // CanvasにLineを追加します
        //    canvas.Children.Add(line);
        //    return canvas;
        //}

        //private static Canvas DrawArrow(Canvas canvas, Point startPoint, Point endPoint, double arrowSize)
        //{
        //    // 矢印の座標を計算します
        //    double deltaX = endPoint.X - startPoint.X;
        //    double deltaY = endPoint.Y - startPoint.Y;
        //    double angle = Math.Atan2(deltaY, deltaX);

        //    // 矢印の先端
        //    Point arrowTip = new(
        //        endPoint.X - arrowSize * Math.Cos(angle),
        //        endPoint.Y - arrowSize * Math.Sin(angle)
        //    );

        //    // 矢印の左側の座標
        //    Point arrowLeft = new(
        //        arrowTip.X + arrowSize * Math.Cos(angle + Math.PI * 0.5),
        //        arrowTip.Y + arrowSize * Math.Sin(angle + Math.PI * 0.5)
        //    );

        //    // 矢印の右側の座標
        //    Point arrowRight = new(
        //        arrowTip.X + arrowSize * Math.Cos(angle - Math.PI * 0.5),
        //        arrowTip.Y + arrowSize * Math.Sin(angle - Math.PI * 0.5)
        //    );

        //    // 矢印を描画するためのPathGeometryを作成します
        //    PathGeometry arrowGeometry = new();
        //    PathFigure pathFigure = new()
        //    {
        //        StartPoint = endPoint
        //    };
        //    pathFigure.Segments.Add(new LineSegment(arrowLeft, true));
        //    pathFigure.Segments.Add(new LineSegment(arrowRight, true));
        //    pathFigure.Segments.Add(new LineSegment(endPoint, true));
        //    arrowGeometry.Figures.Add(pathFigure);

        //    // 矢印を表示するPathを作成します
        //    Path path = new()
        //    {
        //        Stroke = Brushes.BlueViolet,
        //        Fill = Brushes.BlueViolet,
        //        StrokeThickness = 1,
        //        Data = arrowGeometry
        //    };

        //    // Canvasに矢印を追加します
        //    canvas.Children.Add(path);
        //    return canvas;
        //}

        //private static Canvas DrawText(Canvas canvas, string text, Point position)
        //{
        //    // TextBlockを作成します
        //    TextBlock textBlock = new()
        //    {
        //        Text = text, // テキストの内容
        //        FontSize = 12, // フォントサイズ
        //        Foreground = Brushes.Black // テキストの色
        //    };

        //    // Canvas上の位置を設定します
        //    Canvas.SetLeft(textBlock, position.X);
        //    Canvas.SetTop(textBlock, position.Y);

        //    // CanvasにTextBlockを追加します
        //    canvas.Children.Add(textBlock);
        //    return canvas;
        //}

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // テキストボックスがフォーカスを持っていない場合、フォーカスを設定し、全テキストを選択
                textBox.Focus();
                e.Handled = true; // マウスクリックイベントの処理をここで完了させる
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox)
                {
                    var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                    binding?.UpdateSource();
                }
            }
        }

        private void ComboBoxSeismicGrade_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}

