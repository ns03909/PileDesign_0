using PileDesign.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// KCTB 場所打ち鋼管コンクリート杭（TB工法）の適用範囲の検査。
    ///
    /// 出典: BCJ評定-FD0356-08「KCTB 場所打ち鋼管コンクリート杭」
    /// 4. 適用範囲・形状寸法等（表 1.1／表 1.3／表 1.4／表 1.6）および
    /// 5.(7) 鋼管の腐食しろ、2.(1) コンクリート。
    ///
    /// 範囲外でも計算は続けるため、エラーではなく警告として返す
    /// （メーカー別の高支持力杭工法の適用範囲検査と同じ扱い）。
    /// </summary>
    internal static class KctbApplicableRange
    {
        /// <summary>表 1.1 の鋼管外径の標準値（mm）。</summary>
        private static readonly double[] StandardOuterDiameters =
        [
            700, 750, 800, 850, 900, 950, 1000, 1050, 1100, 1150, 1200, 1250, 1300, 1350, 1400,
            1500, 1600, 1700, 1800, 1900, 2000, 2100, 2200, 2300, 2400, 2500, 2600, 2700
        ];

        /// <summary>表 1.4 同時建込み工法の場合の鋼管板厚の下限（腐食しろ 1mm を含む）。</summary>
        private static readonly (double MaxDia, double MinT)[] MinimumThicknesses =
        [
            (1800, 9), (2000, 10), (2200, 11), (2400, 12), (2600, 13), (2700, 14)
        ];

        private const double MinOuterDia = 700.0;
        private const double MaxOuterDia = 2700.0;
        private const double MinThickness = 9.0;
        private const double MaxThickness = 25.0;
        private const double MinFc = 18.0;
        private const double MaxFc = 45.0;
        private const double CorrosionDepthMm = 1.0;

        /// <summary>表 1.6 鋼管長の上限（グラウト充填。オーバーフロー充填は 12.5m）。</summary>
        private const double MaxPipeLengthGrout = 30.0;
        private const double MaxPipeLengthOverflow = 12.5;

        /// <summary>
        /// 杭体 1 本の鋼管コンクリート部について適用範囲を検査し、外れた項目を文で返す。
        /// KCTB オプションが無効なときは呼ばないこと。
        /// </summary>
        internal static IEnumerable<string> Validate(PileBodyInput? pileBody)
        {
            var messages = new List<string>();
            if (pileBody?.PileBodySegments == null) return messages;
            if (pileBody.PileBodyType != PileTypeNames.InsituSteelPipeConcrete) return messages;

            double pipeLength = 0.0;
            int segNo = 0;
            foreach (var seg in pileBody.PileBodySegments)
            {
                segNo++;
                var sec = seg?.PileSection;
                if (sec == null || sec.PileSectionType != PileTypeNames.SteelPipeConcreteSection) continue;

                pipeLength += seg!.SegmentLength;
                string where = $"杭体({pileBody.PileBodyRef}) 区間{segNo}";

                double dia = sec.PipeDia;
                double t = sec.PipeTs;

                if (dia < MinOuterDia || dia > MaxOuterDia)
                    messages.Add($"{where}: 鋼管外径 {dia:N0} mm は適用範囲 φ{MinOuterDia:N0}〜{MaxOuterDia:N0} mm の外です。");
                else if (!StandardOuterDiameters.Any(d => Math.Abs(d - dia) < 0.5))
                    messages.Add($"{where}: 鋼管外径 {dia:N0} mm は標準値にありません（φ700〜1400 は 50mm 刻み、φ1400 超は 100mm 刻み）。");

                if (t < MinThickness || t > MaxThickness)
                    messages.Add($"{where}: 鋼管板厚 {t:N0} mm は適用範囲 {MinThickness:N0}〜{MaxThickness:N0} mm の外です。");
                else if (Math.Abs(t - Math.Round(t)) > 1e-6)
                    messages.Add($"{where}: 鋼管板厚 {t:N1} mm は標準値にありません（1mm 刻み）。");

                double minT = MinimumThicknesses.FirstOrDefault(x => dia <= x.MaxDia).MinT;
                if (minT > 0 && t < minT)
                    messages.Add($"{where}: 鋼管外径 {dia:N0} mm に対する板厚の下限は {minT:N0} mm です（現在 {t:N0} mm、腐食しろ 1mm を含む値）。");

                if (dia > 2500 && sec.PipeGrade != "SKK490")
                    messages.Add($"{where}: 鋼管外径 {dia:N0} mm で使用できる材質は SKK490 のみです（現在 {sec.PipeGrade}）。");

                if (Math.Abs(sec.CorrosionDepth - CorrosionDepthMm) > 1e-6)
                    messages.Add($"{where}: 鋼管の腐食しろは {CorrosionDepthMm:N1} mm と定められています（現在 {sec.CorrosionDepth:N1} mm）。");

                if (sec.ConcreteFc < MinFc || sec.ConcreteFc > MaxFc)
                    messages.Add($"{where}: コンクリートの設計基準強度 {sec.ConcreteFc:N0} N/mm² は適用範囲 {MinFc:N0}〜{MaxFc:N0} N/mm² の外です。");
            }

            if (pipeLength > MaxPipeLengthGrout + 1e-6)
                messages.Add($"杭体({pileBody.PileBodyRef}): 鋼管コンクリート部の長さ {pipeLength:N1} m は鋼管長の上限 {MaxPipeLengthGrout:N0} m（外周グラウト充填）を超えています。");
            else if (pipeLength > MaxPipeLengthOverflow + 1e-6)
                messages.Add($"杭体({pileBody.PileBodyRef}): 鋼管コンクリート部の長さ {pipeLength:N1} m は外周オーバーフロー充填の上限 {MaxPipeLengthOverflow:N1} m を超えています（外周グラウト充填なら 30 m まで）。");

            return messages;
        }
    }
}
