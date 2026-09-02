using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// KCTB 場所打ち鋼管コンクリート杭（TB工法、BCJ評定-FD0356-08）の設計法オプションの検証。
    ///
    ///  (A) 終局の圧縮縁ひずみが 3,000μ → 5,000μ になり、安全限界曲げが増える
    ///      （鋼管の拘束効果。総プロ基礎WG 最終報告書 資料4-7）。
    ///  (B) 鋼管杭のコンクリート充填鋼管部は対象外（3,000μ のまま）。
    ///  (C) 許容時の判定に鉄筋を用いない（純引張端が伸び、鉄筋が支配しない断面では不変）。
    ///  (D) 単純累加（評定 5.(3)）は許容時のみを置き換え、安全限界は動かさない。
    ///  (E) 適用範囲の検査が評定書の範囲外を検出する。
    ///
    /// なお εcu = 5,000μ と「許容時の判定に鉄筋を用いない」は BCJ評定-FD0356-08 の範囲外で、
    /// 出典は Technical Note Vol.1-5 と基礎WG 最終報告書 資料4-7。評定が定めるのは
    /// 許容応力度（告示1113(第8)）と本体部の設計法（単純累加）である。
    /// </summary>
    [TestClass]
    public class KctbDesignMethodTests
    {
        private static PileSection CreateSprcSection(string mainBarSpec = "SD390", string pipeGrade = "SKK400")
        {
            var section = new PileSection
            {
                PileBodyType = PileTypeNames.InsituSteelPipeConcrete,
                PileSectionType = PileTypeNames.SteelPipeConcreteSection,
            };
            ApplySprcSpecs(section, mainBarSpec, pipeGrade);
            return section;
        }

        /// <summary>
        /// 鋼管コンクリート部の諸元を設定する。
        /// PileBodyInput.PileBodySegments へコレクションを代入すると
        /// PileSection.ResetSectionProperties() が走って諸元が既定値に戻るため、
        /// 杭体経由で組み立てる場合は代入の「後」にこれを呼ぶこと。
        /// </summary>
        private static void ApplySprcSpecs(
            PileSection section, string mainBarSpec = "SD390", string pipeGrade = "SKK400")
        {
            section.PileSectionType = PileTypeNames.SteelPipeConcreteSection;
            section.PipeGrade = pipeGrade;
            section.PipeDia = 1000.0;
            section.PipeTs = 12.0;
            section.CorrosionDepth = 1.0;
            section.ConcreteOutDia = 1000.0;
            section.ConcreteGsi = 1.0;
            section.ConcreteFc = 27.0;
            section.MainBarNum = 20;
            section.MainBarSize = "D25";
            section.MainBarSpec = mainBarSpec;
            section.MainBarDr = 150.0;
            section.HoopSize = "D13";
            section.HoopSpacing = 150.0;
            section.HoopSpec = "SD295";
            section.HoopCenterCover = 150.0;
            section.PileDiameter = 1000.0;
        }

        /// <summary>鋼管杭のコンクリート充填鋼管部（KCTB の対象外）。</summary>
        private static PileSection CreateCftSection()
        {
            return new PileSection
            {
                PileBodyType = PileTypeNames.SteelPipe,
                PileSectionType = PileTypeNames.CftSection,
                PipeGrade = "SKK400",
                PipeDia = 1000.0,
                PipeTs = 12.0,
                CorrosionDepth = 1.0,
                ConcreteOutDia = 1000.0,
                ConcreteGsi = 1.0,
                ConcreteFc = 27.0,
                MainBarNum = 0,
                MainBarSize = "D25",
                MainBarSpec = "SD390",
                MainBarDr = 150.0,
                PileDiameter = 1000.0,
            };
        }

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete = false;
            ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = false;
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = true;
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.RebarYieldAt11F = false;
            ConcreteModelOptions.SteelPipeYieldAt11F = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
            ConcreteModelOptions.UseFiberMPhi = false;
            ConcreteModelOptions.Notification1113CompressionCase = 1;
            PileSection.ClearMphiCache();
        }

        [TestInitialize]
        public void Init() => ResetOptions();

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        /// <summary>曲線上で軸力 n に最も近い点の M を返す（単位は kN, kN·m）。</summary>
        private static double MomentAtN((List<double> N, List<double> M) curve, double n)
        {
            double best = 0.0, bestDist = double.MaxValue;
            for (int i = 0; i < curve.N.Count; i++)
            {
                double d = Math.Abs(curve.N[i] - n);
                if (d < bestDist) { bestDist = d; best = curve.M[i]; }
            }
            return best;
        }

        private static double MaxMoment((List<double> N, List<double> M) curve)
            => curve.M.Count == 0 ? 0.0 : curve.M.Max();

        // ───────────── (A) 終局ひずみ 5,000μ ─────────────

        /// <summary>
        /// KCTB ON で安全限界曲げが増える。εcu が 3,000μ → 5,000μ に伸びるため。
        /// 材料側 (InsituConcrete.EpsilonCu) も併せて広げていないと ε&gt;0.003 で σ=0 に脱落し、
        /// 逆に小さくなるので、増えることを確認する意味がある。
        /// </summary>
        [TestMethod]
        public void KctbMethod_UltimateMoment_IncreasesOverDefault()
        {
            ResetOptions();
            double off = MaxMoment(CreateSprcSection().UnfactoredUltimateNM);

            ResetOptions();
            ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete = true;
            double on = MaxMoment(CreateSprcSection().UnfactoredUltimateNM);

            Assert.IsTrue(off > 0.0, $"既定の安全限界曲げが 0 ({off})");
            Assert.IsTrue(on > off,
                $"KCTB(εcu=5,000μ) の安全限界曲げが既定(εcu=3,000μ) を上回らない: on={on:F0} off={off:F0} kN·m");
        }

        /// <summary>断面の終局圧縮縁ひずみが定数どおりであること（3,000μ / 5,000μ）。</summary>
        [TestMethod]
        public void KctbMethod_UltimateCompressiveStrain_Is5000Micro()
        {
            Assert.AreEqual(0.003, SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN, 1e-12);
            Assert.AreEqual(0.005, SectionDesignConstants.KCTB_ULTIMATE_COMPRESSIVE_STRAIN, 1e-12);
        }

        // ───────────── (B) 鋼管杭の充填鋼管部は対象外 ─────────────

        /// <summary>鋼管杭のコンクリート充填鋼管部は KCTB の対象外で、安全限界が変わらない。</summary>
        [TestMethod]
        public void KctbMethod_CftSectionOfSteelPipePile_IsUnaffected()
        {
            ResetOptions();
            double off = MaxMoment(CreateCftSection().UnfactoredUltimateNM);

            ResetOptions();
            ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete = true;
            double on = MaxMoment(CreateCftSection().UnfactoredUltimateNM);

            Assert.IsTrue(off > 0.0, $"充填鋼管部の安全限界曲げが 0 ({off})");
            Assert.AreEqual(off, on, Math.Abs(off) * 1e-9,
                "鋼管杭のコンクリート充填鋼管部は KCTB の対象外なので変わってはいけない");
        }

        // ───────────── (C) 許容時の判定に鉄筋を使わない ─────────────

        /// <summary>
        /// KCTB の許容時の判定は鋼管とコンクリートのみで行い、鉄筋は限界状態を決めない。
        ///
        /// 判定から外すと、圧縮側は εc の min から、引張側は max から鉄筋の項が消えるため、
        /// 断面はより深いひずみまで進める。よって純引張端の軸力はより負側へ伸びる。
        ///
        /// 主筋が既定で支配する組合せを選ぶ必要がある。鋼管 SKK490 (εsy = 315/205,000 = 0.001537) と
        /// 主筋 SD295 (εry = 295/205,000 = 0.001439) なら、引張側で主筋が先に限界に達する。
        /// SKK400 (εsy = 0.001146) では鋼管の方が先に効くため対照にならない。
        ///
        /// なお鉄筋の応力度は判定と無関係に σry 頭打ちのバイリニアで積分に参入し続けるため
        /// （Technical Note Vol.1-5 図4）、曲線が鉄筋の規格に依存しなくなるわけではない。
        /// </summary>
        [TestMethod]
        public void ExcludeRebarOption_RebarDoesNotGovernAllowableLimit()
        {
            ResetOptions();
            var off = CreateSprcSection("SD295", "SKK490").UnfactoredDamageNM;

            ResetOptions();
            ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = true;
            var on = CreateSprcSection("SD295", "SKK490").UnfactoredDamageNM;

            Assert.IsTrue(CountDifferingPoints(off, on) > 0,
                "主筋を判定から外しても曲線が変わっていない（対照条件が成立していない）");
            Assert.IsTrue(on.N.Min() < off.N.Min() - 1e-6,
                $"主筋を判定から外したのに純引張端が伸びていない: on={on.N.Min():F1} off={off.N.Min():F1} kN");
        }

        /// <summary>
        /// 主筋が既定でも支配しない組合せ（SD490 + SKK490）では、KCTB の判定変更で
        /// 許容時 N-M が 1 点も動かない。判定から外したこと以外に副作用がないことの確認。
        /// </summary>
        [TestMethod]
        public void ExcludeRebarOption_WhenRebarNeverGoverns_AllowableNMIsUnchanged()
        {
            ResetOptions();
            var off = CreateSprcSection("SD490", "SKK490").UnfactoredDamageNM;

            ResetOptions();
            ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = true;
            var on = CreateSprcSection("SD490", "SKK490").UnfactoredDamageNM;

            Assert.IsTrue(MaxMoment(off) > 0.0, "許容時曲げが 0");
            Assert.AreEqual(0, CountDifferingPoints(off, on),
                "主筋が支配しない断面なのに KCTB で許容時 N-M が動いた（判定以外に副作用がある）");
        }

        /// <summary>2 本の N-M 曲線で相対差が 1e-9 を超える点の数を返す。</summary>
        private static int CountDifferingPoints(
            (List<double> N, List<double> M) a, (List<double> N, List<double> M) b)
        {
            int count = 0;
            int n = Math.Min(a.N.Count, b.N.Count);
            double scaleN = Math.Max(1.0, a.N.Select(Math.Abs).DefaultIfEmpty(0).Max());
            double scaleM = Math.Max(1.0, a.M.Select(Math.Abs).DefaultIfEmpty(0).Max());
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(a.N[i] - b.N[i]) > scaleN * 1e-9 ||
                    Math.Abs(a.M[i] - b.M[i]) > scaleM * 1e-9)
                    count++;
            }
            return count;
        }

        // ───────────── (D) 単純累加 ─────────────

        /// <summary>単純累加は許容時（使用・損傷限界）だけを置き換え、安全限界は動かさない。</summary>
        [TestMethod]
        public void KctbSuperposition_ReplacesAllowableOnly_UltimateUnchanged()
        {
            ResetOptions();
            var fiberDamage = CreateSprcSection().UnfactoredDamageNM;
            double fiberUltimate = MaxMoment(CreateSprcSection().UnfactoredUltimateNM);

            ResetOptions();
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = false;
            var superDamage = CreateSprcSection().UnfactoredDamageNM;
            double superUltimate = MaxMoment(CreateSprcSection().UnfactoredUltimateNM);

            Assert.IsTrue(MaxMoment(superDamage) > 0.0, "単純累加の許容時曲げが 0");
            Assert.AreNotEqual(MaxMoment(fiberDamage), MaxMoment(superDamage),
                "単純累加とファイバーで許容時 N-M が一致してしまっている");
            Assert.AreEqual(fiberUltimate, superUltimate, Math.Abs(fiberUltimate) * 1e-9,
                "単純累加は安全限界に影響してはいけない");
        }

        /// <summary>
        /// 単純累加は場所打ち鋼管コンクリート杭だけの設計法で、
        /// 鋼管杭のコンクリート充填鋼管部には効かない（評定の対象外）。
        /// </summary>
        [TestMethod]
        public void KctbSuperposition_CftSectionOfSteelPipePile_IsUnaffected()
        {
            ResetOptions();
            double plain = MaxMoment(CreateCftSection().UnfactoredDamageNM);

            ResetOptions();
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = false;
            double withFlag = MaxMoment(CreateCftSection().UnfactoredDamageNM);

            Assert.IsTrue(plain > 0.0, "充填鋼管部の許容時曲げが 0");
            Assert.AreEqual(plain, withFlag, Math.Abs(plain) * 1e-9,
                "鋼管杭のコンクリート充填鋼管部に単純累加が効いてはいけない");
        }

        /// <summary>
        /// 単純累加の N-M は N について単調に並び、両端で M が 0 に落ちる（純引張端・純圧縮端）。
        /// 評定書の (1)〜(3) が rNc・rNt の両境界で M = sM0 となり連続することも、
        /// 曲線に不連続な跳びが無いことで確認する。
        /// </summary>
        [TestMethod]
        public void KctbSuperposition_Envelope_IsContinuousAndClosesAtBothEnds()
        {
            ResetOptions();
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = false;
            var (ns, ms) = CreateSprcSection().UnfactoredDamageNM;

            Assert.IsTrue(ns.Count > 10, "曲線の点数が少なすぎる");
            for (int i = 1; i < ns.Count; i++)
                Assert.IsTrue(ns[i] >= ns[i - 1], $"軸力が単調に増加していない (i={i})");

            double maxM = ms.Max();
            Assert.IsTrue(maxM > 0.0, "許容時曲げが 0");
            Assert.AreEqual(0.0, ms[0], maxM * 1e-6, "純引張端で M が 0 にならない");
            Assert.AreEqual(0.0, ms[^1], maxM * 1e-6, "純圧縮端で M が 0 にならない");

            // 隣接点の跳びが曲線全体の 10% を超えないこと（領域境界での不連続の検出）
            for (int i = 1; i < ms.Count; i++)
                Assert.IsTrue(Math.Abs(ms[i] - ms[i - 1]) < maxM * 0.1,
                    $"曲線に不連続な跳びがある (i={i}, ΔM={Math.Abs(ms[i] - ms[i - 1]):F1} kN·m)");
        }

        /// <summary>損傷限界（短期）の許容時曲げは使用限界（長期）を上回る。</summary>
        [TestMethod]
        public void KctbSuperposition_ShortTermExceedsLongTerm()
        {
            ResetOptions();
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = false;
            var section = CreateSprcSection();

            double longTerm = MaxMoment(section.UnfactoredServiceNM);
            double shortTerm = MaxMoment(section.UnfactoredDamageNM);

            Assert.IsTrue(longTerm > 0.0, "長期の許容時曲げが 0");
            Assert.IsTrue(shortTerm > longTerm,
                $"短期({shortTerm:F0}) が長期({longTerm:F0}) を上回らない");
        }

        /// <summary>
        /// 単純累加を選んだとき、長期（使用限界）・短期（損傷限界）とも
        /// <b>検定が実際に読む曲線</b>が単純累加の包絡線になっていること。
        ///
        /// 検定は Factored 側 (EvaluationService.GetNMCurve) を読む。低減前だけ差し替えて
        /// 低減後に伝わっていないと、画面のグラフだけ単純累加で検定は従来のまま、という
        /// 食い違いが静かに起きる。場所打ち鋼管コンクリート杭は使用・損傷限界とも
        /// 軸力閾値が空・β=1.0 なので、低減後は低減前と一致するのが正しい。
        /// </summary>
        [TestMethod]
        public void KctbSuperposition_FactoredCurvesUsedByEvaluation_AreSuperposed()
        {
            ResetOptions();
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = false;
            var s = CreateSprcSection();

            var svc = s.UnfactoredServiceNM;
            var dmg = s.UnfactoredDamageNM;

            Assert.IsTrue(MaxMoment(svc) > 0.0, "長期の許容時曲げが 0");
            Assert.IsTrue(MaxMoment(dmg) > 0.0, "短期の許容時曲げが 0");

            // 長期（使用限界）
            Assert.AreEqual(0, CountDifferingPoints(svc, s.FactoredServiceNM),
                "長期: 低減後の曲線が単純累加になっていない");
            // 短期（損傷限界）はレベル 1 / 2 とも
            Assert.AreEqual(0, CountDifferingPoints(dmg, s.GetFactoredDamageNM(1)),
                "短期(L1): 低減後の曲線が単純累加になっていない");
            Assert.AreEqual(0, CountDifferingPoints(dmg, s.GetFactoredDamageNM(2)),
                "短期(L2): 低減後の曲線が単純累加になっていない");

            // 断面分割積分のときとは別物であること（対照）
            ResetOptions();
            var fiber = CreateSprcSection();
            Assert.IsTrue(CountDifferingPoints(fiber.FactoredDamageNM, dmg) > 0,
                "単純累加と断面分割積分で短期の曲線が一致してしまっている");
        }

        // ───────────── (E) 適用範囲の検査 ─────────────

        /// <summary>
        /// 検査対象の杭体を 1 区間で作る。
        /// PileBodySegments へコレクションを代入すると各断面の ResetSectionProperties() が走るため、
        /// 断面の諸元は代入の「後」に設定し、必要な変更は configure で重ねる。
        /// </summary>
        private static PileBodyInput CreatePileBody(
            double segmentLength = 10.0, Action<PileSection>? configure = null)
        {
            var body = new PileBodyInput
            {
                PileBodyType = PileTypeNames.InsituSteelPipeConcrete,
                PileBodySegments = new ObservableCollection<PileBodySegment>
                {
                    new() { No = 1, SegmentLength = segmentLength, PileSection = new PileSection() },
                },
            };
            var section = body.PileBodySegments[0].PileSection;
            ApplySprcSpecs(section);
            configure?.Invoke(section);
            return body;
        }

        /// <summary>評定書の適用範囲に収まる断面では警告が出ない。</summary>
        [TestMethod]
        public void ApplicableRange_ValidSection_ReportsNothing()
        {
            var messages = KctbApplicableRange.Validate(CreatePileBody()).ToList();
            Assert.AreEqual(0, messages.Count,
                "適用範囲内なのに警告が出た: " + string.Join(" / ", messages));
        }

        /// <summary>φ2600 で SKK400 は使えない（表 1.1 の注）。</summary>
        [TestMethod]
        public void ApplicableRange_LargeDiameterWithSkk400_IsReported()
        {
            var body = CreatePileBody(configure: s =>
            {
                s.PipeDia = 2600.0;
                s.PipeTs = 16.0;
                s.PipeGrade = "SKK400";
            });

            var messages = KctbApplicableRange.Validate(body).ToList();

            Assert.IsTrue(messages.Any(m => m.Contains("SKK490")),
                "φ2600 は SKK490 のみである旨の警告が無い: " + string.Join(" / ", messages));
        }

        /// <summary>外径に対する板厚の下限（表 1.4）を下回ると警告する。</summary>
        [TestMethod]
        public void ApplicableRange_ThicknessBelowMinimum_IsReported()
        {
            // φ2400 の板厚下限は 12mm
            var body = CreatePileBody(configure: s =>
            {
                s.PipeDia = 2400.0;
                s.PipeTs = 9.0;
                s.PipeGrade = "SKK490";
            });

            var messages = KctbApplicableRange.Validate(body).ToList();

            Assert.IsTrue(messages.Any(m => m.Contains("板厚の下限")),
                "板厚下限の警告が無い: " + string.Join(" / ", messages));
        }

        /// <summary>適用範囲外の外径・腐食しろ・Fc を検出する。</summary>
        [TestMethod]
        public void ApplicableRange_OutOfRangeValues_AreReported()
        {
            var body = CreatePileBody(configure: s =>
            {
                s.PipeDia = 600.0;        // φ700 未満
                s.CorrosionDepth = 2.0;   // 評定は 1mm
                s.ConcreteFc = 50.0;      // 18〜45 の外
            });

            var messages = KctbApplicableRange.Validate(body).ToList();

            Assert.IsTrue(messages.Any(m => m.Contains("鋼管外径")),
                "外径の警告が無い: " + string.Join(" / ", messages));
            Assert.IsTrue(messages.Any(m => m.Contains("腐食しろ")),
                "腐食しろの警告が無い: " + string.Join(" / ", messages));
            Assert.IsTrue(messages.Any(m => m.Contains("設計基準強度")),
                "Fc の警告が無い: " + string.Join(" / ", messages));
        }

        /// <summary>鋼管長の上限（表 1.6）を超えると警告する。</summary>
        [TestMethod]
        public void ApplicableRange_PipeLengthOverLimits_AreReported()
        {
            var overflow = KctbApplicableRange.Validate(CreatePileBody(segmentLength: 20.0)).ToList();
            Assert.IsTrue(overflow.Any(m => m.Contains("オーバーフロー")),
                "12.5m 超の注意が無い: " + string.Join(" / ", overflow));

            var grout = KctbApplicableRange.Validate(CreatePileBody(segmentLength: 35.0)).ToList();
            Assert.IsTrue(grout.Any(m => m.Contains("上限 30 m")),
                "30m 超の警告が無い: " + string.Join(" / ", grout));
        }

        /// <summary>場所打ち鋼管コンクリート杭でない杭体は検査対象外。</summary>
        [TestMethod]
        public void ApplicableRange_OtherPileBodyType_IsSkipped()
        {
            var body = CreatePileBody(configure: s => s.PipeDia = 600.0);
            body.PileBodyType = PileTypeNames.InsituRc;

            Assert.AreEqual(0, KctbApplicableRange.Validate(body).Count(),
                "場所打ち鋼管コンクリート杭以外は検査しない");
        }

        // ───────────── 保存・復元 ─────────────

        /// <summary>
        /// 3 つのオプションがプロジェクトファイルに保存され、読み込みで復元されること。
        ///
        /// System.Text.Json + ReferenceHandler.Preserve という保存形式には
        /// 「get のみのプロパティは書き出されるが復元されない」等の落とし穴があるので、
        /// 新しい設定を足したら往復を実際に確かめる。
        /// UseFiberNMForSteelPipeConcrete は既定 true なので、false で保存して
        /// false のまま戻ることを見る（既定値へ巻き戻ってしまわないか）。
        /// </summary>
        [TestMethod]
        public void Options_SurviveSaveAndLoad()
        {
            // new InputModel() は FundamentalInput を作らないので用意する
            var model = new InputModel { FundamentalInput = new FundamentalInput() };
            model.FundamentalInput.UseUltimateStrain5000ForSteelPipeConcrete = true;
            model.FundamentalInput.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = true;
            model.FundamentalInput.UseFiberNMForSteelPipeConcrete = false;   // 単純累加

            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            };
            string json = System.Text.Json.JsonSerializer.Serialize(model, options);
            var restored = System.Text.Json.JsonSerializer.Deserialize<InputModel>(json, options);

            Assert.IsNotNull(restored, "読み込みに失敗した");
            Assert.IsTrue(restored!.FundamentalInput.UseUltimateStrain5000ForSteelPipeConcrete,
                "終局ひずみ 5,000μ の設定が復元されない");
            Assert.IsTrue(restored.FundamentalInput.ExcludeRebarFromAllowableLimitForSteelPipeConcrete,
                "許容時の判定材料の設定が復元されない");
            Assert.IsFalse(restored.FundamentalInput.UseFiberNMForSteelPipeConcrete,
                "本体部の設計法（単純累加）の設定が復元されない（既定 true へ巻き戻っている）");
        }

        /// <summary>
        /// 設定を持たない古いファイルを読んでも、従来どおりの挙動になること。
        /// UseFiberNMForSteelPipeConcrete の既定は true（断面分割積分）で、
        /// キーが無いファイルを開いただけで検定値が変わってはいけない。
        /// </summary>
        [TestMethod]
        public void Options_AbsentInOldFile_KeepPreviousBehaviour()
        {
            var f = new FundamentalInput();
            Assert.IsFalse(f.UseUltimateStrain5000ForSteelPipeConcrete, "終局ひずみは既定 3,000μ のはず");
            Assert.IsFalse(f.ExcludeRebarFromAllowableLimitForSteelPipeConcrete, "判定材料は既定で全材料のはず");
            Assert.IsTrue(f.UseFiberNMForSteelPipeConcrete, "本体部の設計法は既定で断面分割積分のはず");
        }

        // ───────────── オプションの記録 ─────────────

        /// <summary>計算書「計算条件・仮定」に KCTB の選択が出ること。</summary>
        [TestMethod]
        public void MaterialOptionRows_Kctb_ShowsChoices()
        {
            ResetOptions();
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = false;
            ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete = true;
            ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = true;

            var rows = PileDesign.Output.WordDocument.BuildMaterialOptionRows();

            Assert.AreEqual("単純累加", rows.Single(r => r.Item.Contains("本体部の設計法")).Choice);

            var ecuRow = rows.Single(r => r.Item.Contains("終局の圧縮縁ひずみ"));
            Assert.AreEqual("5,000μ", ecuRow.Choice);
            Assert.IsTrue(ecuRow.Note.Contains("規定は無い"), "評定書に規定が無い旨の記載が無い");

            Assert.AreEqual("コンクリートと鋼管",
                rows.Single(r => r.Item.Contains("判定材料")).Choice);
        }
    }
}
