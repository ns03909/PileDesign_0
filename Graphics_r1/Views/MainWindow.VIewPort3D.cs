using HelixToolkit.Wpf; // ConverterExtensions のため追加
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Media3DMaterial = System.Windows.Media.Media3D.Material;

namespace PileDesign.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;


        // Perspective Viewの更新メソッド
        private void UpdatePerspectiveView()
        {
            if (HelixViewport == null) return;

            HelixViewport.Children.Clear();
            UpdateHelixEmbedment();
            UpdatePile3D();

            HelixViewport.Children.Add(new SunLight());

            if (hasViewportAxes)
            {
                AddViewportAxes3D();
            }

            if (hasViewportGrid)
            {
                AddViewportGrid3D();
            }
        }

        //
        private void AddLinesVisual3D(Point3D start, Point3D end, Color color, double thickness)
        {
            // CA1416 対策: Windows 7.0 以降のみでサポートされる API を使用するため、ガード条件を追加
            if (OperatingSystem.IsWindowsVersionAtLeast(7))
            {
                LinesVisual3D linesVisual = new()
                {
                    Points = [start, end],
                    Color = color,
                    Thickness = thickness
                };
                HelixViewport.Children.Add(linesVisual);
            }
            else
            {
                // サポートされていないプラットフォームの場合は何もしない、またはログ出力
                // 例: //System.Diagnostics.Debug.WriteLine("LinesVisual3D is not supported on this platform.");
            }
        }

        // XYグリッド更新メソッド
        private void AddViewportGrid3D()
        {
            // CA1416 対策: Windows 7.0 以降のみでサポートされる API を使用するため、ガード条件を追加
            if (OperatingSystem.IsWindowsVersionAtLeast(7))
            {
                GridLinesVisual3D gridLines = new()
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
            else
            {
                // サポートされていないプラットフォームの場合は何もしない、またはログ出力
                // 例: //System.Diagnostics.Debug.WriteLine("GridLinesVisual3D is not supported on this platform.");
            }
        }

        // XYZ軸更新メソッド
        private void AddViewportAxes3D()
        {
            // LinesVisual3Dの追加
            double maxValue = 1000;

            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(maxValue, 0, 0), Colors.Red, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, maxValue, 0), Colors.Green, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, 0, maxValue), Colors.Blue, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(-maxValue, 0, 0), Colors.DarkRed, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, -maxValue, 0), Colors.DarkGreen, 1);
            AddLinesVisual3D(new Point3D(0, 0, 0), new Point3D(0, 0, -maxValue), Colors.DarkBlue, 1);
        }


        // 根入部更新メソッド
        private void UpdateHelixEmbedment()
        {
            Brush brush = NikkenBrush.SkyBlue;

            //var viewModel = DataContext as MainWindowViewModel;

            //InputModel InputModel = viewModel.InputModel;
            for (int i = 0; i < InputModel?.EmbedmentInput.EmbedmentLayers.Count; i++)
            {
                double x1 = InputModel.EmbedmentInput.EmbedmentLayers[i].X1;
                double x2 = InputModel.EmbedmentInput.EmbedmentLayers[i].X2;
                double y1 = InputModel.EmbedmentInput.EmbedmentLayers[i].Y1;
                double y2 = InputModel.EmbedmentInput.EmbedmentLayers[i].Y2;
                double z1 = InputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude;
                double z2 = InputModel.EmbedmentInput.EmbedmentLayers[i].TopAltitude;
                Point3D center = new(0.5 * (x1 + x2), 0.5 * (y1 + y2), 0.5 * (z1 + z2));
                AddCube(brush, center, Math.Abs(x2 - x1), Math.Abs(y2 - y1), Math.Abs(z2 - z1));
            }
        }

        // 杭更新メソッド
        private void UpdatePile3D()
        {
            var viewModel = DataContext as MainWindowViewModel;
            if (InputModel == null || InputModel.LoadCasesInput.LoadCaseLevel1Common == null)
            {
                return;
            }

            //var inputModel = viewModel.InputModel;
            if (viewModel.IsActionPointVisible)
            {
                double x = InputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX;
                double y = InputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointY;
                double z = InputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointAltitude;
                AddSphere(NikkenBrush.PaleRed, new Point3D(x, y, z), 0.15);
            }

            foreach (PileLayoutDataItem pileLocation in InputModel.PileLayoutItems)
            {
                double x = pileLocation.Point3D.X;
                double y = pileLocation.Point3D.Y;
                double z = pileLocation.Point3D.Z;

                int pileBodyIndex = pileLocation.PileBodyNo - 1;
                if (pileBodyIndex < 0 || pileBodyIndex >= InputModel.PileBodies.Count)
                {
                    // 不正データをスキップ、またはログ出力
                    continue;
                }

                var pileBodySegments = InputModel.PileBodies[pileBodyIndex].PileBodySegments;
                // ...（以降の処理）

                if (pileBodySegments.Count == 0)
                {
                    AddSphere(NikkenBrush.DeepBlue, new Point3D(x, y, z), 0.15);
                    continue;
                }

                for (int i = 0; i < pileBodySegments.Count; i++)
                {
                    double z1 = (i == 0) ? z : z - pileBodySegments[i - 1].SegmentDepth;
                    double z2 = z - pileBodySegments[i].SegmentDepth;
                    double pileDia = pileBodySegments[i].PileSection.PileDiameter / 1000.0;

                    AddCylinder(NikkenBrush.SkyBlue, new Point3D(x, y, z1), new Point3D(x, y, z2), pileDia);

                    if (i == pileBodySegments.Count - 1)
                    {
                        double pileToeDia = InputModel.PileBodies[pileLocation.PileBodyNo - 1].PileToeDia / 1000.0;
                        if (pileToeDia > pileDia)
                        {
                            AddConeShapePileToe(NikkenBrush.SkyBlue, new Point3D(x, y, z2), pileToeDia, pileDia);
                        }
                    }
                }
            }
        }

        //// 拡底形状更新メソッド
        //private void AddConeShapePileToe(Brush brush, Point3D origin, double baseDia, double topDia, double height = 0.3)
        //{
        //    Point3D coneBottoom = new(origin.X, origin.Y, origin.Z + height);
        //    AddCylinder(brush, origin, coneBottoom, baseDia);
        //    double coneHeight = (baseDia - topDia) * 0.5 / Math.Tan(12 * Math.PI / 180);
        //    AddCone(brush, coneBottoom, new Vector3D(0, 0, 1), baseDia * 0.5, topDia * 0.5, coneHeight);
        //}

        // 拡底形状更新メソッド (下向きに修正 & 直径統一)
        //private void AddConeShapePileToe(Brush brush, Point3D pileBottom, double baseDia, double topDia, double cylHeight = 0.3)
        //{
        //    // 拡底円柱部 (短い下向き円柱)
        //    Point3D cylLower = new(pileBottom.X, pileBottom.Y, pileBottom.Z - cylHeight);
        //    AddCylinder(brush, pileBottom, cylLower, baseDia);

        //    // 円錐台高さ (12度の側面傾斜仮定)
        //    double coneHeight = (baseDia - topDia) * 0.5 / Math.Tan(12 * Math.PI / 180.0);

        //    // 円錐台 (さらに下へ伸ばす)
        //    Point3D coneOrigin = cylLower;
        //    AddCone(brush, coneOrigin, new Vector3D(0, 0, -1), baseDia, topDia, coneHeight);
        //}

        private void AddConeShapePileToe(
            Brush brush,
            Point3D pileBottom,
            double baseDia,
            double topDia,
            double cylHeight = 0.3,
            bool isDownward = false)
        {
            // 向き決定 (Z: 上向き / -Z: 下向き)
            double sign = isDownward ? -1.0 : 1.0;
            Vector3D axis = new(0, 0, sign);

            // 円柱部終点
            Point3D cylEnd = new(pileBottom.X, pileBottom.Y, pileBottom.Z + sign * cylHeight);
            AddCylinder(brush, pileBottom, cylEnd, baseDia);

            // 円錐台高さ (拡底杭の側面角度 12° 仮定)
            double coneHeight = (baseDia - topDia) * 0.5 / Math.Tan(12 * Math.PI / 180.0);

            // 円錐台起点（円柱終点からさらに同方向へ伸ばす）
            Point3D coneOrigin = cylEnd;
            AddCone(brush, coneOrigin, axis, baseDia, topDia, coneHeight);
        }

        private static System.Numerics.Vector3 ToVector3(Point3D p)
        {
            return new System.Numerics.Vector3((float)p.X, (float)p.Y, (float)p.Z);
        }

        // 球更新メソッド
        private void AddSphere(Brush brush, Point3D position, double radius)
        {
            var meshBuilder = new HelixToolkit.Geometry.MeshBuilder();
            meshBuilder.AddSphere(
                ToVector3(position),
                (float)radius
            );

            var mesh = meshBuilder.ToMesh();
            // var wpfMesh = ConverterExtensions.ToMeshGeometry3D(mesh);
            var wpfMesh = ConverterExtensions.ToWndMeshGeometry3D(mesh);
            var material = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(wpfMesh, material);
            var modelVisual = new ModelVisual3D { Content = model };
            HelixViewport.Children.Add(modelVisual);
        }

        // 立方体更新メソッド
        private void AddCube(Brush brush, Point3D center, double x, double y, double z)
        {
            var meshBuilder = new HelixToolkit.Geometry.MeshBuilder();
            meshBuilder.AddBox(ToVector3(center), (float)x, (float)y, (float)z);

            var mesh = meshBuilder.ToMesh();
            // var wpfMesh = ConverterExtensions.ToMeshGeometry3D(mesh);
            var wpfMesh = ConverterExtensions.ToWndMeshGeometry3D(mesh);
            var semiTransparentMaterial = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(wpfMesh, semiTransparentMaterial);
            var modelVisual = new ModelVisual3D { Content = model };
            HelixViewport.Children.Add(modelVisual);
        }

        // 円柱更新メソッド
        //private void AddCylinder(Brush brush, Point3D p1, Point3D p2, double dia, int thetaDiv = 25, bool cap1 = true, bool cap2 = true)
        //{
        //    var meshBuilder = new HelixToolkit.Geometry.MeshBuilder();
        //    meshBuilder.AddCylinder(
        //        ToVector3(p1),
        //        ToVector3(p2),
        //        (float)dia, thetaDiv, cap1, cap2);

        //    var mesh = meshBuilder.ToMesh();
        //    // var wpfMesh = ConverterExtensions.ToMeshGeometry3D(mesh);
        //    var wpfMesh = ConverterExtensions.ToWndMeshGeometry3D(mesh);
        //    var material = new DiffuseMaterial(brush);
        //    var model = new GeometryModel3D(wpfMesh, material);
        //    var modelVisual = new ModelVisual3D { Content = model };
        //    HelixViewport.Children.Add(modelVisual);
        //}
        private void AddCylinder(Brush brush, Point3D p1, Point3D p2, double diameter, int thetaDiv = 25, bool cap1 = true, bool cap2 = true)
        {
            var meshBuilder = new HelixToolkit.Geometry.MeshBuilder();
            double radius = diameter * 0.5;
            meshBuilder.AddCylinder(
                ToVector3(p1),
                ToVector3(p2),
                (float)radius,
                thetaDiv,
                cap1,
                cap2);

            var mesh = meshBuilder.ToMesh();
            var wpfMesh = ConverterExtensions.ToWndMeshGeometry3D(mesh);
            var material = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(wpfMesh, material);
            HelixViewport.Children.Add(new ModelVisual3D { Content = model });
        }

        //// 円錐更新メソッド
        //private void AddCone(Brush brush, Point3D origin, Vector3D direction, double baseRadius, double topRadius, double height, bool baseCap = true, bool topCap = true, int thetaDiv = 25)
        //{
        //    var meshBuilder = new HelixToolkit.Geometry.MeshBuilder();
        //    meshBuilder.AddCone(
        //        ToVector3(origin),
        //        new System.Numerics.Vector3((float)direction.X, (float)direction.Y, (float)direction.Z),
        //        (float)baseRadius, (float)topRadius, (float)height, baseCap, topCap, thetaDiv);

        //    var mesh = meshBuilder.ToMesh();
        //    // var wpfMesh = ConverterExtensions.ToMeshGeometry3D(mesh);
        //    var wpfMesh = ConverterExtensions.ToWndMeshGeometry3D(mesh);
        //    var material = new DiffuseMaterial(brush);
        //    var model = new GeometryModel3D(wpfMesh, material);
        //    var modelVisual = new ModelVisual3D { Content = model };
        //    HelixViewport.Children.Add(modelVisual);
        //}
        // 円錐台更新メソッド (直径受け取り・方向正規化)
        private void AddCone(
            Brush brush,
            Point3D origin,
            Vector3D axis,
            double baseDia,
            double topDia,
            double height,
            bool baseCap = true,
            bool topCap = true,
            int thetaDiv = 25)
        {
            var dir = axis;
            if (dir.Length == 0) dir = new Vector3D(0, 0, -1);
            dir.Normalize();

            double baseRadius = baseDia * 0.5;
            double topRadius = topDia * 0.5;

            var builder = new HelixToolkit.Geometry.MeshBuilder();
            builder.AddCone(
                ToVector3(origin),
                new System.Numerics.Vector3((float)dir.X, (float)dir.Y, (float)dir.Z),
                (float)baseRadius,
                (float)topRadius,
                (float)height,
                baseCap,
                topCap,
                thetaDiv);

            var mesh = builder.ToMesh();
            var wpfMesh = ConverterExtensions.ToWndMeshGeometry3D(mesh);
            var material = new DiffuseMaterial(brush);
            HelixViewport.Children.Add(new ModelVisual3D
            {
                Content = new GeometryModel3D(wpfMesh, material)
            });
        }



        // ビューポートの画像保存メソッド
        private void HelixViewportSaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            SaveViewportToImage("helixViewport.png", 2);
        }

        // ビューポートの画像保存メソッド
        private void SaveViewportToImage(string filename, double scaleFactor)
        {
            // ファイル保存ダイアログを作成し、デフォルトの保存場所をデスクトップに設定します
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
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
                RenderTargetBitmap renderBitmap = new(width, height, 96 * scaleFactor, 96 * scaleFactor, PixelFormats.Pbgra32);

                // 背景色を描画
                DrawingVisual drawingVisual = new();
                using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.DrawRectangle(HelixViewport.Background, null, new Rect(0, 0, width, height));
                }
                renderBitmap.Render(drawingVisual);

                // HelixViewport3Dの内容を描画
                renderBitmap.Render(viewport);

                // BitmapEncoderを使用してRenderTargetBitmapを画像ファイルに書き込みます
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                // ファイルに書き込みます
                using (System.IO.FileStream fileStream = new(saveFileDialog.FileName, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
                MessageBox.Show($"Image saved to {saveFileDialog.FileName}", "Save Image", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        //
        private void HelixViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HelixViewport != null)
            {
                if (HelixViewComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string selectedView = selectedItem.Content.ToString();
                    if (selectedView == "透視図法")
                    {
                        SetPerspectiveCamera();
                    }
                    else if (selectedView == "平行投影図法")
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


        //private void HelixViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.LeftButton == MouseButtonState.Pressed)
        //    {

        //    }
        //}

        private void HelixViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {

        }

        /////
        ///

        private readonly HashSet<GeometryModel3D> _selectedClassic = new();
        private readonly Dictionary<GeometryModel3D, (Media3DMaterial Front, Media3DMaterial? Back)> _origMatClassic = new();
        private DiffuseMaterial? _highlightClassic;

        private void EnsureHighlightClassic()
        {
            if (_highlightClassic != null) return;
            var brush = new SolidColorBrush(Colors.Orange);
            brush.Freeze();
            _highlightClassic = new DiffuseMaterial(brush);
        }

        private void ApplyHighlightClassic(GeometryModel3D g)
        {
            EnsureHighlightClassic();
            if (!_origMatClassic.ContainsKey(g))
                _origMatClassic[g] = (g.Material, g.BackMaterial);
            g.Material = _highlightClassic!;
            g.BackMaterial = _highlightClassic!;
        }

        private void RemoveHighlightClassic(GeometryModel3D g)
        {
            if (_origMatClassic.TryGetValue(g, out var saved))
            {
                g.Material = saved.Front;
                g.BackMaterial = saved.Back;
                _origMatClassic.Remove(g);
            }
        }

        private void ClearSelectionClassic()
        {
            foreach (var g in _selectedClassic)
                RemoveHighlightClassic(g);
            _selectedClassic.Clear();
        }

        private void AddSelectClassic(GeometryModel3D g)
        {
            if (_selectedClassic.Add(g))
                ApplyHighlightClassic(g);
        }

        private void ToggleSelectClassic(GeometryModel3D g)
        {
            if (_selectedClassic.Contains(g))
            {
                _selectedClassic.Remove(g);
                RemoveHighlightClassic(g);
            }
            else
            {
                _selectedClassic.Add(g);
                ApplyHighlightClassic(g);
            }
        }

        // 既存のハンドラに実装（XAMLで HelixViewport_MouseLeftButtonDown がバインド済）
        private void HelixViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (HelixViewport is null) return;

            var pos = e.GetPosition(HelixViewport);
            var hits = Viewport3DHelper.FindHits(HelixViewport.Viewport, pos);

            // 最前面の GeometryModel3D を取得
            GeometryModel3D? geo = null;
            foreach (var h in hits)
            {
                geo = h.Model as GeometryModel3D;
                if (geo != null) break;
            }

            var mods = Keyboard.Modifiers;
            bool ctrl = mods.HasFlag(ModifierKeys.Control);
            bool shift = mods.HasFlag(ModifierKeys.Shift);

            if (geo == null)
            {
                // 無修飾クリックで何もヒットしなければ全解除
                if (!ctrl && !shift)
                    ClearSelectionClassic();
                // カメラ操作との共存を優先して Handled は設定しない
                return;
            }

            if (ctrl)
                ToggleSelectClassic(geo);     // Ctrl: トグル
            else if (shift)
                AddSelectClassic(geo);        // Shift: 追加
            else
            {
                ClearSelectionClassic();      // 無修飾: 単一選択
                AddSelectClassic(geo);
            }

            // 単クリックでカメラ操作を抑制したい場合は true にする
            // e.Handled = true;
        }
    }
}
