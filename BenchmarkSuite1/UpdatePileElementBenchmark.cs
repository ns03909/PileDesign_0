using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using Microsoft.VSDiagnostics;

namespace PileDesign.Benchmarks
{
    [SimpleJob(RuntimeMoniker.Net80, baseline: true)]
    [CPUUsageDiagnoser]
    public class UpdatePileElementBenchmark
    {
        private MainWindowViewModel _vm = null!;
        private PileLayoutDataItem _pile = null!;

        [GlobalSetup]
        public void Setup()
        {
            _vm = new MainWindowViewModel
            {
                IsElementSplit = false,
                IsPileSectionVisible = true,
                IsBeamLocalAxesVisible = false,
                IsElementShownAtSettlementPlane = false,
                IsElementNoVisible = false
            };
            // シンプルな杭体（Example9 と同径の 1000mm）
            var pileBody = new PileBodyInput
            {
                PileBodyRef = "(PB1)",
                PileToeDia = 1000.0,
                PileConstructionType = "場所打ちコンクリート杭",
                PileTopType = "鉄筋定着工法",
                PileBodyType = "場所打ち鉄筋コンクリート杭"
            };
            pileBody.PileBodySegments.Clear();
            pileBody.PileBodySegments.Add(new PileBodySegment { SegmentDepth = 11.5, SegmentLength = 11.5 });
            pileBody.PileBodySegments[0].PileSection.PileDiameter = 1000.0;
            pileBody.PileBodySegments.Add(new PileBodySegment { SegmentDepth = 14.7, SegmentLength = 14.7 });
            pileBody.PileBodySegments[1].PileSection.PileDiameter = 1000.0;
            _vm.CurrentInputModel.PileBodies.Clear();
            _vm.CurrentInputModel.PileBodies.Add(pileBody);

            _pile = new PileLayoutDataItem
            {
                PileNo = 1,
                PileBodyNo = 1,
                SoilPileAltNo = 1,
                IsSelected = false
            };

            _vm.CurrentInputModel.PileLayoutItems.Clear();
            _vm.CurrentInputModel.PileLayoutItems.Add(_pile);
        }

        [Benchmark]
        public void UpdatePileElement()
        {
            UpdatePileElementCore(_vm, _pile);
        }

