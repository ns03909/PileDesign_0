using System;
using System.Collections.Generic;
using System.Linq;
using PileDesign.Models.InputData;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

namespace PileDesign.Output
{
    /// <summary>
    /// 杭・根入れ部・基礎梁の形状を Rhino 3D (.3dm) ファイルにエクスポートする。
    /// ジオメトリ構築は MainWindow.VIewPort3D.cs のロジックに準拠。
    /// </summary>
    public class Rhino3dmExporter
    {
        private readonly InputModel _inputModel;

        public Rhino3dmExporter(InputModel inputModel)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
        }

        public void Export(string filePath)
        {
            var file = new File3dm();

            // レイヤー作成
            int pileLayerIdx = AddLayer(file, "杭", System.Drawing.Color.SteelBlue);
            int embedLayerIdx = AddLayer(file, "根入れ部", System.Drawing.Color.LightGray);
            int beamLayerIdx = AddLayer(file, "基礎梁", System.Drawing.Color.SaddleBrown);

            // ジオメトリ追加
            AddPiles(file, pileLayerIdx);
            AddEmbedment(file, embedLayerIdx);
            AddBeams(file, beamLayerIdx);

            file.Write(filePath, 8);
        }

        private static int AddLayer(File3dm file, string name, System.Drawing.Color color)
        {
            var layer = new Layer { Name = name, Color = color };
            file.AllLayers.Add(layer);
            return file.AllLayers.Count - 1;
        }

        private static ObjectAttributes MakeAttrs(int layerIndex)
        {
            return new ObjectAttributes { LayerIndex = layerIndex };
        }

