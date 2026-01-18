using System;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using HelixToolkit.Wpf;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.IO;
using System.Windows.Media.Imaging;


namespace PileDesignCore
{
    public partial class MainWindow
    {
        // Perspective Viewの更新メソッド
        private void UpdatePerspectiveView()
        {
            if(HelixViewport != null)
            {
                HelixViewport.Children.Clear(); ;

                UpdateEmbedment3D();

                UpdatePile3D();

                //AddCube(new Point3D(8, 11, 2), 18, 24, 4);

                ///

                double length = 20.0;
                double dia = 2.0;
                double baseDia = 3.0;
                double x;
                double y;

                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        x = 7.2 * i;
                        y = 7.2 * j;

                        AddCylinder(new Point3D(x, y, 0), new Point3D(x, y, -length), dia);
                        double height = 0.5 * (baseDia - dia) / Math.Tan(12 * Math.PI / 180f);
                        AddCone(new Point3D(x, y, -20), new Vector3D(0, 0, 1), 0.5 * baseDia, 0.5 * dia, height);
                    }
                }
                //AddSphere(new Point3D(10, 10, 10), 0.5);

                // SunLightの追加
                SunLight sunLight = new SunLight();
                HelixViewport.Children.Add(sunLight);

                if(hasViewportAxes)
                {
                    AddViewportAxes3D();
                }

