using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Ribbon;
using System.Windows.Media;
using System.Windows.Media.Media3D;

using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow
    {

        // 短縮要素の節点を返すメソッド
        private static (Point3D, Point3D) GetShrinkElementPoints(Point3D point0, Point3D point1, double factor = 0.8)
        {
            // factor 
            double factorV = 0.5 + 0.5 * factor;
            Vector3D vector = point1 - point0;
            return (point1 - factorV * vector, point0 + factorV * vector);
        }

        // 杭要素の更新メソッド
        private void UpdatePileElement(PileLayoutDataItem pileLocation)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            if (viewModel.CurrentInputModel.PileBodies.Count == 0) return;

            if (pileLocation.PileBodyNo <= 0 ||
            pileLocation.PileBodyNo > viewModel.CurrentInputModel.PileBodies.Count)
            {
                return;
            }

            ObservableCollection<PileBodySegment> pileBodySegments;
            PileBodyInput pileBody = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1];
            var zs = new List<double>();

            if (!viewModel.IsElementSplit) // 要素未分割の場合
            {
                pileBodySegments = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileBodySegments;
                zs.Add(pileLocation.Point3D.Z);
                foreach (var segment in pileBodySegments)
                {
                    zs.Add(pileLocation.Point3D.Z - segment.SegmentDepth);
                }
            }

            else // 杭要素分割済の場合
            {
                if (pileLocation.SoilPileAltNo <= 0 ||
                pileLocation.SoilPileAltNo > viewModel.CurrentInputModel.ElementDivision.SoilPiles.Count)
                {
                    return;
                }
                var soilPile = viewModel.CurrentInputModel.ElementDivision.SoilPiles[pileLocation.SoilPileAltNo - 1];
                pileBodySegments = soilPile.PileBodySegments;
                zs = soilPile.ZDataItems.Select(zDataItem => zDataItem.Z).ToList();
            }

            double x = pileLocation.Point3D.X;
            double y = pileLocation.Point3D.Y;

            var pointT = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[0]));
            var pointB = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[^1]));

            if (viewModel.IsFoundationBeamVisible && viewModel.IsPileCenterLineVisible)
                AddLineGeometry(pointT, pointB, viewModel.IsElementSplit ? viewModel.CanvasGeometry.PathGeoPileDividedElems : viewModel.CanvasGeometry.PathGeoPileElems);

            if (pileBodySegments.Count == 0) return;

            double pileBottomDia = pileBodySegments[^1].PileSection.PileDiameter / 1000.0;
            double pileToeDia = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileToeDia / 1000.0;
            double pileToeAngle = pileBody.InsituPileToeAngle;
            double pileToeHeight = pileBody.InsituPileToeHeight / 1000.0;
            double pileToeHeightRatio = pileBody.PrecastConcretePileToeHeightRatio;

            double zToeTop = pileToeDia <= pileBottomDia ? zs[^1] :
                (viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileConstructionType == "場所打ちコンクリート杭"
                ? zs[^1] + (pileToeDia - pileBottomDia) * 0.5 / Math.Tan(pileToeAngle * Math.PI / 180) + pileToeHeight
                : zs[^1] + pileToeDia * pileToeHeightRatio);

            for (int i = 0; i < zs.Count - 1; i++)
            {
                double z1 = zs[i];
                double z2 = Math.Max(zs[i + 1], zToeTop);

                var point1 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, z1));
                var point2 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, z2));
                var point3 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[i + 1]));

                AddEllipseGeometry(point2, point3, viewModel.IsElementSplit ? viewModel.CanvasGeometry.PathGeoPileDividedNonTopNodes : viewModel.CanvasGeometry.PathGeoPileNonTopNodes);

                // 杭体形状（親: 梁形状）
                if (viewModel.IsBeamElementSectionVisible && viewModel.IsPileSectionVisible)
                {
                    double pileDia2D = pileBodySegments[i].PileSection.PileDiameter / 1000.0 * viewModel.CanvasThreeDView.Scale;///
                    double pileDia = pileBodySegments[i].PileSection.PileDiameter / 1000.0;
                    double flattening = viewModel.CanvasThreeDView.Flattening;

                    AddPileSectionGeometry(point1, point2, pileDia2D, flattening);

                    if (viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileConstructionType == "場所打ちコンクリート杭")
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            // 拡底部ジオメトリ
                            AddInsituPileToeGeometry(
                                pointB, pileToeDia, pileDia, flattening,
                                viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileConstructionType, pileBodySegments,
                                pileToeAngle, pileToeHeight);
                        }
                    }
                    else
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            string pileConstructionType = "aaa";
                            // 先端球根ジオメトリ
                            AddConcretePrecastPileToeGeometry(pointB, pileToeDia, pileDia, flattening, pileConstructionType, pileBodySegments);
                        }
                    }

                }

                //要素座標系（親: 梁中心線）
                if (viewModel.IsFoundationBeamVisible && viewModel.IsBeamLocalAxesVisible)
                {
                    double length = viewModel.LabelSize / 20.0;
                    Point3D point3D0 = new(x, y, (zs[i] + zs[i + 1]) * 0.5); // 要素中心
                    Matrix<double> t = Utils.GetNodeTransformMatrix(new Vector3D(0, 0, -1));
                    var globalX = Vector<double>.Build.DenseOfArray([length, 0, 0]); // X軸方向
                    var globalY = Vector<double>.Build.DenseOfArray([0, length, 0]); // Y軸方向
                    var globalZ = Vector<double>.Build.DenseOfArray([0, 0, length]); // Z軸方向
                    var localX = t.Transpose() * globalX;
                    var localY = t.Transpose() * globalY;
                    var localZ = t.Transpose() * globalZ;

                    Point3D end3DX = new(point3D0.X + localX[0], point3D0.Y + localX[1], point3D0.Z + localX[2]);
                    Point3D end3DY = new(point3D0.X + localY[0], point3D0.Y + localY[1], point3D0.Z + localY[2]);
                    Point3D end3DZ = new(point3D0.X + localZ[0], point3D0.Y + localZ[1], point3D0.Z + localZ[2]);

                    var axisPoints = new (Point3D start, Point3D end, Brush color, string name, PathGeometry pathGeometry)[]
                    {
                        (point3D0, end3DX, Brushes.Red, "AxisX", viewModel.CanvasGeometry.PathGeoAxisX),
                        (point3D0, end3DY, Brushes.Green, "AxisY", viewModel.CanvasGeometry.PathGeoAxisY),
                        (point3D0, end3DZ, Brushes.Blue, "AxisZ", viewModel.CanvasGeometry.PathGeoAxisZ),
                    };

                    foreach (var (start3D, end3D, color, name, pathGeometry) in axisPoints)
                    {
                        Point start = viewModel.CanvasThreeDView.Transformation(start3D);
                        Point end = viewModel.CanvasThreeDView.Transformation(end3D);
                        AddAxisLine3D(start, end, color, name, pathGeometry);
                    }
                }
            }
        }

        // 杭断面ジオメトリの追加メソッド
        private void AddPileSectionGeometry(Point point1, Point point2, double pileDia2D, double flattening)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedDias
                : viewModel.CanvasGeometry.PathGeoPileDias;

            var ellipse1 = new EllipseGeometry(point1, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            var ellipse2 = new EllipseGeometry(point2, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            path.AddGeometry(ellipse1);
            path.AddGeometry(ellipse2);

            for (int j = -1; j <= 1; j += 2)
            {
                var lineGeometry = new LineGeometry(
                    new Point(point1.X + pileDia2D * 0.5 * j, point1.Y),
                    new Point(point2.X + pileDia2D * 0.5 * j, point2.Y)
                );
                path.AddGeometry(lineGeometry);
            }
        }


        // 杭先端ジオメトリの追加メソッド
        private void AddInsituPileToeGeometry(
            Point pointBtm, double pileToeDia, double pileDia, double flattening, string pileConstructionType, ObservableCollection<PileBodySegment> pileBodySegments,
            double insituPileToeAngle, double insituPileToeHeight)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedDias
                : viewModel.CanvasGeometry.PathGeoPileDias;

            double pileToeDia2D = pileToeDia * viewModel.CanvasThreeDView.Scale;
            double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;
            double phiRad = Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0;
            double factoredToeCylinderHeight2D = Math.Cos(phiRad) * insituPileToeHeight * viewModel.CanvasThreeDView.Scale;
            double coneHeight = (pileToeDia - pileDia) * 0.5 / Math.Tan(insituPileToeAngle * Math.PI / 180);
            double factoredConeHeight2D = Math.Cos(phiRad) * coneHeight * viewModel.CanvasThreeDView.Scale;

            var ellipseBtm = new EllipseGeometry(pointBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredToeCylinderHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipse3 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredConeHeight2D - factoredToeCylinderHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);

            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);
            path.AddGeometry(ellipse3);

            var coneGeneratrixes = GetConeGeneratrixes2D(
                new Point(pointBtm.X, pointBtm.Y - factoredToeCylinderHeight2D),
                pileToeDia2D * 0.5, pileDia2D * 0.5, factoredConeHeight2D, flattening);
            foreach (var lineGeometryConeGeneratrix in coneGeneratrixes)
            {
                path.AddGeometry(lineGeometryConeGeneratrix);
            }

            for (int sign = -1; sign <= 1; sign += 2)
            {
                Point start = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y);
                Point end = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y - factoredToeCylinderHeight2D);
                AddLineGeometry(start, end, path);
            }
        }


        // 杭先端ジオメトリの追加メソッド
        private void AddConcretePrecastPileToeGeometry(Point pointBtm, double pileToeDia, double pileDia, double flattening, string pileConstructionType, ObservableCollection<PileBodySegment> pileBodySegments)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedDias
                : viewModel.CanvasGeometry.PathGeoPileDias;
            var dashedPath = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileToeInnerDashedDivided
                : viewModel.CanvasGeometry.PathGeoPileToeInnerDashed;

            double pileToeDia2D = pileToeDia * viewModel.CanvasThreeDView.Scale;
            double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;
            double phiRad = Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0;
            double height = pileToeDia * 2.0;
            double factoredHeight2D = Math.Cos(phiRad) * height * viewModel.CanvasThreeDView.Scale;

            // 拡大根固め部の外形（実線）
            var ellipseBtm = new EllipseGeometry(pointBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);

            for (int j = -1; j <= 1; j += 2)
            {
                var lineGeometry = new LineGeometry(
                    new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y - factoredHeight2D),
                    new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y)
                );
                path.AddGeometry(lineGeometry);
            }

            // 根固め部内部の杭体（破線）: 杭径の楕円と側線
            var ellipseCore = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            dashedPath.AddGeometry(ellipseCore);

            // 杭体の底面楕円（根固め底面位置）
            var ellipseCoreBtm = new EllipseGeometry(pointBtm, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            dashedPath.AddGeometry(ellipseCoreBtm);

            // 杭体の側線（根固め部内部）
            for (int j = -1; j <= 1; j += 2)
            {
                var innerLine = new LineGeometry(
                    new Point(pointBtm.X + pileDia2D * 0.5 * j, pointBtm.Y - factoredHeight2D),
                    new Point(pointBtm.X + pileDia2D * 0.5 * j, pointBtm.Y)
                );
                dashedPath.AddGeometry(innerLine);
            }
        }

        // 基礎梁描画の更新
        /// <summary>
        /// 接続用節点（杭頭+ΔZc位置）と剛体連結線を描画します。
        /// IsFoundationBeamVisible とは独立して、IsConnectingNodeVisible で制御されます。
        /// </summary>
        private void UpdateConnectingNodes3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;

            // VL/VL0/VLadd（Level=0）では代表節点・剛体連結線を非表示
            var selectedLC0 = LoadCases.GetLoadCase(
                viewModel.CurrentInputModel?.LoadCasesInput?.AllLoadCases, viewModel.SelectedLoadCaseName);
            bool isHorizontalLoadCase = selectedLC0 != null && (selectedLC0.Level == 1 || selectedLC0.Level == 2);
            if (isHorizontalLoadCase && viewModel.IsNodeVisible && viewModel.IsConnectingNodeVisible && viewModel.CurrentInputModel?.PileLayoutItems != null)
            {
                foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    // 非アクティブ杭の接合節点・Rigid link線はスキップ
                    if (!pile.IsVisible) continue;

                    // 各杭の接続用節点位置（杭頭Z + ΔZc）
                    double connectingZ = pile.Z + pile.FoundationBeamDeltaZc;
                    Point3D locConnecting = new(pile.X, pile.Y, connectingZ);
                    Point coordConnecting = viewModel.CanvasThreeDView.Transformation(locConnecting);

                    // 接続用節点を円として追加（杭節点と同じサイズ）
                    double radius = actualNodeSize * 0.5;
                    EllipseGeometry ellipse = new(coordConnecting, radius, radius);
                    viewModel.CanvasGeometry.PathGeoConnectingNodes.AddGeometry(ellipse);

                    // 杭頭から接続用節点への剛体連結線を追加（細い灰色破線）
                    Point3D locPileTop = new(pile.X, pile.Y, pile.Z);
                    Point coordPileTop = viewModel.CanvasThreeDView.Transformation(locPileTop);
                    LineGeometry rigidLine = new() { StartPoint = coordPileTop, EndPoint = coordConnecting };
                    viewModel.CanvasGeometry.PathGeoRigidConnections.AddGeometry(rigidLine);
                }
            }
        }

        private void UpdateFoundationBeams3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 基礎梁要素・節点の描画（FoundationBeamInputが必要）
            if (viewModel.CurrentInputModel?.FoundationBeamInput == null) return;

            var fbInput = viewModel.CurrentInputModel.FoundationBeamInput;

            // 剛体連結モードでも梁要素が存在すれば描画する（自動生成含む）
            // 梁要素がなく、かつ編集モードでもない場合のみスキップ
            if (fbInput.ConnectionMode == FoundationBeamConnectionMode.RigidBody &&
                viewModel.CurrentEditMode == CanvasEditMode.None &&
                fbInput.Beams.Count == 0)
                return;

            var nodeDict = fbInput.Nodes.ToDictionary(n => n.No, n => n);

            // ラベル表示用の材料・断面辞書
            var matDict = fbInput.Materials?.ToDictionary(m => m.No, m => m);
            var secDict = fbInput.Sections?.ToDictionary(s => s.No, s => s);

            // 基礎梁要素を描画
            foreach (var beam in fbInput.Beams)
            {
                // 非アクティブ梁はスキップ
                if (!beam.IsVisible) continue;
                // 選択された梁はUpdateSelectedNodesAndElements3D()で描画するのでスキップ
                if (beam.IsSelected) continue;

                // 新方式: Type + Guid から座標を解決
                Point3D? loc0 = null;
                Point3D? loc1 = null;

                // NodeI の座標を解決
                if (beam.NodeI_Id != Guid.Empty)
                {
                    var coordsI = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                    if (coordsI.HasValue)
                        loc0 = new Point3D(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                }
                else if (beam.NodeI_No > 0 && nodeDict.TryGetValue(beam.NodeI_No, out var nodeI))
                {
                    // 旧方式（後方互換性）
                    loc0 = new Point3D(nodeI.X, nodeI.Y, nodeI.Z);
                }

                // NodeJ の座標を解決
                if (beam.NodeJ_Id != Guid.Empty)
                {
                    var coordsJ = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                    if (coordsJ.HasValue)
                        loc1 = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
                }
                else if (beam.NodeJ_No > 0 && nodeDict.TryGetValue(beam.NodeJ_No, out var nodeJ))
                {
                    // 旧方式（後方互換性）
                    loc1 = new Point3D(nodeJ.X, nodeJ.Y, nodeJ.Z);
                }

                // 座標が両方とも解決できた場合のみ描画
                if (!loc0.HasValue || !loc1.HasValue) continue;

                Point3D point0 = loc0.Value;
                Point3D point1 = loc1.Value;

                // 要素縮小モードの場合は端点を縮小
                if (viewModel.IsShrinkElementMode)
                {
                    (point0, point1) = GetShrinkElementPoints(point0, point1);
                }

                // 3D -> 2D 変換
                Point coord0 = viewModel.CanvasThreeDView.Transformation(point0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(point1);

                // 梁の中心線を追加
                LineGeometry lineGeometry = new() { StartPoint = coord0, EndPoint = coord1 };
                viewModel.CanvasGeometry.PathGeoFoundationBeams.AddGeometry(lineGeometry);

                // 梁断面形状を描画
                if (viewModel.IsBeamElementSectionVisible)
                {
                    var fbSections = fbInput.Sections?.ToDictionary(s => s.No, s => s);
                    double bw = beam.Width;
                    double bh = beam.Height;
                    if (fbSections != null && fbSections.TryGetValue(beam.SectionNo, out var sec))
                    {
                        bw = sec.Width;
                        bh = sec.Height;
                    }
                    AddBeamSectionGeometry2D(viewModel, point0, point1, bw, bh, beam.AngleBeta);
                }

                // 要素番号表示
                if (viewModel.IsElementNoVisible)
                {
                    // 梁の中心点に要素番号を表示
                    Point midPoint = new((coord0.X + coord1.X) / 2, (coord0.Y + coord1.Y) / 2);
                    AddText3D(Brushes.DarkOrange, beam.No.ToString(), midPoint.X, midPoint.Y, "C", "C", 0);
                }

                // 梁要素ラベル（材料No, 材料名称, 断面No, 断面名称, β）
                {
                    var labels = new System.Collections.Generic.List<string>();
                    if (viewModel.IsBeamMaterialNoVisible)
                        labels.Add($"M{beam.MaterialNo}");
                    if (viewModel.IsBeamMaterialNameVisible && matDict != null && matDict.TryGetValue(beam.MaterialNo, out var mat))
                        labels.Add(mat.Name);
                    if (viewModel.IsBeamSectionNoVisible)
                        labels.Add($"S{beam.SectionNo}");
                    if (viewModel.IsBeamSectionNameVisible && secDict != null && secDict.TryGetValue(beam.SectionNo, out var sec2))
                        labels.Add(sec2.Name);
                    if (viewModel.IsBeamAngleBetaVisible)
                        labels.Add($"\u03b2={beam.AngleBeta:0.#}\u00b0");

                    if (labels.Count > 0)
                    {
                        Point midPt = new((coord0.X + coord1.X) / 2, (coord0.Y + coord1.Y) / 2);
                        string labelText = string.Join(" ", labels);
                        AddText3D(Brushes.Teal, labelText, midPt.X, midPt.Y, "C", "T", 0);
                    }
                }

                // 要素座標系
                if (viewModel.IsBeamLocalAxesVisible)
                {
                    double length = viewModel.LabelSize / 20.0;
                    Point3D center = new((point0.X + point1.X) * 0.5, (point0.Y + point1.Y) * 0.5, (point0.Z + point1.Z) * 0.5);

                    // 梁軸方向（局所X軸）
                    Vector3D dir = point1 - point0;
                    double dirLen = dir.Length;
                    if (dirLen > 1e-9)
                    {
                        dir.Normalize();

                        // 局所座標系（AddBeamSectionGeometry2D と同じロジック）
                        Vector3D up = new(0, 0, 1);
                        Vector3D localZ;
                        if (Math.Abs(Vector3D.DotProduct(dir, up)) > 0.999)
                            localZ = new Vector3D(0, 1, 0);
                        else
                        {
                            localZ = up - Vector3D.DotProduct(up, dir) * dir;
                            localZ.Normalize();
                        }
                        Vector3D localY = Vector3D.CrossProduct(localZ, dir);
                        localY.Normalize();

                        Point3D endX = new(center.X + dir.X * length, center.Y + dir.Y * length, center.Z + dir.Z * length);
                        Point3D endY = new(center.X + localY.X * length, center.Y + localY.Y * length, center.Z + localY.Z * length);
                        Point3D endZ = new(center.X + localZ.X * length, center.Y + localZ.Y * length, center.Z + localZ.Z * length);

                        var axisPoints = new (Point3D start, Point3D end, Brush color, string name, PathGeometry pathGeometry)[]
                        {
                            (center, endX, Brushes.Red, "AxisX", viewModel.CanvasGeometry.PathGeoAxisX),
                            (center, endY, Brushes.Green, "AxisY", viewModel.CanvasGeometry.PathGeoAxisY),
                            (center, endZ, Brushes.Blue, "AxisZ", viewModel.CanvasGeometry.PathGeoAxisZ),
                        };

                        foreach (var (start3D, end3D, color, name, pathGeometry) in axisPoints)
                        {
                            Point start2D = viewModel.CanvasThreeDView.Transformation(start3D);
                            Point end2D = viewModel.CanvasThreeDView.Transformation(end3D);
                            AddAxisLine3D(start2D, end2D, color, name, pathGeometry);
                        }
                    }
                }
            }

            // 基礎梁節点を描画
            foreach (var node in fbInput.Nodes)
            {
                if (!node.IsVisible) continue;
                // 選択された節点はUpdateSelectedNodesAndElements3D()で描画するのでスキップ
                if (node.IsSelected) continue;

                Point3D loc = new(node.X, node.Y, node.Z);
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                // 節点を円として追加
                double radius = 0.3; // 2D表示での半径（ピクセル単位相当）
                EllipseGeometry ellipse = new(coord, radius, radius);
                viewModel.CanvasGeometry.PathGeoFoundationNodes.AddGeometry(ellipse);
            }

            // プレビュー線のクリーンアップ（AddElementモードでない、またはTempStartNodeがnullの場合）
            if (viewModel.CurrentEditMode != CanvasEditMode.AddElement || viewModel.TempStartNode == null)
            {
                ClearFoundationBeamPreview();
            }
        }

        /// <summary>
        /// 梁断面形状を2Dキャンバスに描画（3D座標の4隅を投影して矩形を描く）
        /// </summary>
        private void AddBeamSectionGeometry2D(MainWindowViewModel viewModel, Point3D p0, Point3D p1, double width, double height, double angleBetaDeg)
        {
            // 梁軸方向
            Vector3D dir = new(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            double len = dir.Length;
            if (len < 1e-9) return;
            dir.Normalize();

            // 局所座標系（3D AddBeamElement と同じロジック）
            Vector3D up = new(0, 0, 1);
            Vector3D localZ;
            if (Math.Abs(Vector3D.DotProduct(dir, up)) > 0.999)
                localZ = new Vector3D(0, 1, 0);
            else
            {
                localZ = up - Vector3D.DotProduct(up, dir) * dir;
                localZ.Normalize();
            }
            Vector3D localY = Vector3D.CrossProduct(localZ, dir);
            localY.Normalize();

            // AngleBeta 回転
            if (Math.Abs(angleBetaDeg) > 1e-9)
            {
                double rad = angleBetaDeg * Math.PI / 180.0;
                double cosB = Math.Cos(rad);
                double sinB = Math.Sin(rad);
                Vector3D newY = cosB * localY + sinB * localZ;
                Vector3D newZ = -sinB * localY + cosB * localZ;
                localY = newY;
                localZ = newZ;
            }

            double hw = width / 2.0;
            double hh = height / 2.0;

            // 各端の4隅を3D座標で計算し、2Dに投影
            var path = viewModel.CanvasGeometry.PathGeoBeamSections;
            var transform = viewModel.CanvasThreeDView;

            Point3D[] ends = [p0, p1];
            Point[][] corners2D = new Point[2][];

            for (int e = 0; e < 2; e++)
            {
                var c = ends[e];
                corners2D[e] =
                [
                    transform.Transformation(new Point3D(c.X - hw * localY.X - hh * localZ.X, c.Y - hw * localY.Y - hh * localZ.Y, c.Z - hw * localY.Z - hh * localZ.Z)),
                    transform.Transformation(new Point3D(c.X + hw * localY.X - hh * localZ.X, c.Y + hw * localY.Y - hh * localZ.Y, c.Z + hw * localY.Z - hh * localZ.Z)),
                    transform.Transformation(new Point3D(c.X + hw * localY.X + hh * localZ.X, c.Y + hw * localY.Y + hh * localZ.Y, c.Z + hw * localY.Z + hh * localZ.Z)),
                    transform.Transformation(new Point3D(c.X - hw * localY.X + hh * localZ.X, c.Y - hw * localY.Y + hh * localZ.Y, c.Z - hw * localY.Z + hh * localZ.Z)),
                ];
            }

            // 端面の矩形（I端, J端）
            for (int e = 0; e < 2; e++)
            {
                for (int i = 0; i < 4; i++)
                {
                    path.AddGeometry(new LineGeometry(corners2D[e][i], corners2D[e][(i + 1) % 4]));
                }
            }

            // 4本の稜線（I端→J端）
            for (int i = 0; i < 4; i++)
            {
                path.AddGeometry(new LineGeometry(corners2D[0][i], corners2D[1][i]));
            }
        }

        // 一般節点（InputNode）描画の更新
        private void UpdateInputNodes3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel?.InputNodes == null) return;

            foreach (var node in viewModel.CurrentInputModel.InputNodes)
            {
                if (!node.IsVisible) continue;
                // 選択されたノードはUpdateSelectedNodesAndElements3D()で描画するのでスキップ
                if (node.IsSelected) continue;

                // 3D座標を2Dスクリーン座標に変換
                Point3D loc = new(node.X, node.Y, node.Z);
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                // 杭節点と同じサイズ（actualNodeSize * 0.5）
                double radius = actualNodeSize * 0.5;

                // 円として追加
                EllipseGeometry ellipse = new(coord, radius, radius);

                // ノードタイプに応じて適切な PathGeometry に追加
                if (node.Type == NodeType.Pile)
                {
                    viewModel.CanvasGeometry.PathGeoInputNodesPile.AddGeometry(ellipse);
                }
                else
                {
                    viewModel.CanvasGeometry.PathGeoInputNodesGeneral.AddGeometry(ellipse);
                }

                // 一般節点の番号表示（杭配置番号と区別するため Purple を使用）
                if (viewModel.IsNodeNoVisible && node.Type == NodeType.General)
                {
                    AddText3D(Brushes.Purple, node.No.ToString(), coord.X, coord.Y, "L", "B", 0.0);
                }

                // 一般節点のZ座標表示
                if (viewModel.IsGeneralNodeZVisible && node.Type == NodeType.General)
                {
                    AddText3D(Brushes.Purple, $"Z={node.Z:N3}", coord.X, coord.Y, "L", "T", 0.0);
                }
            }
        }

    }
}
