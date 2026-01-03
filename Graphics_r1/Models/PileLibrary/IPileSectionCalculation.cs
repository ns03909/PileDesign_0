
using System.Collections.Generic;

namespace PileDesign.Models.PileLibrary
{
    /// <summary>
    /// 杭断面計算の共通インターフェース
    /// </summary>
    public interface IPileSectionCalculation
    {
        /// <summary>軸力閾値リスト</summary>
        List<double> UltimateLimitAxialForceThresholds { get; }

        // NM 相互作用（N[N], M[N·mm], curvature, strain）
        (List<double> N, List<double> M, List<double> Phi, List<double> Epsilon) UnfactoredServiceNM { get; }
        (List<double> N, List<double> M, List<double> Phi, List<double> Epsilon) UnfactoredDamageNM { get; }
        (List<double> N, List<double> M, List<double> Phi, List<double> Epsilon) UnfactoredUltimateNM { get; }
        (List<double> N, List<double> M, List<double> Phi, List<double> Epsilon) FactoredServiceNM { get; }
        (List<double> N, List<double> M, List<double> Phi, List<double> Epsilon) FactoredDamageNM { get; }
        (List<double> N, List<double> M, List<double> Phi, List<double> Epsilon) FactoredUltimateNM { get; }

        // M-φ 関係
        (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN);
    }
}