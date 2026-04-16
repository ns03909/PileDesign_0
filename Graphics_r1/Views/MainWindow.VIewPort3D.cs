using HelixToolkit.Wpf; // ConverterExtensions のため追加
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Media3DMaterial = System.Windows.Media.Media3D.Material;

namespace PileDesign.Views
{
    public partial class MainWindow : RibbonWindow
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        
        public InputModel? InputModel => _mainWindowViewModel.CurrentInputModel;


        // Perspective Viewの更新メソッド
        private void UpdatePerspectiveView()
        {
            if (HelixViewport == null) return;

            HelixViewport.Children.Clear();
            _selectedClassic.Clear();
            _origMatClassic.Clear();
            UpdateHelixEmbedment();
            UpdatePile3D();
            UpdateBeamElements3D();

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
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (InputModel == null || InputModel.LoadCasesInput.LoadCaseLevel1Common == null) return;

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

        // 一般梁要素更新メソッド
        private void UpdateBeamElements3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (!viewModel.IsBeamElementSectionVisible) return;
            if (InputModel?.FoundationBeamInput == null) return;

            var fbInput = InputModel.FoundationBeamInput;

            // 断面番号から断面情報を取得する辞書（空でも続行）
            var sectionDict = fbInput.Sections?.ToDictionary(s => s.No, s => s)
                              ?? new Dictionary<int, BeamSection>();

            foreach (var beam in fbInput.Beams)
            {
                // 節点座標を解決
                Point3D? loc0 = null;
                Point3D? loc1 = null;

                // NodeI の座標を解決
                if (beam.NodeI_Id != Guid.Empty)
                {
                    var coordsI = InputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                    if (coordsI.HasValue)
                        loc0 = new Point3D(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                }
                else if (beam.NodeI_No > 0 && fbInput.Nodes.FirstOrDefault(n => n.No == beam.NodeI_No) is var nodeI && nodeI != null)
                {
                    // 旧方式（後方互換性）
                    loc0 = new Point3D(nodeI.X, nodeI.Y, nodeI.Z);
                }

                // NodeJ の座標を解決
                if (beam.NodeJ_Id != Guid.Empty)
                {
                    var coordsJ = InputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                    if (coordsJ.HasValue)
                        loc1 = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                }
                else if (beam.NodeJ_No > 0 && fbInput.Nodes.FirstOrDefault(n => n.No == beam.NodeJ_No) is var nodeJ && nodeJ != null)
                {
                    // 旧方式（後方互換性）
                    loc1 = new Point3D(nodeJ.X, nodeJ.Y, nodeJ.Z);
                }

                // 座標が両方とも解決できた場合のみ描画
                if (!loc0.HasValue || !loc1.HasValue) continue;

                // 断面寸法を取得（断面テーブル優先、なければ要素の直接値にフォールバック）
                double width = beam.Width;
                double height = beam.Height;
                if (sectionDict.TryGetValue(beam.SectionNo, out var section))
                {
                    width = section.Width;
                    height = section.Height;
                }

                // 3D 直方体として描画
                Brush brush = new SolidColorBrush(Color.FromArgb(180, 139, 69, 19));  // 半透明の茶色
                brush.Freeze();
                AddBeamElement(brush, loc0.Value, loc1.Value, width, height, beam.AngleBeta);
            }
        }

        //// 拡底形状更新メソッド
        //private void AddConeShapePileToe(Brush brush, Point3D origin, double baseDia, double topDia, double height = 0.3)
        //{
        //    Point3D coneBottom = new(origin.X, origin.Y, origin.Z + height);
        //    AddCylinder(brush, origin, coneBottom, baseDia);
        //    double coneHeight = (baseDia - topDia) * 0.5 / Math.Tan(12 * Math.PI / 180);
        //    AddCone(brush, coneBottom, new Vector3D(0, 0, 1), baseDia * 0.5, topDia * 0.5, coneHeight);
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
            if (!OperatingSystem.IsWindowsVersionAtLeast(7)) return;

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
            if (!OperatingSystem.IsWindowsVersionAtLeast(7)) return;

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

        // 梁要素（直方体）更新メソッド - 2点間に幅・高さを持つ直方体を描画
        // angleBetaDeg: 要素座標系の回転角 β（度）。梁軸周りに断面を回転する。
        private void AddBeamElement(Brush brush, Point3D p1, Point3D p2, double width, double height, double angleBetaDeg = 0.0)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(7)) return;

            // 梁の中心点
            Point3D center = new(
                (p1.X + p2.X) / 2,
                (p1.Y + p2.Y) / 2,
                (p1.Z + p2.Z) / 2
            );

            // 梁の方向ベクトル
            Vector3D direction = new(p2.X - p1.X, p2.Y - p1.Y, p2.Z - p1.Z);
            double length = direction.Length;
            if (length < 1e-9) return;
            direction.Normalize();

            // HelixToolkit の MeshBuilder を使用して回転した直方体を作成
            var meshBuilder = new HelixToolkit.Geometry.MeshBuilder();

            // Z軸（上方向）のベクトル
            Vector3D up = new(0, 0, 1);

            // 梁の局所座標系を計算
            // 局所X軸 = 梁の軸方向
            Vector3D localX = direction;

            // 局所Z軸 = グローバルZ軸に最も近い方向
            // （梁が鉛直の場合は別の基準が必要だが、基礎梁は通常水平）
            Vector3D localZ;
            if (Math.Abs(Vector3D.DotProduct(localX, up)) > 0.999)
            {
                // 梁がほぼ鉛直の場合はY軸を基準にする
                localZ = new Vector3D(0, 1, 0);
            }
            else
            {
                // 局所Z軸 = グローバルZ軸から梁軸方向成分を除去して正規化
                localZ = up - Vector3D.DotProduct(up, localX) * localX;
                localZ.Normalize();
            }