                if(hasViewportGrid)
                {
                    AddViewportGrid3D();
                }
            }
        }

        //
        private void AddLinesVisual3D(Point3D start, Point3D end, System.Windows.Media.Color color, double thickness)
        {
            LinesVisual3D linesVisual = new LinesVisual3D
            {
                Points = new Point3DCollection { start, end },
                Color = color,
                Thickness = thickness
            };
            HelixViewport.Children.Add(linesVisual);
        }

        // XYグリッド更新メソッド
        private void AddViewportGrid3D()
        {
            // GridLinesVisual3Dの追加
            GridLinesVisual3D gridLines = new GridLinesVisual3D
            {
                Width = 100,
                Length = 100,
                MinorDistance = 1,
                MajorDistance = 1,
                Thickness = 0.01,
                Fill = Brushes.LightGray
            };
            HelixViewport.Children.Add(gridLines);
        }

        // XYZ軸更新メソッド
        private void AddViewportAxes3D()
        {
            // LinesVisual3Dの追加

            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(1000, 0, 0), Colors.Red, 1.0);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, 1000, 0), Colors.Green, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, 0, 1000), Colors.Blue, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(-1000, 0, 0), Colors.DarkRed, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, -1000, 0), Colors.DarkGreen, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, 0, -1000), Colors.DarkBlue, 1);
        }

        // 根入部更新メソッド
        private void UpdateEmbedment3D()
        {
            if (DataContext == null) { return; }
            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
            for (int i = 0; i < viewModel.EmbedmentViewModel.EmbedmentCollection.Count; i++)
            {
                double x1 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].X1;
                double x2 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].X2;
                double y1 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].Y1;
                double y2 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].Y2;
                double z1 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].BottomAltitude;
                double z2 = viewModel.EmbedmentViewModel.EmbedmentCollection[i].TopAltitude;
                Point3D center = new Point3D(0.5 * (x1 + x2), 0.5 * (y1 + y2), 0.5 * (z1 + z2));
                AddCube(center, Math.Abs(x2 - x1), Math.Abs(y2 - y1), Math.Abs(z2 - z1));
            }
        }

        // 杭更新メソッド
        private void UpdatePile3D()
        {
            if(DataContext == null) { return; }
            ApplicationViewModel viewModel = (ApplicationViewModel)DataContext;
            foreach (PileLayoutDataItem pilelocation in viewModel.PileLayoutViewModel.PileLayoutCollection)
            {
                double x = pilelocation.X;
                double y = pilelocation.Y;
                double z = pilelocation.PileTopAltitude;
                AddSphere(new Point3D(x, y, z), 0.15);
            }
        }

        // 級更新メソッド
        private void AddSphere(Point3D position, double radius)
        {
            var meshBuilder = new MeshBuilder();

            meshBuilder.AddSphere(position, radius);

            // メッシュをジオメトリとして取得
            var mesh = meshBuilder.ToMesh();

            // マテリアルを作成
            var material = new DiffuseMaterial(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black));

            // ジオメトリモデルを作成
            //var model = new GeometryModel3D(mesh, semiTransparentMaterial);
            var model = new GeometryModel3D(mesh, material);
            // モデルビジュアル3Dを作成
            var modelVisual = new ModelVisual3D { Content = model };

            // ビューポートにモデルを追加
            HelixViewport.Children.Add(modelVisual);
        }

        private void AddCube(Point3D center, double x, double y, double z)
        {
            // メッシュビルダーを使って立方体を作成
            var meshBuilder = new MeshBuilder();
            meshBuilder.AddBox(center, x, y, z);

            // メッシュをジオメトリとして取得
            var mesh = meshBuilder.ToMesh();

            // マテリアルを作成
            var material = new DiffuseMaterial(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White));

            // 半透明なマテリアルを作成
            var semiTransparentBrush = new SolidColorBrush(Colors.AliceBlue)
            {
                Opacity = 0.5
            };

            var semiTransparentMaterial = new DiffuseMaterial(semiTransparentBrush);

            // ジオメトリモデルを作成
            //var model = new GeometryModel3D(mesh, semiTransparentMaterial);
            var model = new GeometryModel3D(mesh, semiTransparentMaterial);
            // モデルビジュアル3Dを作成
            var modelVisual = new ModelVisual3D { Content = model };

            // ビューポートにモデルを追加
            HelixViewport.Children.Add(modelVisual);
        }

        private void AddCylinder(Point3D p1, Point3D p2, double dia, int thetaDiv = 25)
        {
            // メッシュビルダーを使って立方体を作成
            var meshBuilder = new MeshBuilder();
            meshBuilder.AddCylinder(p1, p2, dia, thetaDiv);

            // メッシュをジオメトリとして取得
            var mesh = meshBuilder.ToMesh();

            // マテリアルを作成
            var material = new DiffuseMaterial(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.GreenYellow));

            // ジオメトリモデルを作成
            var model = new GeometryModel3D(mesh, material);

            // モデルビジュアル3Dを作成
            var modelVisual = new ModelVisual3D { Content = model };

            // ビューポートにモデルを追加
            HelixViewport.Children.Add(modelVisual);
        }

        private void AddCone(Point3D origin, Vector3D direction, double baseRadius, double topRadius, double height, bool baseCap = true, bool topCap = false, int thetaDiv = 25)
        {
            // メッシュビルダーを使って立方体を作成
            var meshBuilder = new MeshBuilder();
            meshBuilder.AddCone(origin, direction, baseRadius, topRadius, height, baseCap, topCap, thetaDiv);

            // メッシュをジオメトリとして取得
            var mesh = meshBuilder.ToMesh();

            // マテリアルを作成
            var material = new DiffuseMaterial(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.RoyalBlue));

            // ジオメトリモデルを作成
            var model = new GeometryModel3D(mesh, material);

            // モデルビジュアル3Dを作成
            var modelVisual = new ModelVisual3D { Content = model };

            // ビューポートにモデルを追加
            HelixViewport.Children.Add(modelVisual);
        }

        private void HelixViewportSaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            SaveViewportToImage("helixViewport.png", 2);
        }

        private void SaveViewportToImage(string filename, double scaleFactor)
        {
            // ファイル保存ダイアログを作成し、デフォルトの保存場所をデスクトップに設定します
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNGファイル (*.png)|*.png|すべてのファイル (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                var viewport = HelixViewport.Viewport;
                int width = (int)(viewport.ActualWidth * scaleFactor);
                int height = (int)(viewport.ActualHeight * scaleFactor);

                // RenderTargetBitmapを作成
                RenderTargetBitmap renderBitmap = new RenderTargetBitmap(width, height, 96 * scaleFactor, 96 * scaleFactor, PixelFormats.Pbgra32);

                // 背景色を描画
                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.DrawRectangle(HelixViewport.Background, null, new Rect(0, 0, width, height));
                }
                renderBitmap.Render(drawingVisual);

                // HelixViewport3Dの内容を描画
                renderBitmap.Render(viewport);

                // BitmapEncoderを使用してRenderTargetBitmapを画像ファイルに書き込みます
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                // ファイルに書き込みます
                using (FileStream fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
                System.Windows.MessageBox.Show($"Image saved to {saveFileDialog.FileName}", "Save Image", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void HelixViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HelixViewport != null)
            {
                var selectedItem = HelixViewComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem != null)
                {
                    string selectedView = selectedItem.Content.ToString();
                    if (selectedView == "透視ビュー")
                    {
                        SetPerspectiveCamera();
                    }
                    else if (selectedView == "平行ビュー")
                    {
                        SetOrthographicCamera();
                    }
                }
            }
        }

        // 透視ビューの設定メソッド
        private void SetPerspectiveCamera()
        {
            if (HelixViewport.Camera != null)
            {
                var perspectiveCamera = new PerspectiveCamera
                {
                    Position = HelixViewport.Camera.Position,
                    LookDirection = HelixViewport.Camera.LookDirection,
                    UpDirection = HelixViewport.Camera.UpDirection,
                    FieldOfView = 45
                };
                HelixViewport.Camera = perspectiveCamera;
            }
        }

        // 平行ビューの設定メソッド
        private void SetOrthographicCamera()
        {
            var orthographicCamera = new OrthographicCamera
            {
                Position = HelixViewport.Camera.Position,
                LookDirection = HelixViewport.Camera.LookDirection,
                UpDirection = HelixViewport.Camera.UpDirection,
                Width = 50
            };
            HelixViewport.Camera = orthographicCamera;
        }


        // veiwport mouse event


        private void HelixViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ctrlキーが押されている場合の処理
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                //// Ctrlキーが押されている場合の処理
                //if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                //{
                //    // ここにCtrlキーが押しながら左クリックしたときの処理を記述する
                //}
            }
        }
    }
}