        // --- 以下: MainWindow.UpdatePileElement と同等ロジック（描画先は VM 内の PathGeometry） ---
        private static void UpdatePileElementCore(MainWindowViewModel viewModel, PileLayoutDataItem pileLocation)
        {
            if (viewModel.CurrentInputModel.PileBodies.Count == 0)
                return;
            if (pileLocation.PileBodyNo <= 0 || pileLocation.PileBodyNo > viewModel.CurrentInputModel.PileBodies.Count)
            {
                return;
            }

            ObservableCollection<PileBodySegment> pileBodySegments;
            PileBodyInput pileBody = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1];
            var zs = new ObservableCollection<double>();
            if (!viewModel.IsElementSplit)
            {
                pileBodySegments = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileBodySegments;
                zs.Add(pileLocation.Point3D.Z);
                foreach (var segment in pileBodySegments)
                {
                    zs.Add(pileLocation.Point3D.Z - segment.SegmentDepth);
                }
            }
            else
            {
                if (pileLocation.SoilPileAltNo <= 0 || pileLocation.SoilPileAltNo > viewModel.CurrentInputModel.ElementDivision.SoilPiles.Count)
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
            if (pileBodySegments.Count == 0)
                return;
            double pileBottomDia = pileBodySegments[^1].PileSection.PileDiameter / 1000.0;
            double pileToeDia = viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileToeDia / 1000.0;
            double pileToeAngle = pileBody.InsituPileToeAngle;
            double pileToeHeight = pileBody.InsituPileToeHeight / 1000.0;
            double pileToeHeightRatio = pileBody.PrecastConcretePileToeHeightRatio;
            double zToeTop = pileToeDia <= pileBottomDia ? zs[^1] : (viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileConstructionType == "場所打ちコンクリート杭" ? zs[^1] + (pileToeDia - pileBottomDia) * 0.5 / Math.Tan(pileToeAngle * Math.PI / 180) + pileToeHeight : zs[^1] + pileToeDia * pileToeHeightRatio);
            for (int i = 0; i < zs.Count - 1; i++)
            {
                double z1 = zs[i];
                double z2 = Math.Max(zs[i + 1], zToeTop);
                var point1 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, z1));
                var point2 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, z2));
                var point3 = viewModel.CanvasThreeDView.Transformation(new Point3D(x, y, zs[i + 1]));
                AddEllipseGeometry(point2, point3, viewModel.IsElementSplit ? viewModel.CanvasGeometry.PathGeoPileDividedNonTopNodes : viewModel.CanvasGeometry.PathGeoPileNonTopNodes);
                if (viewModel.IsPileSectionVisible)
                {
                    double pileDia2D = pileBodySegments[i].PileSection.PileDiameter / 1000.0 * viewModel.CanvasThreeDView.Scale;
                    double pileDia = pileBodySegments[i].PileSection.PileDiameter / 1000.0;
                    double flattening = viewModel.CanvasThreeDView.Flattening;
                    AddPileSectionGeometry(point1, point2, pileDia2D, flattening, viewModel);
                    if (viewModel.CurrentInputModel.PileBodies[pileLocation.PileBodyNo - 1].PileConstructionType == "場所打ちコンクリート杭")
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            AddInsituPileToeGeometry(pointB, pileToeDia, pileDia, flattening, pileBodySegments, pileToeAngle, pileToeHeight, viewModel);
                        }
                    }
                    else
                    {
                        if (i == zs.Count - 2 && pileToeDia > pileDia)
                        {
                            AddConcretePrecastPileToeGeometry(pointB, pileToeDia, pileDia, flattening, pileBodySegments, viewModel);
                        }
                    }
                }
            }
        }

        private static void AddLineGeometry(Point start, Point end, PathGeometry target)
        {
            target.AddGeometry(new LineGeometry(start, end));
        }

        private static void AddEllipseGeometry(Point center, Point anchor, PathGeometry target)
        {
            // 半径は中心とアンカーの距離を使用（簡易）
            double radius = Math.Max(0.001, (center - anchor).Length);
            target.AddGeometry(new EllipseGeometry(center, radius * 0.2, radius * 0.2));
        }

        private static void AddPileSectionGeometry(Point point1, Point point2, double pileDia2D, double flattening, MainWindowViewModel vm)
        {
            var path = vm.IsElementSplit ? vm.CanvasGeometry.PathGeoPileDividedDias : vm.CanvasGeometry.PathGeoPileDias;
            var ellipse1 = new EllipseGeometry(point1, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            var ellipse2 = new EllipseGeometry(point2, pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            path.AddGeometry(ellipse1);
            path.AddGeometry(ellipse2);
            for (int j = -1; j <= 1; j += 2)
            {
                path.AddGeometry(new LineGeometry(new Point(point1.X + pileDia2D * 0.5 * j, point1.Y), new Point(point2.X + pileDia2D * 0.5 * j, point2.Y)));
            }
        }

        private static void AddInsituPileToeGeometry(Point pointBtm, double pileToeDia, double pileDia, double flattening, ObservableCollection<PileBodySegment> pileBodySegments, double insituPileToeAngle, double insituPileToeHeight, MainWindowViewModel vm)
        {
            var path = vm.IsElementSplit ? vm.CanvasGeometry.PathGeoPileDividedDias : vm.CanvasGeometry.PathGeoPileDias;
            double pileToeDia2D = pileToeDia * vm.CanvasThreeDView.Scale;
            double pileDia2D = pileDia * vm.CanvasThreeDView.Scale;
            double phiRad = Math.Abs(vm.CanvasThreeDView.Phi) * Math.PI / 180.0;
            double factoredToeCylinderHeight2D = Math.Cos(phiRad) * insituPileToeHeight * vm.CanvasThreeDView.Scale;
            double coneHeight = (pileToeDia - pileDia) * 0.5 / Math.Tan(insituPileToeAngle * Math.PI / 180);
            double factoredConeHeight2D = Math.Cos(phiRad) * coneHeight * vm.CanvasThreeDView.Scale;
            var ellipseBtm = new EllipseGeometry(pointBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredToeCylinderHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipse3 = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredConeHeight2D - factoredToeCylinderHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);
            path.AddGeometry(ellipse3);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Point start = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y);
                Point end = new(pointBtm.X + sign * pileToeDia2D * 0.5, pointBtm.Y - factoredToeCylinderHeight2D);
                AddLineGeometry(start, end, path);
            }
        }

        private static void AddConcretePrecastPileToeGeometry(Point pointBtm, double pileToeDia, double pileDia, double flattening, ObservableCollection<PileBodySegment> pileBodySegments, MainWindowViewModel vm)
        {
            var path = vm.IsElementSplit ? vm.CanvasGeometry.PathGeoPileDividedDias : vm.CanvasGeometry.PathGeoPileDias;
            double pileToeDia2D = pileToeDia * vm.CanvasThreeDView.Scale;
            double pileDia2D = pileDia * vm.CanvasThreeDView.Scale;
            double phiRad = Math.Abs(vm.CanvasThreeDView.Phi) * Math.PI / 180.0;
            double height = pileToeDia * 2.0;
            double factoredHeight2D = Math.Cos(phiRad) * height * vm.CanvasThreeDView.Scale;
            var ellipseBtm = new EllipseGeometry(pointBtm, pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseTop = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileToeDia2D * 0.5, pileToeDia2D * 0.5 * flattening);
            var ellipseCore = new EllipseGeometry(new Point(pointBtm.X, pointBtm.Y - factoredHeight2D), pileDia2D * 0.5, pileDia2D * 0.5 * flattening);
            path.AddGeometry(ellipseBtm);
            path.AddGeometry(ellipseTop);
            path.AddGeometry(ellipseCore);
            for (int j = -1; j <= 1; j += 2)
            {
                path.AddGeometry(new LineGeometry(new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y - factoredHeight2D), new Point(pointBtm.X + pileToeDia2D * 0.5 * j, pointBtm.Y)));
            }
        }
    }
}