            // 局所Y軸 = Z × X（右手系）
            Vector3D localY = Vector3D.CrossProduct(localZ, localX);
            localY.Normalize();

            // AngleBeta による梁軸周りの回転を適用
            if (Math.Abs(angleBetaDeg) > 1e-9)
            {
                double rad = angleBetaDeg * Math.PI / 180.0;
                double cosB = Math.Cos(rad);
                double sinB = Math.Sin(rad);

                // localY, localZ を梁軸（localX）周りに回転
                Vector3D newLocalY = cosB * localY + sinB * localZ;
                Vector3D newLocalZ = -sinB * localY + cosB * localZ;
                localY = newLocalY;
                localZ = newLocalZ;
            }

            // 回転行列を作成（局所座標系 → グローバル座標系）
            Matrix3D transform = new(
                localX.X, localX.Y, localX.Z, 0,
                localY.X, localY.Y, localY.Z, 0,
                localZ.X, localZ.Y, localZ.Z, 0,
                center.X, center.Y, center.Z, 1
            );

            // 局所座標系で直方体を作成（長さ×幅×高さ）
            // 局所X軸方向に長さ、Y軸方向に幅、Z軸方向に高さ
            meshBuilder.AddBox(
                new System.Numerics.Vector3(0, 0, 0),  // 局所座標系の原点
                (float)length,   // X方向（梁軸方向）
                (float)width,    // Y方向（幅）
                (float)height    // Z方向（高さ）
            );

            var mesh = meshBuilder.ToMesh();
            var wpfMesh = ConverterExtensions.ToWndMeshGeometry3D(mesh);

            var material = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(wpfMesh, material);

            // 変換行列を適用
            model.Transform = new MatrixTransform3D(transform);

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
            if (!OperatingSystem.IsWindowsVersionAtLeast(7)) return;

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
            if (!OperatingSystem.IsWindowsVersionAtLeast(7)) return;

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
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                FileName = filename
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

        /// <summary>
        /// HelixViewport3Dの内蔵コンテキストメニューとCopyコマンドを上書きし、
        /// BitmapMetadata例外を回避するカスタム実装に差し替える。
        /// </summary>
        private void SetupHelixViewportContextMenu()
        {
            if (HelixViewport == null) return;

            // 内蔵のApplicationCommands.Copyハンドラを上書き
            HelixViewport.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Copy,
                (s, e) => CopyHelixViewportToClipboard(),
                (s, e) => { e.CanExecute = true; }));

            // コンテキストメニューをカスタムで設定（内蔵メニューを上書き）
            var menu = new ContextMenu();
            var copyItem = new MenuItem { Header = "クリップボードにコピー" };
            copyItem.Click += (s, e) => CopyHelixViewportToClipboard();
            menu.Items.Add(copyItem);

            var saveItem = new MenuItem { Header = "画像保存" };
            saveItem.Click += (s, e) => SaveViewportToImage("helixViewport.png", 2);
            menu.Items.Add(saveItem);

            HelixViewport.ContextMenu = menu;
        }

        // HelixViewport コンテキストメニュー: クリップボードにコピー
        private void CopyHelixViewportToClipboard()
        {
            try
            {
                var viewport = HelixViewport.Viewport;
                double scaleFactor = 2;
                int width = (int)(viewport.ActualWidth * scaleFactor);
                int height = (int)(viewport.ActualHeight * scaleFactor);

                var renderBitmap = new RenderTargetBitmap(width, height, 96 * scaleFactor, 96 * scaleFactor, PixelFormats.Pbgra32);

                var drawingVisual = new DrawingVisual();
                using (var dc = drawingVisual.RenderOpen())
                {
                    dc.DrawRectangle(HelixViewport.Background, null, new Rect(0, 0, width, height));
                }
                renderBitmap.Render(drawingVisual);
                renderBitmap.Render(viewport);

                // BitmapSourceを渡さず生バイトストリームのみでクリップボードに設定
                var pngEnc = new PngBitmapEncoder();
                pngEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
                var pngStream = new System.IO.MemoryStream();
                pngEnc.Save(pngStream);

                var bmpEnc = new BmpBitmapEncoder();
                bmpEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
                var bmpStream = new System.IO.MemoryStream();
                bmpEnc.Save(bmpStream);
                bmpStream.Position = 14;
                var dibBytes = new byte[bmpStream.Length - 14];
                bmpStream.Read(dibBytes, 0, dibBytes.Length);

                var dataObject = new DataObject();
                dataObject.SetData("PNG", pngStream, false);
                dataObject.SetData(DataFormats.Dib, new System.IO.MemoryStream(dibBytes), false);
                Clipboard.SetDataObject(dataObject, true);

                MessageBox.Show($"画像をクリップボードにコピーしました ({width}x{height})", "コピー", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像のコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        //
        private void HelixViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HelixViewport != null)
            {
                if (HelixViewComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string? selectedView = selectedItem.Content.ToString();
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
            if (HelixViewport.Camera == null) return;
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

        private readonly HashSet<GeometryModel3D> _selectedClassic = [];
        private readonly Dictionary<GeometryModel3D, (Media3DMaterial Front, Media3DMaterial? Back)> _origMatClassic = [];
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
            if (_selectedClassic.Remove(g))
            {
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
