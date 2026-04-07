using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using PileDesign.Models.InputData;

namespace PileDesign.Output
{
    /// <summary>
    /// 杭伏図を2D DXFファイルにエクスポートする。
    /// XY平面の伏図: 杭位置(円+番号)、通り心線+シンボル、通り心間寸法、基礎梁線
    /// </summary>
    public class DxfPlanExporter
    {
        private readonly InputModel _inputModel;

        // 描画パラメータ
        private const double GridSymbolRadius = 0.4;   // 通り心シンボル円の半径(m)
        private const double GridExtension = 2.0;      // 通り心線の延長量(m)
        private const double DimOffset = 1.5;          // 寸法線の通り心シンボルからのオフセット(m)
        private const double DimTickSize = 0.15;       // 寸法端部のチック長さ(m)
        private const double TextHeight = 0.3;         // テキスト高さ(m)
        private const double GridSymbolTextHeight = 0.4; // 通り心シンボルのテキスト高さ(m)

        public DxfPlanExporter(InputModel inputModel)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
        }

        public void Export(string filePath)
        {
            var doc = new CadDocument();

            // レイヤー作成
            var pileLayer = CreateLayer(doc, "杭", new Color(0, 0, 255));
            var pileTextLayer = CreateLayer(doc, "杭番号", new Color(0, 0, 255));
            var gridLayer = CreateLayer(doc, "通り心", new Color(255, 0, 255));
            var gridSymbolLayer = CreateLayer(doc, "通り心シンボル", new Color(255, 0, 255));
            var dimLayer = CreateLayer(doc, "寸法", new Color(0, 128, 0));
            var beamLayer = CreateLayer(doc, "基礎梁", new Color(139, 90, 43));

            // 描画範囲の計算
            GetModelBounds(out double xMin, out double xMax, out double yMin, out double yMax);

            // 描画
            AddPiles(doc, pileLayer, pileTextLayer);
            AddGridLines(doc, gridLayer, gridSymbolLayer, xMin, xMax, yMin, yMax);
            AddGridDimensions(doc, dimLayer, xMin, xMax, yMin, yMax);
            AddBeams(doc, beamLayer);

            using var writer = new DxfWriter(filePath, doc, false);
            writer.Write();
        }

        private static Layer CreateLayer(CadDocument doc, string name, Color color)
        {
            var layer = new Layer(name) { Color = color };
            doc.Layers.Add(layer);
            return layer;
        }

        private void GetModelBounds(out double xMin, out double xMax, out double yMin, out double yMax)
        {
            xMin = double.MaxValue; xMax = double.MinValue;
            yMin = double.MaxValue; yMax = double.MinValue;

            if (_inputModel.PileLayoutItems != null)
            {
                foreach (var p in _inputModel.PileLayoutItems)
                {
                    xMin = Math.Min(xMin, p.X); xMax = Math.Max(xMax, p.X);
                    yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
                }
            }
            if (_inputModel.GridXItems != null)
            {
                foreach (var g in _inputModel.GridXItems)
                {
                    xMin = Math.Min(xMin, g.Coord); xMax = Math.Max(xMax, g.Coord);
                }
            }
            if (_inputModel.GridYItems != null)
            {
                foreach (var g in _inputModel.GridYItems)
                {
                    yMin = Math.Min(yMin, g.Coord); yMax = Math.Max(yMax, g.Coord);
                }
            }
            if (xMin > xMax) { xMin = 0; xMax = 10; }
            if (yMin > yMax) { yMin = 0; yMax = 10; }
        }

