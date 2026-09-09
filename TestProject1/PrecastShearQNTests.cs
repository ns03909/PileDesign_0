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
        /// せん断スパン比が 1.0 以下のとき、未実装であることを記録に残すこと。
        ///
        /// a/D ≦ 1.0 では本来「コンクリート充填鋼管構造設計施工指針」の円形鋼管の
        /// せん断強度式 (sQun が両辺に現れる 3 分岐の陰な式) による。未実装なので
        /// (8.26)×0.5 で代替しているが、黙って代替すると気づけない。
        ///
        /// プログラムが持っているのは M/(Q·d) で d = 0.9D なので、a/D = 0.9 × M/(Q·d)。
        /// 既定の M/(Q·d) = 3.0 は a/D = 2.7 で、記録は残らない。
        /// </summary>
        [TestMethod]
        public void TheScUltimateShear_RecordsWhenTheShearSpanRatioIsSmall()
        {
            var s = Sc();

            PileDesign.Common.CalcFallbackTracker.Reset();
            s.GetUltimateQNInteraction(3.0, false);      // a/D = 2.7
            Assert.AreEqual(0L, PileDesign.Common.CalcFallbackTracker.TotalCount,
                "既定のせん断スパン比で記録が残っています");

            PileDesign.Common.CalcFallbackTracker.Reset();
            s.GetUltimateQNInteraction(1.0, false);      // a/D = 0.9
            StringAssert.Contains(PileDesign.Common.CalcFallbackTracker.BuildSummary(), "せん断スパン比",
                "a/D ≦ 1.0 なのに記録が残っていません");
            PileDesign.Common.CalcFallbackTracker.Reset();
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
        /// </summary>
        [TestMethod]
        public void TheScUltimateShearBeta_StaysWithinTheSourceRange()
        {
            using var _ = TestStateScope.Enter();

            ConcreteModelOptions.ScUltimateShearBeta2 = 1.5;
            Assert.AreEqual(1.0, ConcreteModelOptions.ScUltimateShearBeta2, "1.0 を超える値が通っています");

            ConcreteModelOptions.ScUltimateShearBeta2 = 0.0;
            Assert.IsTrue(ConcreteModelOptions.ScUltimateShearBeta2 > 0.0, "0 が通っています");

            ConcreteModelOptions.ScUltimateShearBeta2 = double.NaN;
            Assert.AreEqual(1.0, ConcreteModelOptions.ScUltimateShearBeta2, "数値でない値が既定に戻っていません");
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
