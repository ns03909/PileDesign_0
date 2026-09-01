using System;
using System.Collections.Generic;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// KCTB 場所打ち鋼管コンクリート杭（TB工法）の本体部を、単純累加式で検討する部分。
    ///
    /// 出典: BCJ評定-FD0356-08「KCTB 場所打ち鋼管コンクリート杭」5.(3) 本体部の設計法。
    /// 同評定は鋼管コンクリート部の設計を日本建築学会
    /// 「鉄骨鉄筋コンクリート構造計算規準・同解説」(2014) 4章 許容応力度に基づく設計
    /// 2節 構造各部の算定 に準拠して行うと定める。
    ///
    /// 鋼管部の許容耐力と鉄筋コンクリート部の許容耐力を累加して許容 N-M を作る方式で、
    /// ファイバーモデル（ジャパンパイル Technical Note Vol.1-5）とは択一である。
    /// 置き換えるのは許容時（使用限界・損傷限界）のみで、安全限界・M-φ・解析には影響しない。
    /// </summary>
    internal partial class InsituSteelPipeReinforcedConcreteSection
    {
        // 鉄筋コンクリート部（コンクリート＋主筋、鋼管を含まない）の許容耐力算定用の断面。
        // 評定書の rN・rM は「鉄筋コンクリート構造計算規準・同解説」(2018) によるとされているので、
        // 新たな積分器は書かず、既存の場所打ち鉄筋コンクリート杭断面をそのまま使う。
        // 単純累加を選んだときだけ作る（構築コストが小さくないため遅延生成）。
        private InsituReinforcedConcreteSection? _rcPartForSuperposition;

        private InsituReinforcedConcreteSection RcPartForSuperposition =>
            // 材料オプション（鉄筋 1.1F）は本断面のコンストラクタで MainBars インスタンスに
            // 転写済みなので、ここで再適用しない（applyBodyMaterialOptions: false）。
            _rcPartForSuperposition ??= new InsituReinforcedConcreteSection(
                InsituConcrete, MainBars, applyBodyMaterialOptions: false);

        /// <summary>単純累加で許容 N-M を作るか（評定書 5.(3) の本体部の設計法）。</summary>
        private bool UseSuperposition => _superposedAllowableNM;

        internal override (List<double>, List<double>, List<double>, List<double>) GetServiceLimitMNInteraction()
            => UseSuperposition
                ? GetSuperposedAllowableMNInteraction(limitStateNo: 0)
                : base.GetServiceLimitMNInteraction();

        internal override (List<double>, List<double>, List<double>, List<double>) GetDamageLimitMNInteraction()
            => UseSuperposition
                ? GetSuperposedAllowableMNInteraction(limitStateNo: 1)
                : base.GetDamageLimitMNInteraction();

        /// <summary>
        /// 単純累加による許容 N-M 相関を返す（limitStateNo 0=使用限界/長期、1=損傷限界/短期）。
        ///
        /// 評定書 5.(3) 2) の記号と式:
        ///   (1) rNt ≦ N ≦ rNc または M ≧ sM0 : N = rN,  M ≦ sM0 + rM
        ///   (2) N &gt; rNc または M &lt; sM0      : N ≦ rNc + sN,  M = sM
        ///   (3) N &lt; rNt または引張で M &lt; sM0 : N ≧ rNt + sN,  M = sM
        ///   (7) sM0 = sZ·sft
        ///   (8) sN が圧縮:  sN/sA + sM/sZ = sfc
        ///   (9) sN が引張:  sN/sA − sM/sZ = −sft
        ///   (10) rNc = min(rNc1, rNc2)  (11) rNc1 = Ae·fc  (12) rNc2 = Ae·mfc/n  (13) rNt = −mA·mft
        ///
        /// 包絡線として N を掃引し、各 N での許容曲げ M を返す。
        ///   N &gt; rNc       : sN = N − rNc,  M = sZ·(sfc − sN/sA)
        ///   rNt ≦ N ≦ rNc : M = sM0 + rM(N)
        ///   N &lt; rNt       : sN = N − rNt,  M = sZ·(sft + sN/sA)
        /// sfc = sft のため rNc・rNt の両境界で M = sM0 となり、曲線は連続する
        /// （rM は各端で 0 に落ちるため）。
        ///
        /// 単位は他の N-M 相関と同じ N・N·mm。
        /// 第3・第4要素（圧縮縁ひずみ εc・曲率 φ）は累加式では定義されないため 0 を入れる
        /// （既製杭の許容応力度式と同じ扱い。N-M グラフのクリック→断面応答表示は
        /// GetElasticSectionProps() の弾性復元経路に載る）。
        /// </summary>
        private (List<double>, List<double>, List<double>, List<double>)
            GetSuperposedAllowableMNInteraction(int limitStateNo, int division = 200)
        {
            var ns = new List<double>(division + 1);
            var ms = new List<double>(division + 1);
            var epsilonCs = new List<double>(division + 1);
            var curvatures = new List<double>(division + 1);

            try
            {
                bool longTerm = limitStateNo == 0;

                // ── 鋼管部 ──
                double sA = InsituSteelPipe.AMinus;                                  // 腐食考慮の鋼管断面積
                double rOuter = InsituSteelPipe.OutDiaMinus * 0.5;
                double sZ = rOuter > 1e-9 ? InsituSteelPipe.IMinus / rOuter : 0.0;   // 鋼管の断面係数
                // 鋼管の許容圧縮・許容引張応力度。長期 F/1.5、短期 F（InsituSteelPipe.SetAllowableStrain と同値）。
                // 杭体は充填コンクリートと地盤に拘束されるため、座屈による低減は考慮しない。
                double sf = longTerm ? InsituSteelPipe.F / 1.5 : InsituSteelPipe.F;
                double sM0 = sZ * sf;                                                // (7)

                // ── 鉄筋コンクリート部 ──
                double n = InsituConcrete.Ec > 1e-9 ? MainBars.Er / InsituConcrete.Ec : 0.0;  // ヤング係数比
                // Ae: コンクリート断面に主筋断面積を n 倍して加算した換算断面積。
                // 本クラスのフィールド Ae は鋼管項 ns·AMinus を含む別物なので流用できない。
                double aeRc = InsituConcrete.Ac + (n - 1.0) * MainBars.Ag;
                // コンクリート・主筋の許容応力度は、限界ひずみ × ヤング係数として既存定義から取り出す
                // （告示1113(第8) オプションの分岐もそのまま効く）。
                double fc = (longTerm ? InsituConcrete.ServiceLimitStrainC : InsituConcrete.DamageLimitStrainC)
                            * InsituConcrete.Ec;
                double mfc = (longTerm ? MainBars.ServiceLimitStrainC : MainBars.DamageLimitStrainC) * MainBars.Er;
                double mft = -(longTerm ? MainBars.ServiceLimitStrainT : MainBars.DamageLimitStrainT) * MainBars.Er;

                double rNc1 = aeRc * fc;                                             // (11)
                double rNc2 = n > 1e-9 ? aeRc * mfc / n : double.MaxValue;           // (12)
                double rNc = Math.Min(rNc1, rNc2);                                   // (10)
                double rNt = -MainBars.Ag * mft;                                     // (13)

                // 鉄筋コンクリート部の許容 N-M（rN, rM）
                var rcNM = limitStateNo == 0
                    ? RcPartForSuperposition.UnfactoredServiceNM
                    : RcPartForSuperposition.UnfactoredDamageNM;

                // 掃引範囲: 純引張端（M=0）から純圧縮端（M=0）まで
                double nMin = rNt - sA * sf;
                double nMax = rNc + sA * sf;
                if (!(nMax > nMin))
                    return (ns, ms, epsilonCs, curvatures);

                for (int i = 0; i <= division; i++)
                {
                    double nTarget = nMin + (nMax - nMin) * i / division;

                    double m;
                    if (nTarget > rNc)
                    {
                        double sN = nTarget - rNc;                                   // 鋼管が負担する圧縮
                        m = sA > 1e-9 ? sZ * (sf - sN / sA) : 0.0;                   // (2)(8)
                    }
                    else if (nTarget < rNt)
                    {
                        double sN = nTarget - rNt;                                   // 鋼管が負担する引張（負）
                        m = sA > 1e-9 ? sZ * (sf + sN / sA) : 0.0;                   // (3)(9)
                    }
                    else
                    {
                        m = sM0 + InterpolateMaxMomentAtN(rcNM.Item1, rcNM.Item2, nTarget);  // (1)
                    }

                    ns.Add(nTarget);
                    ms.Add(Math.Max(0.0, m));
                    epsilonCs.Add(0.0);
                    curvatures.Add(0.0);
                }

                return (ns, ms, epsilonCs, curvatures);
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report(
                    "単純累加による許容N-Mの算定（→空）", ex,
                    $"場所打ち鋼管コンクリート杭, 限界状態={limitStateNo}");
                return (ns, ms, epsilonCs, curvatures);
            }
        }

        /// <summary>
        /// N-M 曲線上で軸力 nTarget に対応する曲げモーメントの最大値を線形補間で返す。
        /// 曲線はピークの両側で同じ N に 2 つの解を持ちうるため、交差する全区間のうち最大を採る。
        /// 交差が無い（軸力が耐力範囲外）ときは 0。
        /// </summary>
        private static double InterpolateMaxMomentAtN(List<double> ns, List<double> ms, double nTarget)
        {
            double best = 0.0;
            for (int i = 0; i < ns.Count - 1; i++)
            {
                double n0 = ns[i], n1 = ns[i + 1];
                if ((n0 - nTarget) * (n1 - nTarget) > 0) continue;   // この区間を跨がない

                double denom = n1 - n0;
                double m = Math.Abs(denom) < 1e-10
                    ? Math.Max(ms[i], ms[i + 1])
                    : ms[i] + (nTarget - n0) / denom * (ms[i + 1] - ms[i]);

                if (m > best) best = m;
            }
            return best;
        }
    }
}
