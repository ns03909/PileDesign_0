using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using Material = PileDesign.FEM.Material;

namespace PileDesign.Output
{
    /// <summary>
    /// FEM解析モデルを midas Gen MGT (テキスト) 形式でエクスポートする。
    /// </summary>
    public class MgtExporter
    {
        private readonly AnaModel _anaModel;

        public MgtExporter(AnaModel anaModel)
        {
            _anaModel = anaModel ?? throw new ArgumentNullException(nameof(anaModel));
        }

        public void Export(string filePath)
        {
            // ID マッピング構築
            var nodeIdMap = new Dictionary<Node, int>();
            int nodeId = 1;
            foreach (var node in _anaModel.Nodes)
                nodeIdMap[node] = nodeId++;

            // Material / Section の重複排除（参照同一性）
            var materialIdMap = new Dictionary<Material, int>(ReferenceEqualityComparer.Instance);
            var sectionIdMap = new Dictionary<Section, int>(ReferenceEqualityComparer.Instance);
            int matId = 1;
            int secId = 1;
            foreach (var beam in _anaModel.Beams)
            {
                if (beam.Section?.Material != null && !materialIdMap.ContainsKey(beam.Section.Material))
                    materialIdMap[beam.Section.Material] = matId++;
                if (beam.Section != null && !sectionIdMap.ContainsKey(beam.Section))
                    sectionIdMap[beam.Section] = secId++;
            }

            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);

            WriteHeader(writer);
            WriteUnit(writer);
            WriteNodes(writer, nodeIdMap);
            WriteMaterials(writer, materialIdMap);
            WriteSections(writer, sectionIdMap, materialIdMap);
            WriteElements(writer, nodeIdMap, materialIdMap, sectionIdMap);
            WriteElasticLinks(writer, nodeIdMap);
            WriteConstraints(writer, nodeIdMap);
            WriteSprings(writer, nodeIdMap);
            WriteRigidLinks(writer, nodeIdMap);

            // 非線形曲線データ（コメント形式）
            WriteNonlinearSoilCurves(writer, nodeIdMap);
            WriteNonlinearRotationalCurves(writer, nodeIdMap);
            WriteBeamMPhiCurves(writer, nodeIdMap);

            writer.WriteLine("*ENDDATA");
        }

