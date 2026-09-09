using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 既製杭のせん断 Q-N 曲線を数値で固定する。
    ///
    /// PHC と PRC は、限界状態ごとのせん断耐力と Q-N の組み立てが<b>文字単位で同じ</b>
    /// コードを 2 か所に持っていた (せん断 3 本・Q-N 3 本・プレストレスひずみ 2 本の
    /// 計 8 メソッド、79 行)。片方だけ直すと静かに食い違うため基底クラスへ引き上げたが、
    /// 引き上げは数値を動かしてはいけない。
    ///
    /// SC 杭だけは軸力の範囲もせん断耐力の式も違うので、基底クラスの版を上書きしている。
    /// ここを取り違えると、鋼管のせん断降伏で決まるはずの曲線が PHC の式で描かれる。
    /// <b>いずれも曲線の形は変わるが例外にはならない</b>ので、値で押さえる。
    ///
    /// 期待値は引き上げ前のコードから取った実測値。式を変えるつもりがないのにここが
    /// 動いたら、引き上げのどこかで取り違えている。
    /// </summary>
    [TestClass]
    public class PrecastShearQNTests
    {
        private static PrecastPileSection Calc(string sectionType, string productName, string? pipeGrade = null)
        {
            var s = new PileSection { PileBodyType = PileTypeNames.PrecastConcrete, PileSectionType = sectionType };
            if (pipeGrade != null) s.PipeGrade = pipeGrade;
            s.SelectedPrecastPile.Name = productName;
            s.RecalculateSelectedPrecastPile();

            var calc = s.CreateSectionCalculator();
            Assert.IsNotNull(calc, $"{sectionType} / {productName} の断面計算が作れません");
            var precast = calc as PrecastPileSection;
            Assert.IsNotNull(precast, $"{sectionType} が既製杭断面ではありません");
            return precast!;
        }

        /// <summary>曲線 1 本を、点数と 4 つの値で押さえる。</summary>
        private static void Curve(
            (List<double> Qs, List<double> Ns) c,
            int count, double q0, double qMid, double n0, double nLast, string what)
        {
            Assert.AreEqual(count, c.Qs.Count, $"{what}: 点数");
            Assert.AreEqual(count, c.Ns.Count, $"{what}: せん断力と軸力で点数が違う");
            Assert.AreEqual(q0, c.Qs[0], $"{what}: 先頭のせん断力");
            Assert.AreEqual(qMid, c.Qs[count / 2], $"{what}: 中央のせん断力");
            Assert.AreEqual(n0, c.Ns[0], $"{what}: 先頭の軸力");
            Assert.AreEqual(nLast, c.Ns[count - 1], $"{what}: 末尾の軸力");
        }

        // ── PHC ────────────────────────────────────────────────────────────

        [TestMethod]
        public void ThePhcShearCurves_KeepTheirValues()
        {
            var s = Calc(PileTypeNames.Phc, "PHC-600-標準-85-B");

            Curve(s.GetServiceLimitQNInteraction(3.0, false), 100,
                55141.6204705304, 78281.7680161703, -599324.411199086, 2409712.221885468, "PHC 使用限界");
            Curve(s.GetDamageLimitQNInteraction(3.0, false), 100,
                71324.29362885862, 117422.65202425545, -599324.411199086, 5482320.051443639, "PHC 損傷限界");
            Curve(s.GetUltimateQNInteraction(3.0, false), 100,
                89155.36703607326, 146778.3150303193, -599324.411199086, 5482320.051443639, "PHC 安全限界");

            // 低減後。使用限界と損傷限界(L1)は β=1 なので低減前と同じで、安全限界だけ β1·β2 が効く。
            Curve(s.GetUltimateQNInteraction(3.0, true), 100,
                57950.98857344763, 95405.90476970754, -599324.411199086, 5482320.051443639, "PHC 安全限界(低減後)");
        }

        // ── PRC ────────────────────────────────────────────────────────────

        [TestMethod]
        public void ThePrcShearCurves_KeepTheirValues()
        {
            var s = Calc(PileTypeNames.Prc, "CPRC-300-85-Ⅰ");

            Curve(s.GetServiceLimitQNInteraction(3.0, false), 100,
                8475.375838027383, 18785.051029219332, -336145.2592183433, 870107.3447570759, "PRC 使用限界");
            Curve(s.GetDamageLimitQNInteraction(3.0, false), 100,
                12713.063757041076, 28177.576543829, -336145.2592183433, 2147315.9842604613, "PRC 損傷限界");
            Curve(s.GetUltimateQNInteraction(3.0, false), 100,
                15891.329696301347, 35221.97067978625, -336145.2592183433, 2147315.9842604613, "PRC 安全限界");
            Curve(s.GetUltimateQNInteraction(3.0, true), 100,
                10329.364302595875, 22894.280941861067, -336145.2592183433, 2147315.9842604613, "PRC 安全限界(低減後)");
        }

        /// <summary>
        /// せん断まわりの 8 メソッドが、書き写されて増えていないこと。
        ///
        /// 出典 (「基礎部材の強度と変形性能」) では PHC と PRC のせん断の扱いは同じで、
        /// 引き上げ前も 79 行が文字単位で一致していた。違うのは断面の寸法だけ。
        /// SC 杭だけは式が違うので、Q-N の 3 本は上書きが 1 つ増えて 2 個になる。
        /// </summary>
        [TestMethod]
        public void TheShearMethods_AreNotCopiedPerPileType()
        {
            var lines = TestSource.Read("Graphics_r1", "Models", "InputData", "PrecastPileSection.cs")
                .Split('\n');

            foreach (var (name, allowed) in new (string, int)[]
            {
                ("GetServiceLimitShear", 2),      // 基底 + SC (SC は引数の並びが違う)
                ("GetDamageLimitShear", 2),
                ("GetUltimateLimitShear", 2),
                ("GetServiceLimitQNInteraction", 2),   // 基底 + SC の上書き
                ("GetDamageLimitQNInteraction", 2),
                ("GetUltimateQNInteraction", 2),
                ("SetEpsilonE", 1),
                ("SetEpsilonPi", 1),
            })
            {
                int count = lines.Count(l =>
                {
                    var t = l.TrimStart();
                    return (t.StartsWith("public ") || t.StartsWith("private ")
                            || t.StartsWith("internal ") || t.StartsWith("protected "))
                           && t.Contains(name + "(");
                });

                Assert.IsTrue(count >= 1, $"{name} の宣言が見つかりません。名前を変えたなら、ここも直してください");
                Assert.IsTrue(count <= allowed,
                    $"{name} の宣言が {count} 個あります (多くて {allowed} 個のはず)。"
                    + "杭種ごとに書き写すと、片方だけ直したときに静かに食い違います。"
                    + "同じ式なら基底クラスに 1 つ置いてください");
            }
        }


        // ── SC ─────────────────────────────────────────────────────────────

        private static PrecastPileSection Sc() => Calc(PileTypeNames.Sc, "SC-400-標準-80-4.5", pipeGrade: "SKK400");

        /// <summary>
        /// SC 杭は鋼管のせん断降伏で決まる。使用限界と損傷限界は軸力に依らないので
        /// 水平線になり、安全限界だけが軸力ごとに変わる。
        /// </summary>
        [TestMethod]
        public void TheScShearCurves_KeepTheirValues()
        {
            var s = Sc();
            const double n0 = -336392.8270772405;      // −0.3·N0(引張)
            const double nLast = 3039234.68447165;     // 掃引の末尾 (上限 0.5·N0(圧縮) の 1 つ手前)

            Curve(s.GetServiceLimitQNInteraction(3.0, false), 100,
                196178.27198636768, 196178.27198636768, n0, nLast, "SC 使用限界");
            Curve(s.GetDamageLimitQNInteraction(3.0, false), 100,
                294267.4079795516, 294267.4079795516, n0, nLast, "SC 損傷限界");
            Curve(s.GetUltimateQNInteraction(3.0, false), 100,
                196578.34215732265, 0.0, n0, nLast, "SC 安全限界");
        }

        /// <summary>
        /// SC 杭の使用限界と損傷限界が、軸力に依らないこと。
        ///
        /// 基底クラス (PHC/PRC) の版を継いでしまうと軸力とともに変わる曲線になる。
        /// 値は出るので、グラフを見ても「そういうもの」と読めてしまう。
        /// </summary>
        [TestMethod]
        public void TheScServiceAndDamageShear_DoNotDependOnAxialForce()
        {
            var s = Sc();

            foreach (var (name, c) in new (string, (List<double> Qs, List<double> Ns))[]
            {
                ("使用限界", s.GetServiceLimitQNInteraction(3.0, false)),
                ("損傷限界", s.GetDamageLimitQNInteraction(3.0, false)),
            })
            {
                Assert.AreEqual(1, c.Qs.Distinct().Count(),
                    $"SC 杭の{name}せん断が軸力で変わっています。"
                    + "基底クラス (PHC/PRC) の式を継いでいないか確認してください");
            }
        }

        /// <summary>
        /// せん断の軸力範囲が §7.2 の適用範囲そのものであること。
        ///
        /// 出典は「限界状態の種類にかかわらず」軸力比 N/N0 で
        /// 曲げ −0.40〜0.50、せん断 −0.30〜0.50 を適用範囲としている。
        /// N0 は圧縮では鋼管＋コンクリート、引張では鋼管のみ。
        ///
        /// 以前は曲線を描く範囲に 4·Ae〜45·Ae を直に書いていた。これは PHC/PRC の章から
        /// 来た σ0+σ0e = 4〜45 N/mm² という<b>応力</b>の範囲で、プレストレスを前提にしている。
        /// SC 杭にプレストレスは無いので 4·Ae がそのまま圧縮軸力になり、
        /// <b>引張側に計算点が 1 つも無かった</b>。曲線は 0 から水平部へ斜めに立ち上がり、
        /// 無軸力では本来の半分の値に読めていた。
        /// </summary>
        [TestMethod]
        public void TheScShearAxialRange_FollowsTheApplicabilityRange()
        {
            var s = Sc();
            var pipe = s.PrecastSteelPipe;

            double n0Tension = pipe.As * pipe.Fys;                   // |As·fys|
            double n0Compression = pipe.As * pipe.Fys + s.Ac * s.Fc; // |As·fys + Ac·Fc|

            Assert.AreEqual(-0.3 * n0Tension, s.ShearNMinService, "せん断 引張側 (軸力比 −0.30)");
            Assert.AreEqual(0.5 * n0Compression, s.ShearNMaxService, "せん断 圧縮側 (軸力比 0.50)");

            // 限界状態が変わっても同じ範囲であること (出典の「限界状態の種類にかかわらず」)。
            Assert.AreEqual(s.ShearNMinService, s.ShearNMinDamage, "損傷限界の引張側");
            Assert.AreEqual(s.ShearNMinService, s.ShearNMinUltimate, "安全限界の引張側");
            Assert.AreEqual(s.ShearNMaxService, s.ShearNMaxDamage, "損傷限界の圧縮側");
            Assert.AreEqual(s.ShearNMaxService, s.ShearNMaxUltimate, "安全限界の圧縮側");

            // 曲線もその範囲から始まること。掃引と表示が別々の値だと、
            // 諸元表と図で違う範囲を見ることになる。
            foreach (var (name, c) in new (string, (List<double> Qs, List<double> Ns))[]
            {
                ("使用限界", s.GetServiceLimitQNInteraction(3.0, false)),
                ("損傷限界", s.GetDamageLimitQNInteraction(3.0, false)),
                ("安全限界", s.GetUltimateQNInteraction(3.0, false)),
            })
            {
                Assert.AreEqual(s.ShearNMinService, c.Ns[0], $"{name}: 掃引の始点が適用範囲と違う");
                Assert.IsTrue(c.Ns[0] < 0, $"{name}: 引張側に計算点がない");
            }
        }

        /// <summary>
        /// 安全限界せん断が (8.26) そのものであること。<b>材料強度は 1.1F。</b>
        ///
        /// (8.26) の sσty は (8.18) の記号説明で 1.1 × 1.5 × sft と定義され、
        /// sft は長期許容引張応力度 F/1.5 なので 1.1F になる。基準強度 F をそのまま
        /// 入れると耐力が 1 割低く、しかも η が 1 割大きくなって √(1−η²) も小さくなる。
        /// <b>実際にそうなっていた</b>。鋼管杭と場所打ち鋼管コンクリート杭にある同じ式は
        /// 1.1F を使っており、SC 杭の写しだけが F だった。
        ///
        /// あわせて、解説の「(8.26) 式から得られる値の半分程度を鋼管寄与分と考える」も
        /// ここで見る。1 点ずつ式で突き合わせるので、どちらが抜けても落ちる。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShear_Follows826WithTheMaterialStrength()
        {
            var s = Sc();
            var pipe = s.PrecastSteelPipe;

            double sSigmaTy = 1.1 * pipe.F;                 // (8.18) sσty = 1.1×1.5×sft = 1.1F
            double sQ0 = 2.0 * pipe.T * (pipe.OutDia - pipe.T) * sSigmaTy / System.Math.Sqrt(3.0);
            double sNy = sSigmaTy * pipe.As;

            var c = s.GetUltimateQNInteraction(3.0, false);

            for (int i = 0; i < c.Item2.Count; i++)
            {
                double eta = c.Item2[i] / sNy;
                double interaction = System.Math.Abs(eta) >= 1.0
                    ? 0.0
                    : System.Math.Sqrt(1.0 - eta * eta);
                double expected = 0.5 * (sQ0 * interaction);   // 0.5 = 解説の「半分程度」

                Assert.AreEqual(expected, c.Item1[i],
                    $"N={c.Item2[i]:N0} での安全限界せん断。"
                    + "材料強度 (1.1F か F か) と鋼管寄与分 1/2 のどちらかが違います");
            }
        }

        /// <summary>
        /// 軸力比が 1 に達すると 0 になること、そしてそれが適用範囲の圧縮側より
        /// <b>手前</b>で起きること。
        ///
        /// 鋼管部分のみが外力に抵抗すると考える (解説) 以上、軸力比の分母は鋼管だけの
        /// 降伏軸力 sNy になる。適用範囲の分母 N0 はコンクリートを含むのでずっと大きく、
        /// 圧縮側の適用範囲の途中で耐力が 0 になる。これは出典どおりの帰結で、誤りではない。
        /// 取り違えて N0 で正規化すると 0 にならなくなるので、ここで押さえておく。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShear_ReachesZeroBeforeTheCompressiveLimit()
        {
            var s = Sc();
            var pipe = s.PrecastSteelPipe;
            double sNy = 1.1 * pipe.F * pipe.As;

            Assert.IsTrue(sNy < s.ShearNMaxService,
                "鋼管だけの降伏軸力が適用範囲の圧縮側より大きくなっています。"
                + "軸力比の分母を N0 と取り違えていないか確認してください");

            var c = s.GetUltimateQNInteraction(3.0, false);
            Assert.IsTrue(c.Item1.Any(q => q > 0), "安全限界せん断が全区間で 0 です");
            Assert.IsTrue(c.Item1.Any(q => q == 0), "軸力比 1 以上で 0 になっていません");

            for (int i = 0; i < c.Item2.Count; i++)
                if (System.Math.Abs(c.Item2[i]) >= sNy)
                    Assert.AreEqual(0.0, c.Item1[i], $"N={c.Item2[i]:N0} は鋼管の降伏軸力以上なので 0 のはず");
        }

        /// <summary>
        /// 使用限界・損傷限界のせん断は、せん断スパン比に<b>永続的に</b>依らないこと。
        ///
        /// 鋼管の許容せん断応力度 (長期 F/(1.5√3)、短期 F/√3) だけで決まり、
        /// a/D が式に入らない。(7.8) の 3 分岐を実装しても、それは安全限界の話なので
        /// ここは変わらない。動いたら PHC/PRC の係数 α(M/(Q·d)) を拾っている。
        /// </summary>
        [TestMethod]
        public void TheScServiceAndDamageShear_NeverDependOnTheShearSpanRatio()
        {
            var s = Sc();

            foreach (double monQd in new[] { 0.5, 1.0, 2.0, 3.0, 10.0 })
            {
                CollectionAssert.AreEqual(
                    s.GetServiceLimitQNInteraction(3.0, false).Item1,
                    s.GetServiceLimitQNInteraction(monQd, false).Item1,
                    $"使用限界が M/(Q·d)={monQd} で変わっています");

                CollectionAssert.AreEqual(
                    s.GetDamageLimitQNInteraction(3.0, false).Item1,
                    s.GetDamageLimitQNInteraction(monQd, false).Item1,
                    $"損傷限界が M/(Q·d)={monQd} で変わっています");
            }

            PileDesign.Common.CalcFallbackTracker.Reset();
        }

        /// <summary>
        /// 安全限界のせん断は、<b>a/D &gt; 1.0 の側では</b>せん断スパン比に依らないこと。
        ///
        /// この側は第 8 章の (8.26) による。式に a/D の項がないので、
        /// (7.8) の 3 分岐を実装したあとも<b>ここは変わらない</b>。
        /// a/D = 0.9 × M/(Q·d) なので、分かれ目は M/(Q·d) ≒ 1.11。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShear_AboveShearSpanRatioOne_DoesNotDependOnIt()
        {
            var s = Sc();

            foreach (double monQd in new[] { 1.2, 2.0, 3.0, 10.0 })   // a/D = 1.08〜9.0
            {
                Assert.IsTrue(0.9 * monQd > 1.0, $"この値は a/D ≦ 1.0 側です: {monQd}");
                CollectionAssert.AreEqual(
                    s.GetUltimateQNInteraction(3.0, false).Item1,
                    s.GetUltimateQNInteraction(monQd, false).Item1,
                    $"安全限界が M/(Q·d)={monQd} で変わっています。"
                    + "(8.26) に a/D の項はありません");
            }
        }

        /// <summary>
        /// せん断スパン比が 1.0 以下のとき、(7.8) の 3 分岐によること。
        ///
        /// この式は sQun が条件式にも入る陰な式なので、3 つとも解いてから
        /// 自分の条件を満たすものを採る。<b>分岐を取り違えても曲線は描ける</b>ので、
        /// 値で押さえる。期待値は正規化した形 (sN0 = 1) の実測値。
        /// </summary>
        [TestMethod]
        public void TheShortSpanShear_FollowsTheThreeBranches()
        {
            // a/D, |N|/sN0, sQun/sN0
            foreach (var (aOverD, m, expected) in new[]
            {
                (0.25, 0.0, 0.3183098861837907),   // 分岐 1 (sN0/π)
                (0.25, 0.1, 0.3183098861837907),   // 分岐 1
                (0.25, 0.3, 0.3121872559191483),   // 分岐 2
                (0.50, 0.0, 0.3183098861837907),   // 分岐 1
                (0.50, 0.3, 0.29078636757814036),  // 分岐 2
                (0.75, 0.0, 0.2938245103234991),   // 分岐 3
                (0.75, 0.5, 0.2113057900527215),
                (1.00, 0.0, 0.25464790894703254),  // 分岐 3
                (1.00, 0.7, 0.1198247998860142),
            })
            {
                Assert.AreEqual(expected, ScShortSpanShear.Unfactored(aOverD, 1.0, m),
                    $"a/D={aOverD}, |N|/sN0={m}");
            }
        }

        /// <summary>
        /// 分岐の境目で値がつながること。
        ///
        /// <b>この検査が読み方を決めた。</b> 出典の紙面から不等号の向きと sN の符号の
        /// 扱いを読み取るのが難しく、考えられる 8 通りを全部試した。全域を隙間なく覆い、
        /// 境目で値が一致するのは 1 通りだけだった。つながらなくなったら、
        /// 係数か条件のどこかを読み違えている。
        /// </summary>
        [TestMethod]
        public void TheShortSpanShear_JoinsAtTheBranchBoundaries()
        {
            foreach (double aOverD in new[] { 0.2, 0.4, 0.6, 0.8, 1.0 })
            {
                double previous = ScShortSpanShear.Unfactored(aOverD, 1.0, 0.0);
                Assert.IsTrue(previous > 0, $"a/D={aOverD}: 無軸力で 0 になっています");

                for (int k = 1; k <= 200; k++)
                {
                    double m = 0.6 * k / 200.0;
                    double q = ScShortSpanShear.Unfactored(aOverD, 1.0, m);
                    if (q == 0.0) break;   // 軸力で使い切った先は見ない

                    Assert.IsTrue(q > 0, $"a/D={aOverD}, m={m:F3}: せん断耐力が負です");
                    Assert.IsTrue(q <= previous + 1e-12,
                        $"a/D={aOverD}, m={m:F3}: 軸力が増えたのに耐力が増えています");
                    Assert.IsTrue(previous - q < 0.01,
                        $"a/D={aOverD}, m={m:F3}: 分岐の境目で {previous:F4} → {q:F4} と跳んでいます。"
                        + "条件式か係数の読み違いが疑われます");
                    previous = q;
                }
            }
        }

        /// <summary>
        /// 分岐の境目ちょうどでも、耐力が落ち込まないこと。
        ///
        /// 出典の分岐 1 の条件は<b>等号を含まない</b> (3|sN| + 2π·sā·sQun &lt; sN0)。
        /// そのまま比べると、境界ちょうどの点が丸めでどの分岐からも外れて 0 が返る。
        /// 曲線に 1 点だけ落ち込みができるが、例外にはならない。
        ///
        /// sā = 0.425、|sN|/sN0 = 0.05 が、ちょうど 0.15 + 0.85 = 1.0 になる点。
        /// </summary>
        [TestMethod]
        public void TheShortSpanShear_DoesNotDropAtABranchBoundary()
        {
            // 3m + 2·sā = 1 となる (sā, m) を作って踏む
            foreach (double aOverD in new[] { 0.1, 0.2, 0.3, 0.4, 0.425, 0.49 })
            {
                double m = (1.0 - 2.0 * aOverD) / 3.0;
                double q = ScShortSpanShear.Unfactored(aOverD, 1.0, m);

                Assert.AreEqual(1.0 / System.Math.PI, q, 1e-12,
                    $"a/D={aOverD}, m={m:F6}: 境界ちょうどで sN0/π にならず {q:F6} でした。"
                    + "どの分岐からも外れて 0 が返っていないか確認してください");
            }
        }

        /// <summary>
        /// (7.8) が、軸力がないときの純せん断耐力 sQ0 を超えないこと。
        ///
        /// sQ0 = 2t(D−t)·sσty/√3 は鋼管が周方向にせん断降伏する値で、上限になるはず。
        /// 超えたら係数を読み違えている。
        /// </summary>
        [TestMethod]
        public void TheShortSpanShear_StaysBelowThePureShearCapacity()
        {
            // sN0 = As·fy、sQ0 = 2t(D−t)fy/√3。As = πt(D−t) なので sQ0 = (2/(π√3))·sN0。
            double cap = 2.0 / (System.Math.PI * System.Math.Sqrt(3.0));

            foreach (double aOverD in new[] { 0.1, 0.25, 0.5, 0.75, 1.0 })
                foreach (double m in new[] { 0.0, 0.1, 0.3, 0.5 })
                    Assert.IsTrue(ScShortSpanShear.Unfactored(aOverD, 1.0, m) <= cap,
                        $"a/D={aOverD}, m={m}: 純せん断耐力 {cap:F4} を超えています");
        }

        /// <summary>
        /// 引張と圧縮で同じ値になること。sN は絶対値で入る。
        /// </summary>
        [TestMethod]
        public void TheShortSpanShear_TreatsTensionAndCompressionAlike()
        {
            foreach (double aOverD in new[] { 0.25, 0.5, 1.0 })
                foreach (double m in new[] { 0.1, 0.3, 0.5 })
                    Assert.AreEqual(
                        ScShortSpanShear.Unfactored(aOverD, 1.0, m),
                        ScShortSpanShear.Unfactored(aOverD, 1.0, -m),
                        $"a/D={aOverD}, |N|/sN0={m}: 引張と圧縮で値が違います");
        }

        /// <summary>
        /// せん断スパン比 1.0 の前後で式が切り替わること。
        ///
        /// a/D ≦ 1.0 は (7.8)、a/D &gt; 1.0 は (8.26)×1/2。1/2 は解説が
        /// a/D &gt; 1.0 の場合について述べたもので、<b>(7.8) の側には掛からない</b>。
        /// そのため境目で値は跳ぶ（連続にはならない）。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShear_SwitchesFormulaAtShearSpanRatioOne()
        {
            var s = Sc();

            // a/D = 0.9 (7.8 の側) と a/D = 1.08 (8.26 の側)
            double shortSpan = s.GetUltimateQNInteraction(1.0, false).Item1.Max();
            double longSpan = s.GetUltimateQNInteraction(1.2, false).Item1.Max();

            Assert.AreEqual(303044.85198733147, shortSpan, "a/D = 0.90 (7.8)");
            Assert.AreEqual(206068.37661097015, longSpan, "a/D = 1.08 (8.26)×1/2");

            Assert.IsTrue(shortSpan > longSpan,
                "短いせん断スパンのほうが小さくなっています。"
                + "(8.26) 側の 1/2 を (7.8) 側にも掛けていないか確認してください");
        }

        /// <summary>
        /// 低減係数 β = β1·β2 が、低減後の曲線にだけ効くこと。
        ///
        /// 出典は β1・β2 とも 1.0 以下とし、コンクリートの圧縮破壊や鋼管の座屈が
        /// 変形性能に影響する場合は β2 を 0.75 以下とすることが望ましいとしている。
        /// 既定は 1.0 で、以前は固定だった。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShearBeta_AppliesToTheFactoredCurveOnly()
        {
            using var _ = TestStateScope.Enter();
            var s = Sc();

            ConcreteModelOptions.ScUltimateShearBeta1 = 1.0;
            ConcreteModelOptions.ScUltimateShearBeta2 = 1.0;
            var unfactored = s.GetUltimateQNInteraction(3.0, false).Item1;
            var atOne = s.GetUltimateQNInteraction(3.0, true).Item1;
            CollectionAssert.AreEqual(unfactored, atOne, "β = 1.0 なら低減前と同じはず");

            ConcreteModelOptions.ScUltimateShearBeta2 = 0.75;
            var stillUnfactored = s.GetUltimateQNInteraction(3.0, false).Item1;
            var atThreeQuarters = s.GetUltimateQNInteraction(3.0, true).Item1;

            CollectionAssert.AreEqual(unfactored, stillUnfactored, "低減前の曲線が β で変わっています");
            for (int i = 0; i < unfactored.Count; i++)
                Assert.AreEqual(0.75 * unfactored[i], atThreeQuarters[i], $"[{i}] β2 = 0.75 が効いていない");
        }

        /// <summary>
        /// 低減係数が出典の範囲に収まること。本文は「1.0 以下の値とする」。
        /// 0 を入れると耐力が消えて、原因の分からない検定 NG になる。
        ///
        /// 数値でない値は<b>その係数の既定</b>に戻す。一律 1.0 に戻すと、β2 では
        /// 既定 (0.75) より低減しない側へ動いてしまう。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShearBeta_StaysWithinTheSourceRange()
        {
            using var _ = TestStateScope.Enter();

            foreach (var (name, setter, getter, fallback) in new (string, System.Action<double>, System.Func<double>, double)[]
            {
                ("β1", v => ConcreteModelOptions.ScUltimateShearBeta1 = v,
                       () => ConcreteModelOptions.ScUltimateShearBeta1,
                       ConcreteModelOptions.DefaultScUltimateShearBeta1),
                ("β2", v => ConcreteModelOptions.ScUltimateShearBeta2 = v,
                       () => ConcreteModelOptions.ScUltimateShearBeta2,
                       ConcreteModelOptions.DefaultScUltimateShearBeta2),
            })
            {
                setter(1.5);
                Assert.AreEqual(1.0, getter(), $"{name}: 1.0 を超える値が通っています");

                setter(0.0);
                Assert.IsTrue(getter() > 0.0, $"{name}: 0 が通っています");

                setter(double.NaN);
                Assert.AreEqual(fallback, getter(), $"{name}: 数値でない値がその係数の既定に戻っていません");
            }
        }

        /// <summary>
        /// 既定が β1 = 1.00、β2 = 0.75 であること。
        ///
        /// β2 の既定を 0.75 にしたのは、出典が「コンクリートの圧縮破壊や鋼管の座屈が
        /// 変形性能に影響を与える場合は 0.75 以下とすることが望ましい」としており、
        /// 安全限界状態の SC 杭では通常その条件に当たるため。<b>耐力が 25% 下がる</b>ので、
        /// 気づかずに戻すことがないよう値で押さえておく。
        ///
        /// あわせて、既定が 1 か所にしか書かれていないことも見る。入力の初期値・
        /// 保存ファイルにキーが無いときの補い・画面の初期値の 3 か所に書き写すと、
        /// 片方だけ直したときに食い違う。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShearBetaDefaults_AreOneAndThreeQuarters()
        {
            Assert.AreEqual(1.0, ConcreteModelOptions.DefaultScUltimateShearBeta1, "β1 の既定");
            Assert.AreEqual(0.75, ConcreteModelOptions.DefaultScUltimateShearBeta2, "β2 の既定");

            // 何も設定していない入力モデルが、その既定を持つこと
            var f = new FundamentalInput();
            Assert.AreEqual(ConcreteModelOptions.DefaultScUltimateShearBeta1, f.ScUltimateShearBeta1,
                "入力の初期値が既定と違います");
            Assert.AreEqual(ConcreteModelOptions.DefaultScUltimateShearBeta2, f.ScUltimateShearBeta2,
                "入力の初期値が既定と違います");

            // 既定の数値が書き写されていないこと
            foreach (var (dir, file) in new[]
            {
                ("Models", "InputData/FundamentalInput.cs"),
                ("ViewModels", "FundamentalViewModel.cs"),
                ("ViewModels", "MainWindowViewModel.FileIO.cs"),
            })
            {
                var text = TestSource.Read("Graphics_r1", dir, file.Replace('/', System.IO.Path.DirectorySeparatorChar));
                foreach (var line in text.Split('\n'))
                {
                    // 大文字小文字を問わず拾う。フィールド名は _scUltimateShearBeta2 で
                    // 頭が小文字なので、"ScUltimateShearBeta" だけを見ると空振りする。
                    if (!line.Contains("UltimateShearBeta")) continue;
                    if (line.TrimStart().StartsWith("//")) continue;
                    Assert.IsFalse(line.Contains("0.75") || line.Contains("= 1.0;") || line.Contains("?? 1.0"),
                        $"{file}: 既定の数値が書き写されています。"
                        + $"ConcreteModelOptions.DefaultScUltimateShearBeta1/2 を使ってください:{System.Environment.NewLine}  {line.Trim()}");
                }
            }
        }

        /// <summary>
        /// (8.26) が杭種ごとに書き写されていないこと。
        ///
        /// 同じ式が鋼管杭 (中間部・杭頭部) と SC 杭にあり、<b>SC 杭のものだけ</b>が
        /// 材料強度に F を使っていた。曲線は描けて例外にもならないので、
        /// 値を比べない限り気づけない。式は 1 か所に置く。
        /// </summary>
        [TestMethod]
        public void The826Formula_LivesInOnePlace()
        {
            var root = TestSource.Dir("Graphics_r1");
            var copies = new List<string>();
            int scanned = 0;

            foreach (var file in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}")) continue;
                scanned++;

                foreach (var line in System.IO.File.ReadAllLines(file))
                {
                    var t = line.Trim();
                    if (t.StartsWith("//")) continue;
                    // sQ0 = 2·t·(D−t)·σ/√3 の形
                    if (t.Contains("Math.Sqrt(3") && t.Contains("2.0 *") && t.Contains("* ("))
                        copies.Add(System.IO.Path.GetFileName(file) + ": " + t);
                }
            }

            TestSource.AssertScanned(scanned, 300, "本体のソース");
            Assert.AreEqual(1, copies.Count,
                $"sQ0 = 2t(D−t)·sσty/√3 の式が {copies.Count} か所にあります。"
                + "SteelPipeUltimateShear に 1 つだけ置いてください。"
                + "書き写すと、材料強度を 1.1F にするのを片方だけ忘れます (実際にそうなっていました):"
                + System.Environment.NewLine + "  "
                + string.Join(System.Environment.NewLine + "  ", copies));
        }
    }
}
