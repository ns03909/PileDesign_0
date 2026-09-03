using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// せん断耐力 Q-N 曲線が軸力に依存するか否かを、断面タイプ × 限界状態の表として固定する。
    ///
    /// 「掃引はしているが掃引した N が耐力側に渡っていない」という誤りは、
    /// 曲線が水平線になるだけでビルドもテストも通り、圧縮側と引張側で安全側・危険側の
    /// 両方にずれる。実際 PHC・PRC の斜めひび割れは σ0e = N/Ae を渡し忘れて長らく水平線だった。
    /// 逆に「本来 N に依存しない式に軸力が混じる」誤りも同じく静かに通る。
    /// そこで<b>依存するはずのものが水平でないこと</b>と
    /// <b>依存しないはずのものが厳密に水平であること</b>の両方をここで固定する。
    ///
    /// 断面タイプを増やしたら <see cref="Table"/> に行を足すこと。
    /// 足さないと <see cref="Table_CoversEveryPileSectionType"/> が落ちる。
    /// </summary>
    [TestClass]
    public class ShearAxialDependenceTableTests
    {
        /// <summary>軸力依存の期待値。</summary>
        private enum Dep
        {
            /// <summary>N によって変わる（水平線なら誤り）。</summary>
            Varies,

            /// <summary>N によらない（変化していたら誤り）。</summary>
            Constant,
        }

        /// <summary>断面耐力の計算系統 1 つ分の期待値。</summary>
        private sealed record Row(
            string Family,
            Func<PileSection?> Build,
            Dep Service,
            Dep Damage,
            Dep Ultimate,
            string Why);

        // ─── 表本体 ───
        //
        // 「依存」の根拠は式に軸力項があること、「非依存」の根拠は式が軸力を含まないこと。
        // 非依存の行はいずれも「せん断は鋼管が負担する」型で、安全限界だけ軸力比が入る。
        private static readonly Row[] Table =
        [
            new(PileTypeNames.RcSection, CreateInsituRcSection,
                Dep.Varies, Dep.Varies, Dep.Varies,
                "3 式とも (1 + σ0/14.7) または 0.1·σ0 の形で平均軸応力度を含む"),

            new(PileTypeNames.SteelPipeConcreteSection, CreateSprcSection,
                Dep.Constant, Dep.Constant, Dep.Varies,
                "使用・損傷は鋼管の許容せん断 sfsd·A/κ のみ。安全限界だけ全塑性軸力比 p が入る"),

            new(PileTypeNames.Phc, () => CreatePrecastSection(PileTypeNames.Phc, PileSection.PHCs, "400"),
                Dep.Varies, Dep.Varies, Dep.Varies,
                "斜めひび割れの σG = σe + σ0e に軸力が入る（縦ひび割れ側は非依存）"),

            new(PileTypeNames.Prc, () => CreatePrecastSection(PileTypeNames.Prc, PileSection.PRCs, "400"),
                Dep.Varies, Dep.Varies, Dep.Varies,
                "PHC と同一の 2 式。斜めひび割れに σ0e が入る"),

            new(PileTypeNames.Sc, () => CreatePrecastSection(PileTypeNames.Sc, PileSection.SCs, "-500-"),
                Dep.Constant, Dep.Constant, Dep.Varies,
                "せん断は鋼管が負担する扱いで、使用・損傷にコンクリートの σG が入らない。"
                + "安全限界だけ η = N/Ny が入る"),

            new(PileTypeNames.SteelPipeSection, () => CreateSteelPipeSection(PileTypeNames.SteelPipeSection),
                Dep.Constant, Dep.Constant, Dep.Varies,
                "使用・損傷は fs·A（Qd = 1.5·Qs）。安全限界は √(1 − η²)"),

            new(PileTypeNames.CftSection, () => CreateSteelPipeSection(PileTypeNames.CftSection),
                Dep.Constant, Dep.Constant, Dep.Varies,
                "鋼管部と同じ。安全限界は √(1 − η²) に Mu/sMu 比を乗じる"),
        ];

        // ─── 断面ビルダー ───

        /// <summary>他のテストからも使う場所打ちRC断面 (限界曲線の出所テスト)。</summary>
        internal static PileSection CreateInsituRcSectionForCurveTests() => CreateInsituRcSection();

        private static PileSection CreateInsituRcSection() => new()
        {
            PileBodyType = PileTypeNames.InsituRc,
            PileSectionType = PileTypeNames.RcSection,
            ConcreteOutDia = 1500.0,
            ConcreteFc = 27.0,
            ConcreteGsi = 1.0,
            MainBarNum = 30,
            MainBarSize = "D29",
            MainBarSpec = "SD390",
            MainBarDr = 200.0,
            HoopSize = "D13",
            HoopSpacing = 150.0,
            HoopSpec = "SD295",
            HoopCenterCover = 150.0,
            PileDiameter = 1500.0,
        };

        private static PileSection CreateSprcSection() => new()
        {
            PileBodyType = PileTypeNames.InsituSteelPipeConcrete,
            PileSectionType = PileTypeNames.SteelPipeConcreteSection,
            PipeGrade = "SKK400",
            PipeDia = 1000.0,
            PipeTs = 12.0,
            CorrosionDepth = 1.0,
            ConcreteOutDia = 1000.0,
            ConcreteGsi = 1.0,
            ConcreteFc = 27.0,
            MainBarNum = 20,
            MainBarSize = "D25",
            MainBarSpec = "SD390",
            MainBarDr = 150.0,
            HoopSize = "D13",
            HoopSpacing = 150.0,
            HoopSpec = "SD295",
            HoopCenterCover = 150.0,
            PileDiameter = 1000.0,
        };

        private static PileSection CreateSteelPipeSection(string sectionType) => new()
        {
            PileBodyType = PileTypeNames.SteelPipe,
            PileSectionType = sectionType,
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

        /// <summary>ライブラリ CSV から製品を選んで既製杭断面を作る。CSV が読めない環境では null。</summary>
        private static PileSection? CreatePrecastSection(
            string sectionType, List<PrecastPile>? lib, string preferName)
        {
            if (lib == null || lib.Count == 0) return null;
            var product = lib.Find(p => p.Name != null && p.Name.Contains(preferName)) ?? lib[0];
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = sectionType,
            };
            s.SetSelectedPrecastPileByName(product.Name);
            return s;
        }

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.RebarYieldAt11F = false;
            ConcreteModelOptions.SteelPipeYieldAt11F = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete = false;
            ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = false;
            ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = true;
            ConcreteModelOptions.UseReducedCompression = false;
        }

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        // ─── 判定 ───

        /// <summary>
        /// 曲線 1 本を期待値と突き合わせる。
        /// 「変わる」は 1% 以上の振れ幅を要求する（丸め程度の差で水平を見逃さないため）。
        /// 「変わらない」は相対 1e-9 で厳密に一致することを要求する。
        /// </summary>
        private static void AssertDependence(
            string family, string limitState, Dep expected,
            (List<double> N, List<double> Q) curve, string why)
        {
            string where = $"{family} {limitState}";
            Assert.IsNotNull(curve.Q, $"{where}: Q が null");
            Assert.IsTrue(curve.Q.Count >= 2, $"{where}: 点が 2 未満（曲線が作られていない）");
            Assert.AreEqual(curve.N.Count, curve.Q.Count, $"{where}: N と Q の点数不一致");

            for (int i = 0; i < curve.Q.Count; i++)
            {
                Assert.IsFalse(double.IsNaN(curve.Q[i]) || double.IsInfinity(curve.Q[i]),
                    $"{where}: Q[{i}] が非有限");
            }

            // 低減後の曲線には、せん断の軸力制限の外側に Q=0 の点が挿入される
            // （図で垂直に落とすため）。これは「範囲外」の印であって軸力依存ではないので、
            // 依存の判定からは外す。
            var inRange = curve.Q.Where(q => q > 0.0).ToList();
            Assert.IsTrue(inRange.Count >= 2,
                $"{where}: 軸力制限の内側に点が 2 未満（曲線が作られていない）");

            double min = inRange.Min(), max = inRange.Max();
            Assert.IsTrue(max > 0, $"{where}: せん断耐力が全点ゼロ");
            double span = (max - min) / max;

            if (expected == Dep.Varies)
            {
                Assert.IsTrue(span > 0.01,
                    $"{where}: 軸力で変わるはずが水平線になっている "
                    + $"(N={curve.N[0]:F0}→{curve.N[^1]:F0} kN で Q={min:F1}〜{max:F1} kN)。"
                    + $"根拠: {why}。掃引した N が耐力の算定まで渡っているか確認すること。");
            }
            else
            {
                Assert.IsTrue(span <= 1e-9,
                    $"{where}: 軸力に依存しないはずが変化している (Q={min:F3}〜{max:F3} kN)。"
                    + $"根拠: {why}。");
            }
        }

        // ─── 本体 ───

        [TestMethod]
        public void ShearQN_AxialDependence_MatchesTable()
        {
            ResetOptions();
            var skipped = new List<string>();

            foreach (var row in Table)
            {
                var s = row.Build();
                if (s == null)
                {
                    skipped.Add(row.Family);   // 既製杭ライブラリ CSV が読めない環境
                    continue;
                }

                AssertDependence(row.Family, "使用限界", row.Service, s.UnfactoredServiceNQ, row.Why);
                AssertDependence(row.Family, "損傷限界", row.Damage, s.UnfactoredDamageNQ, row.Why);
                AssertDependence(row.Family, "安全限界", row.Ultimate, s.UnfactoredUltimateNQ, row.Why);
            }

            if (skipped.Count == Table.Length)
                Assert.Inconclusive($"全断面を構築できなかった: {string.Join(", ", skipped)}");
        }

        /// <summary>
        /// 低減後の曲線も低減前と同じ依存関係であること。
        /// 低減係数 β は軸力の関数ではない（場所打ちRC の β2 だけは σ0 の閾値で切り替わるが、
        /// 低減前が既に軸力依存なので依存の有無は変わらない）。
        /// </summary>
        [TestMethod]
        public void ShearQN_AxialDependence_SameForFactoredCurves()
        {
            ResetOptions();

            foreach (var row in Table)
            {
                var s = row.Build();
                if (s == null) continue;

                AssertDependence(row.Family, "使用限界(低減後)", row.Service, s.FactoredServiceNQ, row.Why);
                AssertDependence(row.Family, "損傷限界(低減後)", row.Damage, s.FactoredDamageNQ, row.Why);
                AssertDependence(row.Family, "安全限界(低減後)", row.Ultimate, s.FactoredUltimateNQ, row.Why);
            }
        }

        /// <summary>
        /// 告示1113(第8) のせん断を選ぶと、場所打ちRC の使用・損傷限界は
        /// 許容せん断応力度 × b·j になり<b>軸力非依存</b>に変わる（安全限界は告示の対象外）。
        /// オプションで依存関係が変わるのはここだけなので、切り替わること自体を固定する。
        /// </summary>
        [TestMethod]
        public void ShearQN_InsituRc_BecomesAxialIndependentUnderNotification1113()
        {
            ResetOptions();
            ConcreteModelOptions.UseNotification1113Shear = true;

            var s = CreateInsituRcSection();
            AssertDependence(PileTypeNames.RcSection, "使用限界(告示1113)", Dep.Constant,
                s.UnfactoredServiceNQ, "告示1113(第8) は長期許容せん断応力度 fs·b·j で軸力を含まない");
            AssertDependence(PileTypeNames.RcSection, "損傷限界(告示1113)", Dep.Constant,
                s.UnfactoredDamageNQ, "告示1113(第8) は短期＝長期の 1.5 倍で軸力を含まない");
            AssertDependence(PileTypeNames.RcSection, "安全限界(告示1113)", Dep.Varies,
                s.UnfactoredUltimateNQ, "安全限界は告示の対象外なので従来式（0.1·σ0 を含む）のまま");
        }

        /// <summary>
        /// 表がすべての断面タイプを網羅していること。
        /// 断面タイプを増やしたら、耐力計算が既存と同一でも必ずここに現れる
        /// （<see cref="PileTypeNames"/> のヘルパーで既存系統に寄せるか、表に行を足すか）。
        /// </summary>
        [TestMethod]
        public void Table_CoversEveryPileSectionType()
        {
            // PileBodyType（杭体タイプ）は断面耐力の計算系統ではないので対象外
            var bodyTypes = new HashSet<string>
            {
                PileTypeNames.InsituRc,
                PileTypeNames.InsituSteelPipeConcrete,
                PileTypeNames.PrecastConcrete,
                PileTypeNames.SteelPipe,
            };

            var sectionTypes = typeof(PileTypeNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .Where(v => !bodyTypes.Contains(v))
                .ToList();

            Assert.IsTrue(sectionTypes.Count > 0, "PileTypeNames から断面タイプを取得できていない");

            var covered = Table.Select(r => r.Family).ToHashSet();
            var missing = new List<string>();

            foreach (string t in sectionTypes)
            {
                if (covered.Contains(t)) continue;
                // 節杭・BF.S は断面耐力が PHC / PRC と同一なので、その行が代表する
                if (PileTypeNames.IsPhcLikeSection(t) && covered.Contains(PileTypeNames.Phc)) continue;
                if (PileTypeNames.IsPrcLikeSection(t) && covered.Contains(PileTypeNames.Prc)) continue;
                missing.Add(t);
            }

            Assert.AreEqual(0, missing.Count,
                $"軸力依存の表に無い断面タイプ: {string.Join(", ", missing)}。"
                + "ShearAxialDependenceTableTests.Table に行を足すこと"
                + "（耐力計算が PHC/PRC と同一なら PileTypeNames の IsPhcLikeSection/IsPrcLikeSection に加える）。");
        }
    }
}
