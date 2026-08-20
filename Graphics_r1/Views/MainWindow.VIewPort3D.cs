using HelixToolkit.Wpf; // ConverterExtensions のため追加
using PileDesign.Constants;
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
using PileDesign.Services;

namespace PileDesign.Views
{
    public partial class MainWindow
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        
        public InputModel? InputModel => _mainWindowViewModel.CurrentInputModel;


        // 形状確認ビュー (HelixViewport3D) の機能凍結フラグ。
        // true の間、UpdatePerspectiveView 以下の描画メソッドは早期リターンし、
        // CPU/GPU コストを発生させない。再有効化時は false にすると同時に、
        // MainWindow.xaml の LayoutDocument「形状確認ビュー」内の Grid の
        // Visibility="Collapsed" を外すこと。
        // 'const' ではなく 'static readonly' とすることで、CS0162「到達できないコード」の警告を抑止し
        // 凍結解除時のコードを保持する (実行時定数扱いになるため JIT 上の差は無視できる)。
        private static readonly bool IsHelixViewFrozen = true;

        // Perspective Viewの更新メソッド
        private void UpdatePerspectiveView()
        {
            if (IsHelixViewFrozen) return;
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
                // v2 セマンティクス: 杭体描画の起点は杭頭 Z (= pile.Z - ΔZc)
                double x = pileLocation.Point3D.X;
                double y = pileLocation.Point3D.Y;
                double z = pileLocation.PileHeadZ;

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
                        AddPileToeGeometry3D(pileLocation, x, y, z2, pileDia);
                    }
                }
            }
        }

        /// <summary>
        /// 杭先端の拡張形状 (拡底部 / 拡大根固め部 / 螺旋羽根) を 3D メッシュで描く。
        ///
        /// 工法ごとに形が違う。以前は拡大根固め杭まで拡底コーンとして描いており、
        /// 杭姿図 (円柱) と食い違っていた。また拡底部の立上り・角度もハードコードで、
        /// 入力値が反映されていなかった。ここで杭姿図・擬似 3D と同じ形に揃える。
        ///
        ///   場所打ちコンクリート杭   : 円柱 (立上り) + 円錐台 (側面角度)。いずれも入力値による
        ///   埋込み杭 (プレボーリング / 中掘り) : 円柱。杭先端を下端に 根固め部径 × 高さ径比
        ///   Smart-MAGNUM             : 円柱。杭先端の 2m 上 〜 杭先端の LL 下
        ///   Hybrid ニーディング       : 円柱。杭先端の (Lu + 2m または 3m) 上 〜 杭先端
        ///   回転貫入杭               : 螺旋羽根
        /// </summary>
        private void AddPileToeGeometry3D(
            Models.InputData.PileLayoutDataItem pileLocation, double x, double y, double zToe, double pileDia)
        {
            var body = InputModel.PileBodies[pileLocation.PileBodyNo - 1];
            double pileToeDia = body.PileToeDia / 1000.0;
            string ctype = body.PileConstructionType;

            if (PileToeShape.HasBulb(body, pileDia))
            {
                // 拡大根固め (ソイルセメント球根) は円柱。上端・下端は工法で決まる
                var (topZ, bottomZ) = PileToeShape.BulbRange(body, zToe);
                AddCylinder(NikkenBrush.SkyBlue,
                    new Point3D(x, y, bottomZ), new Point3D(x, y, topZ), pileToeDia);
                return;
            }

            if (pileToeDia <= pileDia) return;   // 拡張形状なし

            if (ctype == PileConstructionTypeNames.Rotary)
            {
                AddHelicalBladePileToe(NikkenBrush.SkyBlue, new Point3D(x, y, zToe), pileToeDia, pileDia);
            }
            else if (ctype == PileConstructionTypeNames.Insitu)
            {
                AddConeShapePileToe(NikkenBrush.SkyBlue, new Point3D(x, y, zToe), pileToeDia, pileDia,
                    cylHeight: body.InsituPileToeHeight / 1000.0,
                    toeAngleDeg: body.InsituPileToeAngle);
            }
        }

        // 一般梁要素更新メソッド
        private void UpdateBeamElements3D()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (!viewModel.IsBeamElementSectionVisible) return;
            if (InputModel?.FoundationBeamInput == null) return;

            var fbInput = InputModel.FoundationBeamInput;

            // SectionNo (1-based 位置インデックス) → BeamSection マップ
            var sectionDict = new Dictionary<int, BeamSection>();
            if (fbInput.Sections != null)
                for (int i = 0; i < fbInput.Sections.Count; i++)
                    sectionDict[i + 1] = fbInput.Sections[i];

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

                // NodeJ の座標を解決
                if (beam.NodeJ_Id != Guid.Empty)
                {
                    var coordsJ = InputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                    if (coordsJ.HasValue)
                        loc1 = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
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
            double toeAngleDeg = 12.0,
            bool isDownward = false)
        {
            // 向き決定 (Z: 上向き / -Z: 下向き)
            double sign = isDownward ? -1.0 : 1.0;
            Vector3D axis = new(0, 0, sign);

            // 円柱部終点
            Point3D cylEnd = new(pileBottom.X, pileBottom.Y, pileBottom.Z + sign * cylHeight);
            AddCylinder(brush, pileBottom, cylEnd, baseDia);

            // 円錐台高さ。側面角度は鉛直からの傾きで、杭姿図の tan(90° − 角度) と等価
            double angle = toeAngleDeg > 0 ? toeAngleDeg : 12.0;
            double coneHeight = (baseDia - topDia) * 0.5 / Math.Tan(angle * Math.PI / 180.0);

            // 円錐台起点（円柱終点からさらに同方向へ伸ばす）
            Point3D coneOrigin = cylEnd;
            AddCone(brush, coneOrigin, axis, baseDia, topDia, coneHeight);
        }

        /// <summary>
        /// 回転貫入杭の螺旋羽根 (1巻き、羽根径Dw、ピッチ=杭径Dp/6) を 3D メッシュとして描画する。
        /// 内径 r=Dp/2、外径 R=Dw/2 の螺旋面を nSteps 分割の三角形ストリップで構築。
        /// 両面レンダリングのため front/back 三角形を両方追加。
        /// </summary>
        /// <param name="pileBottom">杭先端 (羽根の Z 下端、杭軸の最下点)</param>
        /// <param name="bladeDia">羽根径 Dw (m)</param>
        /// <param name="pileDia">杭径 Dp (m)</param>
        private void AddHelicalBladePileToe(Brush brush, Point3D pileBottom, double bladeDia, double pileDia)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(7)) return;
            if (bladeDia <= pileDia || pileDia <= 0) return;

            double R = bladeDia * 0.5;
            double r = pileDia * 0.5;
            double pitch = pileDia / 5.0;  // 1巻きの軸方向長さ

            // 杭体を羽根領域まで延長 (pileBottom から -Z 方向に pitch だけ円柱を追加)
            Point3D bladeBottom = new(pileBottom.X, pileBottom.Y, pileBottom.Z - pitch);
            AddCylinder(brush, pileBottom, bladeBottom, pileDia);

            const int nSteps = 64;
            var positions = new Point3DCollection((nSteps + 1) * 2);
            var triangles = new System.Windows.Media.Int32Collection(nSteps * 12);

            // 螺旋面サンプリング: 各ステップで内側 (r) と外側 (R) の 2 点を生成
            // 杭先端 (pileBottom.Z) から下方 (-Z) に 1 巻き分巻き下がる
            for (int i = 0; i <= nSteps; i++)
            {
                double t = (double)i / nSteps;
                double theta = t * 2.0 * Math.PI;
                double zOff = t * pitch;
                double cos = Math.Cos(theta);
                double sin = Math.Sin(theta);
                positions.Add(new Point3D(
                    pileBottom.X + r * cos,
                    pileBottom.Y + r * sin,
                    pileBottom.Z - zOff));   // inner
                positions.Add(new Point3D(
                    pileBottom.X + R * cos,
                    pileBottom.Y + R * sin,
                    pileBottom.Z - zOff));   // outer
            }

            // 三角形ストリップ (両面レンダリング)
            for (int i = 0; i < nSteps; i++)
            {
                int v00 = i * 2;          // inner, current
                int v01 = i * 2 + 1;      // outer, current
                int v10 = (i + 1) * 2;    // inner, next
                int v11 = (i + 1) * 2 + 1;// outer, next

                // 表面 (front)
                triangles.Add(v00); triangles.Add(v01); triangles.Add(v11);
                triangles.Add(v00); triangles.Add(v11); triangles.Add(v10);
                // 裏面 (back, 巻き方向逆)
                triangles.Add(v00); triangles.Add(v11); triangles.Add(v01);
                triangles.Add(v00); triangles.Add(v10); triangles.Add(v11);
            }

            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = triangles
            };

            var material = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(mesh, material);
            HelixViewport.Children.Add(new ModelVisual3D { Content = model });
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
                MessageService.Show($"Image saved to {saveFileDialog.FileName}", "Save Image", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// HelixViewport3Dの内蔵コンテキストメニューとCopyコマンドを上書きし、
        /// BitmapMetadata例外を回避するカスタム実装に差し替える。
        /// 併せて右ドラッグ（回転操作）後にコンテキストメニューが開かないよう抑制する。
        /// </summary>
        private void SetupHelixViewportContextMenu()
        {
            if (IsHelixViewFrozen) return;
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

            // 右ドラッグ検出: ドラッグ後のマウス離しではメニューを開かない
            HelixViewport.PreviewMouseRightButtonDown += HelixViewport_PreviewMouseRightButtonDown;
            HelixViewport.PreviewMouseMove += HelixViewport_PreviewMouseMove_RightDragDetect;
            HelixViewport.ContextMenuOpening += HelixViewport_ContextMenuOpening;
        }

        // 右ドラッグ検出用の状態
        private Point _helixRightDownPoint;
        private bool _helixIsRightDragging;
        private const double HelixRightDragThresholdPx = 3.0;

        private void HelixViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _helixRightDownPoint = e.GetPosition(HelixViewport);
            _helixIsRightDragging = false;
        }

        private void HelixViewport_PreviewMouseMove_RightDragDetect(object sender, MouseEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed && !_helixIsRightDragging)
            {
                var pt = e.GetPosition(HelixViewport);
                if (Math.Abs(pt.X - _helixRightDownPoint.X) > HelixRightDragThresholdPx ||
                    Math.Abs(pt.Y - _helixRightDownPoint.Y) > HelixRightDragThresholdPx)
                {
                    _helixIsRightDragging = true;
                }
            }
        }

        private void HelixViewport_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_helixIsRightDragging)
            {
                e.Handled = true;       // コンテキストメニュー表示をキャンセル
                _helixIsRightDragging = false; // フラグをリセット
            }
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
                Common.ClipboardHelper.TrySetDataObject(dataObject, true);

                MessageService.Show($"画像をクリップボードにコピーしました ({width}x{height})", "コピー", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageService.Show($"画像のコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
