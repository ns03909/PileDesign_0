using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Element = PileDesign.Models.InputData.Element;
using Point = System.Windows.Point;

namespace PileDesign.Views
{
    public partial class MainWindow : Window
    {
        // 要素描画の更新
        private void UpdateGeneralElement3D()
        {
            if (Canvas3DLayout == null) return;

            if (DataContext is not MainWindowViewModel viewModel) return;

            if (viewModel.CurrentInputModel == null) return;

            foreach (Element element in viewModel.CurrentInputModel.Elements)
            {
                if (element.Nodes.Count == 0 || element.Nodes.Count == 1) { continue; }

                Point3D loc0;
                Point3D loc1;
                double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

                if (viewModel.IsElementShownAtSettlementPlane)
                {
                    loc0 = new(element.Nodes[0].Point3D.X, element.Nodes[0].Point3D.Y, loadingPlaneAlt);
                    loc1 = new(element.Nodes[1].Point3D.X, element.Nodes[1].Point3D.Y, loadingPlaneAlt);
                }
                else
                {
                    loc0 = element.Nodes[0].Point3D;
                    loc1 = element.Nodes[1].Point3D;
                }

                if (viewModel.IsShrinkElementMode)
                {
                    (loc0, loc1) = GetShrinkElementPoints(loc0, loc1);
                }

                Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);

                LineGeometry lineGeometry = new() { StartPoint = coord0, EndPoint = coord1 };
                viewModel.CanvasGeometry.PathElements.AddGeometry(lineGeometry);

                // 要素番号
                if (viewModel.IsElementNoVisible)
                {
                    double x = (coord0.X + coord1.X) * 0.5;
                    double y = (coord0.Y + coord1.Y) * 0.5;
                    double theta;
                    if (coord1.X != coord0.X)
                    {
                        theta = 180 / Math.PI * Math.Atan((coord1.Y - coord0.Y) / (coord1.X - coord0.X));
                    }
                    else
                    {
                        theta = 90;
                    }
                    AddText3D(Brushes.SaddleBrown, GetElementNoText(element), x, y, "C", "B", theta);
                }
            }
        }