        private static void WriteHeader(StreamWriter writer)
        {
            writer.WriteLine("; MGT file exported by PileDesign");
            writer.WriteLine($"; Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine();
        }

        private static void WriteUnit(StreamWriter writer)
        {
            writer.WriteLine("*UNIT");
            writer.WriteLine("   kN, m, degC");
            writer.WriteLine();
        }

        private static void WriteNodes(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            writer.WriteLine("*NODE");
            foreach (var (node, id) in nodeIdMap)
            {
                writer.WriteLine($"   {id}, {node.Coord.X:F6}, {node.Coord.Y:F6}, {node.Coord.Z:F6}");
            }
            writer.WriteLine();
        }

        private static void WriteMaterials(StreamWriter writer, Dictionary<Material, int> materialIdMap)
        {
            writer.WriteLine("*MATERIAL");
            foreach (var (material, id) in materialIdMap)
            {
                // ISOTROPIC material: ID, TYPE, NAME, STYPE(=1)
                // followed by: E, Poisson, ThermalExp, Density, DampRatio, ...
                string name = $"Mat{id}";
                writer.WriteLine($"   {id}, ISOTROPIC, {name}, 1, 0, 0, 0, 0, 0, 0");
                writer.WriteLine($"   , {material.E:E6}, {material.P:F4}, 0, 0, 0, 0");
            }
            writer.WriteLine();
        }

        private static void WriteSections(StreamWriter writer, Dictionary<Section, int> sectionIdMap, Dictionary<Material, int> materialIdMap)
        {
            writer.WriteLine("*SECTION");
            foreach (var (section, id) in sectionIdMap)
            {
                // DBUSER (user-defined general section)
                string name = $"Sec{id}";
                writer.WriteLine($"   {id}, DBUSER, {name}, CC, 0, 0, 0, 0, 0, 0, YES, NO");
                // AX, ASy, IZ, IY, AX2, IX
                writer.WriteLine($"   , {section.AX:E6}, {section.AY:E6}, {section.IZ:E6}, {section.IY:E6}, {section.AZ:E6}, {section.IX:E6}");
            }
            writer.WriteLine();
        }

        private void WriteElements(StreamWriter writer, Dictionary<Node, int> nodeIdMap,
            Dictionary<Material, int> materialIdMap, Dictionary<Section, int> sectionIdMap)
        {
            writer.WriteLine("*ELEMENT");
            int elemId = 1;
            foreach (var beam in _anaModel.Beams)
            {
                if (beam.Section == null || beam.Section.Material == null) continue;
                if (!nodeIdMap.TryGetValue(beam.NodeI, out int nodeI)) continue;
                if (!nodeIdMap.TryGetValue(beam.NodeJ, out int nodeJ)) continue;
                if (!materialIdMap.TryGetValue(beam.Section.Material, out int mId)) continue;
                if (!sectionIdMap.TryGetValue(beam.Section, out int sId)) continue;

                writer.WriteLine($"   {elemId}, BEAM, {mId}, {sId}, {nodeI}, {nodeJ}, 0");
                elemId++;
            }
            writer.WriteLine();
        }

        private void WriteElasticLinks(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            // HorizontalSoilSprings + PenaltySprings をエラスティックリンク要素として出力
            var allSprings = new List<HorizontalSoilSpring>();
            if (_anaModel.HorizontalSoilSprings != null)
                allSprings.AddRange(_anaModel.HorizontalSoilSprings);
            if (_anaModel.PenaltySprings != null)
                allSprings.AddRange(_anaModel.PenaltySprings);

            if (allSprings.Count == 0) return;

            writer.WriteLine("*ELASTICLINK");
            int linkId = 1;
            foreach (var spring in allSprings)
            {
                if (spring.NodeI == null || spring.NodeJ == null) continue;
                if (!nodeIdMap.TryGetValue(spring.NodeI, out int nodeI)) continue;
                if (!nodeIdMap.TryGetValue(spring.NodeJ, out int nodeJ)) continue;

                // KeTan の対角成分から6成分を抽出
                var ke = spring.KeTan;
                if (ke == null) continue;

                double kx = ke[0, 0];
                double ky = ke[1, 1];
                double kz = ke[2, 2];
                double kRx = ke[3, 3];
                double kRy = ke[4, 4];
                double kRz = ke[5, 5];

                writer.WriteLine($"   {linkId}, COMP/TENS, {nodeI}, {nodeJ}, {kx:E6}, {ky:E6}, {kz:E6}, {kRx:E6}, {kRy:E6}, {kRz:E6}");
                linkId++;
            }
            writer.WriteLine();
        }

        private void WriteConstraints(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            writer.WriteLine("*CONSTRAINT");
            foreach (var (node, id) in nodeIdMap)
            {
                // 各DOFについて、genuinely fixed (not slave) かどうかを判定
                char[] dofCode = new char[6];
                bool hasConstraint = false;
                for (int i = 0; i < 6; i++)
                {
                    bool isFixed = node.GetBoundary(i);
                    bool isSlave = node.MasterNodes[i] != null;
                    if (isFixed && !isSlave)
                    {
                        dofCode[i] = '1';
                        hasConstraint = true;
                    }
                    else
                    {
                        dofCode[i] = '0';
                    }
                }

                if (hasConstraint)
                {
                    writer.WriteLine($"   {id}, {new string(dofCode)}");
                }
            }
            writer.WriteLine();
        }

        private void WriteSprings(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            bool headerWritten = false;
            foreach (var (node, id) in nodeIdMap)
            {
                if (!node.IsSpringed) continue;

                if (!headerWritten)
                {
                    writer.WriteLine("*SPRING");
                    headerWritten = true;
                }

                var sp = node.TangentSpring;
                writer.WriteLine($"   {id}, {sp.Kx:E6}, {sp.Ky:E6}, {sp.Kz:E6}, {sp.Kxx:E6}, {sp.Kyy:E6}, {sp.Kzz:E6}");
            }
            if (headerWritten)
                writer.WriteLine();
        }

        private void WriteRigidLinks(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            if (_anaModel.RigidBodies == null || _anaModel.RigidBodies.Count == 0) return;

            writer.WriteLine("*RIGIDLINK");
            foreach (var rb in _anaModel.RigidBodies)
            {
                if (rb.MasterNode == null || rb.SlaveNodes == null) continue;
                if (!nodeIdMap.TryGetValue(rb.MasterNode, out int masterId)) continue;

                // DOFフラグ文字列
                string dofStr = string.Concat(rb.Dofs.Select(d => d ? "1" : "0"));

                foreach (var slave in rb.SlaveNodes)
                {
                    if (slave == null) continue;
                    if (!nodeIdMap.TryGetValue(slave, out int slaveId)) continue;
                    writer.WriteLine($"   {masterId}, {slaveId}, {dofStr}");
                }
            }
            writer.WriteLine();
        }

        // ─── 非線形曲線データ ──────────────────────────

        // サンプリング用変位値 (m)
        private static readonly double[] SoilDisplacementSamples =
        [
            0, 0.0001, 0.0005, 0.001, 0.002, 0.005,
            0.01, 0.02, 0.05, 0.1, 0.15, 0.2, 0.3, 0.5
        ];

        private void WriteNonlinearSoilCurves(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            if (_anaModel.HorizontalSoilSprings == null || _anaModel.HorizontalSoilSprings.Count == 0) return;

            // Node → HorizontalSoilReactionItem マッピング構築
            var soilReactionByNode = new Dictionary<Node, HorizontalSoilReactionItem>();
            foreach (var beam in _anaModel.Beams)
            {
                if (beam.HorizontalSoilReactionItem == null) continue;
                if (beam.NodeI != null && !soilReactionByNode.ContainsKey(beam.NodeI))
                    soilReactionByNode[beam.NodeI] = beam.HorizontalSoilReactionItem;
                if (beam.NodeJ != null && !soilReactionByNode.ContainsKey(beam.NodeJ))
                    soilReactionByNode[beam.NodeJ] = beam.HorizontalSoilReactionItem;
            }

            bool headerWritten = false;
            foreach (var spring in _anaModel.HorizontalSoilSprings)
            {
                if (spring.NodeI == null || spring.NodeJ == null) continue;
                if (!soilReactionByNode.TryGetValue(spring.NodeI, out var reaction)) continue;
                if (!nodeIdMap.TryGetValue(spring.NodeI, out int nI)) continue;
                if (!nodeIdMap.TryGetValue(spring.NodeJ, out int nJ)) continue;

                if (!headerWritten)
                {
                    writer.WriteLine("; ==== NONLINEAR SOIL SPRING CURVES ====");
                    headerWritten = true;
                }

                writer.WriteLine($"; Spring NodeI={nI}, NodeJ={nJ}, Layer={reaction.Name}");
                writer.WriteLine($"; Kh0={reaction.Kh0:F1} kN/m3, PyFrontTop={reaction.PyFrontTop:F1} kN/m2, B={reaction.B:F3} m");
                writer.WriteLine("; Displacement(m), Force(kN)");

                foreach (double y in SoilDisplacementSamples)
                {
                    double force = reaction.GetSoilReaction(y, isTop: true, isFront: true);
                    writer.WriteLine($";   {y:F6}, {force:F6}");
                }
                writer.WriteLine();
            }
        }

        private void WriteNonlinearRotationalCurves(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            if (_anaModel.RotationalSprings == null || _anaModel.RotationalSprings.Count == 0) return;

            bool headerWritten = false;
            foreach (var rs in _anaModel.RotationalSprings)
            {
                if (!rs.IsNonlinear) continue;
                if (rs.NodeI == null || rs.NodeJ == null) continue;
                if (!nodeIdMap.TryGetValue(rs.NodeI, out int nI)) continue;
                if (!nodeIdMap.TryGetValue(rs.NodeJ, out int nJ)) continue;

                var curve = rs.Mode == RotationalSpringMode.CombinedXY ? rs.CurveXY : rs.Curve;
                if (curve == null || curve.Points.Count == 0) continue;

                if (!headerWritten)
                {
                    writer.WriteLine("; ==== NONLINEAR ROTATIONAL SPRING CURVES ====");
                    headerWritten = true;
                }

                writer.WriteLine($"; RotSpring NodeI={nI}, NodeJ={nJ}, Mode={rs.Mode}");
                writer.WriteLine("; Rotation(rad), Moment(kNm)");
                foreach (var (theta, moment) in curve.Points)
                {
                    writer.WriteLine($";   {theta:E6}, {moment:F6}");
                }
                writer.WriteLine();
            }
        }

        private void WriteBeamMPhiCurves(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            bool headerWritten = false;
            foreach (var beam in _anaModel.Beams)
            {
                var curve = beam.ResolvedCombinedCurve;
                if (curve == null || curve.Points.Count == 0) continue;
                if (beam.NodeI == null || beam.NodeJ == null) continue;
                if (!nodeIdMap.TryGetValue(beam.NodeI, out int nI)) continue;
                if (!nodeIdMap.TryGetValue(beam.NodeJ, out int nJ)) continue;

                if (!headerWritten)
                {
                    writer.WriteLine("; ==== BEAM MOMENT-CURVATURE CURVES ====");
                    headerWritten = true;
                }

                writer.WriteLine($"; Beam \"{beam.Name}\", NodeI={nI}, NodeJ={nJ}");
                writer.WriteLine("; Curvature(1/m), Moment(kNm)");
                foreach (var (phi, moment) in curve.Points)
                {
                    writer.WriteLine($";   {phi:E6}, {moment:F6}");
                }
                writer.WriteLine();
            }
        }
    }
}
