using MathNet.Numerics.LinearAlgebra;
using PileDesign.Constants;
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
                // v2 セマンティクス: 杭体描画の起点は杭頭 Z (= pile.Z - ΔZc)
                pileBodySegments = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileBodySegments;
                double pileTopZ = pileLocation.PileHeadZ;
                zs.Add(pileTopZ);
                foreach (var segment in pileBodySegments)
                {
                    zs.Add(pileTopZ - segment.SegmentDepth);
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

            // zToeTop: 杭体の描画下限 (拡底/拡大根固め部の頂点 = 杭体の終わりかつ拡張形状の始まり)
            //   - 場所打ちコンクリート杭: 拡底円錐+円柱の頂点まで
            //   - 既製コンクリート杭の埋込み杭: 拡大根固め球根の頂点まで (PileToeDia × PileToeHeightRatio)
            //   - 回転貫入杭: 拡張形状 (拡底等) が存在しないため、杭体は zs[^1] (真の杭先端) まで描画
            //   - Smart-MAGNUM: 根固め部上端は杭先端の 2m 上で固定 (根固め部径×高さ径比ではない)
            string _ctypeForToe = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileConstructionType;
            bool _isSmartMagnum = PileConstructionTypeNames.IsSmartMagnum(_ctypeForToe);
            double zToeTop = pileToeDia <= pileBottomDia && !_isSmartMagnum ? zs[^1] :
                (_ctypeForToe == "場所打ちコンクリート杭"
                ? zs[^1] + (pileToeDia - pileBottomDia) * 0.5 / Math.Tan(pileToeAngle * Math.PI / 180) + pileToeHeight
                : _ctypeForToe == "回転貫入杭"
                ? zs[^1]
                : _isSmartMagnum
                ? zs[^1] + SoilPile.SmartMagnumBulbTopAboveToe
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
                    AddNodularPilePositionGeometry(
                        x, y, z1, zs[i + 1], zToeTop, pileBodySegments[i], pileDia2D, flattening);

                    var ctype = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileConstructionType;
                    if (ctype == "場所打ちコンクリート杭")
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            // 拡底部ジオメトリ
                            AddInsituPileToeGeometry(
                                pointB, pileToeDia, pileDia, flattening,
                                ctype, pileBodySegments,
                                pileToeAngle, pileToeHeight);
                        }
                    }
                    else if (ctype == "回転貫入杭")
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            // 回転貫入杭の螺旋羽根 (羽根径=pileToeDia, ピッチ=Dp/5)
                            // 杭先端から下方に 1 巻きを描く + 同区間に杭体を延長
                            AddHelicalBladeToeGeometry(x, y, zs[^1], pileToeDia, pileDia);
                        }
                    }
                    else
                    {
                        if (i == zs.Count - 2 && (pileToeDia > pileDia || _isSmartMagnum))
                        {
                            // 先端球根ジオメトリ。
                            // Smart-MAGNUM は「杭先端の 2m 上 〜 杭先端の LL 下」、
                            // 他の埋込み杭は「杭先端を下端として上方へ 根固め部径×高さ径比」。
                            double bulbBelowToe = _isSmartMagnum
                                ? viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].SmartMagnumLL
                                : 0.0;
                            double bulbHeight = _isSmartMagnum
                                ? SoilPile.SmartMagnumBulbTopAboveToe + bulbBelowToe
                                : pileToeDia * pileToeHeightRatio;
                            AddConcretePrecastPileToeGeometry(
                                pointB, pileToeDia, pileDia, flattening, bulbHeight, bulbBelowToe);
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


        /// <summary>
        /// PHC節杭 の節を、節部の円（Do）とテーパーの稜線で描く。
        ///
        /// 形状の根拠: Do・D はカタログ値、テーパーは姿図実測で厳密に 45°（軸方向長 = (Do−D)/2）。
        /// 節部の平坦長のみカタログに寸法記入が無く、姿図実測でテーパーと等長としている。
        /// 図示専用で、断面耐力・自重・支持力の計算には使わない。
        /// 拡大根固め等で杭体の描画下限が切り上がる場合 (zToeTop) は、その範囲内の節のみ描く。
        /// </summary>
        private void AddNodularPilePositionGeometry(
            double x, double y, double zSegmentTop, double zSegmentBottom, double zToeTop,
            PileBodySegment segment, double pileDia2D, double flattening)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var section = segment?.PileSection;
            if (section == null || !section.IsNodularPile) return;

            double segmentLength = zSegmentTop - zSegmentBottom; // Z 上向き正なので上端 - 下端
            if (segmentLength <= 0) return;

            var outline = section.NodularOutline(segmentLength);
            if (outline.Count == 0) return;

            // 節の稜線は杭体輪郭より細い線の専用パスへ入れる
            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedNodeDetails
                : viewModel.CanvasGeometry.PathGeoPileNodeDetails;

            // 断面径 [mm] → 画面上の直径。既存の pileDia2D と同じ縮尺に揃える。
            double scale2D = section.PileDiameter > 0 ? pileDia2D / section.PileDiameter : 0.0;
            if (scale2D <= 0) return;

            Point? prevLeft = null, prevRight = null;

            foreach (var (depth, radius) in outline)
            {
                double z = zSegmentTop - depth;
                if (z < zToeTop || z > zSegmentTop || z < zSegmentBottom)
                {
                    prevLeft = prevRight = null;   // 描画範囲外は稜線を途切れさせる
                    continue;
                }

                var center = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, z));
                double r2D = radius * scale2D;

                // 節の立上り開始・終了位置 (軸部径) と節部・拡頭部 (最大径) の
                // どちらにも水平断面円を描く。形状の折れ位置がすべて見えるようにする。
                path.AddGeometry(new EllipseGeometry(center, r2D, r2D * flattening));

                var left = new Point(center.X - r2D, center.Y);
                var right = new Point(center.X + r2D, center.Y);
                if (prevLeft.HasValue)
                {
                    path.AddGeometry(new LineGeometry(prevLeft.Value, left));
                    path.AddGeometry(new LineGeometry(prevRight!.Value, right));
                }
                prevLeft = left;
                prevRight = right;
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
        /// <param name="bulbHeight">根固め部の全高 (m)。工法ごとの規定に従って呼び出し側が決める。</param>
        /// <param name="bulbBelowToe">根固め部が杭先端より下に張り出す長さ (m)。Smart-MAGNUM の LL。</param>
        private void AddConcretePrecastPileToeGeometry(Point pointBtm, double pileToeDia, double pileDia, double flattening, double bulbHeight, double bulbBelowToe)
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
            double factoredHeight2D = Math.Cos(phiRad) * bulbHeight * viewModel.CanvasThreeDView.Scale;

            // 杭先端より下に張り出す分だけ根固め部の底面を下げる (2D では Y が増える向き)
            double below2D = Math.Cos(phiRad) * bulbBelowToe * viewModel.CanvasThreeDView.Scale;
            var bulbBtm = new Point(pointBtm.X, pointBtm.Y + below2D);

            // 拡大根固め部の外形（実線）
            var ellipseBtm = new EllipseGeometry(bulbBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(bulbBtm.X, bulbBtm.Y - factoredHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);

            for (int j = -1; j <= 1; j += 2)
            {
                var lineGeometry = new LineGeometry(
                    new Point(bulbBtm.X + pileToeDia2D * 0.5 * j, bulbBtm.Y - factoredHeight2D),
                    new Point(bulbBtm.X + pileToeDia2D * 0.5 * j, bulbBtm.Y)
                );
                path.AddGeometry(lineGeometry);
            }

            // 根固め部内部の杭体（破線）: 杭径の楕円と側線
            var ellipseCore = new EllipseGeometry(new Point(bulbBtm.X, bulbBtm.Y - factoredHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            dashedPath.AddGeometry(ellipseCore);

            // 杭体の底面楕円（根固め底面位置）
            var ellipseCoreBtm = new EllipseGeometry(pointBtm, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            dashedPath.AddGeometry(ellipseCoreBtm);

            // 杭体の側線（根固め部内部）
            for (int j = -1; j <= 1; j += 2)
            {
                var innerLine = new LineGeometry(
                    new Point(bulbBtm.X + pileDia2D * 0.5 * j, bulbBtm.Y - factoredHeight2D),
                    new Point(bulbBtm.X + pileDia2D * 0.5 * j, pointBtm.Y)
                );
                dashedPath.AddGeometry(innerLine);
            }
        }

        /// <summary>
        /// 回転貫入杭の螺旋羽根 (1巻き、羽根径Dw、ピッチ=杭径Dp/5) を 3D 透視ビュー上に描画する。
        /// 螺旋の内外縁を実 3D 座標でサンプリングして投影し、線分連結で描く。
        /// 視点角度 (Phi) に応じて螺旋が立体的にレンダリングされる。
        /// </summary>
        /// <param name="xCenter">杭中心 X (m)</param>
        /// <param name="yCenter">杭中心 Y (m)</param>
        /// <param name="zTip">杭先端 Z (m) — 羽根の下端</param>
        /// <param name="bladeDia">羽根径 Dw (m)</param>
        /// <param name="pileDia">杭径 Dp (m)</param>
        private void AddHelicalBladeToeGeometry(
            double xCenter, double yCenter, double zTip,
            double bladeDia, double pileDia)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var path = viewModel.IsElementSplit
                ? viewModel.CanvasGeometry.PathGeoPileDividedDias
                : viewModel.CanvasGeometry.PathGeoPileDias;

            double R = bladeDia * 0.5;
            double r = pileDia * 0.5;
            double pitch = pileDia / 5.0;     // 1巻き軸方向長さ

            const int n = 32;                  // 周方向分割数

            // 外周ヘリックス (R) と内周ヘリックス (r) の 3D 点列を 2D に投影し、連続する線分で接続
            // 注: 軸座標系は +Z = 上向きなので、羽根は zTip → zTip - pitch (下方向) に伸ばす
            Point projOuterPrev = default;
            Point projInnerPrev = default;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;            // 0..1
                double theta = t * 2.0 * Math.PI;
                double z = zTip - t * pitch;
                double cos = Math.Cos(theta);
                double sin = Math.Sin(theta);

                var p3dOuter = new Point3D(xCenter + R * cos, yCenter + R * sin, z);
                var p3dInner = new Point3D(xCenter + r * cos, yCenter + r * sin, z);
                var pOuter = viewModel.CanvasThreeDView.Transformation(p3dOuter);
                var pInner = viewModel.CanvasThreeDView.Transformation(p3dInner);

                if (i > 0)
                {
                    path.AddGeometry(new LineGeometry(projOuterPrev, pOuter));
                    path.AddGeometry(new LineGeometry(projInnerPrev, pInner));
                }
                projOuterPrev = pOuter;
                projInnerPrev = pInner;
            }

            // ブレード上下端の半径方向ラインで「リボン」感を補強 (i=0 と i=n で内外を接続)
            for (int side = 0; side <= 1; side++)
            {
                double theta = side == 0 ? 0.0 : 2.0 * Math.PI;
                double z = zTip - (side == 0 ? 0.0 : pitch);
                double cos = Math.Cos(theta);
                double sin = Math.Sin(theta);
                var pOuter = viewModel.CanvasThreeDView.Transformation(
                    new Point3D(xCenter + R * cos, yCenter + R * sin, z));
                var pInner = viewModel.CanvasThreeDView.Transformation(
                    new Point3D(xCenter + r * cos, yCenter + r * sin, z));
                path.AddGeometry(new LineGeometry(pInner, pOuter));
            }

            // 杭体を羽根領域 (zTip → zTip - pitch) まで延長する追加杭体ジオメトリ
            // (羽根が杭体に巻き付いた状態を可視化)
            var ptCylTop = viewModel.CanvasThreeDView.Transformation(new Point3D(xCenter, yCenter, zTip));
            var ptCylBtm = viewModel.CanvasThreeDView.Transformation(new Point3D(xCenter, yCenter, zTip - pitch));
            double pileDia2D = pileDia * viewModel.CanvasThreeDView.Scale;
            double flattening = viewModel.CanvasThreeDView.Flattening;
            AddPileSectionGeometry(ptCylTop, ptCylBtm, pileDia2D, flattening);
        }

        // 基礎梁描画の更新
        /// <summary>
        /// 接合節点（杭頭+ΔZc位置）と剛体連結線を描画します。
        /// IsFoundationBeamVisible とは独立して、IsConnectionNodeVisible で制御されます。
        /// </summary>
        private void UpdateConnectionNodes3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;

            // VL/VL0/VLadd（Level=0）では代表節点・剛体連結線を非表示
            var selectedLC0 = LoadCases.GetLoadCase(
                viewModel.CurrentInputModel?.LoadCasesInput?.AllLoadCases, viewModel.SelectedLoadCaseName);
            bool isHorizontalLoadCase = selectedLC0 != null && (selectedLC0.Level == 1 || selectedLC0.Level == 2);
            if (isHorizontalLoadCase && viewModel.IsNodeVisible && viewModel.IsConnectionNodeVisible && viewModel.CurrentInputModel?.PileLayoutItems != null)
            {
                foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
                {
                    // 非アクティブ杭の接合節点・Rigid link線はスキップ
                    if (!pile.IsVisible) continue;

                    // 接合節点位置 (v2 セマンティクス: pile.Z は接合節点 Z)
                    double connectionZ = pile.Z;
                    Point3D locConnection = new(pile.X, pile.Y, connectionZ);
                    Point coordConnection = viewModel.CanvasThreeDView.Transformation(locConnection);

                    // 接合節点を円として追加（杭節点と同じサイズ）
                    double radius = actualNodeSize * 0.5;
                    EllipseGeometry ellipse = new(coordConnection, radius, radius);
                    viewModel.CanvasGeometry.PathGeoConnectionNodes.AddGeometry(ellipse);

                    // 杭頭から接合節点への剛体連結線を追加（細い灰色破線）
                    // v2 セマンティクス: 杭頭は PileHeadZ (= pile.Z - ΔZc)
                    Point3D locPileTop = new(pile.X, pile.Y, pile.PileHeadZ);
                    Point coordPileTop = viewModel.CanvasThreeDView.Transformation(locPileTop);
                    LineGeometry rigidLine = new() { StartPoint = coordPileTop, EndPoint = coordConnection };
                    viewModel.CanvasGeometry.PathGeoRigidConnections.AddGeometry(rigidLine);
                }
            }
        }

        private void UpdateFoundationBeams3D()
        {
            if (Canvas3DLayout == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;

            // 基礎梁・節点の描画（FoundationBeamInputが必要）
            if (viewModel.CurrentInputModel?.FoundationBeamInput == null) return;

            var fbInput = viewModel.CurrentInputModel.FoundationBeamInput;

            // 剛体連結モードでも梁要素が存在すれば描画する（自動生成含む）
            // 梁要素がなく、かつ編集モードでもない場合のみスキップ
            if (fbInput.ConnectionMode == FoundationBeamConnectionMode.RigidBody &&
                viewModel.CurrentEditMode == CanvasEditMode.None &&
                fbInput.Beams.Count == 0)
                return;

            // ラベル表示用の材料・断面辞書 (1-based 位置インデックスをキーに)
            var matDict = new Dictionary<int, BeamMaterial>();
            if (fbInput.Materials != null)
                for (int mi = 0; mi < fbInput.Materials.Count; mi++)
                    matDict[mi + 1] = fbInput.Materials[mi];
            var secDict = new Dictionary<int, BeamSection>();
            if (fbInput.Sections != null)
                for (int si = 0; si < fbInput.Sections.Count; si++)
                    secDict[si + 1] = fbInput.Sections[si];

            // 基礎梁を描画
            for (int beamIdx = 0; beamIdx < fbInput.Beams.Count; beamIdx++)
            {
                var beam = fbInput.Beams[beamIdx];
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

                // NodeJ の座標を解決
                if (beam.NodeJ_Id != Guid.Empty)
                {
                    var coordsJ = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                    if (coordsJ.HasValue)
                        loc1 = new Point3D(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);
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
                    // SectionNo (1-based 位置インデックス) から BeamSection を解決
                    BeamSection sec = beam.SectionNo >= 1 && beam.SectionNo <= (fbInput.Sections?.Count ?? 0)
                        ? fbInput.Sections[beam.SectionNo - 1] : null;
                    double bw = beam.Width;
                    double bh = beam.Height;
                    if (sec != null)
                    {
                        bw = sec.Width;
                        bh = sec.Height;
                    }
                    AddBeamSectionGeometry2D(viewModel, point0, point1, bw, bh, beam.AngleBeta);
                }

                // 要素番号表示 (1-based 位置インデックス)
                if (viewModel.IsElementNoVisible)
                {
                    // 梁の中心点に要素番号を表示
                    Point midPoint = new((coord0.X + coord1.X) / 2, (coord0.Y + coord1.Y) / 2);
                    AddText3D(Brushes.DarkOrange, (beamIdx + 1).ToString(), midPoint.X, midPoint.Y, "C", "C", 0);
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