        // ─── 杭 ───────────────────────────────────────
        private void AddPiles(CadDocument doc, Layer pileLayer, Layer textLayer)
        {
            if (_inputModel.PileLayoutItems == null || _inputModel.PileBodies == null) return;

            foreach (var pile in _inputModel.PileLayoutItems)
            {
                int bodyIndex = pile.PileBodyNo - 1;
                if (bodyIndex < 0 || bodyIndex >= _inputModel.PileBodies.Count) continue;

                var body = _inputModel.PileBodies[bodyIndex];
                double dia = 1.0; // デフォルト
                if (body.PileBodySegments != null && body.PileBodySegments.Count > 0)
                    dia = body.PileBodySegments[0].PileSection.PileDiameter / 1000.0;

                // 杭の円
                doc.Entities.Add(new Circle
                {
                    Center = new XYZ(pile.X, pile.Y, 0),
                    Radius = dia * 0.5,
                    Layer = pileLayer
                });

                // 杭番号テキスト
                doc.Entities.Add(new MText
                {
                    InsertPoint = new XYZ(pile.X, pile.Y - dia * 0.5 - TextHeight * 0.5, 0),
                    Height = TextHeight,
                    Value = pile.No.ToString(),
                    Layer = textLayer,
                    AttachmentPoint = AttachmentPointType.TopCenter
                });
            }
        }

        // ─── 通り心線 + シンボル ─────────────────────────
        private void AddGridLines(CadDocument doc, Layer gridLayer, Layer symbolLayer,
            double xMin, double xMax, double yMin, double yMax)
        {
            // GridXItems: Y軸平行線（X座標固定）→ シンボルは下端(yMin)
            if (_inputModel.GridXItems != null)
            {
                foreach (var grid in _inputModel.GridXItems)
                {
                    double x = grid.Coord;
                    double lineYMin = yMin - GridExtension;
                    double lineYMax = yMax + GridExtension;

                    // 通り心線（一点鎖線風 → 破線）
                    var line = new Line
                    {
                        StartPoint = new XYZ(x, lineYMin, 0),
                        EndPoint = new XYZ(x, lineYMax, 0),
                        Layer = gridLayer
                    };
                    doc.Entities.Add(line);

                    // シンボル（下端）
                    double symY = lineYMin - GridSymbolRadius * 1.5;
                    AddGridSymbol(doc, symbolLayer, x, symY, grid.Name);
                }
            }

            // GridYItems: X軸平行線（Y座標固定）→ シンボルは右端(xMax)
            if (_inputModel.GridYItems != null)
            {
                foreach (var grid in _inputModel.GridYItems)
                {
                    double y = grid.Coord;
                    double lineXMin = xMin - GridExtension;
                    double lineXMax = xMax + GridExtension;

                    var line = new Line
                    {
                        StartPoint = new XYZ(lineXMin, y, 0),
                        EndPoint = new XYZ(lineXMax, y, 0),
                        Layer = gridLayer
                    };
                    doc.Entities.Add(line);

                    // シンボル（右端）
                    double symX = lineXMax + GridSymbolRadius * 1.5;
                    AddGridSymbol(doc, symbolLayer, symX, y, grid.Name);
                }
            }
        }

        private static void AddGridSymbol(CadDocument doc, Layer layer, double x, double y, string name)
        {
            // 円
            doc.Entities.Add(new Circle
            {
                Center = new XYZ(x, y, 0),
                Radius = GridSymbolRadius,
                Layer = layer
            });

            // テキスト
            doc.Entities.Add(new MText
            {
                InsertPoint = new XYZ(x, y, 0),
                Height = GridSymbolTextHeight,
                Value = name ?? "",
                Layer = layer,
                AttachmentPoint = AttachmentPointType.MiddleCenter
            });
        }

