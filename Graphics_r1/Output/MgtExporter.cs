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
            WriteRigidLinks(writer, nodeIdMap);

            // 非線形曲線データ（コメント形式）
            WriteNonlinearSoilCurves(writer, nodeIdMap);
            WriteNonlinearRotationalCurves(writer, nodeIdMap);
            WriteBeamMPhiCurves(writer, nodeIdMap);

            writer.WriteLine("*ENDDATA");
        }

        private static void WriteHeader(StreamWriter writer)
        {
            writer.WriteLine("*HEADER");
            writer.WriteLine($"; MGT file exported by PileDesign");
            writer.WriteLine($"; Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine();
        }

        private static void WriteUnit(StreamWriter writer)
        {
            // MIDAS GEN: *UNIT, Force, Length, Heat, Temper
            writer.WriteLine("*UNIT");
            writer.WriteLine("   kN, m, kJ, C");
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
                // MIDAS GEN MGT Material format:
                // ID, TYPE, NAME
                // , E, Poisson, ThermalExp, Density
                string name = $"Mat{id}";
                writer.WriteLine($"   {id}, STEEL, {name},  0, 0, , C, NO, 0.02, 1");
                writer.WriteLine($"   , YES, {material.E:E6}, {material.P:F4}, 1.200E-005, 7.698E+001");
            }
            writer.WriteLine();
        }

        private static void WriteSections(StreamWriter writer, Dictionary<Section, int> sectionIdMap, Dictionary<Material, int> materialIdMap)
        {
            writer.WriteLine("*SECTION");
            foreach (var (section, id) in sectionIdMap)
            {
                // MIDAS GEN: DBUSER section
                // ID, DBUSER, Name, OFFSET, iCENT, iREF, iHORZ, SHAPE, 0, 0, YES, NO, YES
                // , AX, ASy, ASz, IXX, IYY, IZZ
                string name = $"Sec{id}";
                writer.WriteLine($"   {id}, DBUSER, {name}, , 0, 0, 0, 0, 0, 0, YES, NO, YES");
                writer.WriteLine($"   , {section.AX:E6}, {section.AY:E6}, {section.AZ:E6}, {section.IX:E6}, {section.IY:E6}, {section.IZ:E6}");
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

            // MIDAS GEN: *ELASTICLINK
            // ID, TYPE(integer), NodeI, NodeJ, SDx, SDy, SDz, SRx, SRy, SRz
            // TYPE: 1=RIGID, 2=弾性リンク
            writer.WriteLine("*ELASTICLINK");
            int linkId = 1;
            foreach (var spring in allSprings)
            {
                if (spring.NodeI == null || spring.NodeJ == null) continue;
                if (!nodeIdMap.TryGetValue(spring.NodeI, out int nodeI)) continue;
                if (!nodeIdMap.TryGetValue(spring.NodeJ, out int nodeJ)) continue;

                var ke = spring.KeTan;
                if (ke == null) continue;

                double kx = ke[0, 0];
                double ky = ke[1, 1];
                double kz = ke[2, 2];
                double kRx = ke[3, 3];
                double kRy = ke[4, 4];
                double kRz = ke[5, 5];

                // TYPE=2: 弾性リンク（General type）
                writer.WriteLine($"   {linkId}, 2, {nodeI}, {nodeJ}, {kx:E6}, {ky:E6}, {kz:E6}, {kRx:E6}, {kRy:E6}, {kRz:E6}");
                linkId++;
            }
            writer.WriteLine();
        }

        private void WriteConstraints(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            // MIDAS GEN: *CONSTRAINT → *BNDR-GROUP (boundary group)
            // or simply *CONSTRAINT with format: NodeID, DOF1, DOF2, DOF3, DOF4, DOF5, DOF6
            writer.WriteLine("*CONSTRAINT");
            foreach (var (node, id) in nodeIdMap)
            {
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

        private void WriteRigidLinks(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            if (_anaModel.RigidBodies == null || _anaModel.RigidBodies.Count == 0) return;

            // MIDAS GEN: *RIGIDLINK
            // MasterNodeID, SlaveNodeID, DOFs
            writer.WriteLine("*RIGIDLINK");
            foreach (var rb in _anaModel.RigidBodies)
            {
                if (rb.MasterNode == null || rb.SlaveNodes == null) continue;
                if (!nodeIdMap.TryGetValue(rb.MasterNode, out int masterId)) continue;

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

        // ─── 非線形曲線データ（コメント形式で参考出力） ──────────────────────────

        private static readonly double[] SoilDisplacementSamples =
        [
            0, 0.0001, 0.0005, 0.001, 0.002, 0.005,
            0.01, 0.02, 0.05, 0.1, 0.15, 0.2, 0.3, 0.5
        ];

        private void WriteNonlinearSoilCurves(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            if (_anaModel.HorizontalSoilSprings == null || _anaModel.HorizontalSoilSprings.Count == 0) return;

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
                    writer.WriteLine();
                    writer.WriteLine("; ==== NONLINEAR SOIL SPRING CURVES ====");
                    headerWritten = true;
                }

                string layerName = reaction.Name ?? reaction.SoilType ?? "Unknown";
                double kh0 = reaction.Kh0;
                double pyTop = reaction.PyFrontTop;

                writer.WriteLine($"; Spring NodeI={nI}, NodeJ={nJ}, Layer={layerName}");
                writer.WriteLine($"; Kh0={kh0:F1} kN/m3, PyFrontTop={pyTop:F1} kN/m2");
                writer.WriteLine();
            }
        }

        private void WriteNonlinearRotationalCurves(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            if (_anaModel.RotationalSprings == null || _anaModel.RotationalSprings.Count == 0) return;

            writer.WriteLine();
            writer.WriteLine("; ==== ROTATIONAL SPRING (M-theta) CURVES ====");
            foreach (var rspring in _anaModel.RotationalSprings)
            {
                if (rspring.NodeI == null || rspring.NodeJ == null) continue;
                if (!nodeIdMap.TryGetValue(rspring.NodeI, out int nI)) continue;
                if (!nodeIdMap.TryGetValue(rspring.NodeJ, out int nJ)) continue;

                writer.WriteLine($"; RotationalSpring NodeI={nI}, NodeJ={nJ}, Name={rspring.Name}");
                writer.WriteLine($"; Ktheta={rspring.Ktheta:E3}, KthetaXY={rspring.KthetaXY:E3}");
                writer.WriteLine();
            }
        }

        private void WriteBeamMPhiCurves(StreamWriter writer, Dictionary<Node, int> nodeIdMap)
        {
            bool headerWritten = false;
            int elemId = 0;
            foreach (var beam in _anaModel.Beams)
            {
                elemId++;
                var curve = beam.ResolvedCombinedCurve;
                if (curve == null) continue;

                if (!headerWritten)
                {
                    writer.WriteLine();
                    writer.WriteLine("; ==== BEAM M-PHI CURVES ====");
                    headerWritten = true;
                }

                writer.WriteLine($"; Element {elemId}: {beam.Name}");
                writer.WriteLine($"; Curvature(1/m), Moment(kN*m)");
                writer.WriteLine($"; InitialCurveTangent={beam.InitialCurveTangent:E6}");
                writer.WriteLine();
            }
        }
    }
}
