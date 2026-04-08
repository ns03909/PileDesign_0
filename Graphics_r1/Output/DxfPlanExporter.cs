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

        private const double TextHeight = 0.3;

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

            // 描画
            AddPiles(doc, pileLayer, pileTextLayer);
            GridDimHelper.AddGridAndDimensions(doc, _inputModel, gridLayer, gridSymbolLayer, dimLayer, 0);
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
                    dia = (body.PileBodySegments[0].PileSection?.PileDiameter ?? 1000) / 1000.0;

                // 杭の円
                doc.Entities.Add(new Circle
                {
                    Center = new XYZ(pile.X, pile.Y, 0),
                    Radius = dia * 0.5,
                    Layer = pileLayer
                });

                // 拡底部・拡大根固め部の外径円（破線）
                double toeDia = body.PileToeDia / 1000.0;
                if (toeDia > dia)
                {
                    doc.Entities.Add(new Circle
                    {
                        Center = new XYZ(pile.X, pile.Y, 0),
                        Radius = toeDia * 0.5,
                        Layer = pileLayer
                    });
                }

                // 杭番号テキスト（拡底がある場合は外径の外に配置）
                double labelOffset = Math.Max(dia, toeDia) * 0.5 + TextHeight * 0.5;
                doc.Entities.Add(new MText
                {
                    InsertPoint = new XYZ(pile.X, pile.Y - labelOffset, 0),
                    Height = TextHeight,
                    Value = pile.No.ToString(),
                    Layer = textLayer,
                    AttachmentPoint = AttachmentPointType.TopCenter
                });
            }
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
