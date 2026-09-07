using System;
using System.Collections.Generic;
using System.Linq;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using Serilog;

namespace PileDesign.Services
{
    /// <summary>
    /// FEM の梁要素に適用する合成 M-φ 曲線を PileSection から解決する。
    ///
    /// HorizontalCalculationViewModel の「初期セットアップ」と「ステップ毎の軸力再解決」の
    /// 2 箇所に重複していたロジック（場所打ち鋼管コンクリート杭の杭中間部特別扱いを含む）を集約。
    /// 戻り値の単位: φ [rad/m], M [kN·m]。解決できない場合は null（呼び出し側でスキップ＝
    /// 初期セットアップでは曲線なし、ステップ毎再解決では前回曲線を維持）。
    /// </summary>
    internal static class MphiCurveResolver
    {
        /// <param name="section">杭断面（null なら解決不能）</param>
        /// <param name="axialN_kN">軸力 [kN]（PileSection.GetMPhiRelationship と同じ単位規約）</param>
        /// <param name="isPileTop">杭頭要素なら true（杭中間部特別扱いの分岐に使用）</param>
        internal static (IList<double> Phis, IList<double> Moments)? Resolve(
            PileSection? section, double axialN_kN, bool isPileTop)
        {
            if (section == null) return null;

            // 場所打ち鋼管コンクリート杭の杭中間部: ひび割れ後勾配の延長で終局曲率を再定義する
            // ポリリニア固有の補正を適用（ファイバー M-φ オプション ON 時は補正せず共通経路＝ファイバー曲線）。
            if (!isPileTop
                && !ConcreteModelOptions.UseFiberMPhi
                && section.PileBodyType == PileTypeNames.InsituSteelPipeConcrete
                && section.PileSectionType == PileTypeNames.SteelPipeConcreteSection)
            {
                try
                {
                    // 断面は PileSection.CreateSectionCalculator に組み立てさせる。以前はここで自前に
                    // new しており、材料側のオプション (KCTB の εcu=0.005 等) が渡らなかった。
                    // εcu=0.005 では終点の曲率だけ 0.005・材料は 0.003 のままで、頂点の後に下がる
                    // M-φ がそのまま解析に入っていた (この経路は PileSection.GetMPhiRelationship を
                    // 通らないので、そこの単調化も効かない)。
                    if (section.CreateSectionCalculator() is not InsituSteelPipeReinforcedConcreteSection sprcSection)
                        return null;
                    var middle = sprcSection.GetMPhiRelationshipForMiddle(axialN_kN * UnitConversion.KN_TO_N);
                    // FEM ばねとして負勾配にならないよう、通常経路と同じ単調化を通す
                    var (phisRaw, msRaw) = PileSection.MakeMonotonicForAnalysis(middle.Phis, middle.Moments);
                    // 単位変換: kN → N（断面計算は N 単位）、φ [1/mm] → [1/m]、M [N·mm] → [kN·m]
                    var phis = phisRaw.Select(p => p * UnitConversion.PER_MM_TO_PER_M).ToList();
                    var ms = msRaw.Select(m => m * UnitConversion.NMM_TO_KNM).ToList();
                    return ((IList<double>)phis, (IList<double>)ms);
                }
                catch (Exception ex)
                {
                    // 従来は未捕捉で解析ループを破壊し得た経路。null（曲線なし/前回値維持）に落として記録する。
                    Common.CalcFallbackTracker.Report("杭中間部 M-φ の解決（→スキップ）", ex, $"axialN={axialN_kN:F0} kN");
                    return null;
                }
            }

            // 通常経路: PileSection.GetMPhiRelationship（φ [rad/m], M [kN·m] へ変換済みの折線／ファイバー曲線）
            try
            {
                var (phis, moments) = section.GetMPhiRelationship(axialN_kN);
                if (phis == null || moments == null || phis.Count < 2 || phis.Count != moments.Count)
                    return null;
                return (phis, moments);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MphiCurveResolver] M-φ の解決に失敗 (axialN={AxialN} kN)", axialN_kN);
                return null;
            }
        }
    }
}
