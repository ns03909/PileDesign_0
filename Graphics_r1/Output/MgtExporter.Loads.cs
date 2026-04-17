using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Output
{
    // MGT の荷重関連セクション（STLDCASE, CONLOAD, SPDISP, LOADCOMB）を出力する partial。
    public partial class MgtExporter
    {
        // 荷重ケース名生成（ハイフンは算術演算子と誤認されるためアンダースコアを使用）
        private static string UFName(LoadCase lc) => $"UF_L{lc.Level}_{lc.No}";          // 上部構造慣性力
        private static string FFName(LoadCase lc) => $"FF_L{lc.Level}_{lc.No}";          // 基礎構造慣性力
        private static string GDName(LoadCase lc, bool isLiq) => $"GD_L{lc.Level}_{lc.No}{(isLiq ? "_L" : "")}"; // 地盤変位
        private static string CombName(LoadCase lc, LoadCombination comb, bool isLiq) => $"C_L{lc.Level}_{lc.No}_{comb.No}{(isLiq ? "_L" : "")}";

        // 説明文のサニタイズ（カンマは区切り文字と誤認されるため除去）
        private static string SanitizeDesc(string s) => s?.Replace(',', ' ').Replace("  ", " ") ?? "";

        private void WriteLoadCases(StreamWriter writer, ExportContext ctx)
        {
            var nodeIdMap = ctx.NodeIdMap;
            var inputModel = _anaModel.InputModel;
            if (inputModel?.LoadCasesInput == null) return;
            if (_anaModel.AnalysisStepResults == null || _anaModel.AnalysisStepResults.Count == 0) return;

            // 解析済み (LoadCase, IsLiquefaction) の一意化
            var analyzedLCLiqs = _anaModel.AnalysisStepResults
                .Where(r => r.LoadCase != null)
                .GroupBy(r => (r.LoadCase.LoadName, r.LoadCase.Level, r.IsLiquefaction))
                .Select(g => (LC: g.First().LoadCase, IsLiq: g.Key.IsLiquefaction))
                .ToList();

            if (analyzedLCLiqs.Count == 0) return;

            // 解析済み LoadCase の一意化（レベル+番号）
            var analyzedLCs = analyzedLCLiqs
                .GroupBy(x => (x.LC.Level, x.LC.No))
                .Select(g => g.First().LC)
                .ToList();

            // 解析済み (LoadCase, LoadCombination, IsLiquefaction)
            var analyzedCombos = _anaModel.AnalysisStepResults
                .Where(r => r.LoadCase != null && r.LoadCombination != null)
                .GroupBy(r => (r.LoadCase.LoadName, r.LoadCase.Level, r.LoadCombination.No, r.IsLiquefaction))
                .Select(g => (LC: g.First().LoadCase, Comb: g.First().LoadCombination, IsLiq: g.Key.IsLiquefaction))
                .ToList();

            // 杭配置の地盤節点 → ZDataItem（地盤変位情報）マップ
            var soilDispMap = new Dictionary<Node, ZDataItem>(ReferenceEqualityComparer.Instance);
            if (inputModel.PileLayoutItems != null && inputModel.ElementDivision?.SoilPiles != null)
            {
                var soilPiles = inputModel.ElementDivision.SoilPiles;
                foreach (var pItem in inputModel.PileLayoutItems)
                {
                    if (pItem.SoilNodes == null || pItem.SoilPileAltNo <= 0) continue;
                    int idx = pItem.SoilPileAltNo - 1;
                    if (idx >= soilPiles.Count) continue;
                    var soilPile = soilPiles[idx];
                    if (soilPile?.ZDataItems == null) continue;
                    int count = Math.Min(pItem.SoilNodes.Count, soilPile.ZDataItems.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (pItem.SoilNodes[i] == null) continue;
                        soilDispMap[pItem.SoilNodes[i]] = soilPile.ZDataItems[i];
                    }
                }
            }

            // 代表節点（慣性力の作用点）= 解析モデル先頭節点
            var masterNode = _anaModel.Nodes.FirstOrDefault();
            int masterId = (masterNode != null && nodeIdMap.TryGetValue(masterNode, out int mid)) ? mid : -1;

            // === STLDCASE 定義 ===
            writer.WriteLine("*STLDCASE    ; Static Load Cases");
            writer.WriteLine("; LCNAME, LCTYPE, DESC");
            foreach (var lc in analyzedLCs)
            {
                writer.WriteLine($"   {UFName(lc),-14}, USER, L{lc.Level} {SanitizeDesc(lc.LoadName)} Upper Mass Force ({lc.LoadAngle}deg)");
            }
            foreach (var lc in analyzedLCs)
            {
                writer.WriteLine($"   {FFName(lc),-14}, USER, L{lc.Level} {SanitizeDesc(lc.LoadName)} Foundation Mass Force ({lc.LoadAngle}deg)");
            }
            foreach (var (lc, isLiq) in analyzedLCLiqs)
            {
                writer.WriteLine($"   {GDName(lc, isLiq),-14}, USER, L{lc.Level} {SanitizeDesc(lc.LoadName)} Ground Disp ({lc.LoadAngle}deg{(isLiq ? " liq" : "")})");
            }
            writer.WriteLine();

            // === 上部構造慣性力 ===
            foreach (var lc in analyzedLCs)
            {
                double rad = lc.LoadAngle * Math.PI / 180.0;
                double fx = lc.UpperMassForce * Math.Cos(rad);
                double fy = lc.UpperMassForce * Math.Sin(rad);

                writer.WriteLine($"*USE-STLD, {UFName(lc)}");
                writer.WriteLine();
                if (masterId > 0 && Math.Abs(lc.UpperMassForce) > 1e-9)
                {
                    writer.WriteLine("*CONLOAD    ; Nodal Loads");
                    writer.WriteLine("; NODE_LIST, FX, FY, FZ, MX, MY, MZ, GROUP");
                    writer.WriteLine($"   {masterId}, {fx:F4}, {fy:F4}, 0, 0, 0, 0, ,");
                    writer.WriteLine();
                }
                writer.WriteLine($"; End of data for load case [{UFName(lc)}] -------------------------");
                writer.WriteLine();
            }

            // === 基礎構造慣性力 ===
            foreach (var lc in analyzedLCs)
            {
                double rad = lc.LoadAngle * Math.PI / 180.0;
                double fx = lc.FoundationMassForce * Math.Cos(rad);
                double fy = lc.FoundationMassForce * Math.Sin(rad);

                writer.WriteLine($"*USE-STLD, {FFName(lc)}");
                writer.WriteLine();
                if (masterId > 0 && Math.Abs(lc.FoundationMassForce) > 1e-9)
                {
                    writer.WriteLine("*CONLOAD    ; Nodal Loads");
                    writer.WriteLine("; NODE_LIST, FX, FY, FZ, MX, MY, MZ, GROUP");
                    writer.WriteLine($"   {masterId}, {fx:F4}, {fy:F4}, 0, 0, 0, 0, ,");
                    writer.WriteLine();
                }
                writer.WriteLine($"; End of data for load case [{FFName(lc)}] -------------------------");
                writer.WriteLine();
            }

            // === 地盤強制変位 ===
            foreach (var (lc, isLiq) in analyzedLCLiqs)
            {
                double rad = lc.LoadAngle * Math.PI / 180.0;

                writer.WriteLine($"*USE-STLD, {GDName(lc, isLiq)}");
                writer.WriteLine();
                if (soilDispMap.Count > 0)
                {
                    writer.WriteLine("*SPDISP    ; Specified Displacement of Supports");
                    writer.WriteLine("; NODE_LIST, FLAG, Dx, Dy, Dz, Rx, Ry, Rz, GROUP");
                    foreach (var (node, zData) in soilDispMap)
                    {
                        if (!nodeIdMap.TryGetValue(node, out int nodeId)) continue;

                        double dispMm = lc.Level == 1
                            ? (isLiq ? zData.GroundDisp1L : zData.GroundDisp1)
                            : (isLiq ? zData.GroundDisp2L : zData.GroundDisp2);

                        double disp = dispMm / 1000.0; // mm → m
                        double dx = disp * Math.Cos(rad);
                        double dy = disp * Math.Sin(rad);

                        if (Math.Abs(dx) > 1e-9 || Math.Abs(dy) > 1e-9)
                        {
                            writer.WriteLine($"   {nodeId}, 110000, {dx:E4}, {dy:E4}, 0, 0, 0, 0, ");
                        }
                    }
                    writer.WriteLine();
                }
                writer.WriteLine($"; End of data for load case [{GDName(lc, isLiq)}] -------------------------");
                writer.WriteLine();
            }

            // === LOADCOMB（荷重組み合わせ） ===
            if (analyzedCombos.Count > 0)
            {
                writer.WriteLine("*LOADCOMB    ; Combinations");
                writer.WriteLine("; NAME=NAME, KIND, ACTIVE, bES, iTYPE, DESC");
                writer.WriteLine(";         ANAL1, LCNAME1, FACT1, ANAL2, LCNAME2, FACT2, ...");
                foreach (var (lc, comb, isLiq) in analyzedCombos)
                {
                    string name = CombName(lc, comb, isLiq);
                    string desc = $"L{lc.Level} {SanitizeDesc(lc.LoadName)} Comb{comb.No}{(isLiq ? " liq" : "")} b1={comb.Beta1} b2={comb.Beta2} a1={comb.Alpha1}";
                    writer.WriteLine($"   NAME={name}, GEN, ACTIVE, 0, 0, {desc}");
                    writer.WriteLine($"        ST, {UFName(lc),-14}, {comb.Beta1:F4}, ST, {FFName(lc),-14}, {comb.Beta2:F4}, ST, {GDName(lc, isLiq),-14}, {comb.Alpha1:F4}");
                }
                writer.WriteLine();
            }
        }
    }
}