        // 変形後の要素描画の更新
        private void UpdateDeformedGeneralElement3D(
            ObservableCollection<double> values)
        {
            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            double maxArrowLength2D = viewModel.ArrowLength;

            if (viewModel == null || values.Count == 0)
            { return; }

            double flattening = viewModel.CanvasThreeDView.Flattening;
            double flattening0 = Math.Sqrt(1 - Math.Pow(flattening, 2));
            double absMaxValue = Math.Max(Math.Abs(values.Max()), Math.Abs(values.Min()));

            if (absMaxValue == 0.0) return;

            if (Canvas3DLayout == null) return;

            if (viewModel.CurrentInputModel == null) return;

            Point3D loc0;
            Point3D loc1;
            double loadingPlaneAlt = viewModel.CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude;

            foreach (Element element in viewModel.CurrentInputModel.Elements)
            {
                if (element.Nodes.Count == 0 || element.Nodes.Count == 1) { continue; }

                if (viewModel.IsElementShownAtSettlementPlane)
                {
                    loc0 = new(element.Nodes[0].Point3D.X, element.Nodes[0].Point3D.Y, loadingPlaneAlt);
                    loc1 = new(element.Nodes[1].Point3D.X, element.Nodes[1].Point3D.Y, loadingPlaneAlt);
                }
                else
                {
                    loc0 = element.Nodes[0].Point3D;
                    loc1 = element.Nodes[1].Point3D;
                }

                double y0 = maxArrowLength2D * values[element.Nodes[0].No - 1] / absMaxValue;
                double y1 = maxArrowLength2D * values[element.Nodes[1].No - 1] / absMaxValue;

                Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0) + new Vector(0, y0 * flattening0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1) + new Vector(0, y1 * flattening0);

                LineGeometry lineGeometry = new() { StartPoint = coord0, EndPoint = coord1 };
                viewModel.CanvasGeometry.PathElements.AddGeometry(lineGeometry);
            }
        }

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
            var zs = new ObservableCollection<double>();

            if (!viewModel.IsElementSplit) // 要素未分割の場合
            {
                pileBodySegments = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileBodySegments;
                zs.Add(pileLocation.Point3D.Z);
                foreach (var segment in pileBodySegments)
                {
                    zs.Add(pileLocation.Point3D.Z - segment.SegmentDepth);
                }
            }

            else // 要素分割済の場合
            {
                if (pileLocation.SoilPileAltNo <= 0 ||
                pileLocation.SoilPileAltNo > viewModel.CurrentInputModel.ElementDivision.SoilPiles.Count)
                {
                    return;
                }
                var soilPile = viewModel.CurrentInputModel.ElementDivision.SoilPiles[pileLocation.SoilPileAltNo - 1];
                pileBodySegments = soilPile.PileBodySegments;
                zs = new ObservableCollection<double>(soilPile.ZDataItems.Select(zDataItem => zDataItem.Z));
            }

            double x = pileLocation.Point3D.X;
            double y = pileLocation.Point3D.Y;

            var pointT = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[0]));
            var pointB = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[^1]));

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

                // 
                if (viewModel.IsPileSectionVisible)
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

                    //要素座標系
                    if (viewModel.IsBeamLocalAxesVisible)
                    {
                        //double length = 0.01 * viewModel.CanvasThreeDView.Scale; // 軸の長さ
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

            double pileToeDia2D = pileToeDia * viewModel.CanvasThreeDView.Scale;
            double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;
            double phiRad = Math.Abs(viewModel.CanvasThreeDView.Phi) * Math.PI / 180.0;
            double height = pileToeDia * 2.0;
            double factoredHeight2D = Math.Cos(phiRad) * height * viewModel.CanvasThreeDView.Scale;

            var ellipseBtm = new EllipseGeometry(pointBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseCore = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);

            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);
            path.AddGeometry(ellipseCore);

            for (int j = -1; j <= 1; j += 2)
            {
                var lineGeometry = new LineGeometry(
                    new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y - factoredHeight2D),
                    new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y)
                );
                path.AddGeometry(lineGeometry);
            }
        }

        // 基礎梁描画の更新
        private void UpdateFoundationBeams3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (viewModel.CurrentInputModel?.FoundationBeamInput == null) return;

            var fbInput = viewModel.CurrentInputModel.FoundationBeamInput;

            // 剛体連結モードでは描画しない
            if (fbInput.ConnectionMode == FoundationBeamConnectionMode.RigidBody)
                return;

            var nodeDict = fbInput.Nodes.ToDictionary(n => n.No, n => n);

            // 基礎梁要素を描画
            foreach (var beam in fbInput.Beams)
            {
                if (!nodeDict.TryGetValue(beam.NodeI_No, out var nodeI)) continue;
                if (!nodeDict.TryGetValue(beam.NodeJ_No, out var nodeJ)) continue;

                Point3D loc0 = new(nodeI.X, nodeI.Y, nodeI.Z);
                Point3D loc1 = new(nodeJ.X, nodeJ.Y, nodeJ.Z);

                // 3D -> 2D 変換
                Point coord0 = viewModel.CanvasThreeDView.Transformation(loc0);
                Point coord1 = viewModel.CanvasThreeDView.Transformation(loc1);

                // 梁の中心線を追加
                LineGeometry lineGeometry = new() { StartPoint = coord0, EndPoint = coord1 };
                viewModel.CanvasGeometry.PathGeoFoundationBeams.AddGeometry(lineGeometry);
            }

            // 基礎梁節点を描画
            foreach (var node in fbInput.Nodes)
            {
                Point3D loc = new(node.X, node.Y, node.Z);
                Point coord = viewModel.CanvasThreeDView.Transformation(loc);

                // 節点を円として追加
                double radius = 0.3; // 2D表示での半径（ピクセル単位相当）
                EllipseGeometry ellipse = new(coord, radius, radius);
                viewModel.CanvasGeometry.PathGeoFoundationNodes.AddGeometry(ellipse);
            }
        }

    }
}
