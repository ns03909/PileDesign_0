using PileDesign.Common;
using PileDesign.Constants;
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
    /// 杭・根入れ部・基礎梁の形状を DXF ファイルにエクスポートする。
    /// ジオメトリ構築は Rhino3dmExporter / MainWindow.VIewPort3D.cs のロジックに準拠。
    /// 3Dソリッド（ACIS）は作成不可のため Face3D（四角形ファセット）で近似する。
    /// </summary>
    public class DxfExporter
    {
        private readonly InputModel _inputModel;
        private const int CylinderSegments = 24;

        public DxfExporter(InputModel inputModel)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
        }

        public void Export(string filePath)
        {
            var doc = new CadDocument();

            // レイヤー作成
            var pileLayer = CreateLayer(doc, "杭", new Color(150, 180, 220));
            var embedLayer = CreateLayer(doc, "根入れ部", new Color(192, 192, 192));
            var beamLayer = CreateLayer(doc, "基礎梁", new Color(139, 90, 43));
            var gridLayer = CreateLayer(doc, "通り心", new Color(255, 0, 255));
            var gridSymbolLayer = CreateLayer(doc, "通り心シンボル", new Color(255, 0, 255));
            var dimLayer = CreateLayer(doc, "寸法", new Color(0, 128, 0));

            // ジオメトリ追加
            AddPiles(doc, pileLayer);
            AddEmbedment(doc, embedLayer);
            AddBeams(doc, beamLayer);

            // 通り心・寸法を杭頭最高レベルのXY平面に追加
            double topZ = GetMaxPileTopZ();
            GridDimHelper.AddGridAndDimensions(doc, _inputModel, gridLayer, gridSymbolLayer, dimLayer, topZ);

            using var writer = new DxfWriter(filePath, doc, false);
            writer.Write();
        }

        private double GetMaxPileTopZ()
        {
            // v2 セマンティクス: pile.Z は接合節点 Z なので、杭頭最大値は PileHeadZ を使う
            if (_inputModel.PileLayoutItems == null || _inputModel.PileLayoutItems.Count == 0) return 0;
            return _inputModel.PileLayoutItems.Max(p => p.PileHeadZ);
        }

        private static Layer CreateLayer(CadDocument doc, string name, Color color)
        {
            var layer = new Layer(name) { Color = color };
            doc.Layers.Add(layer);
            return layer;
        }

        // ─── 杭 ───────────────────────────────────────
        private void AddPiles(CadDocument doc, Layer layer)
        {
            if (_inputModel.PileLayoutItems == null || _inputModel.PileBodies == null) return;

            foreach (var pile in _inputModel.PileLayoutItems)
            {
                // v2 セマンティクス: 杭体描画の起点は杭頭 Z (= pile.Z - ΔZc)
                double x = pile.Point3D.X;
                double y = pile.Point3D.Y;
                double z = pile.PileHeadZ;

                int bodyIndex = pile.PileBodyNo - 1;
                if (bodyIndex < 0 || bodyIndex >= _inputModel.PileBodies.Count) continue;

                var body = _inputModel.PileBodies[bodyIndex];
                var segments = body.PileBodySegments;
                if (segments == null || segments.Count == 0) continue;

                double lastDia = 0;
                for (int i = 0; i < segments.Count; i++)
                {
                    double z1 = (i == 0) ? z : z - segments[i - 1].SegmentDepth;
                    double z2 = z - segments[i].SegmentDepth;
                    double dia = (segments[i].PileSection?.PileDiameter ?? 0) / 1000.0;
                    lastDia = dia;

                    AddCylinderFaces(doc, layer, x, y, z1, z2, dia * 0.5, CylinderSegments);
                }

                double pileBottomZ = z - segments[^1].SegmentDepth;
                double toeDia = body.PileToeDia / 1000.0;

                // 場所打ち杭の拡底部（円柱+円錐台）。立上り・側面角度は入力値による
                if (toeDia > lastDia && lastDia > 0 &&
                    (body.PileBodyType == PileTypeNames.InsituRc || body.PileBodyType == PileTypeNames.InsituSteelPipeConcrete))
                {
                    AddPileToeFaces(doc, layer, x, y, pileBottomZ, toeDia, lastDia,
                        body.InsituPileToeHeight / 1000.0, body.InsituPileToeAngle);
                }

                // 杭先端の拡大根固め部（円筒）。上端・下端は工法で決まる。
                // 杭姿図・3D 表示と同じく、球根は杭先端を下端として上方へ伸ばす
                // （杭体の最下部が球根に埋まる表現）。Smart-MAGNUM だけは杭下拡大根固め部が
                // 杭先端より下に LL だけ出る。
                if (PileToeShape.HasBulb(body, lastDia))
                {
                    var (bulbTopZ, bulbBottomZ) = PileToeShape.BulbRange(body, pileBottomZ);
                    AddCylinderFaces(doc, layer, x, y, bulbTopZ, bulbBottomZ, toeDia * 0.5, CylinderSegments);
                }
            }
        }

        /// <summary>Face3D で円筒側面 + 上下キャップを描画</summary>
        private static void AddCylinderFaces(CadDocument doc, Layer layer,
            double cx, double cy, double zTop, double zBot, double radius, int segments)
        {
            if (Math.Abs(zTop - zBot) < 1e-9) return;

            var topPts = new XYZ[segments];
            var botPts = new XYZ[segments];

            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;
                double dx = radius * Math.Cos(angle);
                double dy = radius * Math.Sin(angle);
                topPts[i] = new XYZ(cx + dx, cy + dy, zTop);
                botPts[i] = new XYZ(cx + dx, cy + dy, zBot);
            }

            // 側面（四角形）
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                doc.Entities.Add(new Face3D
                {
                    FirstCorner = topPts[i],
                    SecondCorner = topPts[next],
                    ThirdCorner = botPts[next],
                    FourthCorner = botPts[i],
                    Layer = layer
                });
            }

            // 上キャップ（三角形ファン）
            var topCenter = new XYZ(cx, cy, zTop);
            var botCenter = new XYZ(cx, cy, zBot);
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                doc.Entities.Add(new Face3D
                {
                    FirstCorner = topCenter,
                    SecondCorner = topPts[i],
                    ThirdCorner = topPts[next],
                    FourthCorner = topPts[next], // 三角形: 3rd=4th
                    Layer = layer
                });
                doc.Entities.Add(new Face3D
                {
                    FirstCorner = botCenter,
                    SecondCorner = botPts[next],
                    ThirdCorner = botPts[i],
                    FourthCorner = botPts[i],
                    Layer = layer
                });
            }
        }

        /// <summary>拡底部: 円柱(立上り) + 円錐台(側面角度)</summary>
        private static void AddPileToeFaces(CadDocument doc, Layer layer,
            double x, double y, double pileBottomZ, double baseDia, double topDia,
            double cylHeight, double toeAngleDeg)
        {
            if (!(cylHeight > 0)) cylHeight = 0.3;
            double cylBottomZ = pileBottomZ - cylHeight;
            AddCylinderFaces(doc, layer, x, y, pileBottomZ, cylBottomZ, baseDia * 0.5, CylinderSegments);

            // 円錐台
            double toeAngle = toeAngleDeg > 0 ? toeAngleDeg : 12.0;
            double coneHeight = (baseDia - topDia) * 0.5 / Math.Tan(toeAngle * Math.PI / 180.0);
            if (coneHeight < 1e-9) return;

            double coneTopZ = cylBottomZ;
            double coneBotZ = coneTopZ - coneHeight;
            double rTop = baseDia * 0.5;
            double rBot = topDia * 0.5;

            var topPts = new XYZ[CylinderSegments];
            var botPts = new XYZ[CylinderSegments];

            for (int i = 0; i < CylinderSegments; i++)
            {
                double angle = 2.0 * Math.PI * i / CylinderSegments;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                topPts[i] = new XYZ(x + rTop * cos, y + rTop * sin, coneTopZ);
                botPts[i] = new XYZ(x + rBot * cos, y + rBot * sin, coneBotZ);
            }

            // 側面
            for (int i = 0; i < CylinderSegments; i++)
            {
                int next = (i + 1) % CylinderSegments;
                doc.Entities.Add(new Face3D
                {
                    FirstCorner = topPts[i],
                    SecondCorner = topPts[next],
                    ThirdCorner = botPts[next],
                    FourthCorner = botPts[i],
                    Layer = layer
                });
            }

            // キャップ
            var topCenter = new XYZ(x, y, coneTopZ);
            var botCenter = new XYZ(x, y, coneBotZ);
            for (int i = 0; i < CylinderSegments; i++)
            {
                int next = (i + 1) % CylinderSegments;
                doc.Entities.Add(new Face3D
                {
                    FirstCorner = topCenter,
                    SecondCorner = topPts[i],
                    ThirdCorner = topPts[next],
                    FourthCorner = topPts[next],
                    Layer = layer
                });
                doc.Entities.Add(new Face3D
                {
                    FirstCorner = botCenter,
                    SecondCorner = botPts[next],
                    ThirdCorner = botPts[i],
                    FourthCorner = botPts[i],
                    Layer = layer
                });
            }
        }

        // ─── 根入れ部 ─────────────────────────────────
        private void AddEmbedment(CadDocument doc, Layer layer)
        {
            if (_inputModel.EmbedmentInput?.EmbedmentLayers == null) return;

            foreach (var emb in _inputModel.EmbedmentInput.EmbedmentLayers)
            {
                double x1 = Math.Min(emb.X1, emb.X2), x2 = Math.Max(emb.X1, emb.X2);
                double y1 = Math.Min(emb.Y1, emb.Y2), y2 = Math.Max(emb.Y1, emb.Y2);
                double z1 = Math.Min(emb.BottomAltitude, emb.TopAltitude);
                double z2 = Math.Max(emb.BottomAltitude, emb.TopAltitude);

                if (Math.Abs(x2 - x1) < 1e-9 || Math.Abs(y2 - y1) < 1e-9 || Math.Abs(z2 - z1) < 1e-9)
                    continue;

                AddBoxFaces(doc, layer, x1, y1, z1, x2, y2, z2);
            }
        }

        /// <summary>6面の Face3D で直方体を描画</summary>
        private static void AddBoxFaces(CadDocument doc, Layer layer,
            double x1, double y1, double z1, double x2, double y2, double z2)
        {
            // 8頂点
            var v = new XYZ[8];
            v[0] = new XYZ(x1, y1, z1); // 0: 左前下
            v[1] = new XYZ(x2, y1, z1); // 1: 右前下
            v[2] = new XYZ(x2, y2, z1); // 2: 右奥下
            v[3] = new XYZ(x1, y2, z1); // 3: 左奥下
            v[4] = new XYZ(x1, y1, z2); // 4: 左前上
            v[5] = new XYZ(x2, y1, z2); // 5: 右前上
            v[6] = new XYZ(x2, y2, z2); // 6: 右奥上
            v[7] = new XYZ(x1, y2, z2); // 7: 左奥上

            // 6面
            AddFace(doc, layer, v[0], v[1], v[2], v[3]); // 底面
            AddFace(doc, layer, v[4], v[7], v[6], v[5]); // 上面
            AddFace(doc, layer, v[0], v[4], v[5], v[1]); // 前面
            AddFace(doc, layer, v[2], v[6], v[7], v[3]); // 奥面
            AddFace(doc, layer, v[0], v[3], v[7], v[4]); // 左面
            AddFace(doc, layer, v[1], v[5], v[6], v[2]); // 右面
        }

        private static void AddFace(CadDocument doc, Layer layer, XYZ a, XYZ b, XYZ c, XYZ d)
        {
            doc.Entities.Add(new Face3D
            {
                FirstCorner = a,
                SecondCorner = b,
                ThirdCorner = c,
                FourthCorner = d,
                Layer = layer
            });
        }

        // ─── 基礎梁 ──────────────────────────────────
        private void AddBeams(CadDocument doc, Layer layer)
        {
            var fbInput = _inputModel.FoundationBeamInput;
            if (fbInput?.Beams == null) return;

            // SectionNo (1-based 位置インデックス) → BeamSection マップ
            var sectionDict = new Dictionary<int, BeamSection>();
            if (fbInput.Sections != null)
                for (int i = 0; i < fbInput.Sections.Count; i++)
                    sectionDict[i + 1] = fbInput.Sections[i];

            foreach (var beam in fbInput.Beams)
            {
                (double X, double Y, double Z)? coordsI = null;
                (double X, double Y, double Z)? coordsJ = null;

                if (beam.NodeI_Id != Guid.Empty)
                    coordsI = _inputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);

                if (beam.NodeJ_Id != Guid.Empty)
                    coordsJ = _inputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);

                if (!coordsI.HasValue || !coordsJ.HasValue) continue;

                double width = beam.Width;
                double height = beam.Height;
                if (sectionDict.TryGetValue(beam.SectionNo, out var section))
                {
                    width = section.Width;
                    height = section.Height;
                }

                if (width < 1e-9 || height < 1e-9) continue;

                var p1 = new XYZ(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                var p2 = new XYZ(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);

                AddBeamElement(doc, layer, p1, p2, width, height, beam.AngleBeta);
            }
        }

        /// <summary>2点間に幅×高さの矩形断面直方体を Face3D で構築</summary>
        private static void AddBeamElement(CadDocument doc, Layer layer,
            XYZ p1, XYZ p2, double width, double height, double angleBetaDeg)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y, dz = p2.Z - p1.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-9) return;

            // 局所座標系（ViewPort3D.cs:362-400 に準拠）
            double invLen = 1.0 / length;
            var localX = new XYZ(dx * invLen, dy * invLen, dz * invLen);

            var up = new XYZ(0, 0, 1);
            XYZ localZ;
            double dot = localX.X * up.X + localX.Y * up.Y + localX.Z * up.Z;
            if (Math.Abs(dot) > 0.999)
                localZ = new XYZ(0, 1, 0);
            else
            {
                localZ = new XYZ(up.X - dot * localX.X, up.Y - dot * localX.Y, up.Z - dot * localX.Z);
                double lzLen = Math.Sqrt(localZ.X * localZ.X + localZ.Y * localZ.Y + localZ.Z * localZ.Z);
                localZ = new XYZ(localZ.X / lzLen, localZ.Y / lzLen, localZ.Z / lzLen);
            }

            // localY = localZ × localX
            var localY = new XYZ(
                localZ.Y * localX.Z - localZ.Z * localX.Y,
                localZ.Z * localX.X - localZ.X * localX.Z,
                localZ.X * localX.Y - localZ.Y * localX.X);
            double lyLen = Math.Sqrt(localY.X * localY.X + localY.Y * localY.Y + localY.Z * localY.Z);
            localY = new XYZ(localY.X / lyLen, localY.Y / lyLen, localY.Z / lyLen);

            // AngleBeta 回転
            if (Math.Abs(angleBetaDeg) > 1e-9)
            {
                double rad = angleBetaDeg * Math.PI / 180.0;
                double cosB = Math.Cos(rad);
                double sinB = Math.Sin(rad);
                var newY = new XYZ(
                    cosB * localY.X + sinB * localZ.X,
                    cosB * localY.Y + sinB * localZ.Y,
                    cosB * localY.Z + sinB * localZ.Z);
                var newZ = new XYZ(
                    -sinB * localY.X + cosB * localZ.X,
                    -sinB * localY.Y + cosB * localZ.Y,
                    -sinB * localY.Z + cosB * localZ.Z);
                localY = newY;
                localZ = newZ;
            }

            var center = new XYZ((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5, (p1.Z + p2.Z) * 0.5);
            double hw = width * 0.5;
            double hh = height * 0.5;
            double hl = length * 0.5;

            XYZ ToGlobal(double lx, double ly, double lz) => new(
                center.X + lx * localX.X + ly * localY.X + lz * localZ.X,
                center.Y + lx * localX.Y + ly * localY.Y + lz * localZ.Y,
                center.Z + lx * localX.Z + ly * localY.Z + lz * localZ.Z);

            var v = new XYZ[8];
            v[0] = ToGlobal(-hl, -hw, -hh);
            v[1] = ToGlobal(-hl, +hw, -hh);
            v[2] = ToGlobal(-hl, +hw, +hh);
            v[3] = ToGlobal(-hl, -hw, +hh);
            v[4] = ToGlobal(+hl, -hw, -hh);
            v[5] = ToGlobal(+hl, +hw, -hh);
            v[6] = ToGlobal(+hl, +hw, +hh);
            v[7] = ToGlobal(+hl, -hw, +hh);

            AddFace(doc, layer, v[0], v[1], v[2], v[3]); // 始端面
            AddFace(doc, layer, v[4], v[7], v[6], v[5]); // 終端面
            AddFace(doc, layer, v[0], v[4], v[5], v[1]); // 底面
            AddFace(doc, layer, v[3], v[2], v[6], v[7]); // 上面
            AddFace(doc, layer, v[0], v[3], v[7], v[4]); // 左面
            AddFace(doc, layer, v[1], v[5], v[6], v[2]); // 右面
        }
    }
}
