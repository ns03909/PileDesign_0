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
    /// FEM解析モデルを midas iGen / Gen MGT (テキスト) 形式でエクスポートする。
    /// 非線形地盤ばね（MULTI LINEAR）、慣性力（CONLOAD）、強制変位（SPDISP）、
    /// 荷重組合せ（LOADCOMB, β1/β2/α1）を含む。
    /// </summary>
    public partial class MgtExporter
    {
        private readonly AnaModel _anaModel;

        public MgtExporter(AnaModel anaModel)
        {
            _anaModel = anaModel ?? throw new ArgumentNullException(nameof(anaModel));
        }

        // 地盤境界節点のオフセット量（長さゼロのElasticLink回避のため）
        private const double SoilNodeOffsetX = 1.0;
        private const double SoilNodeOffsetY = 1.0;

        /// <summary>
        /// MGTエクスポート時の ID マッピング・節点分類等を保持するコンテキスト。
        /// Export メソッド内で一度構築され、各 Write* メソッドに共有される。
        /// </summary>
        private sealed class ExportContext
        {
            public Dictionary<Node, int> NodeIdMap { get; init; }
            public Dictionary<Material, int> MaterialIdMap { get; init; }
            public Dictionary<Section, int> SectionIdMap { get; init; }
            public HashSet<Node> SoilBoundaryNodes { get; init; }
            public Dictionary<HorizontalSoilSpring, int> SpringFunctionIds { get; init; }
            public Dictionary<HorizontalSoilSpring, int> SpringYNodeIds { get; init; }
        }

        private ExportContext BuildContext()
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

            // 保護対象節点（オフセットしない節点）: 梁端点 + RigidBody master/slave + PenaltySpring端点
            var protectedNodes = new HashSet<Node>(ReferenceEqualityComparer.Instance);
            foreach (var beam in _anaModel.Beams)
            {
                if (beam.NodeI != null) protectedNodes.Add(beam.NodeI);
                if (beam.NodeJ != null) protectedNodes.Add(beam.NodeJ);
            }
            if (_anaModel.RigidBodies != null)
            {
                foreach (var rb in _anaModel.RigidBodies)
                {
                    if (rb.MasterNode != null) protectedNodes.Add(rb.MasterNode);
                    if (rb.SlaveNodes != null)
                        foreach (var s in rb.SlaveNodes)
                            if (s != null) protectedNodes.Add(s);
                }
            }
            if (_anaModel.PenaltySprings != null)
            {
                foreach (var s in _anaModel.PenaltySprings)
                {
                    if (s.NodeI != null) protectedNodes.Add(s.NodeI);
                    if (s.NodeJ != null) protectedNodes.Add(s.NodeJ);
                }
            }

            // 地盤境界節点: 水平地盤ばねのNodeJで保護対象でないもの
            var soilBoundaryNodes = new HashSet<Node>(ReferenceEqualityComparer.Instance);
            if (_anaModel.HorizontalSoilSprings != null)
                foreach (var s in _anaModel.HorizontalSoilSprings)
                    if (s.NodeJ != null && !protectedNodes.Contains(s.NodeJ)) soilBoundaryNodes.Add(s.NodeJ);

            // 各水平地盤ばねに対してFUNCTION IDを割り当て（MULTI LINEAR用）
            var springFunctionIds = new Dictionary<HorizontalSoilSpring, int>(ReferenceEqualityComparer.Instance);
            int nextFuncId = 1;
            if (_anaModel.HorizontalSoilSprings != null)
            {
                foreach (var spring in _anaModel.HorizontalSoilSprings)
                {
                    if (spring.NodeI == null || spring.NodeJ == null) continue;
                    springFunctionIds[spring] = nextFuncId++;
                }
            }

            // Y方向用の仮想地盤節点（Y方向解析が完了している場合のみ）
            var springYNodeIds = new Dictionary<HorizontalSoilSpring, int>(ReferenceEqualityComparer.Instance);
            if (HasYDirectionAnalysis() && _anaModel.HorizontalSoilSprings != null)
            {
                foreach (var spring in _anaModel.HorizontalSoilSprings)
                {
                    if (spring.NodeI == null || spring.NodeJ == null) continue;
                    springYNodeIds[spring] = nodeId++;
                }
            }

            return new ExportContext
            {
                NodeIdMap = nodeIdMap,
                MaterialIdMap = materialIdMap,
                SectionIdMap = sectionIdMap,
                SoilBoundaryNodes = soilBoundaryNodes,
                SpringFunctionIds = springFunctionIds,
                SpringYNodeIds = springYNodeIds,
            };
        }

        public void Export(string filePath)
        {
            // 数値は地域設定によらず「1.25」の形で書く。
            //
            // 各所の数値は $"{x:F4}" のように組み立てていて、実行中の地域設定の小数点で書かれる。
            // 小数点がカンマの地域 (ドイツ語など) では「1,25」になり、MGT の項目の区切りのカンマとぶつかって
            // 値が 2 つに割れる。組み立てた文字列は書き出し先の書式では直せないので、
            // 書き出しのあいだだけこのスレッドの地域設定を InvariantCulture にし、終わったら必ず戻す。
            // (書式を 1 つずつ直すと 5 つのファイルに散らばった書式のどれかが漏れる)
            // 出力先を作る前に確かめる (途中まで書いたファイルを残さない)
            EnsureLoadCaseNumbersAreUnique(_anaModel.AnalysisStepResults);
            EnsureLoadCombinationNumbersAreUnique(_anaModel.AnalysisStepResults);

            var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                ExportCore(filePath);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            }
        }

        /// <summary>
        /// 一時ファイルに書き切ってから保存先と差し替える (<see cref="Services.FileOperationService.WriteAtomically"/>)。
        ///
        /// 以前は保存先を直接開き直して書いていたので、途中で書き込みに失敗する (容量不足・例外) と、
        /// 前に出力した正常な MGT ファイルが途中までの内容で上書きされて失われた。
        /// いまは失敗しても保存先は前の内容のままで、一時ファイルも残さない。
        /// </summary>
        private void ExportCore(string filePath)
        {
            var ctx = BuildContext();
            Services.FileOperationService.WriteAtomically(filePath, stream =>
            {
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), bufferSize: 64 * 1024, leaveOpen: true);
                WriteAll(writer, ctx);
            });
        }

        /// <summary>MGT の全体を書く。書き出しの途中で例外を出すとファイルは差し替わらない (<see cref="ExportCore"/>)。</summary>
        private void WriteAll(StreamWriter writer, ExportContext ctx)
        {
            WriteHeader(writer);
            WriteUnit(writer);
            WriteNodes(writer, ctx);
            WriteMaterials(writer, ctx);
            WriteSections(writer, ctx);
            WriteElements(writer, ctx);
            WriteForcesDeformationFunction(writer, ctx);
            WriteElasticLinks(writer, ctx);
            WriteConstraints(writer, ctx);
            WriteRigidLinks(writer, ctx);
            WriteLoadCases(writer, ctx);

            writer.WriteLine("*ENDDATA");
        }

        private static void WriteHeader(StreamWriter writer)
        {
            writer.WriteLine(";---------------------------------------------------------------------------");
            writer.WriteLine(";  MGT file exported by PileDesign");
            writer.WriteLine($";  Date : {DateTime.Now:yyyy/M/d}");
            writer.WriteLine(";---------------------------------------------------------------------------");
            writer.WriteLine();
            writer.WriteLine("*VERSION");
            writer.WriteLine("   9.4.5");
            writer.WriteLine();
        }

        private static void WriteUnit(StreamWriter writer)
        {
            writer.WriteLine("*UNIT    ; Unit System");
            writer.WriteLine("; FORCE, LENGTH, HEAT, TEMPER");
            writer.WriteLine("   KN   , M, KJ, C");
            writer.WriteLine();
        }

        /// <summary>Y方向荷重解析が実施されているかを判定</summary>
        private bool HasYDirectionAnalysis()
        {
            if (_anaModel.AnalysisStepResults == null) return false;
            foreach (var result in _anaModel.AnalysisStepResults)
            {
                var lc = result.LoadCase;
                if (lc == null) continue;
                double angleRad = lc.LoadAngle * Math.PI / 180.0;
                if (Math.Abs(Math.Sin(angleRad)) > 1e-3) return true;
            }
            return false;
        }



    }
}