        // ─── 寸法線（通り心間） ──────────────────────────
        private void AddGridDimensions(CadDocument doc, Layer dimLayer,
            double xMin, double xMax, double yMin, double yMax)
        {
            // X方向通り心間の寸法（下側に配置）
            if (_inputModel.GridXItems != null && _inputModel.GridXItems.Count >= 2)
            {
                var sortedX = _inputModel.GridXItems.OrderBy(g => g.Coord).ToList();
                double dimY = yMin - GridExtension - GridSymbolRadius * 3 - DimOffset;

                for (int i = 0; i < sortedX.Count - 1; i++)
                {
                    double x1 = sortedX[i].Coord;
                    double x2 = sortedX[i + 1].Coord;
                    double dist = Math.Abs(x2 - x1);

                    AddLinearDimension(doc, dimLayer, x1, dimY, x2, dimY, dist, true);
                }
            }

            // Y方向通り心間の寸法（右側に配置）
            if (_inputModel.GridYItems != null && _inputModel.GridYItems.Count >= 2)
            {
                var sortedY = _inputModel.GridYItems.OrderBy(g => g.Coord).ToList();
                double dimX = xMax + GridExtension + GridSymbolRadius * 3 + DimOffset;

                for (int i = 0; i < sortedY.Count - 1; i++)
                {
                    double y1 = sortedY[i].Coord;
                    double y2 = sortedY[i + 1].Coord;
                    double dist = Math.Abs(y2 - y1);

                    AddLinearDimension(doc, dimLayer, dimX, y1, dimX, y2, dist, false);
                }
            }
        }

        /// <summary>寸法線（線+チック+テキスト）を手動描画</summary>
        private static void AddLinearDimension(CadDocument doc, Layer layer,
            double x1, double y1, double x2, double y2, double value, bool isHorizontal)
        {
            // 寸法線
            doc.Entities.Add(new Line
            {
                StartPoint = new XYZ(x1, y1, 0),
                EndPoint = new XYZ(x2, y2, 0),
                Layer = layer
            });

            // チック（端部の短い線）
            if (isHorizontal)
            {
                // 垂直チック
                doc.Entities.Add(new Line
                {
                    StartPoint = new XYZ(x1, y1 - DimTickSize, 0),
                    EndPoint = new XYZ(x1, y1 + DimTickSize, 0),
                    Layer = layer
                });
                doc.Entities.Add(new Line
                {
                    StartPoint = new XYZ(x2, y2 - DimTickSize, 0),
                    EndPoint = new XYZ(x2, y2 + DimTickSize, 0),
                    Layer = layer
                });
            }
            else
            {
                // 水平チック
                doc.Entities.Add(new Line
                {
                    StartPoint = new XYZ(x1 - DimTickSize, y1, 0),
                    EndPoint = new XYZ(x1 + DimTickSize, y1, 0),
                    Layer = layer
                });
                doc.Entities.Add(new Line
                {
                    StartPoint = new XYZ(x2 - DimTickSize, y2, 0),
                    EndPoint = new XYZ(x2 + DimTickSize, y2, 0),
                    Layer = layer
                });
            }

            // 寸法値テキスト
            double midX = (x1 + x2) * 0.5;
            double midY = (y1 + y2) * 0.5;
            string dimText = value >= 1000 ? $"{value:N0}" : $"{value:N3}";

            var dimMText = new MText
            {
                InsertPoint = new XYZ(midX, midY, 0),
                Height = TextHeight * 0.8,
                Value = dimText,
                Layer = layer,
                AttachmentPoint = AttachmentPointType.MiddleCenter
            };
            if (!isHorizontal)
            {
                // Y方向寸法: テキストをオフセットして配置（回転の代わり）
                dimMText.InsertPoint = new XYZ(midX + TextHeight, midY, 0);
            }
            doc.Entities.Add(dimMText);
        }

        // ─── 基礎梁（2D平面投影線） ─────────────────────
        private void AddBeams(CadDocument doc, Layer beamLayer)
        {
            var fbInput = _inputModel.FoundationBeamInput;
            if (fbInput?.Beams == null) return;

            foreach (var beam in fbInput.Beams)
            {
                (double X, double Y, double Z)? coordsI = null;
                (double X, double Y, double Z)? coordsJ = null;

                if (beam.NodeI_Id != Guid.Empty)
                    coordsI = _inputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                if (beam.NodeJ_Id != Guid.Empty)
                    coordsJ = _inputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);

                if (!coordsI.HasValue || !coordsJ.HasValue) continue;

                doc.Entities.Add(new Line
                {
                    StartPoint = new XYZ(coordsI.Value.X, coordsI.Value.Y, 0),
                    EndPoint = new XYZ(coordsJ.Value.X, coordsJ.Value.Y, 0),
                    Layer = beamLayer
                });
            }
        }
    }
}
