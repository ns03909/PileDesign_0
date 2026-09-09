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

        /// <summary>
        /// SC 杭は鋼管のせん断降伏で決まる。使用限界と損傷限界は軸力に依らないので
        /// 水平線になり、安全限界だけが曲げとの相関で軸力ごとに変わる。
        /// </summary>
        [TestMethod]
        public void TheScShearCurves_KeepTheirValues()
        {
            var s = Calc(PileTypeNames.Sc, "SC-400-標準-80-4.5", pipeGrade: "SKK400");

            Curve(s.GetServiceLimitQNInteraction(3.0, false), 100,
                196178.27198636768, 196178.27198636768, 338022.80315564736, 3768109.198177579, "SC 使用限界");
            Curve(s.GetDamageLimitQNInteraction(3.0, false), 100,
                294267.4079795516, 294267.4079795516, 0.0, 3764728.9701460223, "SC 損傷限界");
            Curve(s.GetUltimateQNInteraction(3.0, false), 100,
                353474.06806923856, 0.0, 338022.80315564736, 3768109.198177579, "SC 安全限界");
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
            var s = Calc(PileTypeNames.Sc, "SC-400-標準-80-4.5", pipeGrade: "SKK400");

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
        /// <b>SC 杭のせん断の軸力範囲が 2 通りある。</b>
        ///
        /// 曲線を描く範囲は 4·Ae ～ 45·Ae を直に書いている。一方、諸元表の表示と
        /// せん断内訳曲線が読む <c>ShearNMaxService</c> は基底クラスの既定
        /// (Fc/3.5 − σE)·Ae のままで、SC 杭では上書きしていない。使用限界では
        /// 表示が曲線の半分ほどの値になる。
        ///
        /// どちらが正かは出典に当たる必要があり、直すと数値が動く。
        /// ここでは<b>食い違いが残っていることを記録する</b>。解決したらこのテストを
        /// 消して、上の値の固定だけを残すこと。
        /// </summary>
        [TestMethod]
        public void TheScShearAxialRange_IsStillDefinedTwice()
        {
            var s = Calc(PileTypeNames.Sc, "SC-400-標準-80-4.5", pipeGrade: "SKK400");

            double curveMax = s.GetServiceLimitQNInteraction(3.0, false).Item2.Max();

            Assert.AreEqual(1931558.8751751278, s.ShearNMaxService, "表示側の使用限界の上限");
            Assert.IsTrue(curveMax > 1.8 * s.ShearNMaxService,
                "食い違いが解消されたようです。このテストを消して、値の固定だけ残してください "
                + $"(曲線の上限 {curveMax:N0} N / 表示の上限 {s.ShearNMaxService:N0} N)");
        }
    }
}