        // ─── 杭 ───────────────────────────────────────
        // ViewPort3D.cs:144-183 に準拠
        private void AddPiles(File3dm file, int layerIdx)
        {
            if (_inputModel.PileLayoutItems == null || _inputModel.PileBodies == null) return;

            foreach (var pile in _inputModel.PileLayoutItems)
            {
                double x = pile.Point3D.X;
                double y = pile.Point3D.Y;
                double z = pile.Point3D.Z;

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
                    double dia = segments[i].PileSection.PileDiameter / 1000.0; // mm→m
                    lastDia = dia;

                    AddCylinder(file, layerIdx, x, y, z1, z2, dia);
                }

                // 拡底部 (pileToeDia > pileDia)
                double toeDia = body.PileToeDia / 1000.0;
                if (toeDia > lastDia && lastDia > 0)
                {
                    double pileBottomZ = z - segments[^1].SegmentDepth;
                    AddPileToe(file, layerIdx, x, y, pileBottomZ, toeDia, lastDia);
                }
            }
        }

        private static void AddCylinder(File3dm file, int layerIdx, double x, double y, double z1, double z2, double diameter)
        {
            double radius = diameter * 0.5;
            double height = z1 - z2; // z1(top) > z2(bottom), height is positive
            if (Math.Abs(height) < 1e-9) return;

            // 底面の円（下端）を基準にして上向きに伸ばす
            var basePlane = new Plane(new Point3d(x, y, z2), Vector3d.ZAxis);
            var circle = new Circle(basePlane, radius);
            var cylinder = new Cylinder(circle, height);
            var brep = cylinder.ToBrep(true, true);
            if (brep != null)
                file.Objects.AddBrep(brep, MakeAttrs(layerIdx));
        }

        // 拡底部: 円柱(0.3m) + 円錐台 — ViewPort3D.cs:274-296 に準拠
        private static void AddPileToe(File3dm file, int layerIdx, double x, double y, double pileBottomZ, double baseDia, double topDia)
        {
            double cylHeight = 0.3;

            // 拡底円柱部（杭底から下へ）
            double cylBottomZ = pileBottomZ - cylHeight;
            AddCylinder(file, layerIdx, x, y, pileBottomZ, cylBottomZ, baseDia);

            // 円錐台（12°傾斜）
            double coneHeight = (baseDia - topDia) * 0.5 / Math.Tan(12.0 * Math.PI / 180.0);
            if (coneHeight < 1e-9) return;

            double coneTopZ = cylBottomZ;
            double coneBotZ = coneTopZ - coneHeight;

            // Cone: 底面(baseDia) → 頂面(topDia) の円錐台をメッシュで構築
            var mesh = CreateConeFrustumMesh(x, y, coneTopZ, coneBotZ, baseDia * 0.5, topDia * 0.5, 24);
            file.Objects.AddMesh(mesh, MakeAttrs(layerIdx));
        }

        private static Mesh CreateConeFrustumMesh(double cx, double cy, double zTop, double zBot, double rTop, double rBot, int segments)
        {
            var mesh = new Mesh();

            // 上面と下面の頂点
            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                mesh.Vertices.Add(cx + rTop * cos, cy + rTop * sin, zTop); // index: i
                mesh.Vertices.Add(cx + rBot * cos, cy + rBot * sin, zBot); // index: segments + i
            }

            // 上面中心と下面中心
            int topCenter = mesh.Vertices.Count;
            mesh.Vertices.Add(cx, cy, zTop);
            int botCenter = mesh.Vertices.Count;
            mesh.Vertices.Add(cx, cy, zBot);

            // 側面（四角形）
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int t0 = i, t1 = next;
                int b0 = segments + i, b1 = segments + next;
                mesh.Faces.AddFace(t0, t1, b1, b0);
            }

            // 上面キャップ
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                mesh.Faces.AddFace(topCenter, next, i);
            }

            // 下面キャップ
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                mesh.Faces.AddFace(botCenter, segments + i, segments + next);
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        // ─── 根入れ部 ─────────────────────────────────
        // ViewPort3D.cs:110-128 に準拠
        private void AddEmbedment(File3dm file, int layerIdx)
        {
            if (_inputModel.EmbedmentInput?.EmbedmentLayers == null) return;

            foreach (var layer in _inputModel.EmbedmentInput.EmbedmentLayers)
            {
                double x1 = layer.X1, x2 = layer.X2;
                double y1 = layer.Y1, y2 = layer.Y2;
                double z1 = layer.BottomAltitude, z2 = layer.TopAltitude;

                if (Math.Abs(x2 - x1) < 1e-9 || Math.Abs(y2 - y1) < 1e-9 || Math.Abs(z2 - z1) < 1e-9)
                    continue;

                var bbox = new BoundingBox(
                    new Point3d(Math.Min(x1, x2), Math.Min(y1, y2), Math.Min(z1, z2)),
                    new Point3d(Math.Max(x1, x2), Math.Max(y1, y2), Math.Max(z1, z2)));
                var brep = Brep.CreateFromBox(bbox);
                if (brep != null)
                    file.Objects.AddBrep(brep, MakeAttrs(layerIdx));
            }
        }

        // ─── 基礎梁 ──────────────────────────────────
        // ViewPort3D.cs:187-430 に準拠
        private void AddBeams(File3dm file, int layerIdx)
        {
            var fbInput = _inputModel.FoundationBeamInput;
            if (fbInput?.Beams == null) return;

            var sectionDict = fbInput.Sections?.ToDictionary(s => s.No, s => s)
                              ?? new Dictionary<int, BeamSection>();

            foreach (var beam in fbInput.Beams)
            {
                // 節点座標解決
                (double X, double Y, double Z)? coordsI = null;
                (double X, double Y, double Z)? coordsJ = null;

                if (beam.NodeI_Id != Guid.Empty)
                    coordsI = _inputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                else if (beam.NodeI_No > 0)
                {
                    var nodeI = fbInput.Nodes?.FirstOrDefault(n => n.No == beam.NodeI_No);
                    if (nodeI != null) coordsI = (nodeI.X, nodeI.Y, nodeI.Z);
                }

                if (beam.NodeJ_Id != Guid.Empty)
                    coordsJ = _inputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                else if (beam.NodeJ_No > 0)
                {
                    var nodeJ = fbInput.Nodes?.FirstOrDefault(n => n.No == beam.NodeJ_No);
                    if (nodeJ != null) coordsJ = (nodeJ.X, nodeJ.Y, nodeJ.Z);
                }

                if (!coordsI.HasValue || !coordsJ.HasValue) continue;

                // 断面寸法
                double width = beam.Width;
                double height = beam.Height;
                if (sectionDict.TryGetValue(beam.SectionNo, out var section))
                {
                    width = section.Width;
                    height = section.Height;
                }

                if (width < 1e-9 || height < 1e-9) continue;

                var p1 = new Point3d(coordsI.Value.X, coordsI.Value.Y, coordsI.Value.Z);
                var p2 = new Point3d(coordsJ.Value.X, coordsJ.Value.Y, coordsJ.Value.Z);

                AddBeamElement(file, layerIdx, p1, p2, width, height, beam.AngleBeta);
            }
        }

        // 2点間に幅×高さの矩形断面メッシュを構築 — ViewPort3D.cs:342-430 に準拠
        private static void AddBeamElement(File3dm file, int layerIdx, Point3d p1, Point3d p2, double width, double height, double angleBetaDeg)
        {
            var dir = p2 - p1;
            double length = dir.Length;
            if (length < 1e-9) return;

            var localX = dir / length; // 正規化

            // 局所Z軸（グローバルZ軸から梁軸方向成分を除去）
            var up = Vector3d.ZAxis;
            Vector3d localZ;
            if (Math.Abs(localX * up) > 0.999) // ほぼ鉛直
                localZ = Vector3d.YAxis;
            else
            {
                localZ = up - (up * localX) * localX;
                localZ.Unitize();
            }

            // 局所Y軸（右手系）
            var localY = Vector3d.CrossProduct(localZ, localX);
            localY.Unitize();

            // AngleBeta 回転
            if (Math.Abs(angleBetaDeg) > 1e-9)
            {
                double rad = angleBetaDeg * Math.PI / 180.0;
                double cosB = Math.Cos(rad);
                double sinB = Math.Sin(rad);
                var newLocalY = cosB * localY + sinB * localZ;
                var newLocalZ = -sinB * localY + cosB * localZ;
                localY = newLocalY;
                localZ = newLocalZ;
            }

            // 中心点
            var center = new Point3d(
                (p1.X + p2.X) * 0.5,
                (p1.Y + p2.Y) * 0.5,
                (p1.Z + p2.Z) * 0.5);

            // 8頂点の直方体メッシュを生成
            double hw = width * 0.5;
            double hh = height * 0.5;
            double hl = length * 0.5;

            // 局所座標 → グローバル座標に変換する関数
            Point3d ToGlobal(double lx, double ly, double lz) =>
                center + lx * localX + ly * localY + lz * localZ;

            // 8頂点: (±hl, ±hw, ±hh)
            var v = new Point3d[8];
            v[0] = ToGlobal(-hl, -hw, -hh); // 0: 始端-左下
            v[1] = ToGlobal(-hl, +hw, -hh); // 1: 始端-右下
            v[2] = ToGlobal(-hl, +hw, +hh); // 2: 始端-右上
            v[3] = ToGlobal(-hl, -hw, +hh); // 3: 始端-左上
            v[4] = ToGlobal(+hl, -hw, -hh); // 4: 終端-左下
            v[5] = ToGlobal(+hl, +hw, -hh); // 5: 終端-右下
            v[6] = ToGlobal(+hl, +hw, +hh); // 6: 終端-右上
            v[7] = ToGlobal(+hl, -hw, +hh); // 7: 終端-左上

            var mesh = new Mesh();
            foreach (var pt in v)
                mesh.Vertices.Add(pt);

            // 6面（法線が外向きになるよう頂点順序を設定）
            mesh.Faces.AddFace(0, 1, 2, 3); // 始端面 (-X)
            mesh.Faces.AddFace(4, 7, 6, 5); // 終端面 (+X)
            mesh.Faces.AddFace(0, 4, 5, 1); // 底面 (-Z)
            mesh.Faces.AddFace(3, 2, 6, 7); // 上面 (+Z)
            mesh.Faces.AddFace(0, 3, 7, 4); // 左面 (-Y)
            mesh.Faces.AddFace(1, 5, 6, 2); // 右面 (+Y)

            mesh.Normals.ComputeNormals();
            mesh.Compact();

            file.Objects.AddMesh(mesh, MakeAttrs(layerIdx));
        }
    }
}
