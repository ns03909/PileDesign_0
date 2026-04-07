using System;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using PileDesign.Models.InputData;

namespace PileDesign.Output
{
    /// <summary>
    /// 通り心線・シンボル・寸法線をDXFドキュメントに追加する共通ヘルパー。
    /// 2D伏図(z=0)と3Dモデル(z=杭頭最高値)の両方から利用可能。
    /// </summary>
    public static class GridDimHelper
    {
        private const double GridSymbolRadius = 0.4;
        private const double GridExtension = 2.0;
        private const double DimOffset = 1.5;
        private const double DimTickSize = 0.15;
        private const double TextHeight = 0.3;
        private const double GridSymbolTextHeight = 0.4;

        public static void AddGridAndDimensions(CadDocument doc, InputModel inputModel,
            Layer gridLayer, Layer symbolLayer, Layer dimLayer, double z)
        {
            GetModelBounds(inputModel, out double xMin, out double xMax, out double yMin, out double yMax);
            AddGridLines(doc, inputModel, gridLayer, symbolLayer, xMin, xMax, yMin, yMax, z);
            AddGridDimensions(doc, inputModel, dimLayer, xMin, xMax, yMin, yMax, z);
        }

        private static void GetModelBounds(InputModel inputModel,
            out double xMin, out double xMax, out double yMin, out double yMax)
        {
            xMin = double.MaxValue; xMax = double.MinValue;
            yMin = double.MaxValue; yMax = double.MinValue;

            if (inputModel.PileLayoutItems != null)
                foreach (var p in inputModel.PileLayoutItems)
                {
                    xMin = Math.Min(xMin, p.X); xMax = Math.Max(xMax, p.X);
                    yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
                }
            if (inputModel.GridXItems != null)
                foreach (var g in inputModel.GridXItems)
                { xMin = Math.Min(xMin, g.Coord); xMax = Math.Max(xMax, g.Coord); }
            if (inputModel.GridYItems != null)
                foreach (var g in inputModel.GridYItems)
                { yMin = Math.Min(yMin, g.Coord); yMax = Math.Max(yMax, g.Coord); }

            if (xMin > xMax) { xMin = 0; xMax = 10; }
            if (yMin > yMax) { yMin = 0; yMax = 10; }
        }

        private static void AddGridLines(CadDocument doc, InputModel inputModel,
            Layer gridLayer, Layer symbolLayer,
            double xMin, double xMax, double yMin, double yMax, double z)
        {
            if (inputModel.GridXItems != null)
                foreach (var grid in inputModel.GridXItems)
                {
                    double x = grid.Coord;
                    double ly = yMin - GridExtension;
                    double hy = yMax + GridExtension;
                    doc.Entities.Add(new Line
                    {
                        StartPoint = new XYZ(x, ly, z),
                        EndPoint = new XYZ(x, hy, z),
                        Layer = gridLayer
                    });
                    AddGridSymbol(doc, symbolLayer, x, ly - GridSymbolRadius * 1.5, z, grid.Name);
                }

            if (inputModel.GridYItems != null)
                foreach (var grid in inputModel.GridYItems)
                {
                    double y = grid.Coord;
                    double lx = xMin - GridExtension;
                    double hx = xMax + GridExtension;
                    doc.Entities.Add(new Line
                    {
                        StartPoint = new XYZ(lx, y, z),
                        EndPoint = new XYZ(hx, y, z),
                        Layer = gridLayer
                    });
                    AddGridSymbol(doc, symbolLayer, hx + GridSymbolRadius * 1.5, y, z, grid.Name);
                }
        }

        private static void AddGridSymbol(CadDocument doc, Layer layer,
            double x, double y, double z, string name)
        {
            doc.Entities.Add(new Circle
            {
                Center = new XYZ(x, y, z),
                Radius = GridSymbolRadius,
                Layer = layer
            });
            doc.Entities.Add(new MText
            {
                InsertPoint = new XYZ(x, y, z),
                Height = GridSymbolTextHeight,
                Value = name ?? "",
                Layer = layer,
                AttachmentPoint = AttachmentPointType.MiddleCenter
            });
        }

        private static void AddGridDimensions(CadDocument doc, InputModel inputModel,
            Layer dimLayer, double xMin, double xMax, double yMin, double yMax, double z)
        {
            if (inputModel.GridXItems != null && inputModel.GridXItems.Count >= 2)
            {
                var sorted = inputModel.GridXItems.OrderBy(g => g.Coord).ToList();
                double dimY = yMin - GridExtension - GridSymbolRadius * 3 - DimOffset;
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    double x1 = sorted[i].Coord, x2 = sorted[i + 1].Coord;
                    AddDim(doc, dimLayer, x1, dimY, x2, dimY, z, Math.Abs(x2 - x1), true);
                }
            }

            if (inputModel.GridYItems != null && inputModel.GridYItems.Count >= 2)
            {
                var sorted = inputModel.GridYItems.OrderBy(g => g.Coord).ToList();
                double dimX = xMax + GridExtension + GridSymbolRadius * 3 + DimOffset;
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    double y1 = sorted[i].Coord, y2 = sorted[i + 1].Coord;
                    AddDim(doc, dimLayer, dimX, y1, dimX, y2, z, Math.Abs(y2 - y1), false);
                }
            }
        }

        private static void AddDim(CadDocument doc, Layer layer,
            double x1, double y1, double x2, double y2, double z, double value, bool isHoriz)
        {
            doc.Entities.Add(new Line { StartPoint = new XYZ(x1, y1, z), EndPoint = new XYZ(x2, y2, z), Layer = layer });

            if (isHoriz)
            {
                doc.Entities.Add(new Line { StartPoint = new XYZ(x1, y1 - DimTickSize, z), EndPoint = new XYZ(x1, y1 + DimTickSize, z), Layer = layer });
                doc.Entities.Add(new Line { StartPoint = new XYZ(x2, y2 - DimTickSize, z), EndPoint = new XYZ(x2, y2 + DimTickSize, z), Layer = layer });
            }
            else
            {
                doc.Entities.Add(new Line { StartPoint = new XYZ(x1 - DimTickSize, y1, z), EndPoint = new XYZ(x1 + DimTickSize, y1, z), Layer = layer });
                doc.Entities.Add(new Line { StartPoint = new XYZ(x2 - DimTickSize, y2, z), EndPoint = new XYZ(x2 + DimTickSize, y2, z), Layer = layer });
            }

            double mx = (x1 + x2) * 0.5, my = (y1 + y2) * 0.5;
            string txt = value >= 1000 ? $"{value:N0}" : $"{value:N3}";
            double offsetX = isHoriz ? 0 : TextHeight;
            doc.Entities.Add(new MText
            {
                InsertPoint = new XYZ(mx + offsetX, my, z),
                Height = TextHeight * 0.8,
                Value = txt,
                Layer = layer,
                AttachmentPoint = AttachmentPointType.MiddleCenter
            });
        }
    }
}
