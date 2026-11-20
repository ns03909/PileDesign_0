using PileDesign.Models.InputData;
using System;
using System.Linq;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 例題の杭配置に基礎梁を足す。収束の回帰に使う例題はどれも基礎梁を持たないので、
    /// 基礎梁と杭頭の剛な接続部 (M-φ を持たない梁) を含むモデルをここで作る。
    /// </summary>
    internal static class FoundationBeamFixture
    {
        /// <summary>
        /// 同じ行 (Y が同じ) の隣どうしを X 方向に、同じ列 (X が同じ) の隣どうしを Y 方向に、基礎梁で結ぶ。
        /// 断面は幅 0.8 m × せい 1.5 m、E = 2.5×10⁷ kN/m²。
        /// </summary>
        internal static void ConnectNeighbours(InputModel model, FoundationBeamConnectionMode mode)
        {
            model.FoundationBeamInput ??= new FoundationBeamInput();
            var fb = model.FoundationBeamInput;
            fb.ConnectionMode = mode;
            if (fb.Materials.Count == 0) fb.Materials.Add(new BeamMaterial { YoungModulus = 2.5e7, PoissonRatio = 0.2 });
            fb.Beams.Clear();

            var piles = model.PileLayoutItems.ToList();
            foreach (var row in piles.GroupBy(p => Math.Round(p.Y, 3)))
                Chain(row.OrderBy(p => p.X).ToList());
            foreach (var column in piles.GroupBy(p => Math.Round(p.X, 3)))
                Chain(column.OrderBy(p => p.Y).ToList());

            void Chain(System.Collections.Generic.List<PileLayoutDataItem> line)
            {
                for (int i = 0; i + 1 < line.Count; i++)
                    fb.Beams.Add(new FoundationBeam
                    {
                        NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = line[i].UniqueId,
                        NodeJ_Type = NodeReferenceType.PileLayout, NodeJ_Id = line[i + 1].UniqueId,
                        MaterialNo = 1, Width = 0.8, Height = 1.5, YoungModulus = 2.5e7,
                    });
            }
        }
    }
}
